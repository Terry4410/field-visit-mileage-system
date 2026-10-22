#!/usr/bin/env python3
"""Only synthetic local metadata. Zero Azure/SQL connections."""
import copy
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
            p.write_text("CREATE TRIGGER dbo.TR_Source004 ON dbo.Locations AFTER UPDATE AS SELECT 1;" if i==4 else "-- reviewed source")
            src[p.relative_to(self.root).as_posix()]=digest(p.read_bytes())
        self.expected={
            "ContractVersion":safe.CONTRACT,"SchemaVersion":{"Latest":"1.8.0-010"},
            "Provenance":{"Kind":"INDEPENDENT_APPROVED_150_TO_010_REPLAY",
                "BaseArchive":{"Approved":True,"Sha256":"f"*64},"SourceFiles":src},
            "Columns":[{"SchemaName":"dbo","TableName":"Locations","Ordinal":1,"ColumnName":"TeamId","IsNullable":True}],
            "Indexes":[{"SchemaName":"dbo","TableName":"Locations","IndexName":"IX_A","IsIncluded":False,"KeyOrdinal":1,"ColumnName":"TeamId"}],
            "ForeignKeys":[{"SchemaName":"dbo","TableName":"Locations","ForeignKeyName":"FK_A","ColumnOrdinal":1,"ParentColumn":"TeamId"}],
            "Checks":[{"SchemaName":"dbo","TableName":"Locations","CheckConstraintName":"CK_A","Definition":"SECRET_OBJECT_DEFINITION"}],
            "Triggers":[{"SchemaName":"dbo","TableName":"Locations","TriggerName":"TR_Source004",
                 "DefinitionSha256":"a"*64,"Events":"UPDATE","IsDisabled":False,"IsInsteadOfTrigger":False}]
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
    def test_changed_trigger_name(self):self.assertEqual(self.trial(change_ref=lambda x:x["Triggers"][0].update(TriggerName="TR_Unknown")),"REFERENCE")
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
