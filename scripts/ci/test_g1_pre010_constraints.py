#!/usr/bin/env python3
import copy
import importlib.util
import unittest
from pathlib import Path

p=Path(__file__).with_name("g1_pre010_private_compare.py")
spec=importlib.util.spec_from_file_location("private",p)
private=importlib.util.module_from_spec(spec);spec.loader.exec_module(private)

class Pre010ConstraintProof(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.contract=private.constraint_module.derive(Path("candidate"))
        cls.fks=cls.contract["ForeignKeys"]
        cls.checks=cls.contract["Checks"]
    def test_pinned_constraints_and_reason_source(self):
        self.assertEqual(len(self.fks),17)
        self.assertEqual(len(self.checks),14)
        self.assertIn("CK_RouteCalculationAttempts_Reason",{r["CheckConstraintName"] for r in self.checks})
        self.assertEqual(self.contract["CheckDefinitionParity"],"UNKNOWN_NOT_CANONICAL_SQLSERVER_DDL")
    def test_positive_synthetic_only(self):
        private.check_constraints(self._fks(),self._checks(),self.contract)
    def _fks(self):
        return [dict(x,IsDisabled=False,IsNotTrusted=False) for x in self.fks]
    def _checks(self):
        return [dict(x,DefinitionVisible=True,IsDisabled=False,IsNotTrusted=False) for x in self.checks]
    def test_fk_drift_and_trust(self):
        for field,value,klass in [("ReferencedTable","Wrong","FK_DRIFT"),
                                  ("IsNotTrusted",True,"FK_STATE"),("IsDisabled",True,"FK_STATE")]:
            vals=self._fks();vals[0][field]=value
            with self.assertRaises(private.Stop) as e:private.check_constraints(vals,self._checks(),self.contract)
            self.assertEqual(e.exception.code,klass)
    def test_fk_missing_extra_and_duplicate(self):
        for fun in [lambda v:v.pop(),lambda v:v.append(dict(v[0])),
                    lambda v:v.append(dict(v[0],ForeignKeyName="Fake"))]:
            vals=self._fks();fun(vals)
            with self.assertRaises(private.Stop):private.check_constraints(vals,self._checks(),self.contract)
    def test_check_trust_and_visibility(self):
        for field,value,klass in [("DefinitionVisible",False,"CHECK_VISIBILITY"),
                                  ("IsNotTrusted",True,"CHECK_STATE"),
                                  ("IsDisabled",True,"CHECK_STATE")]:
            vals=self._checks();vals[0][field]=value
            with self.assertRaises(private.Stop) as e:private.check_constraints(self._fks(),vals,self.contract)
            self.assertEqual(e.exception.code,klass)
    def test_check_missing_extra_duplicate(self):
        for fun in [lambda v:v.pop(),lambda v:v.append(dict(v[0])),
                    lambda v:v.append(dict(v[0],CheckConstraintName="Fake"))]:
            vals=self._checks();fun(vals)
            with self.assertRaises(private.Stop):private.check_constraints(self._fks(),vals,self.contract)
    def test_not_semantic_check_body_parity(self):
        self.assertIn("UNKNOWN",self.contract["CheckDefinitionParity"])
if __name__=="__main__":unittest.main()
