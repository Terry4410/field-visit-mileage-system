import copy,importlib.util,json,tempfile,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('manifest',Path(__file__).with_name('leader_rc_manifest.py'));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class PreflightTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name);self.source='a'*40
  self.e=json.loads(Path('docs/release/leader-uat010-rc/evidence/existing-uat-observations.json').read_text());self.g={'deployment':'HOLD','owner':'NOT_AUTHORIZED'}
  for kind in ['api','frontend']:
   p=self.root/'artifacts'/kind;p.mkdir(parents=True);(p/'source-sha.txt').write_text(self.source)
  p=self.root/'docs/release/leader-uat010-rc/evidence';p.mkdir(parents=True);(p/'existing-uat-observations.json').write_text('{}');(p.parent/'release-gates.json').write_text('{}')
 def tearDown(self):self.tmp.cleanup()
 def build(self,source=None,ref=None,head=None,e=None,g=None):return m.build_manifest(self.root,source or self.source,ref or m.BRANCH,head or self.source,e or self.e,g or self.g)
 def test_exact_manifest_keeps_history_distinct_from_active_source(self):
  d=self.build();self.assertEqual('UNVERIFIED',d['deployedApiSource']);self.assertEqual('NOT_AUTHORIZED',d['deployment']);self.assertEqual('SOURCE_CONTRACT_RESTORED_RUNTIME_EVIDENCE_REQUIRED',d['existingVisitorCompatibility'])
 def test_source_branch_and_stamp_mismatch_are_denied(self):
  for args in [{'head':'b'*40},{'ref':'refs/heads/main'},{'source':'not-sha'}]:
   with self.assertRaises(ValueError):self.build(**args)
  (self.root/'artifacts/api/source-sha.txt').write_text('b'*40)
  with self.assertRaises(ValueError):self.build()
 def test_gate_cannot_be_promoted_to_pass(self):
  with self.assertRaises(ValueError):self.build(g={'deployment':'PASS'})
 def test_unverified_active_source_or_archive_claim_is_denied(self):
  for change in ['active','api','pages']:
   e=copy.deepcopy(self.e)
   if change=='active':e['api']['activeSourceSha']='b'*40
   else:e['rollback'][change+'ArchiveVerified']=True
   with self.assertRaises(ValueError):self.build(e=e)
 def test_wrong_protected_pin_and_release_claim_are_denied(self):
  e=copy.deepcopy(self.e);e['protectedBase']='b'*40
  with self.assertRaises(ValueError):self.build(e=e)
  e=copy.deepcopy(self.e);e['decision']['release']='GO'
  with self.assertRaises(ValueError):self.build(e=e)
 def test_symlink_escape_is_denied(self):
  (self.root/'artifacts/api/escape').symlink_to('/etc/hosts')
  with self.assertRaises(ValueError):self.build()
if __name__=='__main__':unittest.main()
