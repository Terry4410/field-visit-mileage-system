from pathlib import Path
import importlib.util
import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
ROOT=Path(__file__).resolve().parents[3]
def load(path,name):
 s=importlib.util.spec_from_file_location(name,ROOT/path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
c=load('scripts/ci/compare_uat010_schema_inventory.py','comparator')
f=load('scripts/ci/test_uat_schema_fidelity_proposal.py','fixture')
def fixture():
 a=f.sample_inventory();a['SqlSha256']=c.SQL_SHA256
 a['Target']['EvidenceMode']='METADATA_ONLY_NO_ROW_VALUES'
 a['HistoricalAggregates']=[{'HistoricalTable':t,'RowCount':0,'HighWatermark':None} for t in sorted(c.HISTORICAL)]
 return a
class ComparatorContracts(unittest.TestCase):
 def strict(self,a,source='a'*40,content=None):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'private.json';raw=json.dumps(a).encode();p.write_bytes(raw)
   return c.strict_inventory(p,source,content or hashlib.sha256(raw).hexdigest())
 def test_valid_exact_contract(self):self.assertEqual(self.strict(fixture())['SourceSha'],'a'*40)
 def test_exact_facets(self):
  a=fixture();self.assertEqual(c.assert_010_contract(a)+c.compare(a,copy.deepcopy(a)),[])
 def test_literal_whitespace_preserved(self):self.assertNotEqual(c.canonical("DEFAULT N'a  b'"),c.canonical("DEFAULT N'a b'"))
 def test_literal_whitespace_drift(self):
  a=fixture();b=copy.deepcopy(a);a['Columns'][0]['DefaultDefinition']="N'a  b'";b['Columns'][0]['DefaultDefinition']="N'a b'";self.assertTrue(c.compare(a,b))
 def test_facet_drift(self):
  a=fixture();b=copy.deepcopy(a);b['Columns'][0]['IsNullable']=True;self.assertTrue(c.compare(a,b))
 def test_every_section_missing_or_altered(self):
  for section in c.SECTIONS:
   a=fixture();b=copy.deepcopy(a);b[section]=[]
   with self.subTest(section=section,kind='missing'):
    with self.assertRaises(ValueError):self.strict(b)
   b=copy.deepcopy(a)
   key=next(k for k in b[section][0] if k not in c.SECTIONS[section])
   v=b[section][0][key];b[section][0][key]=not v if type(v) is bool else 'SYNTHETIC_DIFFERENCE'
   with self.subTest(section=section,kind='altered'):self.assertTrue(c.compare(a,b))
 def test_wrong_source(self):
  with self.assertRaises(ValueError):self.strict(fixture(),source='b'*40)
 def test_malformed_source(self):
  with self.assertRaises(ValueError):self.strict(fixture(),source='bad')
 def test_wrong_content_hash(self):
  with self.assertRaises(ValueError):self.strict(fixture(),content='0'*64)
 def test_wrong_sql_hash(self):
  a=fixture();a['SqlSha256']='0'*64
  with self.assertRaises(ValueError):self.strict(a)
 def test_missing_section(self):
  a=fixture();del a['Checks']
  with self.assertRaises(ValueError):self.strict(a)
 def test_missing_facet(self):
  a=fixture();del a['Columns'][0]['IsComputed']
  with self.assertRaises(ValueError):self.strict(a)
 def test_unknown_facet(self):
  a=fixture();a['Columns'][0]['Unknown']='SYNTHETIC_SENSITIVE'
  with self.assertRaises(ValueError):self.strict(a)
 def test_invalid_type(self):
  a=fixture();a['Columns'][0]['IsNullable']='false'
  with self.assertRaises(ValueError):self.strict(a)
 def test_duplicate_comparison_key(self):
  a=fixture();a['Columns']*=2
  with self.assertRaises(ValueError):self.strict(a)
 def test_duplicate_json_field(self):
  with self.assertRaises(ValueError):json.loads('{"a":1,"a":2}',object_pairs_hook=c.unique_object)
 def test_wrong_target(self):
  a=fixture();a['Target']['DatabaseName']='wrong'
  with self.assertRaises(ValueError):self.strict(a)
 def test_bad_historical_shape(self):
  a=fixture();a['HistoricalAggregates']=[]
  with self.assertRaises(ValueError):self.strict(a)
 def test_bad_timestamp(self):
  a=fixture();a['CapturedUtc']='2026-10-19T00:00:00'
  with self.assertRaises(ValueError):self.strict(a)
 def test_missing_version(self):
  a=fixture();a['SchemaVersion']['Latest']='1.8.0-009'
  with self.assertRaises(ValueError):self.strict(a)
 def cli(self,expected,actual,extra=()):
  return subprocess.run([sys.executable,str(ROOT/'scripts/ci/compare_uat010_schema_inventory.py'),'--expected',str(expected),'--actual',str(actual),'--expected-source-sha','a'*40,'--actual-source-sha','a'*40,'--expected-sha256',hashlib.sha256(expected.read_bytes()).hexdigest() if expected.exists() else '0'*64,'--actual-sha256',hashlib.sha256(actual.read_bytes()).hexdigest() if actual.exists() else '0'*64,*extra],capture_output=True,text=True)
 def test_cli_match(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'private.json';p.write_text(json.dumps(fixture()));r=self.cli(p,p)
   self.assertEqual((r.returncode,r.stdout,r.stderr),(0,'SCHEMA_FIDELITY_COMPARISON_MATCH\n',''))
 def test_cli_missing_reference_private(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'SYNTHETIC_SENSITIVE';r=self.cli(p,p)
   self.assertEqual((r.returncode,r.stdout,r.stderr),(2,'SCHEMA_FIDELITY_COMPARISON_HOLD\n',''))
 def test_cli_malformed_private(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'private.json';p.write_text('SYNTHETIC_SENSITIVE');r=self.cli(p,p)
   self.assertEqual((r.returncode,r.stdout,r.stderr),(2,'SCHEMA_FIDELITY_COMPARISON_HOLD\n',''))
 def test_cli_difference_private(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'expected.json';q=Path(d)/'actual.json';a=fixture();b=copy.deepcopy(a);b['Columns'][0]['DefaultDefinition']="N'SYNTHETIC_SENSITIVE'";p.write_text(json.dumps(a));q.write_text(json.dumps(b));r=self.cli(p,q)
   self.assertEqual((r.returncode,r.stdout,r.stderr),(1,'SCHEMA_FIDELITY_COMPARISON_HOLD\n',''))
 def test_cli_unknown_arg_private(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'private.json';p.write_text(json.dumps(fixture()));r=self.cli(p,p,('--SYNTHETIC_SENSITIVE',))
   self.assertEqual((r.returncode,r.stdout,r.stderr),(2,'SCHEMA_FIDELITY_COMPARISON_HOLD\n',''))
if __name__=='__main__':unittest.main()
