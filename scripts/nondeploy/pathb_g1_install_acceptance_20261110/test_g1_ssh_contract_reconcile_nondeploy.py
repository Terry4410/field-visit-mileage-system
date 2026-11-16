"""NONDEPLOY: G1 Kudu V2 contract and original SSH negative regression; zero Azure/SQL."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import p3_kudu_cloudshell_parent as parent
import p3_app_installer_runner as runner
import p3_blob_guard as guard
from p3_kudu_transport import TransportHold
from p3_g1_pinned_ssh import validate_tunnel_host_fingerprint

ROOT = Path(__file__).resolve().parent
OLD_KUDU_KEY = 'operator-once/pathb-g1-original-installer-single-kudu-20261112.json'

class SSHOnceContract(unittest.TestCase):
    def test_same_transport_and_new_auth(self):
        self.assertEqual(parent.TRANSPORT,'OFFICIAL_AZ_WEBAPP_KUDU_INTERACTIVE_V2')
        self.assertEqual(parent.CONTRACT,'FIELDVISIT_PATHB_G1_KUDU_INTERACTIVE_INSTALL_V2')
        self.assertEqual(parent.DECISION,runner.EXPECTED_APPROVAL)
        self.assertNotEqual(parent.DECISION,'OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE')
        self.assertIn('KUDU_INTERACTIVE_V2',parent.DECISION)

    def test_all_preddl_gates_ssh_only(self):
        evidence=dict(package_sha_verified=True,seal_and_layers_verified=True,
            protected_kudu_stage_verified=True,owner_sql_token_verified=True,
            private_dns_verified=True,fresh_sql_tls_verified=True,
            fresh_sql_identity_verified=True,fresh_empty_verified=True,
            installer_dry_run_verified=True,sql_call_count=1)
        kw=dict(sql_expires=100000,storage_expires=100000,deadline=100000,
                now_wall=90000,now_mono=90000)
        self.assertEqual(set(runner.verified_pre_ddl_gates(evidence,**kw)),
                         set(guard.REQUIRED_PRE_DDL_GATES))
        evidence['protected_kudu_stage_verified']=False
        with self.assertRaises(runner.Hold):
            runner.verified_pre_ddl_gates(evidence,**kw)

    def test_old_contract_and_decision_rejected(self):
        good=dict(operation_id='a'*32,contract=parent.CONTRACT,decision=parent.DECISION,
            artifact_sha256='b'*64,binding_index_sha256='c'*64,
            server=parent.SERVER,database=parent.DATABASE,installation_attempts=1,
            instance_sha256='d'*64,not_before='2026-11-16T00:00:00Z',
            expires_at='2026-11-16T00:30:00Z')
        now=1794788100
        with tempfile.TemporaryDirectory() as d:
            file=Path(d)/'auth.json'
            file.write_text(json.dumps(good))
            parent.validate_authorization(file,'b'*64,'c'*64,None,now=now)
            runner.verify_authorization(good,'b'*64,'c'*64,'d'*64,now=now)
            for bad in [dict(good,contract='FIELDVISIT_PATHB_G1_KUDU_INSTALL_V1'),
                        dict(good,decision='OWNER_APPROVED_PATHB_G1_KUDU_NEW_EXACT_ONCE'),
                        dict(good,decision='OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE'),
                        dict(good,decision='OWNER_APPROVED_PATHB_G1_SSH_NEW_EXACT_ONCE')]:
                file.write_text(json.dumps(bad))
                with self.assertRaises(parent.Hold):
                    parent.validate_authorization(file,'b'*64,'c'*64,None,now=now)
                with self.assertRaises(runner.Hold):
                    runner.verify_authorization(bad,'b'*64,'c'*64,'d'*64,now=now)

    def test_adapter_approval_gate_matches(self):
        raw=(ROOT/'token_adapter_g1_REVIEW_ONLY.py').read_text()
        self.assertIn("config.get('g1_live_approval') != '"+parent.DECISION+"'",raw)
        self.assertNotIn("!= 'OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE'",raw)

    def test_new_marker_distinct_old_kudu_protected(self):
        self.assertEqual(guard.PRIOR_G1_KUDU_BLOB,OLD_KUDU_KEY)
        self.assertNotEqual(guard.NEW_BLOB,guard.PRIOR_G1_KUDU_BLOB)
        self.assertNotEqual(guard.NEW_BLOB,guard.PRIOR_G1_SSH_BLOB)
        self.assertTrue(guard.NEW_BLOB.endswith('-kudu-interactive-v2-20261116.json'))
        self.assertIn('PROTECTED_OWNER_KUDU_INTERACTIVE',guard.REQUIRED_PRE_DDL_GATES)
        self.assertNotIn('PROTECTED_OWNER_SSH',guard.REQUIRED_PRE_DDL_GATES)

    def test_prior_kudu_present_or_unknown_stops_no_put(self):
        for status in [200,503]:
            seen=[]
            def request(method,name,headers,body=b''):
                seen.append((method,name))
                if name==OLD_KUDU_KEY:
                    return guard.Response(status,'etag',b'','ServerBusy')
                return guard.Response(404,'',b'','BlobNotFound')
            with patch.object(guard,'validate_owner_storage_token',return_value=True):
                g=guard.OneTimeInstallBlobGuard('NONSECRET_FAKE',request=request)
            g.predecessors_verified=True
            with self.assertRaises(guard.InstallGuardHold):g.check_available()
            self.assertNotIn(('HEAD',guard.NEW_BLOB),seen)
            self.assertFalse(any(method=='PUT' for method,_ in seen))

    def test_all_prior_absent_permits_only_availability(self):
        seen=[]
        def request(method,name,headers,body=b''):
            seen.append((method,name))
            return guard.Response(404,'',b'','BlobNotFound')
        with patch.object(guard,'validate_owner_storage_token',return_value=True):
            g=guard.OneTimeInstallBlobGuard('NONSECRET_FAKE',request=request)
        g.predecessors_verified=True
        g.check_available()
        self.assertTrue(g.new_key_available)
        self.assertEqual(seen[-3:],[('HEAD',OLD_KUDU_KEY),('HEAD',guard.PRIOR_G1_SSH_BLOB),('HEAD',guard.NEW_BLOB)])
        self.assertFalse(any(method=='PUT' for method,_ in seen))

    def test_unverified_ssh_host_key_rejected(self):
        with self.assertRaises(TransportHold):
            validate_tunnel_host_fingerprint(b'2048 SHA256:UNTRUSTED test (ED25519)\n')

    def test_frozen_installer_sha(self):
        self.assertEqual(hashlib.sha256((ROOT/'frozen_install.py.snapshot').read_bytes()).hexdigest(),
            'b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f')

if __name__=='__main__':
    unittest.main()
