#!/usr/bin/env python3
"""Only synthetic local metadata. Zero Azure/SQL connections."""
import copy
import contextlib
import io
import sys
from unittest.mock import patch
import hashlib
import importlib.util
import json
import re
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location("safe",Path(__file__).with_name("g1_uat010_safe_compare.py"))
safe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(safe)

def digest(b): return hashlib.sha256(b).hexdigest()

class G1PrivateContractTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)
        src={}
        for i in range(1,11):
            p=self.root/f"database/migrations/1800_{i:03d}_fixture/Up.sql"
            p.parent.mkdir(parents=True,exist_ok=True)
            statement = ("CREATE TRIGGER dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations ON dbo.Locations AFTER UPDATE AS SELECT 1;" if i==4
                         else "CREATE TRIGGER dbo.TR_Locations_SelectedGeocodingAttempt ON dbo.Locations AFTER INSERT AS SELECT 1;" if i==7
                         else "-- reviewed source")
            p.write_text(statement)
            src[p.relative_to(self.root).as_posix()]=digest(p.read_bytes())
        self.expected={
            "ContractVersion":safe.CONTRACT,"SchemaVersion":{"Latest":"1.8.0-010"},
            "Provenance":{"Kind":"INDEPENDENT_APPROVED_150_TO_010_REPLAY",
                "BaseArchive":{"Approved":True,"Sha256":"f"*64},"SourceFiles":src},
            "Columns":[{"SchemaName":"dbo","TableName":"Locations","Ordinal":1,"ColumnName":"TeamId",
                "SqlType":"int","MaxLengthBytesOrCharacters":4,"NumericPrecision":10,
                "NumericScale":0,"IsNullable":True,"IsIdentity":False,"IsComputed":False,
                "IsRowVersion":False,"DefaultDefinition":None,"CollationName":None}],
            "Indexes":[{"SchemaName":"dbo","TableName":"Locations","IndexName":"IX_A",
                "IsIncluded":False,"KeyOrdinal":1,"ColumnName":"TeamId",
                "IndexType":"NONCLUSTERED","IsPrimaryKey":False,"IsUniqueConstraint":False,
                "IsUnique":False,"IsDisabled":False,"HasFilter":False,
                "FilterDefinition":None,"IsDescending":False}],
            "ForeignKeys":[{"SchemaName":"dbo","TableName":"Locations","ForeignKeyName":"FK_A",
                "ColumnOrdinal":1,"ParentColumn":"TeamId","ReferencedSchema":"dbo",
                "ReferencedTable":"Teams","ReferencedColumn":"TeamId",
                "DeleteAction":"NO_ACTION","UpdateAction":"NO_ACTION",
                "IsDisabled":False,"IsNotTrusted":False}],
            "Checks":[{"SchemaName":"dbo","TableName":t,"CheckConstraintName":n,
                 "Definition":"SECRET_OBJECT_DEFINITION","IsDisabled":False,"IsNotTrusted":False}
                 for t,n in (("RouteCalculationAttempts","CK_RouteCalculationAttempts_Reason"),
                    ("ImportBatches","CK_ImportBatches_Status"),
                    ("ImportBatchItems","CK_ImportBatchItems_Status"))],
            "Triggers":[{"SchemaName":"dbo","TableName":"Locations","TriggerName":name,
                 "DefinitionSha256":"a"*64,"Events":event,"IsDisabled":False,"IsInsteadOfTrigger":False}
                  for name,event in (("TR_Locations_ProtectCurrentDeploymentSiteLocations","UPDATE"),
                                     ("TR_Locations_SelectedGeocodingAttempt","INSERT"))]
        }
        self.actual=copy.deepcopy(self.expected)
        self.actual["SourceSha"]="c"*40
        self.actual["Target"]={"ServerName":safe.TARGET[0],"DatabaseName":safe.TARGET[1]}
        self.actual["Provenance"]={"Kind":"READONLY_UAT_CAPTURE"}

    def trial(self, change=None, change_ref=None, wrong_pin=False):
        ref=copy.deepcopy(self.expected);actual=copy.deepcopy(self.actual)
        if change:change(actual)
        if change_ref:change_ref(ref)
        a=self.root/"actual.json";b=self.root/"reference.json"
        a.write_text(json.dumps(actual));b.write_text(json.dumps(ref))
        args=SimpleNamespace(actual=a,reference=b,repo_root=self.root,protected_sha="c"*40,
             reference_sha256="d"*64 if wrong_pin else digest(b.read_bytes()))
        try:return safe.check(args)
        except safe.Hold as e:
            # Guaranteed fixed allowlisted output; no detailed exception or user content.
            self.assertRegex(e.code,r"^[A-Z_]+$")
            self.assertNotIn("Locations",e.code)
            self.assertNotIn("SECRET_OBJECT_DEFINITION",e.code)
            return e.code

    def test_positive_synthetic_only(self):self.assertEqual(self.trial(),"PASS")
    def test_reference_pin(self):self.assertEqual(self.trial(wrong_pin=True),"PIN")
    def test_fake_source(self):self.assertEqual(self.trial(change_ref=lambda x:x["Provenance"].update(Kind="SYNTHETIC")),"REFERENCE")
    def test_missing_base(self):self.assertEqual(self.trial(change_ref=lambda x:x["Provenance"]["BaseArchive"].update(Approved=False)),"REFERENCE")
    def test_source_sha_drift(self):self.assertEqual(self.trial(change_ref=lambda x:x["Provenance"]["SourceFiles"].update({next(iter(x["Provenance"]["SourceFiles"])):"0"*64})),"SOURCE")
    def test_removed_trigger(self):self.assertEqual(self.trial(change=lambda x:x.update(Triggers=[])),"SHAPE")
    def test_changed_trigger_name(self):self.assertEqual(self.trial(change_ref=lambda x:x["Triggers"][0].update(TriggerName="TR_Unknown")),"TRIGGER_SCOPE")
    def test_changed_trigger_body(self):self.assertEqual(self.trial(change=lambda x:x["Triggers"][0].update(DefinitionSha256="e"*64)),"METADATA_DRIFT")
    def test_invisible_trigger_body(self):self.assertEqual(self.trial(change=lambda x:x["Triggers"][0].update(DefinitionSha256=None)),"TRIGGER_VISIBILITY")
    def test_disabled_trigger(self):self.assertEqual(self.trial(change=lambda x:x["Triggers"][0].update(IsDisabled=True)),"TRIGGER_STATE")
    def test_changed_events(self):self.assertEqual(self.trial(change=lambda x:x["Triggers"][0].update(Events="INSERT")),"METADATA_DRIFT")
    def test_duplicate_trigger(self):self.assertEqual(self.trial(change=lambda x:x["Triggers"].append(copy.deepcopy(x["Triggers"][0]))),"SHAPE")
    def test_column_drift(self):self.assertEqual(self.trial(change=lambda x:x["Columns"][0].update(IsNullable=False)),"METADATA_DRIFT")
    def test_check_drift(self):self.assertEqual(self.trial(change=lambda x:x["Checks"][0].update(Definition="PERSONAL_SECRET")),"METADATA_DRIFT")
    def test_wrong_target(self):self.assertEqual(self.trial(change=lambda x:x["Target"].update(DatabaseName="another-db")),"TARGET")
    def test_wrong_source_sha(self):self.assertEqual(self.trial(change=lambda x:x.update(SourceSha="0"*40)),"PIN")
    def test_wrong_capture(self):self.assertEqual(self.trial(change=lambda x:x["Provenance"].update(Kind="DISPOSABLE_SQL")),"CAPTURE")
    def test_missing_default_facet(self):self.assertEqual(self.trial(change=lambda x:x["Columns"][0].pop("DefaultDefinition")),"SHAPE")
    def test_missing_collation_facet(self):self.assertEqual(self.trial(change=lambda x:x["Columns"][0].pop("CollationName")),"SHAPE")
    def test_missing_index_filter_facet(self):self.assertEqual(self.trial(change=lambda x:x["Indexes"][0].pop("FilterDefinition")),"SHAPE")
    def test_missing_foreign_key_trust(self):self.assertEqual(self.trial(change=lambda x:x["ForeignKeys"][0].pop("IsNotTrusted")),"SHAPE")
    def test_foreign_key_untrusted(self):self.assertEqual(self.trial(change=lambda x:x["ForeignKeys"][0].update(IsNotTrusted=True)),"FK_STATE")
    def test_missing_check_definition(self):self.assertEqual(self.trial(change=lambda x:x["Checks"][0].pop("Definition")),"SHAPE")
    def test_check_definition_hidden(self):self.assertEqual(self.trial(change=lambda x:x["Checks"][0].update(Definition=None)),"CHECK_VISIBILITY")
    def test_check_untrusted(self):self.assertEqual(self.trial(change=lambda x:x["Checks"][0].update(IsNotTrusted=True)),"CHECK_STATE")
    def test_import_constraint_missing(self):self.assertEqual(self.trial(change=lambda x:x["Checks"].pop()),"CHECK_SCOPE")
    def test_legacy_007_trigger_missing(self):self.assertEqual(self.trial(change=lambda x:x["Triggers"].pop()),"TRIGGER_SCOPE")
    def test_collation_value_drift(self):self.assertEqual(self.trial(change=lambda x:x["Columns"][0].update(CollationName="Latin1_General_BIN2")),"METADATA_DRIFT")
    def test_missing_expected_facet(self):self.assertEqual(self.trial(change_ref=lambda x:x["Indexes"][0].pop("IndexType")),"SHAPE")
    def test_import_check_trust_deviation(self):self.assertEqual(self.trial(change=lambda x:x["Checks"][1].update(IsDisabled=True)),"CHECK_STATE")
    def test_cli_never_presents_synthetic_parity_as_release_go(self):
        ref=self.root/"approved-reference.json"
        actual=self.root/"synthetic-actual.json"
        ref.write_text(json.dumps(self.expected))
        actual.write_text(json.dumps(self.actual))
        argv=["offline-compare", "--reference",str(ref), "--actual",str(actual),
              "--repo-root",str(self.root), "--protected-sha","c"*40,
              "--reference-sha256",digest(ref.read_bytes())]
        output=io.StringIO()
        with patch.object(sys,"argv",argv), contextlib.redirect_stdout(output):
            code=safe.main()
        self.assertEqual(code,0)
        result=output.getvalue().strip()
        self.assertEqual(result,"G1OfflineParity=PASS; ReleaseGate=HOLD; LiveAuthorization=NONE;")
        self.assertNotIn("G1Evidence=PASS",result)
        self.assertNotIn("Locations",result)

    def test_sql_is_not_live_wired_or_mutating(self):
        path=Path(__file__).with_name("g1_uat010_triggers_readonly.sql")
        sql=path.read_text()
        self.assertIn("sys.triggers",sql);self.assertIn("sys.trigger_events",sql)
        self.assertIn("HASHBYTES",sql);self.assertNotIn(" AS Definition,",sql)
        cleaned=re.sub(r"(?m)^\s*--.*$","",sql)
        cleaned=re.sub(r"N?'(?:''|[^'])*'","''",cleaned).upper()
        for word in ("INSERT","UPDATE","DELETE","CREATE","ALTER","EXEC","DROP","MERGE","GRANT","REVOKE","BACKUP","RESTORE"):
            self.assertIsNone(re.search(r"\b"+word+r"\b",cleaned))
if __name__=="__main__":
    unittest.main()
