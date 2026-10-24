"""G1 isolated integration evidence only. No Azure/shared SQL/deployment path."""
import hashlib,json,os,re,secrets,subprocess,sys,time
from pathlib import Path
P='4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc'
A='138cd8352ab180254ed83bce4a77acb3fdfc948f'
BASE='b1a507c90c577fb2c0cdd6a98ef33481d3a5f790'
BRANCH='refs/heads/work/g1-uat-rc-integration-20261020'
ART=Path('artifacts')
def git(*args):return subprocess.check_output(['git',*args])
def digest(raw):return hashlib.sha256(raw).hexdigest()
def source():
 s=os.environ['GITHUB_SHA']
 assert re.fullmatch('[0-9a-f]{40}',s) and git('rev-parse','HEAD').decode().strip()==s
 assert os.environ['GITHUB_REF']==BRANCH
 return s

def verify():
 source()
 for ancestor in (P,A):subprocess.run(['git','merge-base','--is-ancestor',ancestor,'HEAD'],check=True)
 protected=git('diff','--name-only',BASE,P).decode().splitlines()+[
  'docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql',
  'scripts/ci/uat_schema_fidelity_evidence.ps1',
  '.github/workflows/api-azure.yml','.github/workflows/frontend-pages.yml']
 for path in protected:assert Path(path).read_bytes()==git('show',P+':'+path)
 exceptions={'backend/src/FieldVisit.Api/Program.cs','backend/src/FieldVisit.Api/G1ReportExportGuard.cs',
  'backend/tests/FieldVisit.Application.Tests/G1ReportExportHttpSqlTests.cs'}
 assert set(git('diff','--name-only',A,'HEAD','--','backend','frontend').decode().splitlines()) <= exceptions
 for path in git('ls-tree','-r','--name-only',A,'backend','frontend').decode().splitlines():
  if path not in exceptions:assert Path(path).read_bytes()==git('show',A+':'+path)
 assert not Path('database/migrations/1800_013').exists()
 config=json.loads(Path('backend/src/FieldVisit.Api/appsettings.json').read_text())
 assert not config['PackageB']['B3']['Enabled'] and not config['OptionC']['Approval']['Enabled'] and not config['OptionC']['Candidate']['Enabled']
 assert 'true' not in Path('frontend/src/optionc-approval-flags.ts').read_text().lower()
 assert not os.environ.get('UAT_SQL_CONNECTION') and not os.environ.get('PRODUCTION_SQL_CONNECTION')
 print('G1_SOURCE_CLOSURE_PASS protectedDiagnostics=UNCHANGED acceptedApplication=PINNED flags=OFF')

def sql():
 verify();assert os.environ.get('GITHUB_ACTIONS')=='true'
 pwd=secrets.token_urlsafe(30)+'!G1a9'
 conn=f'Server=localhost,1433;Database=master;User Id=sa;Password={pwd};Encrypt=False;TrustServerCertificate=True;Connect Timeout=5'
 print('::add-mask::'+pwd,flush=True);print('::add-mask::'+conn,flush=True)
 envfile=Path(os.environ['RUNNER_TEMP'])/'g1-sql.env'
 envfile.write_text('ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_SA_PASSWORD='+pwd+'\n');envfile.chmod(0o600)
 try:subprocess.run(['docker','run','-d','--name','g1-disposable-sql','-p','127.0.0.1:1433:1433','--env-file',str(envfile),'mcr.microsoft.com/mssql/server:2022-latest'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 finally:envfile.unlink(missing_ok=True)
 for _ in range(18):
  try:
   result=subprocess.run(['docker','exec','-e','SQLCMDPASSWORD='+pwd,'g1-disposable-sql','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-l','3','-Q','SELECT 1'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=5)
   if result.returncode==0:break
  except subprocess.TimeoutExpired:pass
  time.sleep(5)
 else:raise ValueError('local SQL readiness')
 with open(os.environ['GITHUB_ENV'],'a') as f:f.write('LEADER_RC_SQL_CONNECTION='+conn+'\n')
 print('G1_DISPOSABLE_SQL_READY localOnly=true provider=SQLServer2022')

def manifest():
 s=source();files={}
 for kind in ('api','frontend'):
  folder=ART/kind;assert folder.is_dir()
  assert (folder/'source-sha.txt').read_text().strip()==s
  for p in folder.rglob('*'):
   assert not p.is_symlink()
   if p.is_file():files[str(p.relative_to(ART))]=digest(p.read_bytes())
 sqls={str(p):digest(p.read_bytes()) for p in Path('docs/release/optionc-canonical-sql-draft/candidate').rglob('*.sql')}
 doc={'contract':'g1-rc-nondeploy-v1','sourceSha':s,'parents':git('show','-s','--format=%P','HEAD').decode().split(),
  'tree':git('rev-parse','HEAD^{tree}').decode().strip(),'protectedSha':P,'acceptedAcSha':A,
  'authorization':'NONDEPLOY_ONLY','scope':'ACCEPTED_AC_PLUS_PROTECTED_R3_PLUS_NARROW_EXPORT_GUARD',
  'files':files,'candidateSqlSha256':sqls,'reserved013':'NOT_EXECUTED',
  'runtimeVersion':'1.8.0-uat-candidate','configuredSchemaLabel':'1.8.0-010_LABEL_NOT_CATALOG_PROOF',
  'backendAndFrontendFlags':'DEFAULT_OFF','requiredCanonicalSchema':'011,012,014,015,016,017,018,019,020; independently reviewed promotion HOLD',
  'actualUatSchemaCompatibility':'HOLD','activeRuntimeProvenance':'HOLD','priorRollbackBinaryPair':'HOLD',
  'businessUat':'NOT_AUTHORIZED','cleanBuild':'DEFER_G3','itHandover':'DEFER_G4','deployment':'NOT_AUTHORIZED'}
 (ART/'release-manifest.json').write_text(json.dumps(doc,indent=2)+'\n')
 print('G1_IMMUTABLE_BUILD_MANIFEST_CREATED deployment=NOT_AUTHORIZED')

def sanitized_trx(raw):
 import xml.etree.ElementTree as ET
 original=ET.fromstring(raw);ns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
 root=ET.Element('{'+ns+'}TestRun')
 results=ET.SubElement(root,'{'+ns+'}Results')
 for row in original.iter():
  if row.tag.endswith('UnitTestResult'):
   # Parameter values can contain intentionally sensitive provider canaries.
   # Keep method identity/outcome/duration, never values, errors or stdout.
   attrs={k:row.attrib[k] for k in ('outcome','duration','testId') if k in row.attrib}
   attrs['testName']=row.attrib.get('testName','').split('(')[0]
   ET.SubElement(results,'{'+ns+'}UnitTestResult',attrs)
 counters=next(x for x in original.iter() if x.tag.endswith('Counters'))
 summary=ET.SubElement(root,'{'+ns+'}ResultSummary')
 ET.SubElement(summary,'{'+ns+'}Counters',counters.attrib)
 return ET.tostring(root,encoding='utf-8',xml_declaration=True)

def finalize():
 import xml.etree.ElementTree as ET
 source();counts={}
 for name in ['backend.trx','worker-sql.trx','leader-http-sql.trx','browser-sql-probe.trx','g1-critical-sql.trx']:
  root=ET.parse(ART/'test-results'/name).getroot();results=[x for x in root.iter() if x.tag.endswith('UnitTestResult')]
  assert results and all(x.attrib.get('outcome')=='Passed' for x in results)
  counts[name]=len(results)
 assert counts['worker-sql.trx']==13 and counts['g1-critical-sql.trx']>=6
 # Existing acceptance reader verifies two real browser flows and their separate SQL probe.
 subprocess.run([sys.executable,'scripts/ci/leader_rc_acceptance.py'],check=True)
 p=ART/'release-manifest.json';d=json.loads(p.read_text());d['testCounts']=counts;d['realBrowserScenarios']=2
 d['decision']='G1_RC_NONDEPLOY_READY_FOR_INDEPENDENT_AUDIT'
 for trx in (ART/'test-results').glob('*.trx'):trx.write_bytes(sanitized_trx(trx.read_bytes()))
 d['evidenceFilesSha256']={str(f.relative_to(ART)):digest(f.read_bytes()) for f in ART.rglob('*') if f.is_file() and f!=p}
 p.write_text(json.dumps(d,indent=2)+'\n')
 print('G1_RC_NONDEPLOY_READY_FOR_INDEPENDENT_AUDIT release=HOLD sharedSql=NOT_EXECUTED')

if __name__=='__main__':
 try: {'verify':verify,'sql':sql,'manifest':manifest,'finalize':finalize}[sys.argv[1]]()
 except Exception:raise SystemExit('G1_EVIDENCE_STOP phase='+sys.argv[1]+' details=SUPPRESSED') from None
