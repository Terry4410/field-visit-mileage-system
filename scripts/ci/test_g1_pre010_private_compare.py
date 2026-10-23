#!/usr/bin/env python3
"""Fail-closed private-collector tests. Fixture values never represent real UAT."""
import copy
import importlib.util
import unittest
from pathlib import Path

MODULE=Path(__file__).with_name("g1_pre010_private_compare.py")
spec=importlib.util.spec_from_file_location("g1private",MODULE)
v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v)
SOURCE_SHA="c"*40

class PrivateComparatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sources=v.source_module.extract(Path("candidate"))
        src=cls.sources[v.REQUIRED007]
        header=v.source_module.core.TRIGGER_HEADER.search(src["Ddl"])
        cls.good={
            "ContractVersion":"g1-pre010-private-observed-v1",
            "SourceSha":SOURCE_SHA,
            "Provenance":"DISPOSABLE_SQL",
            "SchemaVersion":"DISPOSABLE",
            "Triggers":[{
                "SchemaName":"dbo","TableName":header.group(2),
                "TriggerName":v.REQUIRED007,"Events":",".join(sorted(x.upper() for x in header.groups()[2:] if x)),
                "IsDisabled":False,"IsInsteadOfTrigger":False,
                "DefinitionSha256":src["Utf16LeSha256"]
            }]
        }
    def check(self, edit=lambda x:None, mode="disposable"):
        d=copy.deepcopy(self.good);edit(d)
        return v.compare(d,self.sources,mode,SOURCE_SHA)
    def test_positive_fixture_only(self):self.assertEqual(self.check(),"PASS")
    def test_hash_drift(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d["Triggers"][0].update(DefinitionSha256="a"*64))
        self.assertEqual(e.exception.code,"007_DEFINITION_DRIFT")
    def test_disabled(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d["Triggers"][0].update(IsDisabled=True))
        self.assertEqual(e.exception.code,"TRIGGER_STATE")
    def test_event_drift(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d["Triggers"][0].update(Events="DELETE"))
        self.assertEqual(e.exception.code,"TRIGGER_EVENTS")
    def test_wrong_table(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d["Triggers"][0].update(TableName="Wrong"))
        self.assertEqual(e.exception.code,"TRIGGER_PARENT")
    def test_missing_definition(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d["Triggers"][0].update(DefinitionSha256=None))
        self.assertEqual(e.exception.code,"DEFINITION_VISIBILITY")
    def test_missing(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d.update(Triggers=[]))
        self.assertEqual(e.exception.code,"REQUIRED_TRIGGER_MISSING")
    def test_source_sha(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d.update(SourceSha="0"*40))
        self.assertEqual(e.exception.code,"SOURCE")
    def test_live_never_accepts_single_trigger_fixture(self):
        with self.assertRaises(v.Stop) as e:self.check(mode="live")
        self.assertEqual(e.exception.code,"TARGET_PREDECESSOR")
    def test_duplicate(self):
        with self.assertRaises(v.Stop) as e:self.check(lambda d:d["Triggers"].append(dict(d["Triggers"][0])))
        self.assertEqual(e.exception.code,"DUPLICATE")

    def test_47_created_table_facets_source(self):
        rows=v.column_module.derive(Path("candidate"))
        self.assertEqual(len(rows),47)
        self.assertEqual(len({(x["TableName"],x["ColumnName"]) for x in rows}),47)
        self.assertEqual({r["TableName"] for r in rows},set(v.column_module.SCOPE))
        self.assertEqual(next(r for r in rows if r["TableName"]=="RouteCalculationAttempts" and r["ColumnName"]=="RequestBasisHash")["MaxLengthBytes"],32)
        self.assertEqual(next(r for r in rows if r["TableName"]=="TeamLocationNotes" and r["ColumnName"]=="RowVersion")["SqlType"],"timestamp")
    def test_created_table_col_type_and_nullability_drift(self):
        rows=v.column_module.derive(Path("candidate"))
        observed=copy.deepcopy(rows)
        v.check_columns(observed,rows)
        observed[0]["MaxLengthBytes"]=99
        with self.assertRaises(v.Stop) as e:v.check_columns(observed,rows)
        self.assertEqual(e.exception.code,"COLUMN_DRIFT")
    def test_created_table_missing_extra_and_duplicate(self):
        rows=v.column_module.derive(Path("candidate"))
        for change in (lambda o:o.pop(),lambda o:o.append(dict(o[0])),lambda o:o.append(dict(o[0],ColumnName="Unknown"))):
            observed=copy.deepcopy(rows)
            change(observed)
            with self.assertRaises(v.Stop):v.check_columns(observed,rows)
    def test_non007_trigger_definition_drift(self):
        sources=self.sources
        row=next((n,s) for n,s in sources.items() if n!=v.REQUIRED007)
        m,s=row
        header=v.source_module.core.TRIGGER_HEADER.search(s["Ddl"])
        record={"SchemaName":"dbo","TableName":header.group(2),"TriggerName":m,
                "Events":",".join(sorted(x.upper() for x in header.groups()[2:] if x)),
                "IsDisabled":False,"IsInsteadOfTrigger":False,
                "DefinitionSha256":"f"*64}
        d=copy.deepcopy(self.good)
        d["Provenance"]="READONLY_UAT_CAPTURE"
        d["Target"]={"ServerName":"sql-fieldvisit-jpe-uat","DatabaseName":"db-fieldvisit-uat"}
        d["SchemaVersion"]="1.8.0-010"
        d["ReasonCheck"]="PASS"
        d["Columns"]=v.column_module.derive(Path("candidate"))
        idxref=v.index_module.derive(Path("candidate"))
        d["Indexes"]=[dict(row) for row in idxref["Indexes"]]
        d["Defaults"]=[dict(row,Definition=row["DefinitionNorm"]) for row in idxref["Defaults"]]
        constraints=v.constraint_module.derive(Path("candidate"))
        d["ForeignKeys"]=[dict(x,IsDisabled=False,IsNotTrusted=False) for x in constraints["ForeignKeys"]]
        d["Checks"]=[dict(x,DefinitionVisible=True,IsDisabled=False,IsNotTrusted=False) for x in constraints["Checks"]]
        d["Triggers"]=[]
        for k,item in sources.items():
            h=v.source_module.core.TRIGGER_HEADER.search(item["Ddl"])
            d["Triggers"].append({
                "SchemaName":"dbo","TableName":h.group(2),"TriggerName":k,
                "Events":",".join(sorted(x.upper() for x in h.groups()[2:] if x)),
                "IsDisabled":False,"IsInsteadOfTrigger":False,
                "DefinitionSha256":item["Utf16LeSha256"]})
        self.assertEqual(v.compare(d,sources,"live",SOURCE_SHA,d["Columns"],constraints,idxref),"PASS")
        d["Triggers"][0]["DefinitionSha256"]="f"*64
        with self.assertRaises(v.Stop) as e:v.compare(d,sources,"live",SOURCE_SHA,d["Columns"],constraints,idxref)
        self.assertIn(e.exception.code,("TRIGGER_DEFINITION_DRIFT","007_DEFINITION_DRIFT"))

if __name__=="__main__":unittest.main()
