import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import unittest
from unittest import mock

spec=importlib.util.spec_from_file_location('handoff', Path(__file__).with_name('g1_exact_parent_handoff.py'))
g=importlib.util.module_from_spec(spec);spec.loader.exec_module(g)
SOURCE=Path(__file__).with_name('FieldVisit_PathB_G1_Consolidated_SQLCMD_ArgFix_SafetyFix_NONDEPLOY_20261110.zip')

class HandoffTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory()
  self.addCleanup(self.tmp.cleanup)
  self.base=Path(self.tmp.name)
  self.zip=self.base/'candidate.zip';shutil.copyfile(SOURCE,self.zip)
  self.auth=self.base/'auth.json';self.claim=self.base/'claims';self.claim.mkdir(mode=0o700)
  self.authdata={'operation_id':'a'*32,'contract':g.EXPECTED_AUTH_CONTRACT,'decision':g.EXPECTED_DECISION,'artifact_sha256':g.EXPECTED_ARCHIVE,'binding_index_sha256':g.EXPECTED_BINDING,'server':g.EXPECTED_SERVER,'database':g.EXPECTED_DATABASE,'installation_attempts':1,'instance_sha256':'b'*64,'not_before':'2026-11-10T00:00:00Z','expires_at':'2026-11-10T00:29:00Z'}
  self.write_auth()
 def write_auth(self):
  self.auth.write_text(json.dumps(self.authdata));self.auth.chmod(0o600)
 def args(self,live=False):
  return type('Args',(),dict(candidate=str(self.zip),live=live,authorization=str(self.auth),claim_dir=str(self.claim),owner_confirm='EXACT_G1_ONE_SHOT'))()
 def test_01_candidate_pass(self):self.assertGreater(g.inspect_candidate(self.zip),25)
 def test_02_candidate_tamper_rejected(self):
  with self.zip.open('ab') as f:f.write(b'X')
  with self.assertRaises(g.GateHold):g.inspect_candidate(self.zip)
 def test_03_auth_match(self):self.assertEqual(g.validate_auth_file(self.auth),'a'*32)
 def test_04_auth_mode_rejected(self):
  self.auth.chmod(0o644)
  with self.assertRaises(g.GateHold):g.validate_auth_file(self.auth)
 def test_05_auth_symlink_rejected(self):
  link=self.base/'link';link.symlink_to(self.auth)
  with self.assertRaises(g.GateHold):g.validate_auth_file(link)
 def test_06_auth_target_rejected(self):
  self.authdata['database']='prod';self.write_auth()
  with self.assertRaises(g.GateHold):g.validate_auth_file(self.auth)
 def test_07_auth_not_automatically_approved(self):
  self.authdata['decision']='NOT_APPROVED';self.write_auth()
  with self.assertRaises(g.GateHold):g.validate_auth_file(self.auth)
 def test_08_claim_one_shot(self):
  g.consume_claim(self.claim,'a'*32)
  with self.assertRaises(g.GateHold):g.consume_claim(self.claim,'a'*32)
 def test_09_claim_dir_mode_rejected(self):
  self.claim.chmod(0o755)
  with self.assertRaises(g.GateHold):g.consume_claim(self.claim,'a'*32)
 def test_10_real_parent_staged_exact(self):
  with tempfile.TemporaryDirectory() as t:
   p=g.stage(Path(t),self.zip)
   self.assertEqual(g.sha(p.read_bytes()),json.loads((Path(t)/'binding-index.json').read_text())['files']['p3_kudu_cloudshell_parent.py'])
 def test_11_check_doesnt_launch(self):
  with mock.patch.object(g.subprocess,'call',side_effect=AssertionError('launched')):
   self.assertEqual(g.run(self.args(False)),0)
  self.assertFalse(list(self.claim.iterdir()))
 def test_12_live_invokes_parent_only_after_claim(self):
  def intercepted(command,**kw):
   self.assertIn('--live',command)
   self.assertIn('--authorization',command)
   self.assertTrue((self.claim/('g1-'+'a'*32+'.consumed')).exists())
   self.assertTrue(Path(command[1]).is_file())
   return 0
  with mock.patch.object(g,'preclaim_parent_gates',return_value=True), mock.patch.object(g.subprocess,'call',side_effect=intercepted) as m:
   self.assertEqual(g.run(self.args(True)),0)
   self.assertEqual(m.call_count,1)
 def test_13_failed_parent_no_retry(self):
  with mock.patch.object(g,'preclaim_parent_gates',return_value=True), mock.patch.object(g.subprocess,'call',return_value=1) as m:
   self.assertEqual(g.run(self.args(True)),1)
   with self.assertRaises(g.GateHold):g.run(self.args(True))
   self.assertEqual(m.call_count,1)
 def test_14_auth_missing_never_launch(self):
  self.auth.unlink()
  with mock.patch.object(g.subprocess,'call',side_effect=AssertionError('launched')):
   with self.assertRaises(FileNotFoundError):g.run(self.args(True))
 def test_15_missing_candidate_never_launch(self):
  self.zip.unlink()
  with mock.patch.object(g.subprocess,'call',side_effect=AssertionError('launched')):
   with self.assertRaises(FileNotFoundError):g.run(self.args(True))
 def test_16_claim_symlink_dir_rejected(self):
  link=self.base/'claimlink';link.symlink_to(self.claim,target_is_directory=True)
  with self.assertRaises(g.GateHold):g.consume_claim(link,'a'*32)
 def test_17_no_self_minting_approval(self):
  source=Path(g.__file__).read_text()
  self.assertNotIn('os.O_CREAT|os.O_EXCL,0o600)\nwith os.fdopen',source)
  self.assertNotIn('secrets.token_hex',source)
 def test_18_explicit_live_gate(self):
  args=self.args(True);args.owner_confirm='NO'
  with mock.patch.object(g.subprocess,'call',side_effect=AssertionError('launched')):
   with self.assertRaises(g.GateHold):g.run(args)

 def test_19_failed_preclaim_no_claim_no_launch(self):
  with mock.patch.object(g,'preclaim_parent_gates',side_effect=g.GateHold('PRECLAIM_HOLD')), mock.patch.object(g.subprocess,'call',side_effect=AssertionError('launched')):
   with self.assertRaises(g.GateHold):g.run(self.args(True))
  self.assertEqual(list(self.claim.iterdir()),[])
 def test_20_preclaim_order_before_consumption(self):
  calls=[]
  def precheck(*a):
   calls.append('precheck')
   self.assertFalse(list(self.claim.iterdir()))
   return True
  def child(*a,**kw):
   calls.append('child')
   self.assertTrue(list(self.claim.iterdir()))
   return 0
  with mock.patch.object(g,'preclaim_parent_gates',side_effect=precheck),mock.patch.object(g.subprocess,'call',side_effect=child):
   self.assertEqual(g.run(self.args(True)),0)
  self.assertEqual(calls,['precheck','child'])
 def test_21_preclaim_checks_original_source_entrypoints(self):
  import zipfile
  with tempfile.TemporaryDirectory() as d:
   root=Path(d);g.stage(root,self.zip)
   with zipfile.ZipFile(self.zip) as z:
    parent=z.read('p3_kudu_cloudshell_parent.py').decode()
   self.assertIn('def management(cfg):',parent)
   self.assertIn('def validate_authorization(',parent)
   self.assertIn('from p3_install_gates import digest, validate_local_source',parent)
 def test_22_failed_parent_still_consumed(self):
  with mock.patch.object(g,'preclaim_parent_gates',return_value=True),mock.patch.object(g.subprocess,'call',return_value=1):
   self.assertEqual(g.run(self.args(True)),1)
  self.assertEqual(len(list(self.claim.iterdir())),1)
 def test_24_actual_parent_preclaim_failure_keeps_claim_unconsumed(self):
  # Exercise real pinned Parent import and validation, but prevent any Azure CLI
  # operation by mocking the Parent's management helper after module import.
  import importlib.util
  with tempfile.TemporaryDirectory() as d:
   root=Path(d);g.stage(root,self.zip)
   # The actual Parent rejects a mismatched authorization binding before a
   # caller can create any claim; this verifies the fail-closed local path.
   self.authdata['instance_sha256']='broken';self.write_auth()
   try:
    with mock.patch('subprocess.run',side_effect=RuntimeError('AZURE_NETWORK_FORBIDDEN')):
     with self.assertRaises(g.GateHold):g.preclaim_parent_gates(root,self.auth,self.zip)
   finally:
    self.assertFalse(list(self.claim.iterdir()))
 def test_23_preclaim_never_calls_sql_ssh_or_blob_by_own_code(self):
  import inspect
  src=inspect.getsource(g.preclaim_parent_gates)
  self.assertIn('module.management(cfg)',src)
  self.assertIn('module.validate_authorization(',src)
  self.assertNotIn('execute(',src)
  self.assertNotIn('blob_download(',src)

if __name__=='__main__':unittest.main(verbosity=2)
