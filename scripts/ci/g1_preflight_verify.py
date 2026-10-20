"""Bounded NONDEPLOY verifier; never opens shared SQL or obtains Azure identity."""
import hashlib,json,os,pathlib,re,secrets,subprocess,sys,time
P=pathlib.Path('docs/release/g1-minimal-preflight')
BASE='88c21921860888f531a0962867f916c307452bbc'
DIAGNOSTIC_BASE='b093160434a0314bca1f0d7a2d40cc25021a2b84'
RC='f29affb3299828f45b1f131321779e8bea2f86a8'
def sha(b):return hashlib.sha256(b).hexdigest()
def git(*args):return subprocess.check_output(['git',*args]).decode().strip()
def check():
 assert os.environ['GITHUB_SHA']==git('rev-parse','HEAD')
 assert git('rev-parse','HEAD^')==DIAGNOSTIC_BASE
 assert set(git('diff','--name-only',DIAGNOSTIC_BASE,'HEAD').splitlines())=={'.github/workflows/leader-uat010-rc-nondeploy.yml','scripts/ci/g1_preflight_verify.py','scripts/ci/test_g1_minimal_preflight.ps1'}
 assert os.environ['GITHUB_REPOSITORY']=='Terry4410/field-visit-mileage-system'
 assert os.environ['GITHUB_REF']=='refs/heads/work/g1-uat-migration-readiness-20261020'
 assert os.environ['GITHUB_EVENT_NAME']=='push'
 assert not any(os.environ.get(x) for x in ('UAT_SQL_CONNECTION','PRODUCTION_SQL_CONNECTION'))
 assert not any(os.environ.get(x,'').lower()=='true' for x in ('ACTIONS_STEP_DEBUG','ACTIONS_RUNNER_DEBUG'))
 permitted={'.github/workflows/leader-uat010-rc-nondeploy.yml','.github/workflows/g1-sql-lock-root-cause-diagnostic.yml','scripts/ci/g1_preflight_verify.py','scripts/ci/test_g1_minimal_preflight.ps1'}
 files=git('diff','--name-only',BASE,'HEAD').splitlines()
 assert files and all(f in permitted or f.startswith(str(P)+'/') or f.startswith('scripts/ci/g1-preflight-model/') for f in files)
 assert not git('diff','--name-only',BASE,'HEAD','--','backend','frontend','database','docs/release/optionc-canonical-sql-draft','docs/release/g1-execution/promotion')
 assert sha((P/'manifest.json').read_bytes())=='63c81e16359aa82112d4f19f6e6eb3cdcaeece136e93196b11f12731cda3947b'
 assert sha((P/'SHA256SUMS.txt').read_bytes())=='5fa0e435908499db687398843681efdfb1b9f77689bb6b7190b522add54da75d'
 for line in (P/'SHA256SUMS.txt').read_text().splitlines():
  h,n=line.split('  ',1);assert pathlib.PurePosixPath(n).name==n and sha((P/n).read_bytes())==h
 reference=json.loads((P/'necessary-reference.json').read_text())
 for f,h in reference['inputs'].items():assert sha(subprocess.check_output(['git','show',RC+':'+f]))==h and sha(pathlib.Path(f).read_bytes())==h
 print('PreflightSource=PASS; AcceptedDelta=UNCHANGED; ApplicationSql=UNCHANGED;')
def provider():
 check();assert os.environ['GITHUB_ACTIONS']=='true'
 password=secrets.token_urlsafe(30)+'!G1a9'
 conn=f'Server=localhost,1433;Database=master;User Id=sa;Password={password};Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;ConnectRetryCount=0'
 print('::add-mask::'+password,flush=True);print('::add-mask::'+conn,flush=True)
 env=pathlib.Path(os.environ['RUNNER_TEMP'])/'g1-preflight-sql.env'
 env.write_text('ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_SA_PASSWORD='+password+'\n');env.chmod(0o600)
 try:subprocess.run(['docker','run','-d','--name','g1-preflight-sql','-p','127.0.0.1:1433:1433','--env-file',str(env),'mcr.microsoft.com/mssql/server:2022-latest'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,check=True,timeout=90)
 finally:env.unlink(missing_ok=True)
 # Readiness polling only for a new LOCAL disposable service, not query retries.
 for _ in range(18):
  try:r=subprocess.run(['docker','exec','-e','SQLCMDPASSWORD='+password,'g1-preflight-sql','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-l','3','-Q','SELECT 1'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=5)
  except subprocess.TimeoutExpired:r=None
  if r is not None and r.returncode==0:break
  time.sleep(5)
 else:raise ValueError('local readiness')
 with open(os.environ['GITHUB_ENV'],'a') as f:f.write('E1_R1_SQL_CONNECTION='+conn+'\n')
 print('PreflightProvider=PASS; Target=LOCAL_SQL2022_ONLY;')
def test():
 check()
 r=subprocess.run(['pwsh','-NoProfile','-File','scripts/ci/test_g1_minimal_preflight.ps1'],capture_output=True,text=True,timeout=900)
 for line in r.stdout.splitlines():
  if re.fullmatch(r'PreflightCase=[A-Za-z0-9_]+; Status=(PASS|STOP);(?: Details=SUPPRESSED;)?',line) or re.fullmatch(r'PreflightHarnessLine=[0-9]+;',line) or line=='PreflightCleanup=STOP; Details=SUPPRESSED;':print(line)
 assert r.returncode==0
 report=json.loads(pathlib.Path('artifacts/preflight-sql.json').read_text(encoding='utf-8-sig'))
 assert report['source']==os.environ['GITHUB_SHA'] and report['passed'] and len(report['cases'])==18
 assert all(x['result']=='PASS' for x in report['cases'])
 assert report['liveSql'] is False and report['apiPrincipalBinding']=='UNKNOWN'
 print('PreflightEvidence=PASS; LiveExecution=NOT_AUTHORIZED; G1=HOLD;')
def model():
 check()
 r=subprocess.run(['dotnet','run','--project','scripts/ci/g1-preflight-model/G1PreflightModel.csproj','--configuration','Release'],capture_output=True,text=True,timeout=600)
 for line in r.stdout.splitlines():
  if line in ('PreflightModel=PASS; ConnectionOpened=FALSE; ActualSchema=UNKNOWN;','PreflightModel=STOP; Details=SUPPRESSED;'):print(line)
 assert r.returncode==0
def diagnose():
 check()
 r=subprocess.run(['pwsh','-NoProfile','-File','scripts/ci/test_g1_minimal_preflight.ps1','-FirstStopOnly'],capture_output=True,text=True,timeout=240)
 allowed={'COLLECTOR_EXIT_UNEXPECTED','EXPECTED_STAGE_SEQUENCE_MISMATCH','EVIDENCE_MISSING_OR_INVALID','CATALOG_ASSERTION_MISMATCH','PERMISSIONS_ASSERTION_MISMATCH','HARNESS_FAILURE_OTHER'}
 for line in r.stdout.splitlines():
  if line.startswith('PreflightDiagnostic=') and line.removeprefix('PreflightDiagnostic=').removesuffix(';') in allowed:print(line)
  if line in ('PreflightCase=Collector010FindingsStillCollectPermissions; Status=PASS;','PreflightCase=Collector010FindingsStillCollectPermissions; Status=STOP; Details=SUPPRESSED;'):print(line)
 report=json.loads(pathlib.Path('artifacts/preflight-sql.json').read_text(encoding='utf-8-sig'))
 assert report['source']==os.environ['GITHUB_SHA'] and report['diagnosticOnly'] is True
 assert report['diagnosis'] in allowed|{'NONE'} and len(report['cases'])<=1
 assert report['liveSql'] is False and report['apiPrincipalBinding']=='UNKNOWN'
 # A classified STOP is retained as a failed test, never converted to suite PASS.
 assert r.returncode==0 and report['passed']
if __name__=='__main__':
 try:{'check':check,'provider':provider,'test':test,'model':model,'diagnose':diagnose}[sys.argv[1]]()
 except Exception:raise SystemExit('PreflightVerification=STOP; Details=SUPPRESSED;') from None
