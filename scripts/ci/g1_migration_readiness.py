"""NONDEPLOY promotion dossier. No shared SQL/Azure execution path."""
import hashlib,json,os,re,secrets,subprocess,sys,time
from pathlib import Path
RC='f29affb3299828f45b1f131321779e8bea2f86a8'
BRANCH='refs/heads/work/g1-uat-migration-readiness-20261020'
REG=Path('docs/release/g1-execution/registry.json')
ALLOWED={'.github/workflows/leader-uat010-rc-nondeploy.yml','scripts/ci/g1_migration_readiness.py','scripts/ci/test_g1_migration_readiness.ps1','docs/release/g1-execution/registry.json','docs/release/g1-execution/READINESS.md','docs/release/g1-execution/AUTHORIZATION-DRAFT.md','.github/workflows/g1-sql-lock-root-cause-diagnostic.yml','docs/release/g1-minimal-preflight/01-target.sql','docs/release/g1-minimal-preflight/02-marker.sql','docs/release/g1-minimal-preflight/03-catalog.sql','docs/release/g1-minimal-preflight/04-permissions.sql','docs/release/g1-minimal-preflight/GAP-CLOSURE.md','docs/release/g1-minimal-preflight/README.md','docs/release/g1-minimal-preflight/SCHEMA010-AND-VERIFICATION.md','docs/release/g1-minimal-preflight/SHA256SUMS.txt','docs/release/g1-minimal-preflight/collect.ps1','docs/release/g1-minimal-preflight/derive_reference.py','docs/release/g1-minimal-preflight/manifest.json','docs/release/g1-minimal-preflight/necessary-reference.json','docs/release/g1-minimal-preflight/provenance.json','docs/release/g1-minimal-preflight/requirements.json','docs/release/g1-minimal-preflight/test_contract.py','docs/release/g1-minimal-preflight/test_phase_separation.py','docs/release/g1-minimal-preflight/test_reference.py','docs/release/g1-minimal-preflight/uat_schema_fidelity_evidence.ps1','docs/release/g1-minimal-preflight/validate.py','docs/release/g1-minimal-preflight/validation-results.json','scripts/ci/g1-preflight-model/G1PreflightModel.csproj','scripts/ci/g1-preflight-model/Program.cs','scripts/ci/g1_preflight_verify.py','scripts/ci/g1_sql_lock_root_cause_diagnostic.ps1','scripts/ci/test_g1_minimal_preflight.ps1'}
def git(*a):return subprocess.check_output(['git',*a]).decode().strip()
def sha(b):return hashlib.sha256(b).hexdigest()
def verify():
 assert os.environ['GITHUB_SHA']==git('rev-parse','HEAD') and re.fullmatch('[a-f0-9]{40}',os.environ['GITHUB_SHA'])
 assert os.environ['GITHUB_REF']==BRANCH and os.environ['GITHUB_REPOSITORY']=='Terry4410/field-visit-mileage-system'
 subprocess.run(['git','merge-base','--is-ancestor',RC,'HEAD'],check=True)
 assert all(f in ALLOWED or f in {'scripts/ci/g1_promotion.py','scripts/ci/test_g1_promotion.ps1'} or f.startswith('docs/release/g1-execution/promotion/') for f in git('diff','--name-only',RC,'HEAD').splitlines())
 assert not git('diff','--name-only',RC,'HEAD','--','backend','frontend','database','docs/release/optionc-canonical-sql-draft')
 assert not os.environ.get('UAT_SQL_CONNECTION') and not os.environ.get('PRODUCTION_SQL_CONNECTION')
 d=json.loads(REG.read_text());assert d['rcSource']==RC and d['registryDecision']=='OWNER_DECISION_PENDING' and d['promotedSql'] is None
 for entry in d['sequence']:
  assert entry['approval']=='PROPOSED_NOT_ASSIGNED_NOT_EXECUTABLE'
  for f in entry['files']:assert sha(Path(f['path']).read_bytes())==f['sha256']
 f=d['reusedFixture'];assert sha(Path(f['path']).read_bytes())==f['sha256']
 print('PreparationSource=PASS; Application=UNCHANGED; CandidateSql=UNCHANGED; UatExecution=NOT_AUTHORIZED;')
def sql():
 verify();assert os.environ.get('GITHUB_ACTIONS')=='true'
 password=secrets.token_urlsafe(30)+'!G1a9'
 conn=f'Server=localhost,1433;Database=master;User Id=sa;Password={password};Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;ConnectRetryCount=0'
 print('::add-mask::'+password,flush=True);print('::add-mask::'+conn,flush=True)
 envfile=Path(os.environ['RUNNER_TEMP'])/'g1-preparation-sql.env'
 envfile.write_text('ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_SA_PASSWORD='+password+'\n');envfile.chmod(0o600)
 try:subprocess.run(['docker','run','-d','--name','g1-preparation-sql','-p','127.0.0.1:1433:1433','--env-file',str(envfile),'mcr.microsoft.com/mssql/server:2022-latest'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 finally:envfile.unlink(missing_ok=True)
 for _ in range(18):
  try:
   r=subprocess.run(['docker','exec','-e','SQLCMDPASSWORD='+password,'g1-preparation-sql','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-l','3','-Q','SELECT 1'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=5)
   if r.returncode==0:break
  except subprocess.TimeoutExpired:pass
  time.sleep(5)
 else:raise ValueError('local readiness')
 with open(os.environ['GITHUB_ENV'],'a') as f:f.write('E1_R1_SQL_CONNECTION='+conn+'\n')
 print('DisposableProvider=PASS; Target=LOCAL_SQL_SERVER_2022;')
def faults():
 verify()
 r=subprocess.run(['pwsh','-NoProfile','-File','scripts/ci/test_g1_migration_readiness.ps1'],capture_output=True,text=True,timeout=900)
 for line in r.stdout.splitlines():
  if re.fullmatch(r'HarnessDiagnostic=STOP; Line=[0-9]+; ProviderCode=-?[0-9]+;',line):print(line)
  if re.fullmatch(r'MigrationCase=[A-Za-z0-9_]+; Status=(PASS|KNOWN_PROMOTION_GAP|STOP);(?: Details=SUPPRESSED;)?',line):print(line)
 assert r.returncode==0
def finalize():
 verify()
 import xml.etree.ElementTree as ET
 p=Path('artifacts');m=json.loads((p/'migration-cases.json').read_text())
 assert m['sourceSha']==os.environ['GITHUB_SHA'] and m['rcSource']==RC
 assert len(m['cases'])==55 and all(c['result'] in ['PASS','KNOWN_PROMOTION_GAP'] for c in m['cases'])
 assert set(m['knownPromotionGaps'])=={'014_MARKER_CAN_EXIST_WITHOUT_BACKFILL','020_OBJECTS_WITHOUT_MARKER_PREFLIGHT_ACCEPTS','011_NULL_PREDECESSOR_ACCEPTED','014_IGNORES_NEGATIVE_APPLOCK_RETURN'}
 trx=p/'compatibility.trx';raw=trx.read_bytes();root=ET.fromstring(raw)
 rows=[e for e in root.iter() if e.tag.endswith('UnitTestResult')]
 assert len(rows)==5 and all(e.attrib['outcome']=='Passed' and 'full_010_to_020_chain_runs_' in e.attrib['testName'] for e in rows)
 import importlib.util
 spec=importlib.util.spec_from_file_location('g1_evidence','scripts/ci/g1_rc_evidence.py');old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)
 trx.write_bytes(old.sanitized_trx(raw))
 m['compatibilityTests']=5;m['preparationSourceSha']=os.environ['GITHUB_SHA'];m['parent']=git('show','-s','--format=%P','HEAD');m['tree']=git('rev-parse','HEAD^{tree}')
 m['filesChanged']=git('diff','--name-only',RC,'HEAD').splitlines();m['reusedRcEvidence']=json.loads(REG.read_text())['rcArtifact']
 m['registrySha256']=sha(REG.read_bytes());m['evidenceSha256']={str(f.relative_to(p)):sha(f.read_bytes()) for f in p.rglob('*') if f.is_file() and f.name!='migration-cases.json'}
 m['decision']='G1_UAT_EXECUTION_PACKAGE_HOLD_WITH_EXACT_GAPS'
 (p/'migration-cases.json').write_text(json.dumps(m,indent=2)+'\n')
 print('PreparationEvidence=PASS; UatPromotion=HOLD_WITH_EXACT_GAPS; SharedOperations=NOT_EXECUTED;')
if __name__=='__main__':
 try:{'verify':verify,'sql':sql,'faults':faults,'finalize':finalize}[sys.argv[1]]()
 except Exception:raise SystemExit('G1_PREPARATION_STOP phase='+sys.argv[1]+' details=SUPPRESSED') from None
