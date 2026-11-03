#!/usr/bin/env python3
"""Synthetic tests only. No Azure SDK, token acquisition, socket or SQL client."""
import hashlib, importlib.util, json, os, shutil, subprocess, sys, tempfile, time, unittest
from pathlib import Path
from unittest.mock import patch
ROOT=Path(__file__).resolve().parent
def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
A=load('adapter',ROOT/'token_adapter.py');I=load('original',ROOT/'install.py')
def sha(b):return hashlib.sha256(b).hexdigest()
class Compatibility(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.r=Path(self.tmp.name)
        self.token=self.r/'dummy-token';self.token.write_bytes('FAKE-NOT-A-JWT'.encode('utf-16le'));self.token.chmod(0o600)
        self.state=self.r/'state.json';self.state.write_text(json.dumps({'next':0,'terminal':False,'records':[]}));self.state.chmod(0o600)
        self.fake=self.r/'fake-sqlcmd'
        self.fake.write_text('#!'+sys.executable+'\nimport json,os,sys,time\nfrom pathlib import Path\na=sys.argv[1:]\nassert a[-4:-2]==["-Nm","-G"]\nassert a[-2]=="-P"\nassert "-C" not in a and "-U" not in a\nassert not any(k.startswith(("SQLCMD","OSQL","AZURE_")) for k in os.environ)\nassert Path(a[-1]).read_bytes()=="FAKE-NOT-A-JWT".encode("utf-16le")\nb=sys.stdin.buffer.read()\nif b==b"SLEEP": time.sleep(3)\nif b==b"ERROR": print("28000 18456 private-user FAKE-NOT-A-JWT",file=sys.stderr);sys.exit(7)\nassert b==b"SELECT synthetic;\\r\\nGO\\n-- "+bytes([0xe4,0xb8,0xad])+b"\\n" or b in [b"OK",b"SLEEP"]\n')
        self.fake.chmod(0o700)
        self.raw=b'SELECT synthetic;\r\nGO\n-- '+bytes([0xe4,0xb8,0xad])+b'\n'
        # No near-term wall clock deadline in synthetic success fixtures. Explicit
        # clock/deadline/timeout tests below independently exercise bounded behavior.
        self.cfg={'token_file':str(self.token),'binary':str(self.fake),'binary_sha256':sha(self.fake.read_bytes()),'state_file':str(self.state),'deadline':253402300799,'call_timeout':30,'payload_sha256':[sha(self.raw)]}
    def tearDown(self):self.tmp.cleanup()
    def call(self,raw=None,argv=None):
        with patch.dict(os.environ,{},clear=True):return A.invoke(self.cfg,A.EXPECTED if argv is None else argv,self.raw if raw is None else raw)
    def state_data(self):return json.loads(self.state.read_text())
    def test_bytes_stdin_token_file_tls_and_argv(self):
        self.assertEqual(self.call(),0);self.assertEqual(self.state_data()['records'][0]['input_sha256'],sha(self.raw))
        self.assertNotIn('FAKE-NOT-A-JWT',self.state.read_text())
    def test_error_exit_sqlstate_redaction_and_terminal(self):
        self.cfg['payload_sha256']=[sha(b'ERROR')];self.assertEqual(self.call(b'ERROR'),7)
        r=self.state_data()['records'][0];self.assertEqual(r['child_exit'],7);self.assertEqual(r['sqlstate'],['28000']);self.assertEqual(r['native'],['18456'])
        self.assertNotIn('private-user',self.state.read_text());self.assertNotIn('FAKE-NOT-A-JWT',self.state.read_text())
        with self.assertRaises(ValueError):self.call(b'ERROR')
    def test_classifier_failure_preserves_original_exit(self):
        self.cfg['payload_sha256']=[sha(b'ERROR')]
        with patch.object(A,'classify',side_effect=RuntimeError):self.assertEqual(self.call(b'ERROR'),7)
        self.assertEqual(self.state_data()['records'][0]['child_exit'],7)
    def test_timeout_kills_child_and_is_terminal(self):
        self.cfg.update(payload_sha256=[sha(b'SLEEP')],call_timeout=.05)
        self.assertEqual(self.call(b'SLEEP'),124);self.assertTrue(self.state_data()['terminal']);self.assertTrue(self.state_data()['records'][0]['timeout'])
    def test_replay_blocked(self):
        self.assertEqual(self.call(),0)
        with self.assertRaises(ValueError):self.call()
    def test_deadline_before_process(self):
        self.cfg['deadline']=time.time()-1
        with self.assertRaises(ValueError):self.call()
        self.assertEqual(self.state_data()['next'],0)
    def test_bad_target_auth_tls_options_rejected(self):
        for args in [A.EXPECTED+['-C'],A.EXPECTED+['-U','someone'],A.EXPECTED+['-P','secret'],[x.replace('db-fieldvisit-pathb-uat','db-fieldvisit-uat') for x in A.EXPECTED],A.EXPECTED[:-2]]:
            with self.assertRaises(ValueError):self.call(argv=args)
    def test_wrong_payload_and_binary_rejected(self):
        with self.assertRaises(ValueError):self.call(b'changed')
        self.cfg['binary_sha256']='0'*64
        with self.assertRaises(ValueError):self.call()
    def test_token_permissions_and_symlink_rejected(self):
        self.token.chmod(0o644)
        with self.assertRaises(ValueError):self.call()
        self.token.chmod(0o600);p=self.r/'link';p.symlink_to(self.token);self.cfg['token_file']=str(p)
        with self.assertRaises(ValueError):self.call()
    def test_ambient_identity_rejected(self):
        for k in ['SQLCMDUSER','SQLCMDPASSWORD','OSQLUSER','OSQLPASSWORD','SQLCMDINI']:
            with patch.dict(os.environ,{k:'synthetic'},clear=True):
                with self.assertRaises(ValueError):A.invoke(self.cfg,A.EXPECTED,self.raw)
    def test_single_writer_lock(self):
        import fcntl
        with self.state.open('r+') as f:
            fcntl.flock(f,fcntl.LOCK_EX|fcntl.LOCK_NB)
            with self.assertRaises(BlockingIOError):self.call()
    def test_adapter_cli_suppresses_raw_output(self):
        self.cfg['payload_sha256']=[sha(b'ERROR')];cfg=self.r/'config';cfg.write_text(json.dumps(self.cfg));cfg.chmod(0o600)
        p=subprocess.run([sys.executable,str(ROOT/'token_adapter.py'),*A.EXPECTED],input=b'ERROR',capture_output=True,env={'FV_ADAPTER_CONFIG':str(cfg)})
        self.assertEqual(p.returncode,7);self.assertEqual(p.stdout+p.stderr,b'')
    def synthetic_installer(self,fail=None,timeout=False,dry=False,bad_auth=False):
        files=[]
        for n in range(14):
            p=self.r/f'layer{n:02}.sql';p.write_bytes(f'-- SYNTHETIC LAYER {n}\nSELECT {n};\nGO\n'.encode());files.append({'file':p.name,'sha256':sha(p.read_bytes())})
        plan={'pathb-uat':{'server':A.SERVER,'database':A.DB,'schemaProfile':'PathBNewEmpty','sequence':files}}
        mf=self.r/'standalone-install-manifest.json';mf.write_text(json.dumps(plan));calls=[]
        def fake_run(args,**kw):
            self.assertEqual(args,['sqlcmd',*A.EXPECTED]);self.assertEqual(kw['timeout'],120)
            self.assertEqual(kw['stdout'],subprocess.DEVNULL);self.assertEqual(kw['stderr'],subprocess.DEVNULL)
            calls.append(kw['input'])
            if timeout and len(calls)==fail:raise subprocess.TimeoutExpired(args,120)
            return subprocess.CompletedProcess(args,7 if len(calls)==fail else 0)
        env={'FIELDVISIT_APPROVED_INSTALL_MANIFEST':('0'*64 if bad_auth else sha(mf.read_bytes()))}
        args=['install.py','--root',str(self.r),'--target','pathb-uat']+([] if dry else ['--execute'])
        with patch.dict(os.environ,env,clear=True),patch.object(sys,'argv',args),patch.object(I.subprocess,'run',side_effect=fake_run):
            error=None
            try:I.main()
            except Exception as e:error=type(e).__name__
        return calls,error,files
    def test_original_16_calls_14_layer_order_prologue(self):
        calls,error,files=self.synthetic_installer();self.assertIsNone(error);self.assertEqual(len(calls),16)
        self.assertIn(b'EMPTY_DATABASE_REQUIRED',calls[0]);self.assertIn(b'CONSTRAINT_TRUST_FAILED',calls[-1])
        for n,x in enumerate(files):
            self.assertTrue(calls[n+1].endswith((self.r/x['file']).read_bytes()));self.assertTrue(calls[n+1].startswith(b'SET ANSI_NULLS ON;'))
    def test_original_stops_at_every_failure_including_all14layers(self):
        for n in range(1,17):
            calls,error,_=self.synthetic_installer(fail=n);self.assertEqual(len(calls),n);self.assertEqual(error,'ValueError')
    def test_original_timeout_stops_no_next_layer(self):
        calls,error,_=self.synthetic_installer(fail=8,timeout=True);self.assertEqual(len(calls),8);self.assertEqual(error,'TimeoutExpired')
    def test_original_dryrun_and_authorization_no_child(self):
        calls,error,_=self.synthetic_installer(dry=True);self.assertFalse(calls);self.assertIsNone(error)
        calls,error,_=self.synthetic_installer(bad_auth=True);self.assertFalse(calls);self.assertEqual(error,'ValueError')
    def test_outer_once_and_independent_cleanup_synthetic(self):
        marker=self.r/'once';marker.open('x').close()
        with self.assertRaises(FileExistsError):marker.open('x')
        tools=self.r/'tools';tools.mkdir()
        try:raise RuntimeError('synthetic failure')
        except RuntimeError:pass
        finally:shutil.rmtree(tools);self.token.unlink()
        p=subprocess.run([sys.executable,'-c','from pathlib import Path;import sys;sys.exit(int(any(Path(x).exists() for x in sys.argv[1:])))',str(tools),str(self.token)])
        self.assertEqual(p.returncode,0);self.assertTrue(marker.exists())
    def test_full_subprocess_chain_unmodified_installer_adapter_fake_backend(self):
        # This invokes original main in a fresh process, with no monkeypatch.
        calls,error,files=self.synthetic_installer(dry=True)
        self.assertFalse(calls);self.assertIsNone(error)
        mf=self.r/'standalone-install-manifest.json'
        sql=[]
        def capture(args,**kw):
            sql.append(kw['input']);return subprocess.CompletedProcess(args,0)
        with patch.object(I.subprocess,'run',side_effect=capture),patch.object(sys,'argv',['install.py','--root',str(self.r),'--target','pathb-uat','--execute']),patch.dict(os.environ,{'FIELDVISIT_APPROVED_INSTALL_MANIFEST':sha(mf.read_bytes())},clear=True):I.main()
        self.assertEqual(len(sql),16)
        # A fake accepting backend still asserts exact options and stdin digest
        # is checked by the actual adapter for every original invocation.
        self.fake.write_text('#!'+sys.executable+'\nimport sys\na=sys.argv[1:]\nassert a=='+repr(A.EXPECTED[:-2]+['-Nm','-G','-P',str(self.token)])+'\nsys.stdin.buffer.read()\n')
        self.cfg.update(binary_sha256=sha(self.fake.read_bytes()),payload_sha256=[sha(x) for x in sql])
        cfg=self.r/'config.json';cfg.write_text(json.dumps(self.cfg));cfg.chmod(0o600)
        bindir=self.r/'bin';bindir.mkdir();shim=bindir/'sqlcmd'
        shim.write_text('#!'+sys.executable+'\nimport runpy\nrunpy.run_path('+repr(str(ROOT/'token_adapter.py'))+',run_name="__main__")\n');shim.chmod(0o700)
        p=subprocess.run([sys.executable,str(ROOT/'install.py'),'--root',str(self.r),'--target','pathb-uat','--execute'],capture_output=True,env={'PATH':str(bindir),'FV_ADAPTER_CONFIG':str(cfg),'FIELDVISIT_APPROVED_INSTALL_MANIFEST':sha(mf.read_bytes())})
        self.assertEqual(p.returncode,0,p.stdout);self.assertIn(b'FINAL_FOURTEEN_LAYER_SCOPE_PASS',p.stdout)
        state=self.state_data();self.assertEqual(state['next'],16);self.assertTrue(state['terminal']);self.assertEqual([x['input_sha256'] for x in state['records']],[sha(x) for x in sql])
if __name__=='__main__':unittest.main(verbosity=2)
