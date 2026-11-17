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
            for age in (0,899,900):
                parent.validate_launch_window({'observed_at':start+200-age},
                    p,'b'*64,'c'*64,'instance',now=start+200)
            for age in (901,-1):
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

    def test_kudu_timeouts_are_bounded_without_extending_sql_budget(self):
        import inspect
        import p3_kudu_transport as kt
        self.assertEqual(inspect.signature(kt.KuduExecSession.await_cli_shell_banner)
                         .parameters['seconds'].default,120)
        source=(ROOT/'p3_kudu_transport.py').read_text()
        self.assertIn('self._challenge(tag="ECHO", seconds=120, no_echo=True)',source)
        self.assertIn('self.expect_fixed(b"FV_KUDU_STAGE_READY", 600',source)
        cfg=json.loads((ROOT/'executor_config.json').read_text())
        self.assertEqual(cfg['sql_deadline_seconds'],2400)
        self.assertEqual(cfg['total_deadline_seconds'],3600)
        self.assertEqual(cfg['cleanup_reserve_seconds'],300)
        self.assertEqual(cfg['minimum_token_ttl_seconds'],3000)

    def test_microsoft_geofence_one_https_302_and_exact_original_path(self):
        # Mock all I/O: never download Microsoft packages from CI.
        import io
        import urllib.request
        import p3_g1_owner_management as owner
        cfg=json.loads((ROOT/'executor_config.json').read_text())
        original=cfg['tools'][1]['url']
        redirected=original.replace('https://packages.microsoft.com/',
            'https://pmc-geofence.trafficmanager.net/',1)
        payload=b'LOCAL_MOCK_MS_SQL_DEB'
        handlers=[]
        def make_opener(handler):
            handlers.append(handler)
            return Mock(open=Mock(return_value=io.BytesIO(payload)))
        with tempfile.TemporaryDirectory() as folder, \
             patch.object(owner.urllib.request,'build_opener',side_effect=make_opener):
            target=Path(folder)/'mssql-tools18.deb'
            owner.download_once(original,target,hashlib.sha256(payload).hexdigest())
            self.assertEqual(target.read_bytes(),payload)
        self.assertEqual(len(handlers),1)
        handler=handlers[0]
        req=urllib.request.Request(original)
        # Both original unparameterized and Microsoft's real ?geofence=true
        # Location are permitted, but only as the FIRST 302 hop.
        for location in (redirected, redirected+'?geofence=true'):
            h=type(handler)()
            follow=h.redirect_request(req,None,302,'Found',{},location)
            self.assertIsNotNone(follow)
            self.assertEqual(follow.full_url,location)
            self.assertIsNone(h.redirect_request(follow,None,302,'Found',{},location))

    def test_microsoft_geofence_rejects_all_other_redirects(self):
        import io
        import urllib.request
        import p3_g1_owner_management as owner
        cfg=json.loads((ROOT/'executor_config.json').read_text())
        original=cfg['tools'][1]['url']
        good=original.replace('https://packages.microsoft.com/',
            'https://pmc-geofence.trafficmanager.net/',1)
        handlers=[]
        with tempfile.TemporaryDirectory() as folder, \
             patch.object(owner.urllib.request,'build_opener',side_effect=lambda h:
                (handlers.append(h) or Mock(open=Mock(return_value=io.BytesIO(b'x'))))):
            owner.download_once(original,Path(folder)/'tool.deb',hashlib.sha256(b'x').hexdigest())
        req=urllib.request.Request(original)
        blocked=(
            good.replace('https:','http:',1),
            good.replace('pmc-geofence.trafficmanager.net','other.trafficmanager.net',1),
            good.replace('pmc-geofence.trafficmanager.net','pmc-geofence.trafficmanager.net.evil.example',1),
            good.replace('.net/', '.net:443/',1),
            good.replace('.net/', '.net:444/',1),
            good.replace('.net/', '.net/other/',1),
            good+'?version=changed',
            good+'?geofence=false',
            good+'?geofence=true&other=1',
            good+'?geofence=true&geofence=true',
            good+'?geofence=TRUE',
            good+'#fragment',
            good.replace('https://','https://user@',1),
        )
        for url in blocked:
            with self.subTest(url=url):
                self.assertIsNone(type(handlers[0])().redirect_request(
                    req,None,302,'Found',{},url))
        self.assertIsNone(type(handlers[0])().redirect_request(
            req,None,301,'Moved',{},good))
        self.assertIsNone(type(handlers[0])().redirect_request(
            req,None,307,'Temporary',{},good))
        # Debian mirror redirects are deliberately NOT enabled.
        debian=cfg['tools'][2]['url']
        self.assertIsNone(type(handlers[0])().redirect_request(
            urllib.request.Request(debian),None,302,'Found',{},good))

    def test_offline_download_sha_mismatch_never_writes_file(self):
        import io
        import p3_g1_owner_management as owner
        cfg=json.loads((ROOT/'executor_config.json').read_text())
        original=cfg['tools'][1]['url']
        with tempfile.TemporaryDirectory() as folder, \
             patch.object(owner.urllib.request,'build_opener',
                return_value=Mock(open=Mock(return_value=io.BytesIO(b'wrong')))):
            target=Path(folder)/'should-not-exist.deb'
            with self.assertRaises(owner.ManagementHold):
                owner.download_once(original,target,hashlib.sha256(b'expected').hexdigest())
            self.assertFalse(target.exists())

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

    def test_echo_disable_must_be_acknowledged_before_no_echo_probe(self):
        """Synthetic delayed shell: old pipelined stty/challenge could echo."""
        s=KuduExecSession('a'*20,deadline=300,clock=lambda:1)
        s.state.begin();s.fd=123;s.proc=Mock();s.proc.poll.return_value=None
        sent=[]
        digest=hashlib.sha256(('FV_G1_KUDU_ECHO:'+NONCE).encode()).hexdigest()
        marker=('FV_G1_KUDU_ECHO:'+digest+'\n').encode()
        def send(raw,audit):
            sent.append((audit,raw))
            # Before stty acknowledgment the terminal may echo the *public*
            # disable command. After its proof, the next command must not echo.
            if len(sent)==1:
                self.assertTrue(raw.startswith(b'stty -echo -icanon min 1 time 0 && umask 077 && '))
                s.buffer=raw+marker
            else:
                self.assertEqual(audit,'KUDU_ECHO_CHALLENGE_SENT')
                self.assertFalse(raw.startswith(b'stty '))
                s.buffer=marker
        with patch.object(transport.secrets,'token_hex',return_value=NONCE),\
             patch.object(s,'send_public',side_effect=send):
            s.establish_safe_channel()
        self.assertEqual(len(sent),2)
        self.assertEqual(s.audit.count('ECHO_READY'),2)
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


class StorageCheckpointOnlyTests(unittest.TestCase):
    """Synthetic-only: no az, networking, credentials, Blob write or installer."""

    @staticmethod
    def _config():
        return {'storage_account': guard.ACCOUNT,
                'storage_container': guard.CONTAINER}

    @staticmethod
    def _predecessor_body(name, attempt):
        obj = {
            'contract': 'FIELDVISIT_PATHB_SELECT22_BLOB_ONCE_V1',
            'status': 'CONSUMED_BEFORE_SQL',
            'run_id': 'pathb-select22-manual-v2-20261103',
            'release': guard.RELEASE,
            'server': guard.SERVER, 'database': guard.DATABASE,
            'owner_oid_sha256': guard.OWNER_OID_SHA,
            'preflight_sha256': guard.SELECT_SHA,
            'started_at_utc': 1760000000,
            'invocation_nonce': 'a' * 32,
        }
        if attempt is not None:
            obj['attempt_id'] = attempt
        return json.dumps(obj, sort_keys=True).encode()

    def _run_guard(self, *, break_at=None):
        import p3_historical_blob_evidence as historical
        events, cli_calls, rest_calls = [], [], []
        predecessor = list(guard.PREDECESSORS)
        def cli(args):
            self.assertEqual(args[:3], ['storage','blob','show'])
            self.assertIn('--auth-mode', args)
            self.assertEqual(args[-1], 'login')
            name = args[args.index('--name')+1]
            cli_calls.append(name)
            if break_at == 'CLI1' and len(cli_calls) == 1:
                raise RuntimeError('SENSITIVE_RAW_CLI_OUTPUT_DO_NOT_EXPOSE')
            if break_at == 'CLI2' and len(cli_calls) == 2:
                raise RuntimeError('SENSITIVE_RAW_CLI_OUTPUT_DO_NOT_EXPOSE')
            if break_at == 'METADATA2' and len(cli_calls) == 2:
                return {'name': 'not-the-pinned-name', 'properties': {'etag':'"etag-2"'}}
            return {'name': name, 'properties': {'etag':'"etag-%d"'%len(cli_calls)}}
        def http(method,name,headers,body=b''):
            self.assertEqual(method,'GET')
            self.assertEqual(body,b'')
            rest_calls.append(name)
            idx = 1 if name == predecessor[0][0] else 2
            if break_at == 'GET1' and idx == 1:
                raise RuntimeError('SENSITIVE_GET_EXCEPTION_DO_NOT_EXPOSE')
            if break_at == 'GET2' and idx == 2:
                raise RuntimeError('SENSITIVE_GET_EXCEPTION_DO_NOT_EXPOSE')
            etag = '"etag-%d"'%idx
            if break_at == 'ETAG1' and idx == 1:
                etag = '"mismatched"'
            content = self._predecessor_body(name,predecessor[idx-1][1])
            if break_at == 'CONTENT1' and idx == 1:
                content = b'{not-json:'
            return guard.Response(200,etag,content,'')
        with patch.object(guard,'validate_owner_storage_token',return_value=True):
            ret=historical.verify_readonly(self._config(),cli,'FAKE_TOKEN',
                                           request=http,checkpoint=events.append)
        return ret,events,cli_calls,rest_calls

    def test_historical_two_blob_success_labels_and_unchanged_contract(self):
        ret, events, meta, gets = self._run_guard()
        self.assertEqual(ret['current_head_get_contract'],'PASS')
        self.assertEqual(len(meta),2)
        self.assertEqual(gets,[name for name,_ in guard.PREDECESSORS])
        self.assertEqual(events,[
            'STORAGE_BLOB_1_METADATA_CLI','STORAGE_BLOB_1_METADATA_VALIDATE',
            'STORAGE_BLOB_2_METADATA_CLI','STORAGE_BLOB_2_METADATA_VALIDATE',
            'STORAGE_HTTPS_CLIENT_INIT','STORAGE_GUARD_INIT',
            'STORAGE_BLOB_1_GET','STORAGE_BLOB_1_ETAG_CHECK',
            'STORAGE_BLOB_1_CONTENT_CHECK',
            'STORAGE_BLOB_2_GET','STORAGE_BLOB_2_ETAG_CHECK',
            'STORAGE_BLOB_2_CONTENT_CHECK'])
        self.assertTrue(set(events).issubset(parent.STORAGE_CHECKPOINTS))
        self.assertFalse(any('operator-once/' in event for event in events))

    def test_metadata_cli_failure_classifies_exact_blob(self):
        for fault, phase in [('CLI1','STORAGE_BLOB_1_METADATA_CLI'),
                             ('CLI2','STORAGE_BLOB_2_METADATA_CLI')]:
            with self.subTest(fault=fault):
                events=[]
                import p3_historical_blob_evidence as historical
                def bad_cli(args):
                    events.append(args[args.index('--name')+1])
                    if len(events) == int(fault[-1]):raise RuntimeError('PRIVATE_ERROR')
                    return {'name':events[-1], 'properties':{'etag':'"e"'}}
                logs=[]
                with self.assertRaises(RuntimeError):
                    historical.verify_readonly(self._config(),bad_cli,'FAKE',
                        request=lambda *a:None,checkpoint=logs.append)
                self.assertEqual(logs[-1],phase)
                self.assertEqual(len(events),int(fault[-1]))

    def test_blob_metadata_identity_failure_classified(self):
        import p3_historical_blob_evidence as historical
        logs=[]
        names=[n for n,_ in guard.PREDECESSORS]
        def cli(args):
            name=args[args.index('--name')+1]
            if name==names[1]: return {'name':'wrong','properties':{'etag':'"e"'}}
            return {'name':name,'properties':{'etag':'"e"'}}
        with self.assertRaises(historical.HistoricalEvidenceHold):
            historical.verify_readonly(self._config(),cli,'FAKE',
                 request=lambda *a:None,checkpoint=logs.append)
        self.assertEqual(logs[-1],'STORAGE_BLOB_2_METADATA_VALIDATE')

    def test_http_get_etag_and_content_fail_closed(self):
        import p3_historical_blob_evidence as historical
        cases=[('GET1','STORAGE_BLOB_1_GET'),
               ('GET2','STORAGE_BLOB_2_GET'),
               ('ETAG1','STORAGE_BLOB_1_ETAG_CHECK'),
               ('CONTENT1','STORAGE_BLOB_1_CONTENT_CHECK')]
        for fault, label in cases:
            with self.subTest(fault=fault):
                logs=[]; cnt=[0]
                def cli(args):
                    cnt[0]+=1
                    name=args[args.index('--name')+1]
                    return {'name':name,'properties':{'etag':'"etag-%d"'%cnt[0]}}
                getcount=[0]
                def http(method,name,headers,body=b''):
                    self.assertEqual(method,'GET')
                    getcount[0]+=1
                    if fault==('GET%d'%getcount[0]):raise RuntimeError('DO_NOT_LOG_ME')
                    if fault=='ETAG1':return guard.Response(200,'"wrong"',b'{}')
                    if fault=='CONTENT1':return guard.Response(200,'"etag-1"',b'{broken')
                    idx=getcount[0]
                    return guard.Response(200,'"etag-%d"'%idx,
                        self._predecessor_body(name,guard.PREDECESSORS[idx-1][1]))
                with patch.object(guard,'validate_owner_storage_token',return_value=True):
                    with self.assertRaises(Exception):
                        historical.verify_readonly(self._config(),cli,'FAKE',
                                                  request=http,checkpoint=logs.append)
                self.assertEqual(logs[-1],label)
                self.assertFalse(any('DO_NOT_LOG_ME' in x for x in logs))

    def test_parent_stage_catch_reports_fixed_label_and_never_secret(self):
        from types import SimpleNamespace
        from contextlib import redirect_stdout
        import io
        pkg=b'LOCAL_TEST_PACKAGE';seal=b'LOCAL_TEST_SEAL'
        cfg={'total_deadline_seconds':3600,
             'package_blob':'test-pkg','seal_blob':'test-seal',
             'package_sha256':hashlib.sha256(pkg).hexdigest(),
             'seal_sha256':hashlib.sha256(seal).hexdigest(),
             'tools':[]}
        def blob_dl(_,name,output):
            Path(output).write_bytes(pkg if name=='test-pkg' else seal)
        cases=[('CLI','STORAGE_TOKEN_CLI'),
               ('RESPONSE','STORAGE_TOKEN_RESPONSE'),
               ('VALIDATE','STORAGE_TOKEN_VALIDATE'),
               ('BLOB2','STORAGE_BLOB_2_CONTENT_CHECK'),
               ('RESULT','STORAGE_HISTORICAL_RESULT')]
        with tempfile.TemporaryDirectory() as d:
            f=Path(d)/'fake-source.zip';f.write_bytes(b'FAKE_NONSECRET_ZIP')
            args=SimpleNamespace(distribution=str(f),
                                 artifact_sha256=hashlib.sha256(f.read_bytes()).hexdigest(),
                                 authorization=str(Path(d)/'never-opened'))
            for fault, expected in cases:
                with self.subTest(fault=fault):
                    def az_mock(commands):
                        self.assertEqual(commands[:2],['account','get-access-token'])
                        if fault=='CLI':raise RuntimeError('SENSITIVE_CLI_ERROR')
                        if fault=='RESPONSE':return {}
                        return {'accessToken':'SENSITIVE_TOKEN_NEVER_PRINT'}
                    def token_validator(*a,**kw):
                        if fault=='VALIDATE':raise RuntimeError('SENSITIVE_CLAIMS')
                    def hist(*a,**kw):
                        if fault=='BLOB2':
                            kw['checkpoint']('STORAGE_BLOB_2_CONTENT_CHECK')
                            raise RuntimeError('SENSITIVE_BLOB_DATA')
                        if fault=='RESULT':return {'current_head_get_contract':'HOLD',
                                 'historical_immutability_proven':False,
                                 'historical_parent_execution_proven':False}
                        raise AssertionError('SHOULD_NOT_CALL')
                    with patch.object(parent,'validate_local_source'), \
                         patch.object(parent,'validate_binding',return_value=({'files':{}},'b'*64)), \
                         patch.object(parent,'validate_distribution_archive'), \
                         patch.object(parent,'load',return_value=cfg), \
                         patch.object(parent,'validate_authorization'), \
                         patch.object(parent,'management',return_value=('INSTANCE',{'observed_at':0})), \
                         patch.object(parent,'blob_download',side_effect=blob_dl), \
                         patch.object(parent,'validate_original_package'), \
                         patch.object(parent,'strict_seal_crossbinding'), \
                         patch.object(parent,'az',side_effect=az_mock), \
                         patch.object(parent,'validate_owner_token',side_effect=token_validator), \
                         patch.object(parent,'verify_historical_blobs',side_effect=hist):
                        output=io.StringIO()
                        with redirect_stdout(output):
                            result=parent.execute(args)
                    self.assertEqual(result,1)
                    data=output.getvalue()
                    self.assertIn('STOP_STAGE='+expected,data)
                    self.assertIn('SQL_TOKEN_REQUEST=NO',data)
                    self.assertIn('SQL_CALL_COUNT=UNKNOWN',data)
                    self.assertNotIn('0_OR_1_UNKNOWN',data)
                    self.assertIn('AUTO_RETRY=FORBIDDEN',data)
                    self.assertNotIn('SENSITIVE_',data)
                    self.assertNotIn('DO_NOT_LOG_ME',data)


class StorageTokenSubgateTests(unittest.TestCase):
    """Only generated mock JWTs; no actual Azure token or identity is used."""
    def _fake_token(self, **kw):
        import base64
        payload=dict(oid='fixture_owner',tid='fixture_tenant',
                     aud='https://storage.azure.com',nbf=999900,exp=1004000)
        payload.update(kw)
        b64=lambda b:base64.urlsafe_b64encode(b).decode().rstrip('=')
        return b64(b'{}')+'.'+b64(json.dumps(payload).encode())+'.'+b64(b'sig')

    def test_single_expiration_boundary_and_identical_fail_closed_code(self):
        import p3_ssh_transport_contract as trust
        with patch.object(trust,'OWNER_SHA',hashlib.sha256(b'fixture_owner').hexdigest()), \
             patch.object(trust,'TENANT_SHA',hashlib.sha256(b'fixture_tenant').hexdigest()):
            for ttl,ok in ((3020,True),(3000,True),(2990,False)):
                with self.subTest(ttl=ttl):
                    stages=[];t=self._fake_token(exp=1000000+ttl)
                    if ok:
                        self.assertTrue(trust.validate_owner_token(t,'storage',1000000,
                                                                     checkpoint=stages.append))
                        self.assertTrue(trust.validate_owner_token(t,'storage',1000000))
                    else:
                        with self.assertRaises(trust.Hold) as raised:
                            trust.validate_owner_token(t,'storage',1000000,
                                                       checkpoint=stages.append)
                        self.assertEqual(str(raised.exception),'TOKEN_OWNER_AUDIENCE_OR_TTL')
                        with self.assertRaises(trust.Hold):
                            trust.validate_owner_token(t,'storage',1000000)
                    self.assertEqual(stages[-1],'STORAGE_TOKEN_TTL')
                    self.assertTrue(set(stages).issubset(parent.STORAGE_CHECKPOINTS))

    def test_failure_reason_labels_are_static_and_stop_at_exact_gate(self):
        import p3_ssh_transport_contract as trust
        good_owner=hashlib.sha256(b'fixture_owner').hexdigest()
        good_tenant=hashlib.sha256(b'fixture_tenant').hexdigest()
        with patch.object(trust,'OWNER_SHA',good_owner), \
             patch.object(trust,'TENANT_SHA',good_tenant):
            cases=[
                ({'oid':'incorrect'},'STORAGE_TOKEN_OWNER'),
                ({'tid':'incorrect'},'STORAGE_TOKEN_TENANT'),
                ({'aud':'https://database.windows.net'},'STORAGE_TOKEN_AUDIENCE'),
                ({'nbf':1000031},'STORAGE_TOKEN_NBF'),
                ({'exp':1002999},'STORAGE_TOKEN_TTL'),
            ]
            for changes,expected in cases:
                with self.subTest(expected=expected):
                    stages=[]
                    with self.assertRaises(trust.Hold) as raised:
                        trust.validate_owner_token(self._fake_token(**changes),'storage',
                                                   1000000,checkpoint=stages.append)
                    self.assertEqual(stages[-1],expected)
                    self.assertEqual(str(raised.exception),'TOKEN_OWNER_AUDIENCE_OR_TTL')
            for text_value,label in [('bad.token','STORAGE_TOKEN_CLAIMS'),
                                     ('','STORAGE_TOKEN_CLAIMS')]:
                stages=[]
                with self.assertRaises(trust.Hold):
                    trust.validate_owner_token(text_value,'storage',1000000,
                                               checkpoint=stages.append)
                self.assertEqual(stages[-1],label)


class TokenHandoffAcknowledgementTests(unittest.TestCase):
    """Synthetic tokens only. Reproduces terminal input loss and exercises ack gate."""
    @staticmethod
    def _pty_tokens(batched):
        import os, pty, subprocess, select, signal, sys, time
        script = (
            'import getpass; a=getpass.getpass("FIRST:"); '
            'print("ACK",flush=True); b=getpass.getpass("SECOND:"); '
            'print("DONE",flush=True)'
        )
        master, slave = pty.openpty()
        proc = subprocess.Popen([sys.executable, '-c', script], stdin=slave,
                                stdout=slave, stderr=slave, start_new_session=True)
        os.close(slave)
        def until(marker, limit=3.0):
            seen=b'';deadline=time.monotonic()+limit
            while time.monotonic()<deadline:
                if select.select([master], [], [], 0.05)[0]:
                    try: chunk=os.read(master,4096)
                    except OSError: break
                    if not chunk: break
                    seen+=chunk
                    if marker in seen: return True
            return False
        try:
            if not until(b'FIRST:'): return False
            os.write(master, b'SYNTHETIC_SQL\n' +
                     (b'SYNTHETIC_STORAGE\n' if batched else b''))
            if not until(b'SECOND:'): return False
            if not batched:
                os.write(master,b'SYNTHETIC_STORAGE\n')
            return until(b'DONE', 1.0)
        finally:
            if proc.poll() is None:
                os.killpg(proc.pid, signal.SIGKILL)
            proc.wait(timeout=5)
            os.close(master)

    def test_real_local_pty_batched_input_can_be_discarded(self):
        self.assertFalse(self._pty_tokens(batched=True))

    def test_real_local_pty_acknowledged_second_input_is_received(self):
        self.assertTrue(self._pty_tokens(batched=False))

    def test_fixed_ack_marker_requires_exact_one_token_state(self):
        session=KuduExecSession('a'*20, deadline=100, clock=lambda:1)
        session.state.begin()
        session.state.advance('CHANNEL','STAGED')
        session.fd=123
        session.buffer=b'FV_P3_SQL_TOKEN_RECEIVED\n'
        with self.assertRaises(TransportHold):
            session.expect_fixed(b'FV_P3_SQL_TOKEN_RECEIVED', 1, 'SQL_TOKEN_ACK', after=0)
        session._token_count=1
        session.expect_fixed(b'FV_P3_SQL_TOKEN_RECEIVED', 1, 'SQL_TOKEN_ACK', after=0)
        self.assertEqual(session.audit[-1], 'SQL_TOKEN_ACK')

    def test_parent_never_sends_storage_token_before_sql_ack(self):
        source=(ROOT/'p3_kudu_cloudshell_parent.py').read_text()
        a=source.index('session.send_secret_line(sql_token, validator=')
        b=source.index('session.expect_fixed(b"FV_P3_SQL_TOKEN_RECEIVED"')
        c=source.index('session.send_secret_line(storage_token, validator=')
        self.assertLess(a,b)
        self.assertLess(b,c)
        runner_source=(ROOT/'p3_app_installer_runner.py').read_text()
        x=runner_source.index("sql_token=getpass.getpass('OWNER_SQL_TOKEN:')")
        y=runner_source.index("print('\\nFV_P3_SQL_TOKEN_RECEIVED',flush=True)")
        z=runner_source.index("storage_token=getpass.getpass('OWNER_STORAGE_TOKEN:')")
        self.assertLess(x,y)
        self.assertLess(y,z)
        self.assertIn("sql_exp=token_exp(sql_token,'sql',time.time())",runner_source[x:y])

class SqlAttemptTelemetryTests(unittest.TestCase):
    def test_validated_started_completed_unknown(self):
        base={'status':'HOLD','sql_call_count':0,'layer_progress':0,
              'marker':'NOT_CONSUMED','cleanup':'PASS'}
        for started,done,outcome in ((0,0,'NOT_STARTED'),(1,0,'UNKNOWN'),
                                     (3,2,'UNKNOWN'),(1,1,'CONFIRMED')):
            self.assertEqual(parent.validate_final_receipt(dict(base,
                sql_calls_started=started,sql_calls_completed=done,
                sql_outcome=outcome))['sql_outcome'],outcome)
        for extra in ({'sql_calls_started':1,'sql_calls_completed':2,'sql_outcome':'UNKNOWN'},
                      {'sql_calls_started':1,'sql_calls_completed':0,'sql_outcome':'CONFIRMED'},
                      {'sql_calls_started':True,'sql_calls_completed':0,'sql_outcome':'UNKNOWN'},
                      {'sql_calls_started':1,'sql_calls_completed':0,'sql_outcome':'TOKEN_SECRET'},
                      {'sql_calls_started':1}):
            with self.subTest(extra=extra),self.assertRaises(parent.Hold):
                parent.validate_final_receipt(dict(base,**extra))

    def test_runner_ledger_before_cleanup_and_no_sql_dispatch_assertion(self):
        source=(ROOT/'p3_app_installer_runner.py').read_text()
        self.assertLess(source.index('SQL_CALLS_STARTED = max(SQL_CALLS_STARTED, index)'),
                        source.index('p=subprocess.Popen(args,stdin=subprocess.PIPE'))
        self.assertIn("reserved=ledger.get('next')",source)
        self.assertIn("SQL_CALLS_STARTED=max(SQL_CALLS_STARTED,1+reserved)",source)
        self.assertIn("result['sql_outcome']=",source)

class RunnerFailureClassificationTests(unittest.TestCase):
    def test_only_fixed_failure_category_is_exposed(self):
        safe=parent.safe_runner_failure_category
        self.assertEqual(safe({'status':'HOLD','failure':{'category':'TOKEN_TTL',
                         'raw_error':'private secret here'}}),'TOKEN_TTL')
        self.assertEqual(safe({'status':'HOLD','failure':{'category':'PRE_DDL_EVIDENCE_MISSING'}}),
                         'PRE_DDL_EVIDENCE_MISSING')
        self.assertEqual(safe({'status':'HOLD','failure':{'category':'secret\nTOKEN=ABC'}}),'UNKNOWN')
        self.assertEqual(safe({'status':'HOLD','failure':{'category':'a'*101}}),'UNKNOWN')
        self.assertEqual(safe({'status':'HOLD','failure':{'category':12}}),'UNKNOWN')
        self.assertEqual(safe({'status':'HOLD','failure':'SECRET'}),'UNKNOWN')
        self.assertEqual(safe({'status':'HOLD'}),'UNKNOWN')
        self.assertEqual(safe(None),'UNKNOWN')
        self.assertEqual(safe({'status':'PASS'}),'NOT_APPLICABLE')

if __name__=='__main__':unittest.main()
