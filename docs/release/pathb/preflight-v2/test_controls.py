#!/usr/bin/env python3
"""All tokens/data/processes are synthetic; no Azure/SQL/network calls."""
import ast,base64,contextlib,copy,hashlib,importlib.util,io,json,os,signal,subprocess,sys,tempfile,time,unittest,uuid,zipfile
from pathlib import Path
from unittest.mock import patch
R=Path(__file__).resolve().parent
sys.path.insert(0,str(R))
import supervisor as S,transport as T,cleanup as C
def h(b):return hashlib.sha256(b).hexdigest()
def fake_token(claims):return 'fake.'+base64.urlsafe_b64encode(json.dumps(claims).encode()).decode().rstrip('=')+'.not-a-signature'
class Controls(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.r=Path(self.tmp.name);self.c=json.loads((R/'config.json').read_text())
        self.oid='00000000-0000-0000-0000-000000000001';self.tenant='00000000-0000-0000-0000-000000000002'
        self.c.update(owner_oid_sha256=h(self.oid.encode()),tenant_sha256=h(self.tenant.encode()))
        self.now=2000000000
        self.claims={'oid':self.oid,'tid':self.tenant,'aud':'https://database.windows.net/','nbf':self.now-60,'exp':self.now+3000}
    def tearDown(self):self.tmp.cleanup()
    def row(self,**kw):
        a=['FV_SQL',self.c['server'],self.c['database'],'5',uuid.UUID(self.oid).bytes_le.hex(),'1','1','0','TRUE','SYNTHETIC-NON-FEDAUTH','TCP']+['0']*11
        for k,v in kw.items():a[int(k)]=v
        return ('|'.join(a)+'\n').encode()
    def test_exact_config_and_control_binding(self):
        cfg=(R/'config.json').read_bytes();self.assertEqual(S.load_config(h(cfg))['payload_sha256'],self.c['payload_sha256'])
        with self.assertRaisesRegex(ValueError,'CONFIG_HASH'):S.load_config('0'*64)
    def test_token_fifty_minutes_exact_threshold(self):
        self.assertEqual(S.token_claims(fake_token(self.claims),self.c,self.now),self.now+3000)
        self.claims['exp']-=1
        with self.assertRaisesRegex(ValueError,'TOKEN_TTL'):S.token_claims(fake_token(self.claims),self.c,self.now)
    def test_token_owner_tenant_audience_and_notbefore(self):
        for k,v in [('oid',str(uuid.UUID(int=3))),('tid',str(uuid.UUID(int=4))),('aud','https://example.invalid'),('nbf',self.now+31)]:
            data={**self.claims,k:v}
            with self.assertRaises(ValueError):S.token_claims(fake_token(data),self.c,self.now)
    def test_budget_fixed_clock_no_machine_speed_assumption(self):
        need=16*120+2*110+120;self.assertEqual(need,2260)
        S.ttl_gate(self.now+2860,self.now,2260,self.c,True)
        with self.assertRaisesRegex(ValueError,'TOKEN_REMAINING'):S.ttl_gate(self.now+2859,self.now,2260,self.c,True)
        with self.assertRaisesRegex(ValueError,'SQL_BUDGET'):S.ttl_gate(self.now+4000,self.now,2259,self.c,True)
        S.ttl_gate(self.now+3000,self.now,2400,self.c)
    def test_preflight_accepts_five_without_rewriting_historical_auxiliary(self):
        self.assertEqual(set(S.preflight_row(self.row(),self.c).values()),{'PASS'})
    def test_preflight_rejects_each_bad_safety_gate(self):
        for index,value in [(1,'wrong.invalid'),(2,'db-fieldvisit-uat'),(3,'1'),(4,'00'*16),(5,'0'),(6,'0'),(7,'1'),(8,'FALSE'),(10,'Session')]:
            with self.assertRaises(ValueError):S.preflight_row(self.row(**{str(index):value}),self.c)
    def test_marker_present_with_zero_user_tables_fails_closed(self):
        with self.assertRaisesRegex(ValueError,'PARTIAL_SCHEMA_OR_MARKER'):
            S.preflight_row(self.row(**{'21':'1'}),self.c)
    def test_partial_schema_without_user_tables_fails_closed(self):
        for index in range(11,21):
            with self.subTest(index=index),self.assertRaisesRegex(ValueError,'PARTIAL_SCHEMA_OR_MARKER'):
                S.preflight_row(self.row(**{str(index):'1'}),self.c)
    def test_unknown_or_missing_metadata_fails_closed(self):
        for value in ('', 'NULL', '?', '-1', '00'):
            with self.subTest(value=value),self.assertRaises(ValueError):
                S.preflight_row(self.row(**{'11':value}),self.c)
        with self.assertRaisesRegex(ValueError,'PREFLIGHT_SHAPE'):
            S.preflight_row(self.row()+self.row(),self.c)
        with self.assertRaisesRegex(ValueError,'PREFLIGHT_SHAPE'):
            S.preflight_row(b'|'.join(self.row().split(b'|')[:-1]),self.c)
    def test_one_select_invocation_budget_and_no_mutating_sql(self):
        sql=(R/'preflight.sql').read_text()
        self.assertEqual(h(sql.encode()),self.c['preflight_sha256'])
        self.assertEqual(sql.upper().count('SELECT '),13)
        self.assertNotRegex(sql.upper(),r'\b(CREATE|ALTER|DROP|INSERT|UPDATE|DELETE|MERGE|EXEC|GRANT|REVOKE)\b')
        self.assertEqual(1+16+2,19)
    def test_catalog_hash_and_hidden_disabled_metadata(self):
        tables=[{'table':str(i),'indexes':[],'checks':[],'foreignKeys':[],'triggers':[]} for i in range(69)]
        text=json.dumps(tables,separators=(',',':'));r=S.catalog_result((text[:50]+'\r\n'+text[50:]+'\n').encode());self.assertEqual(r['schema_sha256'],h(text.encode()))
        for key,obj in [('indexes',{'has_filter':1,'filter_definition':None}),('triggers',{'definition':None,'is_disabled':False}),('foreignKeys',{'is_disabled':False,'is_not_trusted':True}),('checks',{'is_disabled':True,'is_not_trusted':False})]:
            t=copy.deepcopy(tables);t[0][key]=[obj]
            with self.assertRaises(ValueError):S.catalog_result(json.dumps(t).encode())
    def test_original_adapter_bytes_remain_approved(self):
        self.assertEqual(h((R/'token_adapter.py').read_bytes()),self.c['adapter_sha256'])
        self.assertEqual(h((R/'install.py').read_bytes()),self.c['installer_sha256'])
        self.assertEqual(len(self.c['layers']),14);self.assertEqual(len(self.c['payload_sha256']),16)
    def test_once_exclusive_no_automatic_recreation(self):
        p=self.r/'once';S.private_write(p,b'synthetic')
        with self.assertRaises(FileExistsError):S.private_write(p,b'again')
        self.assertEqual(p.stat().st_mode&0o777,0o600)
    def test_child_failure_retains_exit_and_sanitizes(self):
        with self.assertRaises(S.ChildFailure) as e:S.child([sys.executable,'-c',"print('28000 18456 FAKE-SECRET private-user');raise SystemExit(7)"],{},time.monotonic()+60)
        self.assertEqual(e.exception.record['child_exit'],7);self.assertEqual(e.exception.record['sqlstate'],['28000']);self.assertNotIn('private-user',json.dumps(e.exception.record))
    def test_child_classifier_failure_retains_exit(self):
        import token_adapter
        with patch.object(token_adapter,'classify',side_effect=RuntimeError):
            with self.assertRaises(S.ChildFailure) as e:S.child([sys.executable,'-c','raise SystemExit(9)'],{},time.monotonic()+60)
        self.assertEqual(e.exception.record['child_exit'],9)
    def test_deadline_no_spawn(self):
        with patch.object(S.subprocess,'Popen') as spawn:
            with self.assertRaisesRegex(ValueError,'DEADLINE'):S.child(['never'],{},time.monotonic()-1)
            spawn.assert_not_called()
    def test_child_timeout_kills_process_no_retry(self):
        with patch.object(S,'kill_owned') as cleanup:
            with self.assertRaises(S.ChildFailure) as e:S.child([sys.executable,'-c','import time;time.sleep(60)'],{},time.monotonic()+60,cap=.05)
            self.assertEqual(e.exception.record['category'],'PROCESS_TIMEOUT');cleanup.assert_called_once()
    def test_bootstrap_chunked_binary_integrity_and_no_sql(self):
        z=self.r/'t.zip'
        with zipfile.ZipFile(z,'w') as w:w.writestr('debs/dummy.deb',b'x'*20000)
        with patch.object(T,'REMOTE',str(self.r/'copy')):source=T.bootstrap_source(base64.b64encode(z.read_bytes()).decode())
        self.assertLess(max(map(len,source.splitlines())),1000)
        p=subprocess.run([sys.executable,'-c',source],capture_output=True);self.assertEqual(p.returncode,0);self.assertEqual((self.r/'copy/debs/dummy.deb').read_bytes(),b'x'*20000)
    def test_independent_cleanup_process_and_watchdog(self):
        root=self.r/'operator';ram=self.r/'ram';root.mkdir();ram.mkdir();(ram/'dummy-token').write_bytes(b'FAKE')
        copyfile=self.r/'cleanup-test-copy.py';text=(R/'cleanup.py').read_text().replace("Path('/tmp/fv-pathb-install-d6c2208-once1')",'Path('+repr(str(root))+')').replace("Path('/dev/shm/fv-pathb-install-d6c2208-once1')",'Path('+repr(str(ram))+')');copyfile.write_text(text)
        sleeper=root/'sleeper.py';sleeper.write_text('import time,signal\nsignal.signal(signal.SIGTERM,signal.SIG_IGN)\ntime.sleep(60)\n')
        p=subprocess.Popen([sys.executable,str(sleeper)])
        try:
            # Expired deadline is deterministic; independent process escalates and cleans.
            q=subprocess.run([sys.executable,str(copyfile),'--watch',str(p.pid),str(time.monotonic()-1)],timeout=15)
            p.wait(timeout=5);self.assertIsNotNone(p.returncode);self.assertEqual(q.returncode,0)
            self.assertEqual(subprocess.run([sys.executable,str(copyfile),'--verify']).returncode,0)
        finally:
            if p.poll() is None:p.kill();p.wait()
    def test_transport_split_prompt_and_secret_echo_guard(self):
        script=self.r/'pty-fake.py';script.write_text("import sys,time\nsys.stdout.write('READY');sys.stdout.flush()\n")
        real=subprocess.Popen
        def spawn(_args,**kw):return real([sys.executable,str(script)],**kw)
        with patch.object(T.subprocess,'Popen',side_effect=spawn):s=T.Session(time.monotonic()+60)
        try:
            self.assertEqual(s.expect(b'READY',30),b'READY')
            s.buf=b'synthetic-secret';s.secrets=[b'synthetic-secret']
            with self.assertRaisesRegex(ValueError,'SECRET_ECHO'):s.expect(b'anything',30)
        finally:s.close()
    def test_management_is_get_only_and_wrong_subscription_holds(self):
        c=copy.deepcopy(self.c);c['subscription_sha256']=h(b'synthetic-sub')
        answers=[{'id':'synthetic-sub','tenantId':self.tenant},{'fullyQualifiedDomainName':c['server'],'publicNetworkAccess':'Disabled','minimalTlsVersion':'1.2'},[{'sid':self.oid}],[],{'name':c['app'],'state':'Running','virtualNetworkSubnetId':'synthetic-existing-subnet'}]
        with patch.object(T,'az',side_effect=answers) as a:
            f=T.management(c);self.assertEqual(f['pna'],'Disabled');self.assertEqual(a.call_count,5)
            self.assertTrue(all(args[0][0] in ['account','sql','webapp'] and not any(x in args[0] for x in ['update','create','delete','login']) for args,kw in a.call_args_list))
        with patch.object(T,'az',return_value={'id':'wrong','tenantId':self.tenant}):
            with self.assertRaisesRegex(ValueError,'ACCOUNT'):T.management(c)
    def test_transport_default_no_azure_or_token(self):
        p=subprocess.run([sys.executable,str(R/'transport.py'),'--package','nonexistent','--packages','nonexistent','--config-sha','not-execute'],capture_output=True,env={'PATH':'/no/azure'})
        self.assertEqual(p.returncode,0);self.assertIn(b'NO_AZURE_NO_TOKEN',p.stdout)
    def test_seal_failure_precedes_management_once_guard_and_token(self):
        with patch.object(S,'load_config',return_value=self.c), \
             patch('seal_verifier.verify',side_effect=ValueError('SEAL_HASH')) as binding, \
             patch.object(T,'management') as management:
            with self.assertRaisesRegex(ValueError,'SEAL_HASH'):
                T.execute('synthetic.zip','synthetic.seal','synthetic.debs',
                          'synthetic-approval','synthetic-approval',
                          self.c['blob_object_name'])
            binding.assert_called_once();management.assert_not_called()
    def full_operator(self,fail_at=None,short_ttl=False):
        """Real supervisor -> original installer -> approved adapter -> FAKE backend.
        Only cleanup TEST-COPY paths and synthetic config/package are changed.
        """
        root=self.r/'operator';ram=self.r/'ram';control=self.r/'controls';control.mkdir();marker=self.r/'home'/'once'
        c=copy.deepcopy(self.c)
        originals=(R/'install.py').read_bytes();tree=ast.parse(originals)
        guards=[n.args[0].value for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name) and n.func.id=='run' and n.args and isinstance(n.args[0],ast.Constant)]
        pro=[n.value.value for n in ast.walk(tree) if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='prologue' for t in n.targets)][0]
        blobs={x['file']:('-- SYNTHETIC ONLY '+str(i)+'\nSELECT '+str(i)+';\nGO\n').encode() for i,x in enumerate(c['layers'])}
        c['layers']=[{'file':n,'sha256':h(b)} for n,b in blobs.items()]
        plan={'candidateSourceSha':c['release'],'pathb-uat':{'server':c['server'],'database':c['database'],'schemaProfile':'PathBNewEmpty','sequence':[{'step':i+1,**x} for i,x in enumerate(c['layers'])]}}
        manifest=json.dumps(plan).encode();c['manifest_sha256']=h(manifest)
        c['payload_sha256']=[h(guards[0]),*[h(pro+b) for b in blobs.values()],h(guards[1])]
        package=self.r/'synthetic-package.zip'
        with zipfile.ZipFile(package,'w') as z:
            z.writestr('install.py',originals);z.writestr('standalone-install-manifest.json',manifest)
            for n,b in blobs.items():z.writestr(n,b)
        c['package_sha256']=h(package.read_bytes())
        for n in ['token_adapter.py','postcheck.sql','schema-fingerprint.sql']:(control/n).write_bytes((R/n).read_bytes())
        cleanup=(R/'cleanup.py').read_text().replace("Path('/tmp/fv-pathb-install-d6c2208-once1')",'Path('+repr(str(root))+')').replace("Path('/dev/shm/fv-pathb-install-d6c2208-once1')",'Path('+repr(str(ram))+')');(control/'cleanup.py').write_text(cleanup)
        tables=[{'table':str(i),'indexes':[],'checks':[],'foreignKeys':[],'triggers':[]} for i in range(69)]
        program='#!'+sys.executable+'\nimport sys,json\nfrom pathlib import Path\na=sys.argv[1:]\nif "-Q" in a:\n q=a[a.index("-Q")+1]\n if "FV_SQL" in q:\n  assert a[a.index("-w")+1]=="65535"\n  print('+repr(self.row().decode().strip())+')\n elif "FV_POST" in q:print("FV_POST|69|3|3|0|0|0")\n else:print('+repr(json.dumps(tables,separators=(',',':')))+')\nelse:\n sys.stdin.buffer.read()\n p=Path('+repr(str(root/'fake-count'))+')\n n=int(p.read_text())+1 if p.exists() else 1\n p.write_text(str(n))\n if n=='+repr(fail_at)+':print("42000 56611 FAKE-SECRET");sys.exit(7)\n'
        def stage(_c,_p):
            binary=root/'tools'/'fake-sqlcmd';binary.parent.mkdir();binary.write_text(program);binary.chmod(0o700)
            return binary,{'PATH':'/usr/bin:/bin','LANG':'C.UTF-8'}
        facts={'server':c['server'],'database':c['database'],'app':c['app'],'pna':'Disabled','firewall_count':0,'minimum_tls':'1.2','owner_admin_match':True,'private_sql_ip':'10.90.2.4','observed_at':time.time()}
        claims={**self.claims,'nbf':time.time()-60,'exp':time.time()+(10 if short_ttl else 3600)}
        stream=io.StringIO()
        with patch.object(S,'R',control),patch.object(S,'ROOT',root),patch.object(S,'RAM',ram),patch.object(S,'MARKER',marker),patch.object(S,'load_config',return_value=c),patch.object(S,'stage_tools',side_effect=stage),patch.object(S.socket,'getaddrinfo',return_value=[(2,1,6,'',('10.90.2.4',1433))]),patch.object(S.getpass,'getpass',side_effect=['synthetic-dummy',fake_token(claims)]),patch('builtins.input',return_value=json.dumps(facts)),contextlib.redirect_stdout(stream):
            rc=S.run(str(package),'unused','unused','synthetic-approval','synthetic-approval')
        result=json.loads(stream.getvalue().split('FV_FINAL_RESULT=')[-1]);self.assertTrue(marker.exists());self.assertFalse(root.exists());self.assertFalse(ram.exists())
        self.assertEqual(h((control/'token_adapter.py').read_bytes()),self.c['adapter_sha256'])
        self.assertNotIn('FAKE-SECRET',stream.getvalue());self.assertNotIn(fake_token(claims),stream.getvalue())
        return rc,result
    def test_full_supervisor_success_all16_and_two_postchecks_synthetic(self):
        rc,r=self.full_operator();self.assertEqual(rc,0);self.assertEqual(len(r['calls']),16);self.assertEqual(r['postcheck_calls'],2);self.assertEqual(r['sql_preflight_calls'],1);self.assertEqual(r['installer_invocations'],1);self.assertEqual(r['schema']['tables'],69)
    def test_full_supervisor_partial_failure_stops_and_cleans_synthetic(self):
        rc,r=self.full_operator(fail_at=8);self.assertEqual(rc,1);self.assertEqual(len(r['calls']),8);self.assertEqual(r['calls'][-1]['child_exit'],7);self.assertEqual(r['calls'][-1]['sqlstate'],['42000']);self.assertEqual(r['failure']['child_exit'],1);self.assertNotIn('postcheck_calls',r)
    def test_full_supervisor_short_token_no_sql_synthetic(self):
        rc,r=self.full_operator(short_ttl=True);self.assertEqual(rc,1);self.assertEqual(r['sql_preflight_calls'],0);self.assertEqual(r['installer_invocations'],0);self.assertFalse(r['ddl_started']);self.assertEqual(r['failure']['category'],'TOKEN_TTL')
if __name__=='__main__':unittest.main(verbosity=2)
