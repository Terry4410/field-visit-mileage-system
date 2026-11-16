"""NONDEPLOY G1 Kudu V2: exact target, dynamic handshake, old-grant denial."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import p3_kudu_cloudshell_parent as parent
import p3_app_installer_runner as runner
import p3_blob_guard as guard
import p3_kudu_transport as transport
from p3_kudu_transport import KuduExecSession, TransportHold

ROOT = Path(__file__).resolve().parent
NONCE = 'ab' * 16

class KuduV2OnlyTests(unittest.TestCase):
    def test_contract(self):
        self.assertEqual(parent.TRANSPORT, 'OFFICIAL_AZ_WEBAPP_KUDU_INTERACTIVE_V2')
        self.assertEqual(parent.CONTRACT, 'FIELDVISIT_PATHB_G1_KUDU_INTERACTIVE_INSTALL_V2')
        self.assertEqual(parent.DECISION, runner.EXPECTED_APPROVAL)
        self.assertNotEqual(parent.DECISION,'OWNER_APPROVED_PATHB_G1_SSH_NEW_EXACT_ONCE')
        source=(ROOT/'p3_kudu_cloudshell_parent.py').read_text()
        self.assertIn('return KuduExecSession(instance, deadline=deadline)',source)
        self.assertNotIn('return SshExecSession',source)

    def test_exact_main_container_only(self):
        args=transport.interactive_exec_args('a'*20)
        self.assertEqual(args[:3],['az','webapp','exec'])
        self.assertEqual(args[args.index('--mode')+1],'shell')
        self.assertEqual(args[args.index('--target')+1],'app')
        self.assertEqual(args[args.index('--instance')+1],'a'*20)
        self.assertNotIn('execute',args)
        self.assertNotIn('--command',args)

    def test_preddl_requires_new_transport_gate(self):
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
        with self.assertRaises(runner.Hold):runner.verified_pre_ddl_gates(evidence,**kw)
        del evidence['protected_kudu_stage_verified']
        evidence['protected_ssh_stage_verified']=True
        with self.assertRaises(runner.Hold):runner.verified_pre_ddl_gates(evidence,**kw)

    def test_old_auth_denied(self):
        good=dict(operation_id='a'*32,contract=parent.CONTRACT,decision=parent.DECISION,
            artifact_sha256='b'*64,binding_index_sha256='c'*64,
            server=parent.SERVER,database=parent.DATABASE,installation_attempts=1,
            instance_sha256='d'*64,not_before='2026-11-16T00:00:00Z',
            expires_at='2026-11-16T00:30:00Z')
        now=1794788100
        with tempfile.TemporaryDirectory() as d:
            f=Path(d)/'auth.json'
            f.write_text(json.dumps(good))
            parent.validate_authorization(f,'b'*64,'c'*64,None,now=now)
            runner.verify_authorization(good,'b'*64,'c'*64,'d'*64,now=now)
            for bad in (dict(good,contract='FIELDVISIT_PATHB_G1_SSH_INSTALL_V1'),
                        dict(good,contract='FIELDVISIT_PATHB_G1_KUDU_INSTALL_V1'),
                        dict(good,decision='OWNER_APPROVED_PATHB_G1_SSH_NEW_EXACT_ONCE'),
                        dict(good,decision='OWNER_APPROVED_PATHB_G1_KUDU_NEW_EXACT_ONCE')):
                f.write_text(json.dumps(bad))
                with self.assertRaises(parent.Hold):parent.validate_authorization(f,'b'*64,'c'*64,None,now=now)
                with self.assertRaises(runner.Hold):runner.verify_authorization(bad,'b'*64,'c'*64,'d'*64,now=now)

    def test_firewall_management_private_only_zero_one_and_reject_public(self):
        from p3_ssh_transport_contract import OWNER_SHA
        cfg={'server':parent.SERVER}
        owner={'id':'owner'}
        # Preserve real identity hash logic, without contacting Azure.
        with (patch.object(parent,'OWNER_SHA',parent.ident('owner')),
              patch.object(parent,'check_owner_account',return_value=None),
              patch.object(parent,'validate_cli_version',return_value=None),
              patch.object(parent,'select_single_instance',return_value='instance'),
              patch.object(parent,'private_endpoint_ip',return_value='10.90.2.4')):
            for count in (0,1,2):
                server={'fullyQualifiedDomainName':parent.SERVER,
                        'publicNetworkAccess':'Disabled','minimalTlsVersion':'1.2',
                        'id':'/subscriptions/example/sql'}
                responses={'account show':{},'ad signed-in-user':owner,
                           'version':{},'sql server show':server,
                           'sql server firewall-rule':[{}]*count,
                           'sql server ad-admin':[{'sid':'owner'}],
                           'webapp show':{'name':parent.APP,'state':'Running',
                                          'virtualNetworkSubnetId':'subnet'},
                           'webapp list-instances':[{}]}
                def fake_az(args):
                    key=' '.join(args[:3])
                    if args[:2]==['account','show']:key='account show'
                    if args[:3]==['ad','signed-in-user','show']:key='ad signed-in-user'
                    if args[0]=='version':key='version'
                    if args[:3]==['sql','server','show']:key='sql server show'
                    if args[:3]==['sql','server','firewall-rule']:key='sql server firewall-rule'
                    if args[:3]==['sql','server','ad-admin']:key='sql server ad-admin'
                    if args[:2]==['webapp','show']:key='webapp show'
                    if args[:2]==['webapp','list-instances']:key='webapp list-instances'
                    return responses[key]
                with patch.object(parent,'az',side_effect=fake_az):
                    _,facts=parent.management(cfg)
                    self.assertEqual(facts['firewall_count'],count)
                    server['publicNetworkAccess']='Enabled'
                    with self.assertRaises(parent.Hold):parent.management(cfg)

    def test_launch_window_freshness_and_authorization_boundaries(self):
        grant=dict(operation_id='a'*32,contract=parent.CONTRACT,
            decision=parent.DECISION,artifact_sha256='b'*64,
            binding_index_sha256='c'*64,server=parent.SERVER,
            database=parent.DATABASE,installation_attempts=1,
            instance_sha256=parent.ident('instance'),
            not_before='2026-11-16T00:00:00Z',
            expires_at='2026-11-16T00:30:00Z')
        from datetime import datetime, timezone
        start=datetime(2026,11,16,tzinfo=timezone.utc).timestamp()
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'grant.json';p.write_text(json.dumps(grant))
            for age in (0,179,180):
                parent.validate_launch_window({'observed_at':start+200-age},
                    p,'b'*64,'c'*64,'instance',now=start+200)
            for age in (181,-1):
                with self.assertRaises(parent.Hold):
                    parent.validate_launch_window({'observed_at':start+200-age},
                        p,'b'*64,'c'*64,'instance',now=start+200)
            for now in (start-1,start+1801):
                with self.assertRaises(parent.Hold):
                    parent.validate_launch_window({'observed_at':now},
                        p,'b'*64,'c'*64,'instance',now=now)
            for now in (start,start+1800):
                parent.validate_launch_window({'observed_at':now},
                    p,'b'*64,'c'*64,'instance',now=now)

    def test_predecessor_markers_never_put(self):
        for target in (guard.PRIOR_G1_KUDU_BLOB,guard.PRIOR_G1_SSH_BLOB):
            for status in (200,503):
                calls=[]
                def request(method,name,headers,body=b''):
                    calls.append((method,name))
                    if name==target:return guard.Response(status,'etag',b'','ServerBusy')
                    return guard.Response(404,'',b'','BlobNotFound')
                with patch.object(guard,'validate_owner_storage_token',return_value=True):
                    g=guard.OneTimeInstallBlobGuard('FAKE_NONSECRET',request=request)
                g.predecessors_verified=True
                with self.assertRaises(guard.InstallGuardHold):g.check_available()
                self.assertNotIn(('HEAD',guard.NEW_BLOB),calls)
                self.assertFalse(any(method=='PUT' for method,_ in calls))

    def test_actual_https_guard_allows_only_reading_prior_ssh_marker(self):
        # The original fake request tests bypassed the real HTTPS target filter.
        # Mock the network boundary itself: no Azure or credentials involved.
        reply=Mock()
        reply.status=404
        reply.getheader.side_effect=lambda key,default='': (
            'BlobNotFound' if key=='x-ms-error-code' else default)
        reply.read.return_value=b''
        client=Mock()
        client.getresponse.return_value=reply
        with patch.object(guard,'validate_owner_storage_token',return_value=True), \
             patch.object(guard.ssl,'create_default_context',return_value=object()), \
             patch.object(guard.http.client,'HTTPSConnection',return_value=client):
            send=guard.pinned_http_request('NONSECRET_FAKE')
            old=send('HEAD',guard.PRIOR_G1_SSH_BLOB,{'Authorization':'NONSECRET_FAKE'})
            self.assertEqual((old.status,old.error_code),(404,'BlobNotFound'))
            client.request.assert_called_once()
            self.assertIn(guard.PRIOR_G1_SSH_BLOB,client.request.call_args.args[1])
            calls=client.request.call_count
            for target in (guard.PRIOR_G1_SSH_BLOB,guard.PRIOR_G1_KUDU_BLOB,
                           guard.PRIOR_G1_INSTALL_BLOB):
                with self.assertRaises(guard.InstallGuardHold):
                    send('PUT',target,{'If-None-Match':'*'},b'bad')
                with self.assertRaises(guard.InstallGuardHold):
                    send('GET',target,{},b'')
            with self.assertRaises(guard.InstallGuardHold):
                send('PUT',guard.NEW_BLOB,{},b'bad')
            with self.assertRaises(guard.InstallGuardHold):
                send('DELETE',guard.NEW_BLOB,{},b'')
            self.assertEqual(client.request.call_count,calls)

    def test_https_allowlist_all_predecessor_read_targets(self):
        # Static fail-closed source-level check, not a live Storage probe.
        import inspect
        src=inspect.getsource(guard.pinned_http_request)
        self.assertIn('PRIOR_G1_SSH_BLOB',src)
        self.assertIn('predecessor_select_markers',src)
        self.assertIn("blob_name == NEW_BLOB",src)

    def test_shell_hash_challenge_not_a_cli_banner(self):
        s=KuduExecSession('a'*20,deadline=300,clock=lambda:1)
        s.state.begin();s.fd=123;s.proc=Mock();s.proc.poll.return_value=None
        digest=hashlib.sha256(('FV_G1_KUDU_SHELL:'+NONCE).encode()).hexdigest()
        reads=[b'Opening remote shell...\r\nroot@sample:/home/site/wwwroot# ',
               ('\nFV_G1_KUDU_SHELL:'+digest+'\n').encode()]
        with patch.object(transport.secrets,'token_hex',return_value=NONCE),\
             patch.object(s,'send_public') as sent,\
             patch.object(transport.select,'select',return_value=([123],[],[])),\
             patch.object(transport.os,'read',side_effect=reads):
            s.await_cli_shell_banner(seconds=2)
        self.assertNotIn(digest.encode(),sent.call_args.args[0])
        self.assertIn('SHELL_PROMPT_OBSERVED',s.audit)
        self.assertIn('CLI_SHELL_BANNER_READY',s.audit)

    def test_shell_prompt_parser_rejects_cli_progress_and_fake_challenge(self):
        match=KuduExecSession._remote_shell_prompt_present
        self.assertTrue(match(b'Connected\r\nroot@worker:/home/site/wwwroot# '))
        self.assertTrue(match(b'\n# '))
        for invalid in (b'',b'Connecting to remote shell...\n',
                        b'Authentication required: password:',
                        b'root@worker:/# password:', b'\nFV_G1_KUDU_SHELL:abc\n',
                        b'\nother output # unrelated'):
            self.assertFalse(match(invalid))

    def test_does_not_send_challenge_before_actual_prompt(self):
        s=KuduExecSession('a'*20,deadline=300,clock=lambda:1)
        s.state.begin();s.fd=123;s.proc=Mock();s.proc.poll.return_value=None
        digest=hashlib.sha256(('FV_G1_KUDU_SHELL:'+NONCE).encode()).hexdigest()
        chunks=[b'Connecting...\r\n',
                b'root@worker:/home/site/wwwroot# ',
                ('\nFV_G1_KUDU_SHELL:'+digest+'\n').encode()]
        with patch.object(transport.secrets,'token_hex',return_value=NONCE),\
             patch.object(s,'send_public') as sent,\
             patch.object(transport.select,'select',return_value=([123],[],[])),\
             patch.object(transport.os,'read',side_effect=chunks):
            s.await_cli_shell_banner(seconds=3)
        self.assertEqual(sent.call_count,1)
        self.assertIn('SHELL_PROMPT_OBSERVED',s.audit)
        self.assertIn('CLI_SHELL_BANNER_READY',s.audit)

    def test_no_remote_prompt_fails_without_sending_commands(self):
        s=KuduExecSession('a'*20,deadline=100,clock=iter(range(100)).__next__)
        s.state.begin();s.fd=123;s.proc=Mock();s.proc.poll.return_value=None
        with patch.object(s,'send_public') as sent,\
             patch.object(transport.select,'select',return_value=([123],[],[])),\
             patch.object(transport.os,'read',return_value=b'CLI startup...\n'):
            with self.assertRaises(TransportHold) as e:
                s.await_cli_shell_banner(seconds=3)
        self.assertEqual(e.exception.code,'KUDU_CHANNEL_TIMEOUT')
        sent.assert_not_called()
        self.assertEqual(s._token_count,0)

    def test_echoed_command_denied(self):
        s=KuduExecSession('a'*20,deadline=300,clock=lambda:1)
        s.state.begin();s.fd=123;s.proc=Mock()
        digest=hashlib.sha256(('FV_G1_KUDU_ECHO:'+NONCE).encode()).hexdigest()
        def echo(raw,audit):
            if audit=='KUDU_ECHO_CHALLENGE_SENT':
                s.buffer=raw+b'\nFV_G1_KUDU_ECHO:'+digest.encode()+b'\n'
        with patch.object(transport.secrets,'token_hex',return_value=NONCE),\
             patch.object(s,'send_public',side_effect=echo):
            with self.assertRaises(TransportHold) as e:s.establish_safe_channel()
        self.assertEqual(e.exception.code,'KUDU_SECRET_ECHO')
        self.assertEqual(s._token_count,0)

    def test_exact_nonce_accepts_only_bounded_shell_prompt_prefix(self):
        nonce_digest = hashlib.sha256(('FV_G1_KUDU_SHELL:'+NONCE).encode()).hexdigest()
        expected = ('FV_G1_KUDU_SHELL:' + nonce_digest).encode()
        prefix = b'root@worker:/home/site/wwwroot# '
        find = KuduExecSession._consume_shell_challenge
        for frame in (expected + b'\r\n',
                      prefix + expected + b'\r\n',
                      b'Connecting...\r\n' + prefix + expected + b'\r\n'):
            ok, remaining = find(frame + b'next', expected)
            self.assertTrue(ok)
            self.assertEqual(remaining, b'next')
        for frame in (b'garbage:' + expected + b'\r\n',
                      prefix + expected + b'TRAILING\r\n',
                      prefix + expected[:24] + b'\r\n',
                      b'root@worker:/home/site/wwwroot# oops ' + expected + b'\r\n'):
            self.assertFalse(find(frame, expected)[0])

    def test_real_local_pty_interactive_shell_nonce_handshake(self):
        """Exercise the actual PTY/read/write lifecycle, without Azure or Tokens."""
        import shutil
        import time
        bash = shutil.which('bash')
        self.assertIsNotNone(bash)
        # A private bash in the GitHub runner stands in for a remote shell.
        # No Azure CLI, network, SQL, Storage, credentials, or installer.
        command = ['/usr/bin/env', 'PS1=root@worker:/home/site/wwwroot# ',
                   bash, '--noprofile', '--norc', '-i']
        s = KuduExecSession('a'*20, deadline=time.monotonic()+15)
        try:
            with patch.object(transport, 'interactive_exec_args', return_value=command):
                s.start()
                s.await_cli_shell_banner(seconds=6)
                s.establish_safe_channel()
            self.assertIn('SHELL_PROMPT_OBSERVED', s.audit)
            self.assertIn('CLI_SHELL_BANNER_READY', s.audit)
            self.assertIn('ECHO_READY', s.audit)
            self.assertEqual(s._token_count, 0)
            self.assertEqual(s.state.state, 'CHANNEL')
        finally:
            s.close()

    def test_real_local_pty_archive_stage_no_cloud(self):
        """Transport exact hash-bound ZIP into a local shell; never connect Azure."""
        import io
        import shutil
        import time
        import zipfile
        bash = shutil.which('bash')
        self.assertIsNotNone(bash)
        remote = Path(transport.REMOTE_STAGE)
        self.assertFalse(remote.exists())
        self.assertFalse(remote.is_symlink())
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, mode='w', compression=zipfile.ZIP_DEFLATED) as z:
            z.writestr('public-fixture.txt', b'NO_SECRET_NO_SQL')
        payload = buf.getvalue()
        digest = hashlib.sha256(payload).hexdigest()
        command = ['/usr/bin/env', 'PS1=root@worker:/home/site/wwwroot# ',
                   bash, '--noprofile', '--norc', '-i']
        s = KuduExecSession('a'*20, deadline=time.monotonic()+20)
        try:
            with patch.object(transport, 'interactive_exec_args', return_value=command):
                s.start()
                s.await_cli_shell_banner(seconds=6)
                s.establish_safe_channel()
                s.stage_archive(payload, digest)
            self.assertEqual(s.state.state, 'STAGED')
            self.assertEqual((remote / 'public-fixture.txt').read_bytes(),
                             b'NO_SECRET_NO_SQL')
            self.assertEqual(s._token_count, 0)
        finally:
            s.close()
            if remote.is_dir() and not remote.is_symlink():
                self.assertEqual({p.name for p in remote.iterdir()},
                                 {'public-fixture.txt'})
                shutil.rmtree(remote)

    def test_remote_owned_cleanup_source_is_syntactically_and_scope_safe(self):
        """The generated cleanup must HOLD safely when remote state is unexpected."""
        import ast
        import base64
        import re
        for verify_only in (False, True):
            command = parent.cleanup_command(
                {'public-fixture.txt': 'a'*64}, verify_only=verify_only)
            encoded = re.search(rb"base64\.b64decode\('([A-Za-z0-9+/=]+)'\)", command)
            self.assertIsNotNone(encoded)
            source = base64.b64decode(encoded.group(1)).decode('utf-8')
            ast.parse(source, filename='<g1-owned-cleanup>', mode='exec')
            self.assertNotIn('OWNER_SQL_TOKEN', source)
            self.assertNotIn('OWNER_STORAGE_TOKEN', source)
            if not verify_only:
                # Both children and expected_root must remain guarded by 'if ok'.
                self.assertIn('\n  children=set(os.listdir(r))', source)
                self.assertIn('\n  expected_root=', source)
                self.assertIn('\n  ok=children==expected_root', source)

    def test_no_live_when_missing_auth(self):
        with tempfile.TemporaryDirectory() as d:
            f=Path(d)/'none.json';f.write_text('{}')
            with self.assertRaises(parent.Hold):parent.validate_authorization(f,'a'*64,'b'*64,None)

    def test_frozen_original(self):
        self.assertEqual(hashlib.sha256((ROOT/'frozen_install.py.snapshot').read_bytes()).hexdigest(),
            'b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f')
        parent.validate_binding(ROOT)

if __name__=='__main__':unittest.main()
