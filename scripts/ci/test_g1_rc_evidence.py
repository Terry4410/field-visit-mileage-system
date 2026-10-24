import importlib.util,os,unittest
from pathlib import Path
from unittest.mock import patch
import xml.etree.ElementTree as ET
spec=importlib.util.spec_from_file_location('g1',Path(__file__).with_name('g1_rc_evidence.py'));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class G1EvidenceTests(unittest.TestCase):
 def test_parameter_payload_stdout_and_error_never_exported(self):
  xml=b'<TestRun><Results><UnitTestResult testName="Worker_sql_privacy(payload: SYNTHETIC_SECRET)" outcome="Passed" duration="00:00:01"><Output><StdOut>SELECT secret FROM Employees token=SYNTHETIC_SECRET</StdOut><ErrorInfo>employee@example.invalid</ErrorInfo></Output></UnitTestResult></Results><ResultSummary><Counters total="1" executed="1" passed="1" failed="0"/></ResultSummary></TestRun>'
  safe=m.sanitized_trx(xml)
  for token in [b'SYNTHETIC_SECRET',b'SELECT',b'employee@',b'ErrorInfo',b'StdOut']:self.assertNotIn(token,safe)
  r=ET.fromstring(safe);result=next(x for x in r.iter() if x.tag.endswith('UnitTestResult'));self.assertEqual(result.attrib['testName'],'Worker_sql_privacy');self.assertEqual(result.attrib['outcome'],'Passed')
  self.assertEqual(next(x for x in r.iter() if x.tag.endswith('Counters')).attrib['passed'],'1')
 def test_wrong_exact_source_fails_before_sql(self):
  with patch.dict(os.environ,{'GITHUB_SHA':'0'*40,'GITHUB_REF':m.BRANCH,'GITHUB_ACTIONS':'true'}):
   with self.assertRaises(AssertionError):m.sql()
 def test_wrong_branch_fails_before_sql(self):
  actual=m.git('rev-parse','HEAD').decode().strip()
  with patch.dict(os.environ,{'GITHUB_SHA':actual,'GITHUB_REF':'refs/heads/main','GITHUB_ACTIONS':'true'}):
   with self.assertRaises(AssertionError):m.sql()
if __name__=='__main__':unittest.main()
