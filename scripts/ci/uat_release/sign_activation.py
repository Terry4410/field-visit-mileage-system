"""Future separately approved activation: sign exact inspected facts; never enable."""
import argparse,hashlib,hmac,json,os
from datetime import datetime,timedelta,timezone
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--contract',type=Path,required=True);p.add_argument('--inspected-facts',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
c=json.loads(a.contract.read_text());f=json.loads(a.inspected_facts.read_text())
profile=c.get('schemaProfile')
targets={'Existing010Upgrade':('sql-fieldvisit-jpe-uat.database.windows.net','db-fieldvisit-uat'),'PathBNewEmpty':('sql-fieldvisit-jpe-pathb-uat.database.windows.net','db-fieldvisit-pathb-uat'),None:('sql-fieldvisit-jpe-uat.database.windows.net','db-fieldvisit-uat'),'NewEmpty':('sql-fieldvisit-jpe-uat.database.windows.net','db-fieldvisit-uat')}
assert profile in targets
assert (f['server'],f['database'])==targets[profile]
assert f['engineEdition']==5 and f['nonOwnerNoDdl'] and f['encryptedCertificateVerified']
assert f['schemaSha256']==c['schemaSha256']
key=bytes.fromhex(os.environ['FIELDVISIT_UAT_TRUST_KEY']);assert len(key)>=32
now=datetime.now(timezone.utc);features=('DemoAccounts,LocationReview,LowRiskDirect,ExcelReview,OptionCCandidate,OptionCApproval,GoogleMapsRoutes,Worker' if profile=='PathBNewEmpty' else 'LocationReview,LowRiskDirect,ExcelReview')
payload={**{k:c[k] for k in ['candidateSourceSha','apiSha256','frontendSha256','schemaSha256']},'principalSidSha256':f['principalSidSha256'],'principalName':f['principalName'],'server':f['server'],'database':f['database'],'features':features,'notBefore':now.isoformat(),'expires':(now+timedelta(hours=8)).isoformat()}
if c.get('schemaProfile')=='Existing010Upgrade':
    import re
    assert c.get('tableCount') in range(70,101) and f.get('tableCount')==c['tableCount']
    for k in ['historicalMarkerSha256','promotionPackageId','ledgerSha256']:
        assert re.fullmatch('[0-9a-f]{64}',c.get(k,'')) and f.get(k)==c[k]
    payload.update(schemaProfile='Existing010Upgrade',tableCount=c['tableCount'],
                   historicalMarkerSha256=c['historicalMarkerSha256'],
                   promotionPackageId=c['promotionPackageId'],ledgerSha256=c['ledgerSha256'])
elif profile!='PathBNewEmpty':
    assert c.get('schemaProfile') in (None,'NewEmpty')
    assert not any(k in c or k in f for k in ['historicalMarkerSha256','promotionPackageId','ledgerSha256','tableCount'])
if profile=='PathBNewEmpty':
    assert c.get('tableCount')==69 and f.get('tableCount')==69
    assert f.get('optionCExecutionMode')=='Uat'
    assert f.get('demoAuthEnabled') is True and f.get('optionCCandidateEnabled') is True
    assert f.get('optionCApprovalEnabled') is True and f.get('workerEnabled') is True
    assert f.get('routeProvider')=='GoogleMaps' and f.get('googleApiKeyPresent') is True
    assert not any(k in c or k in f for k in ['historicalMarkerSha256','promotionPackageId','ledgerSha256'])
    payload.update(schemaProfile='PathBNewEmpty',tableCount=69)
raw=json.dumps(payload,separators=(',',':')).encode();a.output.write_bytes(raw)
a.output.with_suffix('.signature').write_text(hmac.new(key,raw,hashlib.sha256).hexdigest())
print('ATTESTATION=SIGNED_NOT_ACTIVATED')
