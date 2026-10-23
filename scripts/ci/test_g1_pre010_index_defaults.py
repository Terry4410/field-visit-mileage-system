#!/usr/bin/env python3
"""Synthetic negative cases never establish UAT deployment or Schema GO."""
import copy
import importlib.util
import unittest
from pathlib import Path
P=Path(__file__).with_name("g1_pre010_private_compare.py")
sp=importlib.util.spec_from_file_location("g1private",P)
v=importlib.util.module_from_spec(sp);sp.loader.exec_module(v)
class IndexDefaultTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.contract=v.index_module.derive(Path("candidate"))
    def indices(self):return [dict(x) for x in self.contract["Indexes"]]
    def defaults(self):return [dict(x,Definition=x["DefinitionNorm"]) for x in self.contract["Defaults"]]
    def test_source_scope(self):
        idx=self.indices()
        self.assertEqual((len(idx),len(self.defaults())),(12,2))
        self.assertEqual(sum(x["IsPrimaryKey"] for x in idx),4)
        self.assertEqual(sum(x["IsUniqueConstraint"] for x in idx),3)
        self.assertEqual(sum(bool(x["IncludeColumns"]) for x in idx),2)
        f=next(x for x in idx if x["IndexName"]=="IX_RouteCalculationAttempts_BasisSnapshot")
        self.assertEqual(f["FilterDefinition"],"BASISVISITTRIPSNAPSHOTIDISNOTNULL")
        self.assertTrue(f["HasFilter"])
    def test_positive_fixture_only(self):
        v.check_index_defaults(self.indices(),self.defaults(),self.contract)
    def test_missing_duplicate_extra(self):
        for base in (self.indices(),self.defaults()):
            for fn in (lambda x:x.pop(),lambda x:x.append(dict(x[0])),
                       lambda x:x.append(dict(x[0],IndexName="EXTRA",DefaultName="EXTRA"))):
                x=copy.deepcopy(base);fn(x)
                with self.assertRaises(v.Stop):
                    if "IndexName" in base[0]:v.check_index_defaults(x,self.defaults(),self.contract)
                    else:v.check_index_defaults(self.indices(),x,self.contract)
    def test_key_include_filter_uniqueness_and_state(self):
        for k,value in (("KeyColumns","Different:ASC"),("IncludeColumns","Other"),
                        ("HasFilter",True),("IsUnique",True),("IsDisabled",True)):
            x=self.indices();x[0][k]=value
            with self.assertRaises(v.Stop):v.check_index_defaults(x,self.defaults(),self.contract)
    def test_default_expression_normalization(self):
        x=self.defaults();x[0]["Definition"]="(sysutcdatetime())"
        v.check_index_defaults(self.indices(),x,self.contract)
        x[0]["Definition"]="(GETDATE())"
        with self.assertRaises(v.Stop):v.check_index_defaults(self.indices(),x,self.contract)
if __name__=="__main__":unittest.main()
