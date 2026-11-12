"""Focused G1 installer control tests. No Azure, tokens, Blob or SQL executed."""
import datetime
import hashlib
import inspect
import json
from pathlib import Path
import re
import tempfile
import unittest
from unittest import mock

import g1_blob_call_journal_REVIEW_ONLY as ledger
import p3_blob_guard as blob
import p3_app_installer_runner as child
import p3_kudu_cloudshell_parent as parent
import p3_kudu_transport as stage
import p3_g1_pinned_ssh as ssh
import p3_g1_blob_transport as transport
import p3_g1_owner_management as owner
import p3_install_gates as original

HERE=Path(__file__).parent
OP='a'*32
SHA='b'*64
INST='instance-0123456789'

class Tests(unittest.TestCase):
    def test_original_files_remain_immutable(self):
        self.assertTrue(original.validate_local_source(HERE))
        self.assertEqual(original.digest((HERE/'frozen_install.py.snapshot').read_bytes()),original.INSTALLER_SHA)
        self.assertEqual(original.digest((HERE/'preflight.sql').read_bytes()),original.SELECT_SHA)
        self.assertEqual(len(original.LAYER_NAMES),14)
        self.assertEqual(len(original.PAYLOAD_SHA256),16)

    def test_parent_child_exact_binding(self):
        b,h=parent.validate_binding(HERE)
        self.assertTrue(child.verify_bundle(b))
        self.assertEqual(h,hashlib.sha256((HERE/'binding-index.json').read_bytes()).hexdigest())
        self.assertEqual(len(b['files']),21)
        for n in ('g1_blob_call_journal_REVIEW_ONLY.py','p3_g1_pinned_ssh.py',
                  'p3_g1_blob_transport.py','token_adapter_g1_REVIEW_ONLY.py'):
            self.assertIn(n,b['files'])

    def test_new_contract_no_consumed_parent_or_claim(self):
        self.assertIn('G1',parent.CONTRACT)
        self.assertIn('G1',child.EXPECTED_APPROVAL)
        self.assertIn('g1-original-installer-single',blob.NEW_BLOB)
        self.assertNotEqual(blob.NEW_BLOB,blob.PRIOR_V3_INSTALL_BLOB)
        self.assertNotEqual(blob.NEW_BLOB,blob.PRIOR_V2_INSTALL_BLOB)
        self.assertNotEqual(blob.NEW_BLOB,blob.PRIOR_G1_INSTALL_BLOB)
        self.assertEqual(parent.TRANSPORT,'OFFICIAL_AZ_WEBAPP_EXEC_APP_KUDU_V1')
        self.assertEqual(parent.REMOTE_STAGE,stage.REMOTE_STAGE)

    def test_only_pinned_instance_tunnel(self):
        argv=ssh.ssh_tunnel_args(INST,57311,'12345678-1234-1234-1234-123456789012')
        self.assertEqual(argv[0:3],['az','webapp','create-remote-connection'])
        self.assertEqual(argv[argv.index('--instance')+1],INST)
        self.assertEqual(argv[argv.index('--port')+1],'57311')
        self.assertNotIn('password',argv)
        self.assertNotIn('--timeout',argv)
        self.assertEqual(argv[argv.index('--subscription')+1],'12345678-1234-1234-1234-123456789012')
        with self.assertRaises(stage.TransportHold):ssh.ssh_tunnel_args('../abc',57311,'12345678-1234-1234-1234-123456789012')

    def test_exact_instance_kudu_interface_and_parent_child_contract(self):
        for name in ('start','await_cli_shell_banner','establish_safe_channel',
                     'stage_archive','send_secret_line','expect_fixed','mark_sql_started',
                     'mark_complete','close'):
            self.assertTrue(callable(getattr(stage.KuduExecSession,name)))
        src=(HERE/'p3_kudu_cloudshell_parent.py').read_text()
        child_src=(HERE/'p3_app_installer_runner.py').read_text()
        self.assertIn('return KuduExecSession(instance, deadline=deadline)',src)
        self.assertNotIn('return SshExecSession(instance, deadline=deadline)',src)
        self.assertEqual(parent.CONTRACT, 'FIELDVISIT_PATHB_G1_KUDU_INSTALL_V1')
        self.assertIn('exec_transport',child_src)
        self.assertIn('PROTECTED_OWNER_KUDU',blob.REQUIRED_PRE_DDL_GATES)
        self.assertNotIn('PROTECTED_OWNER_SSH',blob.REQUIRED_PRE_DDL_GATES)

    def test_no_hardcoded_ssh_password_in_deliverable(self):
        src='\n'.join((HERE/n).read_text() for n in json.loads((HERE/'binding-index.json').read_text())['files']
                      if n.endswith('.py'))
        self.assertNotIn('Docker!',src)
        self.assertNotIn('root@127.0.0.1\'s password:',src)

    def test_g1_owner_ssh_authorization_distinct_from_consumed(self):
        now=datetime.datetime.now(datetime.timezone.utc)
        auth={
          'contract':parent.CONTRACT,'decision':parent.DECISION,
          'artifact_sha256':SHA,'binding_index_sha256':SHA,
          'server':parent.SERVER,'database':parent.DATABASE,
          'installation_attempts':1,'instance_sha256':parent.ident(INST),
          'operation_id':OP,
          'not_before':(now-datetime.timedelta(seconds=3)).isoformat(timespec='seconds').replace('+00:00','Z'),
          'expires_at':(now+datetime.timedelta(seconds=80)).isoformat(timespec='seconds').replace('+00:00','Z'),
        }
        with tempfile.TemporaryDirectory() as d:
            f=Path(d)/'owner.json';f.write_text(json.dumps(auth))
            self.assertEqual(parent.validate_authorization(f,SHA,SHA,INST)['operation_id'],OP)
            self.assertTrue(child.verify_authorization(auth,SHA,SHA,parent.ident(INST)))
            auth['decision']='OWNER_APPROVED_PATHB_P3_FINAL_EXECUTOR_EXACT_SHA_ONE_TIME'
            f.write_text(json.dumps(auth))
            with self.assertRaises(parent.Hold):parent.validate_authorization(f,SHA,SHA,INST)

    def test_atomic_predecessor_must_be_exact_BlobNotFound(self):
        orig=blob.validate_owner_storage_token
        blob.validate_owner_storage_token=lambda *_args,**_kw:True
        try:
            calls=[]
            def request(method,key,headers,body=b''):
                calls.append((method,key))
                return blob.Response(404,'',b'','BlobNotFound')
            guard=blob.OneTimeInstallBlobGuard('dummy-owner-token',request=request)
            guard.predecessors_verified=True
            guard.check_available()
            self.assertTrue(guard.new_key_available)
            self.assertEqual([c[1] for c in calls],
                             [blob.PRIOR_INSTALL_BLOB,blob.PRIOR_V2_INSTALL_BLOB,
                              blob.PRIOR_V3_INSTALL_BLOB,blob.PRIOR_G1_INSTALL_BLOB,blob.NEW_BLOB])
            guard2=blob.OneTimeInstallBlobGuard('dummy-owner-token',request=lambda *args:blob.Response(404))
            guard2.predecessors_verified=True
            with self.assertRaises(blob.InstallGuardHold):guard2.check_available()
        finally:blob.validate_owner_storage_token=orig

    def test_prepare_before_claim_and_exact_readback(self):
        with mock.patch.object(blob,'validate_owner_storage_token',return_value=True):
            recorded={}
            def request(method,key,headers,body=b''):
                if method=='PUT':
                    recorded['payload']=body
                    self.assertEqual(headers['If-None-Match'],'*')
                    return blob.Response(201,'"one"',b'')
                if method=='GET':return blob.Response(200,'"one"',recorded['payload'])
                return blob.Response(404,'',b'','BlobNotFound')
            guard=blob.OneTimeInstallBlobGuard('owner',request=request)
            guard.predecessors_verified=True
            guard.new_key_available=True
            gates={n:'PASS' for n in blob.REQUIRED_PRE_DDL_GATES}
            digest=guard.prepare(gates,operation_id=OP,artifact_sha256=SHA,instance_sha256=SHA)
            self.assertFalse(guard.creation_attempted)
            self.assertEqual(guard.consume(gates),digest)
            self.assertTrue(guard.consumed)
            with self.assertRaises(blob.InstallGuardHold):guard.consume(gates)

    def test_fail_closed_external_receipt_contract(self):
        r=ledger.construct(OP,SHA,0,'BEFORE',SHA)
        self.assertEqual(r.key,ledger.PREFIX+OP+'/00-before.json')
        self.assertIn('postcheck.json',ledger.construct_postcheck(OP,SHA,SHA).key)
        self.assertIn('schema_fingerprint_sha256',ledger.construct_postcheck(OP,SHA,SHA).payload.decode())
        self.assertIn('g1_storage_token_file',(HERE/'p3_app_installer_runner.py').read_text())
        source=(HERE/'p3_app_installer_runner.py').read_text()
        self.assertLess(source.index('guard.prepare(gates,'),source.index('guard.consume(gates)'))
        self.assertLess(source.index('guard.consume(gates)'),source.index("str(package_root/'install.py'),'--root'", source.index("guard.consume(gates)")))
        self.assertLess(source.index('parse_postcheck(post)'),source.index('construct_postcheck('))
        self.assertLess(source.index('construct_postcheck('),source.index("result.update(status='PASS'"))

    def test_no_live_on_import_or_unapproved_default(self):
        # The G1 parent requires --live plus exact fresh approval; no automatic execution.
        self.assertIn('if not args.live:',(HERE/'p3_kudu_cloudshell_parent.py').read_text())
        self.assertIn("config.get('g1_live_approval')",(HERE/'token_adapter_g1_REVIEW_ONLY.py').read_text())
        self.assertEqual(ledger.main(),1)

    def test_dual_PTY_echo_and_canonical_off_before_token(self):
        src=(HERE/'p3_g1_pinned_ssh.py').read_text()
        self.assertIn('termios.ECHO|termios.ICANON',src)
        self.assertIn('stty -echo -icanon min 1 time 0',src)
        self.assertIn('ssh-keyscan',src)
        self.assertIn('pinned_host_fingerprint',src)

    def test_prepare_all_files_before_atomic_marker_consumption(self):
        src=(HERE/'p3_app_installer_runner.py').read_text()
        self.assertLess(src.index("private_write(storage_token_file"),src.index('guard.consume(gates)'))
        self.assertLess(src.index('private_write(acfg,'),src.index('guard.consume(gates)'))
        self.assertLess(src.index('guard.prepare(gates,'),src.index("private_write(acfg,"))
        self.assertLess(src.index('guard.consume(gates)'),src.index('proc=run_installer_managed([sys.executable'))

    def test_exact_storage_transcript_no_duplicate_or_update(self):
        src=(HERE/'p3_g1_blob_transport.py').read_text()
        self.assertIn("'If-None-Match':'*'",src)
        self.assertIn("method not in ('PUT','GET')",src)
        self.assertNotIn("method not in ('PUT','GET','DELETE')",src)
        self.assertIn('CONTAINER',src)
        self.assertIn('BLOB_ACCOUNT',src)

    def test_storage_token_security_checks_survive_python_optimization(self):
        source=inspect.getsource(transport._validate_token)
        self.assertNotIn('assert ',source)
        for field in ('OWNER_OID_SHA','TENANT_SHA','storage.azure.com','nbf','exp'):
            self.assertIn(field,source)

    def test_ledger_final_sql_pass_not_equal_business_uat(self):
        r=ledger.construct_postcheck(OP,SHA,SHA)
        self.assertIn(b'"status":"SCHEMA_VERIFIED"',r.payload)
        self.assertNotIn(b'Business_UAT_PASS',r.payload)
        self.assertNotIn(b'IT_HANDOVER_PASS',r.payload)

    def test_archive_parent_excludes_consumed_static_password_dependency(self):
        binding=json.loads((HERE/'binding-index.json').read_text())
        self.assertNotIn('p3_cloudshell_parent.py',binding['files'])
        self.assertIn('p3_g1_owner_management.py',binding['files'])
        self.assertIn('from p3_g1_owner_management import az',
                       (HERE/'p3_kudu_cloudshell_parent.py').read_text())

    def test_no_unnecessary_new_Azure_resources(self):
        src=(HERE/'p3_g1_owner_management.py').read_text()
        for term in ["['az','group','create'", "['az','webapp','create'", "['az','sql','server','create'",'az vm create']:
            self.assertNotIn(term,src)
        self.assertEqual(ledger.BLOB_ACCOUNT,'stfvsc261026a')

if __name__=='__main__':unittest.main()
