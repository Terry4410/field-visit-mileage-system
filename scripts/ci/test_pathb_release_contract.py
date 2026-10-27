#!/usr/bin/env python3
"""Path B target, installer and signed-activation contract checks. NONDEPLOY."""
from __future__ import annotations
import hashlib, hmac, json, os, subprocess, sys, tempfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
INSTALL=ROOT/'scripts/ci/uat_release/install.py'
SIGN=ROOT/'scripts/ci/uat_release/sign_activation.py'
GUIDE=ROOT/'docs/uat/BUSINESS-UAT-GUIDE-v1.8.0.md'
CASES=ROOT/'docs/uat/BUSINESS-UAT-TEST-CASES-v1.8.0.md'
PY=sys.executable
PATHB_SERVER='sql-fieldvisit-jpe-pathb-uat.database.windows.net'
PATHB_DB='db-fieldvisit-pathb-uat'
OLD_SERVER='sql-fieldvisit-jpe-uat.database.windows.net'
OLD_DB='db-fieldvisit-uat'

def run(args,env=None,ok=True):
    result=subprocess.run(args,capture_output=True,text=True,env=env)
    if (result.returncode==0)!=ok:
        raise AssertionError('EXPECTED_PROCESS_RESULT_MISMATCH')
    return result

def main():
    with tempfile.TemporaryDirectory() as td:
        root=Path(td)
        sql=root/'pathb-uat-sql'/'one.sql';sql.parent.mkdir(parents=True);sql.write_text('SELECT 1;\n')
        sha=hashlib.sha256(sql.read_bytes()).hexdigest()
        plan={'candidateSourceSha':'a'*40,'pathb-uat':{'server':PATHB_SERVER,'database':PATHB_DB,'schemaProfile':'PathBNewEmpty','sequence':[{'step':1,'file':'pathb-uat-sql/one.sql','sha256':sha}]},'uat':{'server':OLD_SERVER,'database':OLD_DB,'sequence':[{'step':1,'file':'pathb-uat-sql/one.sql','sha256':sha}]},'fixture':{'sequence':[{'step':1,'file':'pathb-uat-sql/one.sql','sha256':sha}]},'executionDefault':'DRY_RUN_ONLY'}
        (root/'standalone-install-manifest.json').write_text(json.dumps(plan))
        p=run([PY,str(INSTALL),'--root',str(root),'--target','pathb-uat'])
        assert 'HASH_VERIFIED_DRY_RUN_NO_CONNECTION' in p.stdout
        p=run([PY,str(INSTALL),'--root',str(root),'--target','uat'])
        assert 'HASH_VERIFIED_DRY_RUN_NO_CONNECTION' in p.stdout
        plan['pathb-uat']['server']=OLD_SERVER;(root/'standalone-install-manifest.json').write_text(json.dumps(plan))
        run([PY,str(INSTALL),'--root',str(root),'--target','pathb-uat'],ok=False)
        plan['pathb-uat']['server']=PATHB_SERVER;(root/'standalone-install-manifest.json').write_text(json.dumps(plan))
        run([PY,str(INSTALL),'--root',str(root),'--target','production'],ok=False)
        run([PY,str(INSTALL),'--root',str(root),'--target','pathb-uat','--execute'],ok=False)
        # Execution is blocked by absent exact-manifest authorization before sqlcmd.
        install_text=INSTALL.read_text()
        assert "PATHB_SERVER='"+PATHB_SERVER+"'" in install_text and "PATHB_DATABASE='"+PATHB_DB+"'" in install_text
        assert "TARGET_PAIR_MISMATCH" in install_text and "EMPTY_DATABASE_REQUIRED" in install_text

        key='11'*32
        contract={'candidateSourceSha':'a'*40,'apiSha256':'b'*64,'frontendSha256':'c'*64,'schemaSha256':'d'*64,'schemaProfile':'PathBNewEmpty','tableCount':69,'server':PATHB_SERVER,'database':PATHB_DB}
        facts={'server':PATHB_SERVER,'database':PATHB_DB,'engineEdition':5,'nonOwnerNoDdl':True,'encryptedCertificateVerified':True,'schemaSha256':'d'*64,'principalSidSha256':'e'*64,'principalName':'fieldvisit_runtime','tableCount':69,'optionCExecutionMode':'Uat','demoAuthEnabled':True,'optionCCandidateEnabled':True,'optionCApprovalEnabled':True,'workerEnabled':True,'routeProvider':'GoogleMaps','googleApiKeyPresent':True}
        (root/'contract.json').write_text(json.dumps(contract));(root/'facts.json').write_text(json.dumps(facts))
        out=root/'attestation.json';env=os.environ.copy();env['FIELDVISIT_UAT_TRUST_KEY']=key
        p=run([PY,str(SIGN),'--contract',str(root/'contract.json'),'--inspected-facts',str(root/'facts.json'),'--output',str(out)],env)
        assert 'SIGNED_NOT_ACTIVATED' in p.stdout
        raw=out.read_bytes();sig=out.with_suffix('.signature').read_text()
        assert hmac.compare_digest(hmac.new(bytes.fromhex(key),raw,hashlib.sha256).hexdigest(),sig)
        att=json.loads(raw);assert att['server']==PATHB_SERVER and att['database']==PATHB_DB and att['schemaProfile']=='PathBNewEmpty' and att['tableCount']==69
        facts['server']=OLD_SERVER;facts['database']=OLD_DB;(root/'facts.json').write_text(json.dumps(facts))
        run([PY,str(SIGN),'--contract',str(root/'contract.json'),'--inspected-facts',str(root/'facts.json'),'--output',str(root/'wrong.json')],env,ok=False)
        facts['server']=PATHB_SERVER;facts['database']=PATHB_DB;facts['routeProvider']='Mock';(root/'facts.json').write_text(json.dumps(facts))
        run([PY,str(SIGN),'--contract',str(root/'contract.json'),'--inspected-facts',str(root/'facts.json'),'--output',str(root/'wrong-flags.json')],env,ok=False)

    guide=GUIDE.read_text();cases=CASES.read_text()
    assert 'VIS02' in cases and 'Google Maps 成功' in cases and 'VIS03' in cases and '路線失敗轉人工備援' in cases
    assert 'Google Routes provider' in guide and 'Mock、單元測試' in guide and 'Path B 保持 HOLD' in guide
    assert cases.count('| NOT RUN |')==25
    print('PATHB_TARGET=EXACT; OLD_EXISTING010=UNCHANGED; INSTALLER=CREATE_ONLY_DRYRUN; SIGNER=PROFILE_BOUND; GOOGLE_VIS02_VIS03=RETAINED; LIVE_SQL=NOT_ACCESSED;')

if __name__=='__main__':
    main()
