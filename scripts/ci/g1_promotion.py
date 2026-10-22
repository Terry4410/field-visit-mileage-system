"""Promotion candidate builder and private disposable evidence boundary."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
ROOT=Path('docs/release/g1-execution/promotion')
def digest(b):return hashlib.sha256(b).hexdigest()
def run(mode):
 r=subprocess.run(['pwsh','-NoProfile','-File','scripts/ci/test_g1_promotion.ps1','-Mode',mode],capture_output=True,text=True,timeout=900)
 for line in r.stdout.splitlines():
  if re.fullmatch(r'PromotionCase=[A-Za-z0-9_]+; Status=(PASS|STOP);(?: Details=SUPPRESSED;)?',line) or re.fullmatch(r'HarnessDiagnostic=STOP; Line=[0-9]+; ProviderCode=-?[0-9]+;',line) or re.fullmatch(r'RefusalDiagnostic=STOP; Guard=(G1_[A-Z0-9_]+|SUPPRESSED); ProviderCode=([0-9]+|SUPPRESSED);',line) or re.fullmatch(r'ContractDiagnostic=STOP; Actual=[a-fA-F0-9]{64}; Expected=[a-fA-F0-9]{64};',line):print(line)
 assert r.returncode==0
# Deterministic source builder. Original disposable files are never written.
IDS=[11,12,14,15,16,17,18,19,20]
ALIASES={16:'1.8.0-LEADER-REVIEW',17:'1.8.0-CREATE-LOWRISK',18:'1.8.0-LOWRISK-DIRECT',20:'1.8.0-EXCEL-CANONICAL-STATUS'}
def lit(s):return "N'"+s.replace("'","''")+"'"
def dynamic(sql):return 'EXEC sys.sp_executesql '+lit(sql)+';\n'
def hash_call(query,var):
 query=query.replace('SELECT CONVERT(', 'SELECT @G1Hash=CONVERT(',1)
 return f"EXEC sys.sp_executesql {lit(query)},N'@G1Hash varchar(64) OUTPUT',@G1Hash={var} OUTPUT;\n"
def target_guard():
 return """SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N'EngineEdition'))=3
 AND CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N'Edition')) LIKE N'Developer Edition%'
 AND DB_NAME() LIKE N'OptionCIsolated[_]%' AND LEN(DB_NAME())=LEN(N'OptionCIsolated_')+32
 AND SUBSTRING(DB_NAME(),LEN(N'OptionCIsolated_')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%'
 AND TRY_CONVERT(int,SESSION_CONTEXT(N'FieldVisit.G1.NonDeploy'))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),N'') COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat')
 THROW 52001,N'G1_TARGET_STOP',1;
"""
def marker_names(upto):
 out=[]
 for n in IDS:
  if n>upto:break
  out.append(f'1.8.0-{n:03d}')
  if n in ALIASES:out.append(ALIASES[n])
 return out
def marker_query(upto):
 return "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN("+','.join(lit(x) for x in marker_names(upto))+") ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);"
def marker_state(upto,code=52012):
 names=marker_names(upto);allowed=[f'1.8.0-{i:03d}' for i in range(1,11)]+names
 latest=ALIASES.get(upto,f'1.8.0-{upto:03d}') if upto else '1.8.0-010'
 return dynamic(f"""IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'') COLLATE Latin1_General_100_BIN2<>{lit(latest)}
 THROW {code},N'G1_PREDECESSOR_STOP',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N'1.8.0-%' AND VersionNumber NOT IN({','.join(lit(x) for x in allowed)}))
 THROW {code},N'G1_MARKER_SET_STOP',1;
"""+(f"IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN({','.join(lit(x) for x in names)}))<>{len(names)} THROW {code},N'G1_MARKER_SET_STOP',1;" if names else ''))
def payload(n,phase):
 path=Path(f'docs/release/optionc-canonical-sql-draft/candidate/1800_{n:03d}/{phase}.sql')
 sql=path.read_text()
 # A separately reviewed derivative replaces the copied isolation check with
 # the strict Azure target / Developer-only offline guard, never a blank bypass.
 pattern=r"IF DB_NAME\(\) NOT LIKE N'OptionCIsolated\[_\]%'\s*THROW\s+\d+,\s*N?'[^']*',\s*1;"
 sql,count=re.subn(pattern,'-- Strict promotion target guard is above; original disposable file is unchanged.',sql,flags=re.I)
 assert count==(1 if n>=14 else 0)
 if n==14:
  pattern=r"EXEC sys\.sp_getapplock @Resource=N'([^']+)',\s*@LockMode=N'Exclusive',\s*@LockOwner=N'Transaction',\s*@LockTimeout=0;"
  def checked(m):return "DECLARE @G1LocalLock int; EXEC @G1LocalLock=sys.sp_getapplock @Resource="+lit(m[1])+",@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0; IF @G1LocalLock IS NULL OR @G1LocalLock<0 THROW 52003,N'G1_LOCK_STOP',1;"
  sql,locks=re.subn(pattern,checked,sql,flags=re.I);assert locks==1
 for tag in ["N'isolated-candidate'","N'OptionCIsolated-test'","N'OPTION C candidate; execution prohibited'","N'Package B candidate; execution requires Owner approval'"]:sql=sql.replace(tag,"N'G1 fixed-target promotion candidate'")
 return target_guard()+"IF COALESCE(APPLOCK_MODE(N'public',N'FieldVisit.SchemaMigration',N'Transaction'),N'')<>N'Exclusive' THROW 52003,N'G1_LOCK_STOP',1;\n"+sql

def build():
 ref=json.loads((ROOT/'reference.json').read_text());catalog=(ROOT/'CatalogContract.sql').read_text();history=(ROOT/'HistoryContract.sql').read_text();ledger=(ROOT/'LedgerContract.sql').read_text()
 assert [x['id'] for x in ref['steps']]==IDS
 assert all(re.fullmatch('[a-fA-F0-9]{64}',v) for v in [ref['initial'],ref['emptyLedger']]+[x['catalog'] for x in ref['steps']])
 expected={x['id']:x['catalog'].lower() for x in ref['steps']}
 bodies={n:payload(n,'Up')+(payload(n,'Backfill') if n==14 else '') for n in IDS}
 hashes={n:digest(bodies[n].encode()) for n in IDS}
 sources={str(p):digest(p.read_bytes()) for p in [Path('scripts/ci/g1_promotion.py'),ROOT/'reference.json',ROOT/'CatalogContract.sql',ROOT/'HistoryContract.sql',ROOT/'LedgerContract.sql']}
 package=digest(json.dumps({'sources':sources,'payloads':hashes},sort_keys=True).encode())
 approve=f"IF @G1Offline=0 AND (TRY_CONVERT(binary(32),SESSION_CONTEXT(N'FieldVisit.G1.OwnerApprovedContract')) IS NULL OR TRY_CONVERT(binary(32),SESSION_CONTEXT(N'FieldVisit.G1.OwnerApprovedContract'))<>0x{package}) THROW 52004,N'G1_AUTHORIZATION_STOP',1;\n"
 header='-- NONDEPLOY REVIEW CANDIDATE. No execution or registry assignment authorized.\n'+target_guard()+approve+"IF @@TRANCOUNT<>0 THROW 52002,N'G1_AMBIENT_TRANSACTION_STOP',1;\nSET XACT_ABORT ON; SET LOCK_TIMEOUT 0; SET DEADLOCK_PRIORITY LOW;\n"
 header+="IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1 THROW 52005,N'G1_PERMISSION_STOP',1;\n"
 for name in ['SchemaVersions','Organizations','Teams','Users','Locations','VisitTrips','VisitTripStops','VisitTripSnapshots','VisitTripSnapshotStops','ImportBatches','ImportBatchItems']:
  header+=f"IF OBJECT_ID(N'dbo.{name}',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;\n"
 # Reconcile only fully committed, hash/marker/catalog-equivalent packages.
 final=marker_state(20,52014)+hash_call(catalog,'@G1Hash')+f"IF @G1Hash IS NULL OR LOWER(@G1Hash)<>{lit(expected[20])} THROW 52014,N'G1_CATALOG_STOP',1;\n"
 rows=','.join(f"({lit(f'1.8.0-{n:03d}')},0x{package},0x{hashes[n]},0x{expected[n]})" for n in IDS)
 final+=dynamic(f"IF (SELECT COUNT(*) FROM dbo.FieldVisitMigrationLedger)<>9 OR EXISTS(SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256 FROM (VALUES{rows}) v(VersionNumber,PackageId,PayloadSha256,CatalogSha256) EXCEPT SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256 FROM dbo.FieldVisitMigrationLedger) THROW 52014,N'G1_CHECKSUM_STOP',1;")
 for n in IDS:
  final+=hash_call(marker_query(n),'@G1Hash')+f"EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber={lit(f'1.8.0-{n:03d}').replace(chr(39),chr(39)*2)} AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;\n"
 catch="END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; IF @G1Offline=1 AND ERROR_NUMBER()=52990 THROW; IF ERROR_NUMBER() BETWEEN 52001 AND 52020 THROW; THROW 52090,N'G1_PROMOTION_STOP',1; END CATCH;\n"
 sql=header+"IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE'),0)<>1 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER'),0)<>1 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'REFERENCES'),0)<>1 THROW 52005,N'G1_PERMISSION_STOP',1;\nBEGIN TRY BEGIN TRANSACTION;\nDECLARE @G1Lock int,@G1Hash varchar(64),@G1History varchar(64),@G1After varchar(64),@G1Rows bigint;\nEXEC @G1Lock=sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;\nIF @G1Lock IS NULL OR @G1Lock<0 THROW 52003,N'G1_LOCK_STOP',1;\n"
 for name in ['SchemaVersions','Organizations','Teams','Users','Locations','VisitTrips','VisitTripStops','VisitTripSnapshots','VisitTripSnapshotStops','ImportBatches','ImportBatchItems']:
  sql+=dynamic(f'SELECT @rows=COUNT_BIG(*) FROM dbo.{name} WITH(TABLOCKX,HOLDLOCK);').replace('EXEC sys.sp_executesql ', 'EXEC sys.sp_executesql ',1).replace(';\n',",N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;\n",1)
 sql+="IF OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U') IS NOT NULL BEGIN\n"+final+"THROW 52020,N'G1_ALREADY_APPLIED_VERIFIED_NO_REPLAY',1; END;\n"
 sql+=marker_state(0,52011)+hash_call(catalog,'@G1Hash')+f"IF @G1Hash IS NULL OR LOWER(@G1Hash)<>{lit(ref['initial'].lower())} THROW 52013,N'G1_PARTIAL_OBJECT_STATE_STOP',1;\n"
 sql+=hash_call(history,'@G1History')+dynamic(ledger)+hash_call(catalog,'@G1Hash')+f"IF LOWER(@G1Hash)<>{lit(ref['emptyLedger'].lower())} THROW 52013,N'G1_LEDGER_CONTRACT_STOP',1;\n"
 previous=0
 for n in IDS:
  sql+=f'-- G1_STEP_{n:03d}_BEGIN\n'+marker_state(previous,52012)+(dynamic(payload(n,'Up'))+('-- G1_BACKFILL_BEGIN\n'+dynamic(payload(n,'Backfill')) if n==14 else ''))+marker_state(n,52012)
  if n==14:
   sql+=dynamic("IF EXISTS(SELECT 1 FROM dbo.Locations l WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND NOT EXISTS(SELECT 1 FROM dbo.LocationPublishedVersions v WHERE v.LocationId=l.LocationId AND v.VersionNumber=1 AND v.IsCurrent=1 AND v.ChangeRequestId IS NULL AND v.SourceLocationRowVersion=l.RowVersion AND v.PublishedSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),v.PublishedJson)))) OR (SELECT COUNT_BIG(*) FROM dbo.LocationPublishedVersions)<>(SELECT COUNT_BIG(*) FROM dbo.Locations WHERE ApprovalStatus=N'Approved' AND IsActive=1) THROW 52015,N'G1_BACKFILL_COMPLETION_STOP',1;")
  sql+=hash_call(catalog,'@G1Hash')+f"IF @G1Hash IS NULL OR LOWER(@G1Hash)<>{lit(expected[n])} THROW 52013,N'G1_POSTCONDITION_{n:03d}_STOP',1;\n"
  sql+=hash_call(marker_query(n),'@G1After')+dynamic(f"INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES({lit(f'1.8.0-{n:03d}')},0x{package},0x{hashes[n]},0x{expected[n]},CONVERT(binary(32),@mh,2),SYSUTCDATETIME());").replace(';\n',",N'@mh varchar(64)',@mh=@G1After;\n",1)
  sql+=f'-- G1_STEP_{n:03d}_END\n';previous=n
 sql+=hash_call(history,'@G1After')+"IF @G1History IS NULL OR @G1After IS NULL OR @G1History<>@G1After THROW 52016,N'G1_HISTORY_STOP',1;\n"+final+"COMMIT TRANSACTION; SELECT N'G1_PROMOTION_COMPLETE' AS Status;\n"+catch
 verify=header+"BEGIN TRY BEGIN TRANSACTION; DECLARE @G1Hash varchar(64),@G1Lock int;\nEXEC @G1Lock=sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=0; IF @G1Lock IS NULL OR @G1Lock<0 THROW 52003,N'G1_LOCK_STOP',1;\nIF OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U') IS NULL THROW 52014,N'G1_LEDGER_MISSING',1;\n"+final+"COMMIT TRANSACTION; SELECT N'G1_PROMOTION_VERIFIED' AS Status;\n"+catch
 (ROOT/'Promotion.sql').write_text(sql);(ROOT/'Verify.sql').write_text(verify)
 manifest={'contract':'g1-fixed-target-promotion-v1','approval':'PROPOSED_NOT_AUTHORIZED','rcSource':'f29affb3299828f45b1f131321779e8bea2f86a8','acceptedPreparation':'8e4d3dc72a63e324b69253dff9e0cc1d7fef2273','packageId':package,'server':'sql-fieldvisit-jpe-uat','database':'db-fieldvisit-uat','transaction':'ALL_NINE_STEPS_AND_BACKFILL_ATOMIC','reserved013':'UNCHANGED_ABSENT','offlineTestGuard':'SQL2022_Developer_only;OptionCIsolated_GUID;explicit_session_context;caller_localhost_only','sourceHashes':sources,'originalRegistrySha256':digest(Path('docs/release/g1-execution/registry.json').read_bytes()),'originalSqlSha256':{f['path']:f['sha256'] for e in json.loads(Path('docs/release/g1-execution/registry.json').read_text())['sequence'] for f in e['files']},'steps':[{'proposedId':f'1.8.0-{n:03d}','assignment':'OWNER_APPROVAL_PENDING','payloadSha256':hashes[n],'postCatalogSha256':expected[n],'aliases':[ALIASES[n]] if n in ALIASES else []} for n in IDS],'files':{str(ROOT/f):digest((ROOT/f).read_bytes()) for f in ['Promotion.sql','Verify.sql','CatalogContract.sql','HistoryContract.sql','LedgerContract.sql','reference.json']}}
 (ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')

def verify_build():
 committed={f:(ROOT/f).read_bytes() for f in ['Promotion.sql','Verify.sql','manifest.json']}
 build()
 assert all((ROOT/f).read_bytes()==raw for f,raw in committed.items())
 ref=json.loads((ROOT/'reference.json').read_text())
 for f in ['CatalogContract.sql','HistoryContract.sql','LedgerContract.sql']:
  original=subprocess.check_output(['git','show',ref['provenance']['source']+':'+str(ROOT/f)])
  assert original==(ROOT/f).read_bytes()
 print('PromotionSource=PASS; DeterministicBuild=PASS; DisposableInputs=UNCHANGED;')

def compatibility():
 # Preserve accepted test source and every business assertion. Generate only
 # a new class with its database-construction seam using the promotion batch.
 path=Path('backend/tests/FieldVisit.Application.Tests/V180OptionCFinalSecurityGateSqlTests.cs')
 s=path.read_text();start=s.index('                foreach(var p in new[]{',s.index('internal static async Task<Harness?> TryCreateAsync'))
 end=s.index('                return new Harness(root,db,name,dbb.ConnectionString);',start)
 old=s[start:end]
 match=re.search(r'CREATE TABLE dbo.ImportBatches.*?CREATE INDEX IX_ImportBatchItems_ImportBatchId ON dbo.ImportBatchItems\(ImportBatchId\);',old,re.S);assert match
 setup=match[0]+"\nCREATE TABLE dbo.VisitTrips(VisitTripId bigint NOT NULL PRIMARY KEY,Status nvarchar(30) NOT NULL,LocationId int NULL,UserId int NULL,OrganizationId int NULL,TeamId int NULL,VisitDate date NULL);\nCREATE TABLE dbo.VisitTripSnapshots(VisitTripSnapshotId bigint NOT NULL PRIMARY KEY,VisitTripId bigint NOT NULL,ApprovedDistanceKmSnapshot decimal(10,2) NOT NULL,StatusSnapshot nvarchar(30) NOT NULL);"
 replacement='''                if(!fullCanonicalChain) throw new InvalidOperationException("PROMOTION_TEST_FULL_CHAIN_REQUIRED");
                await V180OptionCFinalSecurityGateSqlTests.Exec(db,"""\n'''+setup+'''\n""");
                await V180OptionCFinalSecurityGateSqlTests.Exec(db,"EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;");
                await V180OptionCFinalSecurityGateSqlTests.Exec(db,File.ReadAllText(Path.Combine(repo,"docs","release","g1-execution","promotion","Promotion.sql")));
                await VerifyCanonicalBackfill(db);
'''
 generated=(s[:start]+replacement+s[end:]).replace('V180OptionCFinalSecurityGateSqlTests','G1PromotionCompatibilityGenerated')
 out=path.with_name('G1PromotionCompatibilityGenerated.cs');assert not out.exists();out.write_text(generated)
 try:
  cmd=['dotnet','test','backend/tests/FieldVisit.Application.Tests/FieldVisit.Application.Tests.csproj','-c','Release','--filter','FullyQualifiedName~G1PromotionCompatibilityGenerated&FullyQualifiedName~full_010_to_020_chain_runs_','--logger','trx;LogFileName=promotion-compatibility.trx','--results-directory','artifacts']
  r=subprocess.run(cmd,capture_output=True,text=True,timeout=900)
  if r.returncode:
   # Only reviewed compiler identifiers or test method IDs, never payload/error text.
   for code in sorted(set(re.findall(r'\berror (CS\d+)\b',r.stdout))):print('GeneratedTestCompiler=STOP; Code='+code+';')
   import xml.etree.ElementTree as ET
   trx=Path('artifacts/promotion-compatibility.trx')
   if trx.exists():
    for row in ET.parse(trx).iter():
     if row.tag.endswith('UnitTestResult') and row.attrib.get('outcome')!='Passed':
      method=row.attrib.get('testName','').split('(')[0]
      if re.fullmatch(r'[A-Za-z0-9_.]+',method):print('CompatibilityCase='+method+'; Status=STOP; Details=SUPPRESSED;')
      for tag in sorted(set(re.findall(r'\b(?:G1|PROMOTION)_[A-Z_]+\b',' '.join(row.itertext())))):print('CompatibilityGuard='+tag+';')
   raise ValueError('compatibility')
  Path('artifacts/compatibility-source.json').write_text(json.dumps({'originalTestSha256':digest(path.read_bytes()),'generatedTestSha256':digest(generated.encode()),'applicationSource':'f29affb3299828f45b1f131321779e8bea2f86a8','seam':'DATABASE_CONSTRUCTION_ONLY','businessAssertions':'UNCHANGED','executionSql':'Promotion.sql'},indent=2)+'\n')
  print('PromotionCompatibility=PASS; RealSqlCases=5; ApplicationSource=UNCHANGED;')
 finally:out.unlink(missing_ok=True)

def finalize():
 import xml.etree.ElementTree as ET,importlib.util
 p=Path('artifacts');m=json.loads((p/'promotion-cases.json').read_text())
 assert m['sourceSha']==os.environ['GITHUB_SHA'] and len(m['cases'])==43 and len({c['case'] for c in m['cases']})==40 and all(c['result']=='PASS' for c in m['cases'])
 root=ET.parse(p/'promotion-compatibility.trx').getroot();rows=[x for x in root.iter() if x.tag.endswith('UnitTestResult')]
 assert len(rows)==5 and all(x.attrib.get('outcome')=='Passed' for x in rows)
 spec=importlib.util.spec_from_file_location('old','scripts/ci/g1_rc_evidence.py');old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)
 (p/'promotion-compatibility.trx').write_bytes(old.sanitized_trx((p/'promotion-compatibility.trx').read_bytes()))
 for f in ['Promotion.sql','Verify.sql','CatalogContract.sql','HistoryContract.sql','LedgerContract.sql','reference.json','manifest.json']:(p/f).write_bytes((ROOT/f).read_bytes())
 m['parent']=subprocess.check_output(['git','show','-s','--format=%P','HEAD']).decode().strip();m['tree']=subprocess.check_output(['git','rev-parse','HEAD^{tree}']).decode().strip()
 m['compatibilityCases']=5;m['original51SafetyAnd5WriterEvidence']='REUSED:39361520046/12237068114';m['formalRegistry']='OWNER_APPROVAL_PENDING';m['liveUatMetadataAndPermissions']='NOT_EXECUTED_HOLD';m['decision']='G1_MIGRATION_PROMOTION_NONDEPLOY_READY_FOR_AUDIT'
 m['evidenceSha256']={f.name:digest(f.read_bytes()) for f in p.iterdir() if f.is_file() and f.name!='promotion-cases.json'}
 (p/'promotion-cases.json').write_text(json.dumps(m,indent=2)+'\n')
 print('G1_MIGRATION_PROMOTION_NONDEPLOY_READY_FOR_AUDIT LiveExecution=NOT_AUTHORIZED;')

if __name__=='__main__':
 try:{'reference':lambda:run('Reference'),'build':build,'verify':verify_build,'test':lambda:run('Test'),'compatibility':compatibility,'finalize':finalize}[sys.argv[1]]()
 except Exception:raise SystemExit('G1_PROMOTION_STOP phase='+sys.argv[1]+' details=SUPPRESSED') from None
