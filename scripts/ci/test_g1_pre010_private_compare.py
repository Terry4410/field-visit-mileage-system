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
if __name__=="__main__":unittest.main()
