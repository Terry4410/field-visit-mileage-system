"""Future separately approved activation: sign exact inspected facts; never enable."""
import argparse,hashlib,hmac,json,os
from datetime import datetime,timedelta,timezone
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--contract',type=Path,required=True);p.add_argument('--inspected-facts',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
c=json.loads(a.contract.read_text());f=json.loads(a.inspected_facts.read_text())
assert f['server']=='sql-fieldvisit-jpe-uat.database.windows.net' and f['database']=='db-fieldvisit-uat'
assert f['engineEdition']==5 and f['nonOwnerNoDdl'] and f['encryptedCertificateVerified']
assert f['schemaSha256']==c['schemaSha256']
key=bytes.fromhex(os.environ['FIELDVISIT_UAT_TRUST_KEY']);assert len(key)>=32
now=datetime.now(timezone.utc);payload={**{k:c[k] for k in ['candidateSourceSha','apiSha256','frontendSha256','schemaSha256']},'principalSidSha256':f['principalSidSha256'],'principalName':f['principalName'],'server':f['server'],'database':f['database'],'features':'LocationReview,LowRiskDirect,ExcelReview','notBefore':now.isoformat(),'expires':(now+timedelta(hours=8)).isoformat()}
raw=json.dumps(payload,separators=(',',':')).encode();a.output.write_bytes(raw)
a.output.with_suffix('.signature').write_text(hmac.new(key,raw,hashlib.sha256).hexdigest())
print('ATTESTATION=SIGNED_NOT_ACTIVATED')
