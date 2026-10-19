from pathlib import Path
import importlib.util
import copy
import unittest
ROOT=Path(__file__).resolve().parents[3]
def load(path,name):
 s=importlib.util.spec_from_file_location(name,ROOT/path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
c=load('scripts/ci/compare_uat010_schema_inventory.py','comparator')
f=load('scripts/ci/test_uat_schema_fidelity_proposal.py','fixture')
class OriginalContracts(unittest.TestCase):
 def test_original_valid_facets(self):
  a=f.sample_inventory();self.assertEqual(c.assert_010_contract(a)+c.compare(a,copy.deepcopy(a)),[])
 def test_original_facet_drift(self):
  a=f.sample_inventory();b=copy.deepcopy(a);b['Columns'][0]['IsNullable']=True;self.assertTrue(c.compare(a,b))
 def test_characterize_unchecked_source_binding(self):
  a=f.sample_inventory();b=copy.deepcopy(a);b['SourceSha']='b'*40;self.assertEqual(c.compare(a,b),[])
 def test_characterize_literal_whitespace_collision(self):
  self.assertEqual(c.canonical("DEFAULT N'a  b'"),c.canonical("DEFAULT N'a b'"))
if __name__=='__main__':unittest.main()
