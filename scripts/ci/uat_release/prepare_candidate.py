"""Source-pinned release delta. Preserves accepted RC and accepted install inputs."""
import argparse,hashlib,json,os,re,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'scripts/ci/clean_install'))
from build_007_checks import extract_balanced_check

def digest(b): return hashlib.sha256(b).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--runtime',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 rc=a.runtime; out=a.output;out.mkdir(parents=True,exist_ok=True)
 assert subprocess.check_output(['git','-C',str(rc),'rev-parse','HEAD'],text=True).strip()=='9d431ea318fcec34f04f3cc36a8fb6d01d00c48a'
 source=os.environ['GITHUB_SHA'];assert re.fullmatch('[0-9a-f]{40}',source)
 subprocess.run([sys.executable,str(ROOT/'scripts/ci/clean_install/apply_rc_runtime_candidate.py'),'--rc-root',str(rc),'--output',str(out)],check=True)
 changes=[]
 def write(rel,new):
  path=rc/rel;old=path.read_bytes() if path.exists() else b'';raw=new.encode() if isinstance(new,str) else new
  path.write_bytes(raw);changes.append({'path':rel,'beforeSha256':digest(old),'afterSha256':digest(raw)})
 infra='backend/src/FieldVisit.Infrastructure/'
 for name in ['V180UatReleaseTrust.cs','SchemaFingerprint.sql']:
  write(infra+name,(ROOT/'tools/uat-release-candidate/runtime'/name).read_bytes())
 write(infra+'UatReleaseIdentity.g.cs','namespace FieldVisit.Infrastructure; public static class UatReleaseIdentity { public const string SourceSha="'+source+'"; }\n')
 file=rc/infra/'FieldVisit.Infrastructure.csproj';old=file.read_text()
 assert '</Project>' in old
 write(infra+file.name,old.replace('</Project>','<ItemGroup><EmbeddedResource Include="SchemaFingerprint.sql" LogicalName="FieldVisit.Release.SchemaFingerprint.sql" /></ItemGroup></Project>'))
 path=infra+'V180OptionCUatTargetBoundary.cs';old=(rc/path).read_text()
 text='throw new InvalidOperationException("OPTIONC_UAT_TRUST_NOT_AVAILABLE");';assert old.count(text)==1
 write(path,old.replace('RequireUat(SqlConnection? connection)', 'RequireUat(SqlConnection? connection, SqlTransaction? transaction=null)').replace(text,'V180UatReleaseTrust.Require(connection,transaction);'))
 path=infra+'V180OptionCExecutionPolicy.cs';old=(rc/path).read_text();marker='    public static void RequireIsolatedSqlTest(SqlConnection connection)'
 transaction_policy='    public static void RequireTransactionTarget(SqlConnection connection, SqlTransaction transaction)\n    {\n        if(transaction.Connection!=connection || transaction.IsolationLevel!=System.Data.IsolationLevel.Serializable)\n            throw new InvalidOperationException("OPTIONC_TRANSACTION_TARGET_DENIED");\n        if(Environment.GetEnvironmentVariable(ModeVariable)=="Uat")\n            V180OptionCUatTargetBoundary.RequireUat(connection,transaction);\n        else RequireExecutionTarget(connection);\n    }\n\n'
 assert old.count(marker)==1;write(path,old.replace(marker,transaction_policy+marker))
 for name,connection,tx in [('V180OptionCLowRiskDirectWriter.cs','connection','transaction'),('V180OptionCCreateLowRiskPublicationWriter.cs','connection','transaction'),('V180OptionCAtomicPublicationWriter.cs','connection','transaction'),('V180OptionCMergePublicationWriter.cs','db','tx')]:
  path=infra+name;old=(rc/path).read_text();call=f'RequireExecutionTarget({connection})';assert old.count(call)==1
  write(path,old.replace(call,f'RequireTransactionTarget({connection},{tx})'))
 for name in ['V180OptionCPublicationCatalogRules.cs','V180OptionCUnknownCommitReconciler.cs']:
  path=infra+name;old=(rc/path).read_text();assert old.count('RequireIsolatedSqlTest(connection)')==1
  write(path,old.replace('RequireIsolatedSqlTest(connection)','RequireExecutionTarget(connection)'))
 path=infra+'V180OptionCMergeService.cs';old=(rc/path).read_text()
 marker='        V180OptionCExecutionPolicy.RequireExecutionTarget(connection);';assert old.count(marker)==1
 write(path,old.replace(marker,'        if(Environment.GetEnvironmentVariable("OPTIONC_EXECUTION_MODE")=="Uat") throw new InvalidOperationException("OPTIONC_MERGE_NOT_IN_APPROVED_UAT_SCOPE");\n'+marker))
 # Same normal frontend binary remains OFF unless a separately controlled
 # runtime config enables exactly this compiled release identity.
 path='frontend/src/optionc-approval-flags.ts';old=(rc/path).read_text()
 oldexpr='import.meta.env.MODE === "optionc-activation-candidate"';assert old.count(oldexpr)==1
 prefix='const release=typeof window === "undefined"?undefined:(window as unknown as {APP_CONFIG?:{OPTIONC_RELEASE?:{enabled?:boolean;sourceSha?:string}}}).APP_CONFIG?.OPTIONC_RELEASE;\n'
 write(path,old.replace('/// <reference types="vite/client" />\n','/// <reference types="vite/client" />\n'+prefix).replace(oldexpr,'release?.enabled === true && release.sourceSha === import.meta.env.VITE_RELEASE_SOURCE_SHA'))
 path='frontend/src/pages/ScopedLocationPage.tsx';old=(rc/path).read_text()
 marker='{role==="leader"&&<LeaderLowRiskAction'
 assert old.count(marker)==1
 addition='{role==="visitor"&&x.isActive&&x.approvalStatus==="Approved"&&x.createdByUserId===user?.userId&&<button className="btn small outline" disabled={busy||!OPTIONC_DIRECT_LOWRISK_UI_ENABLED} onClick={()=>setLowRiskLocation(x)}>LowRisk 主檔備註</button>}'
 write(path,old.replace(marker,addition+marker))
 path='frontend/src/App.tsx';old=(rc/path).read_text();write(path,old.replace('NONDEPLOY OPTION C ACTIVATION CANDIDATE — NOT AUTHORIZED FOR UAT','地點變更審核測試模式'))
 path='frontend/src/pages/ScopedLocationPage.tsx';old=(rc/path).read_text()
 write(path,'import AdminImportPanel from "../components/AdminImportPanel";\n'+old.replace('<ScopedLocationFields role={role}', '{role==="leader"&&<AdminImportPanel type="locations" reviewOnly onDone={load}/>}<ScopedLocationFields role={role}'))
 path='frontend/src/pages/ScopedLocationPage.tsx';old=(rc/path).read_text()
 old=old.replace('useState<"review"|"drafts">','useState<"review"|"drafts"|"published">')
 old=old.replace('const visibleRows=role===', 'const publishedRows=rows.filter(x=>x.isActive&&x.approvalStatus==="Approved");\n const visibleRows=role===')
 old=old.replace('leaderTab==="review"?"待審地點":"我的地點草稿"','leaderTab==="review"?"待審地點":leaderTab==="published"?"已發布地點":"我的地點草稿"')
 old=old.replace('leaderTab==="review"?pendingReviewRows:leaderDraftRows','leaderTab==="review"?pendingReviewRows:leaderTab==="published"?publishedRows:leaderDraftRows')
 old=old.replace('待審地點（{pendingReviewRows.length}）</button>', '待審地點（{pendingReviewRows.length}）</button><button type="button" role="tab" aria-selected={leaderTab==="published"} className="btn secondary" onClick={()=>setLeaderTab("published")}>已發布地點</button>')
 write(path,old)
 path='frontend/src/pages/AdminPage.tsx';old=(rc/path).read_text();write(path,old.replace('<AdminImportPanel type="locations" onDone={load}/>', '<AdminImportPanel type="locations" reviewOnly onDone={load}/>'))
 path='frontend/src/App.tsx';old=(rc/path).read_text()
 mobile='<div className="mobile-session-controls"><span>{roleLabel[role]}</span>{roles.length>1&&<select aria-label="切換角色" value={role} onChange={e=>switchRole(e.target.value)}>{roles.map(r=><option key={r} value={r}>{roleLabel[r]||r}</option>)}</select>}<button className="btn small secondary" onClick={logout}>登出</button></div>'
 write(path,old.replace('<section className="content">',mobile+'<section className="content">'))
 path='frontend/src/styles.css';old=(rc/path).read_text();write(path,old+'\n.mobile-session-controls{display:none}@media(max-width:1100px){.mobile-session-controls{display:flex;align-items:center;justify-content:flex-end;gap:8px;padding:6px 14px}.mobile-session-controls select,.mobile-session-controls button{min-height:44px}}\n')
 path='frontend/src/pages/LocationApprovalQueuePage.tsx';old=(rc/path).read_text()
 old=old.replace('const isCreate=row?.operationCode==="CreateLocation";', 'const isCreate=row?.operationCode==="CreateLocation";const requiresEvidence=isCreate||row?.riskCode==="High";')
 old=old.replace('action==="approve"&&isCreate&&','action==="approve"&&requiresEvidence&&').replace('{isCreate&&stage?.stageStatus', '{requiresEvidence&&stage?.stageStatus').replace('{isCreate&&<div className="actions">','{requiresEvidence&&<div className="actions">').replace('(isCreate&&stage?.stageStatus!==','(requiresEvidence&&stage?.stageStatus!==')
 write(path,old)
 for path in sorted((rc/'backend/src/FieldVisit.Api').glob('appsettings*.json')):
  config=json.loads(path.read_text());config.setdefault('App',{}).update({'Version':'1.8.0-uat-rc+'+source[:12],'DbSchemaVersion':'1.8.0-new-baseline-functional-3cap'})
  write(path.relative_to(rc).as_posix(),json.dumps(config,ensure_ascii=False,indent=2)+'\n')
 # Complete final 018/020 capabilities only after compiling each source
 # predicate on a zero-row temporary shape and matching SQL's actual definition.
 contract={'018':{'ChangeRequests':['CK_B3_ChangeRequests_ReviewState'],'ChangeRequestEvents':['CK_B3_ChangeRequestEvents_EventType','CK_B3_ChangeRequestEvents_DecisionState']},'020':{'ImportBatches':['CK_ImportBatches_Status'],'ImportBatchItems':['CK_ImportBatchItems_Status']}}
 chunks=["SET XACT_ABORT ON; SET NOCOUNT ON;\nIF DB_NAME() NOT LIKE N'OptionCIsolated[_]%' THROW 56600,N'DISPOSABLE_ONLY',1;\nBEGIN TRANSACTION;\nIF (SELECT COUNT(*) FROM dbo.SchemaVersions)<>1 OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK') THROW 56601,N'BASE_CAPABILITY_REQUIRED',1;\n"]
 sources=[]
 for mid,tables in contract.items():
  rel=f'docs/release/optionc-canonical-sql-draft/candidate/1800_{mid}/Up.sql';raw=(rc/rel).read_bytes();sources.append({'path':rel,'sha256':digest(raw)})
  for table,names in tables.items():
   for n,name in enumerate(names):
    # SQL Server normalizes both the source and installed CHECK identically.
    proof=extract_balanced_check(raw.decode('utf-8-sig'),name)
    temp=f'#capProof{mid}{table}{n}'
    chunks.append(f"SELECT TOP(0) * INTO {temp} FROM dbo.{table};\nALTER TABLE {temp} ADD {proof};\nIF NOT EXISTS(SELECT 1 FROM sys.check_constraints c JOIN tempdb.sys.check_constraints p ON p.parent_object_id=OBJECT_ID(N'tempdb..{temp}') WHERE c.parent_object_id=OBJECT_ID(N'dbo.{table}') AND c.name=N'{name}' AND c.is_disabled=0 AND c.is_not_trusted=0 AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(c.definition,CHAR(13),''),CHAR(10),''),CHAR(9),''),' ',''),'[','') COLLATE Latin1_General_100_BIN2=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(p.definition,CHAR(13),''),CHAR(10),''),CHAR(9),''),' ',''),'[','') COLLATE Latin1_General_100_BIN2) THROW 56602,N'FINAL_SOURCE_CHECK_DRIFT',1;\nDROP TABLE {temp};\n")
  cap='1.8.0-LOWRISK-DIRECT' if mid=='018' else '1.8.0-EXCEL-CANONICAL-STATUS'
  chunks.append(f"INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'{cap}',N'New-schema source-verified capability; source SHA256 {digest(raw)}; not historical migration',SYSUTCDATETIME(),N'uat-release-new-schema');\n")
 chunks.append("IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N'1.8.0-0%') THROW 56603,N'FALSE_HISTORY_DENIED',1;\nCOMMIT;\n")
 sql=''.join(chunks);(out/'60_final_functional_capabilities.sql').write_text(sql)
 (out/'release-source-overlay.json').write_text(json.dumps({'candidateSourceSha':source,'immutableRc':'9d431ea318fcec34f04f3cc36a8fb6d01d00c48a','changes':changes,'capabilitySources':sources,'capabilitySqlSha256':digest(sql.encode()),'reserved013':'RESERVED_NO_HISTORY'},indent=2)+'\n')
 print('UAT_RC=SOURCE_OVERLAY_AND_FINAL_CAPABILITIES_PREPARED')
if __name__=='__main__':main()
