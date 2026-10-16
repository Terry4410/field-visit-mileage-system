"""Inventory true protected chain prerequisites; never infer fidelity from EF."""
import hashlib,json,os
from pathlib import Path
protected='7f0cf0cb55467916fb0f57239301a8787fd5cb69'
migrations=sorted(p for p in Path('database/migrations').iterdir() if p.name.startswith(('1600_','1700_','1800_')) and p.is_dir())
entries=[]
for p in migrations:
 for phase in ['Up.sql','Verify.sql']:
  f=p/phase
  if not f.exists():raise SystemExit('PROTECTED_CHAIN_FILE_MISSING '+str(f))
  entries.append({'path':str(f),'sha256':hashlib.sha256(f.read_bytes()).hexdigest()})
assert len([p for p in migrations if p.name.startswith('1800_')])==10
out={'sourceSha':os.environ.get('GITHUB_SHA','LOCAL_ASSESSMENT'),'protectedBase':protected,'state':'HOLD','reason':'APPROVED_SCHEMA_ONLY_150_BASELINE_AND_PROVENANCE_NOT_SUPPLIED','referenceIsEnsureCreated':False,'migrationFiles':entries,'actualUatInventory':'HOLD_INDEPENDENT_AUTHORIZED_EVIDENCE_REQUIRED','applicationPrincipalPermissions':'HOLD','requiredInputs':['LEADER_RC_BASELINE_SCHEMA_SQL','LEADER_RC_BASELINE_SHA256','LEADER_RC_BASELINE_PROVENANCE'],'excludedSubstitute':'database/015_schema_v1_5_verify.sql is verification only, not CREATE baseline'}
Path('artifacts').mkdir(exist_ok=True)
Path('artifacts/reference-prerequisites.json').write_text(json.dumps(out,indent=2)+'\n')
print('PROTECTED_CHAIN_ASSESSMENT=HOLD missing approved schema-only 1.5 baseline; files='+str(len(entries)))
