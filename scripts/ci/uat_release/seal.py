import hashlib,json,os,shutil,sys,zipfile,subprocess
from pathlib import Path
out=Path(os.environ['RELEASE_OUTPUT']);runtime=Path(os.environ['RC_SOURCE_ROOT']);api=Path(os.environ['RELEASE_API_ROOT'])
sha=lambda b:hashlib.sha256(b).hexdigest()
def seal(root,destination):
 files=sorted(p for p in root.rglob('*') if p.is_file()); entries=[]
 with zipfile.ZipFile(destination,'w',compression=zipfile.ZIP_DEFLATED) as z:
  for p in files:
   rel=p.relative_to(root).as_posix();raw=p.read_bytes();info=zipfile.ZipInfo(rel,(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16;z.writestr(info,raw)
   entries.append({'path':rel,'sha256':sha(raw)})
 with zipfile.ZipFile(destination) as z:
  assert len(z.namelist())==len(entries)
  for x in entries: assert sha(z.read(x['path']))==x['sha256']
 return {'file':destination.name,'sha256':sha(destination.read_bytes()),'files':entries}
if sys.argv[1]=='frontend':
 config=(runtime/'frontend/dist/config.js').read_text();assert 'AUTH_MODE: "Demo"' in config and 'enabled:true' not in config
 manifest=seal(runtime/'frontend/dist',out/'frontend-release-candidate.zip');(out/'frontend-manifest.json').write_text(json.dumps(manifest,indent=2))
 print('UAT_RC=FRONTEND_SAME_SOURCE_RUNTIME_DEFAULT_OFF_BUILD_PASS')
else:
 contract=json.loads((out/'release-contract.json').read_text());assert contract['candidateSourceSha']==os.environ['GITHUB_SHA']
 assert contract['apiSha256']==sha(''.join(n+':'+sha((api/n).read_bytes())+'\n' for n in ['FieldVisit.Api.dll','FieldVisit.Application.dll','FieldVisit.Domain.dll','FieldVisit.Infrastructure.dll']).encode())
 assert contract['frontendSha256']==sha((out/'frontend-release-candidate.zip').read_bytes())
 assert contract['schemaSha256']==sha((out/'schema-contract.json').read_bytes())
 for config in api.glob('appsettings*.json'):
  data=json.loads(config.read_text());assert data.get('OptionC',{}).get('Candidate',{}).get('Enabled',False)==False
  assert data['ConnectionStrings']['DefaultConnection']=='SET-IN-AZURE-APP-SERVICE'
 app=seal(api,out/'api-release-candidate.zip');(out/'api-manifest.json').write_text(json.dumps(app,indent=2))
 accepted=Path(os.environ['RUNNER_TEMP'])/'accepted';sql=out/'sql';sql.mkdir(exist_ok=True)
 order=json.loads((accepted/'it-sql-install-order.json').read_text())
 for item in order['sequence']:
  raw=(accepted/item['file']).read_bytes();assert sha(raw)==item['sha256'];(sql/item['file']).write_bytes(raw)
 shutil.copyfile(out/'60_final_functional_capabilities.sql',sql/'60_final_functional_capabilities.sql')
 order['sequence'].append({'step':14,'file':'60_final_functional_capabilities.sql','sha256':sha((sql/'60_final_functional_capabilities.sql').read_bytes())})
 order.update({'candidateSourceSha':os.environ['GITHUB_SHA'],'acceptedEvidenceRun':'39981135659','status':'FINAL_FEATURE_CANDIDATE_REQUIRES_SEPARATE_LIVE_APPROVAL'})
 (out/'sql-install-order.json').write_text(json.dumps(order,indent=2))
 from install import promoted
 uat=out/'uat-sql';uat.mkdir(exist_ok=True)
 promoted_entries=[]
 for item in order['sequence']:
  raw=promoted((sql/item['file']).read_bytes(),item['file']);(uat/item['file']).write_bytes(raw)
  promoted_entries.append({'step':item['step'],'file':'uat-sql/'+item['file'],'sha256':sha(raw),'sourceSha256':item['sha256']})
 standalone={'candidateSourceSha':os.environ['GITHUB_SHA'],'fixture':{'sequence':[{**x,'file':'sql/'+x['file']} for x in order['sequence']]},'uat':{'server':'sql-fieldvisit-jpe-uat.database.windows.net','database':'db-fieldvisit-uat','sequence':promoted_entries},'executionDefault':'DRY_RUN_ONLY_REQUIRES_SEPARATE_EXACT_MANIFEST_APPROVAL'}
 (out/'standalone-install-manifest.json').write_text(json.dumps(standalone,indent=2))
 shutil.copyfile('scripts/ci/uat_release/install.py',out/'install.py');shutil.copyfile('scripts/ci/uat_release/sign_activation.py',out/'sign_activation.py')
 shutil.copyfile('docs/release/uat-release-candidate/IT-ACCEPTANCE.md',out/'IT-ACCEPTANCE.md')
 subprocess.run([sys.executable,str(out/'install.py'),'--root',str(out),'--target','uat'],check=True)
 subprocess.run([sys.executable,str(out/'install.py'),'--root',str(out),'--target','fixture'],check=True)

 shutil.copyfile(accepted/'source-code-immutable-rc.zip',out/'source-code-immutable-rc.zip')
 subprocess.run(['git','archive','--format=zip','--output='+str(out/'release-source-delta.zip'),os.environ['GITHUB_SHA'],'tools/uat-release-candidate','tools/clean-install','scripts/ci/uat_release','scripts/ci/clean_install','docs/release/uat-release-candidate','.github/workflows/uat-release-candidate-nondeploy.yml'],check=True)
 browser=json.loads((out/'browser-evidence.json').read_text());observed={x['case'] for x in browser['cases']}
 source_cases=(runtime/'docs/release/SCHEME_A_BUSINESS_UAT_ROLE_CASES_NONDEPLOY.md').read_text()
 import re
 cases=re.findall(r'^\| ((?:VIS|LEAD|ADMIN|XLS|TRIP|SEC)-\d{2}) \|',source_cases,re.M);assert len(cases)==25
 coverage=[]
 for case in cases:
  status='PASS' if case in observed else 'PARTIAL'
  detail='Focused same-build browser and SQL assertions' if case in observed else 'Accepted isolated API/security evidence reused; complete case still needs bounded Business UAT disposition'
  coverage.append({'case':case,'technicalCoverage':status,'basis':detail,'liveBusinessUat':'NOT_EXECUTED'})
 (out/'frozen-25-coverage.json').write_text(json.dumps({'sourceSha256':sha(source_cases.encode()),'caseCount':25,'cases':coverage,'notAllCasesClaimedPass':True},indent=2))
 manifest={'candidateSourceSha':os.environ['GITHUB_SHA'],'runId':os.environ['GITHUB_RUN_ID'],'acceptedInstallEvidence':{'sha':'39c86f03cc8d66881a78a800ded974b2f338cf07','run':'39981135659','artifact':'12513786826','zipSha256':'0f8e86063bb11edc223f2ec2cd628dc45fd64d059343b410826163e21586d61a'},'binding':contract,'packages':[{'file':app['file'],'sha256':app['sha256']},{'file':'frontend-release-candidate.zip','sha256':contract['frontendSha256']}],'activationDefault':'OFF','physicalIphone':'NOT_EXECUTED','liveUat':'NOT_AUTHORIZED_NOT_EXECUTED'}
 (out/'release-manifest.json').write_text(json.dumps(manifest,indent=2))
 (out/'checksums.sha256').write_text(''.join(sha(p.read_bytes())+'  '+p.relative_to(out).as_posix()+'\n' for p in sorted(out.rglob('*')) if p.is_file() and p.name!='checksums.sha256'))
 print('UAT_RC=FINAL_SAME_SOURCE_API_FRONTEND_SQL_TRUST_BINDING_PASS')
 print('UAT_RC_API_SHA256='+app['sha256']);print('UAT_RC_FRONTEND_SHA256='+contract['frontendSha256'])
