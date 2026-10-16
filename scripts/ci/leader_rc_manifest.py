import os,json,hashlib
from pathlib import Path
art=Path('artifacts')
files={str(p.relative_to(art)):hashlib.sha256(p.read_bytes()).hexdigest() for root in ['api','frontend'] for p in (art/root).rglob('*') if p.is_file()}
(art/'release-manifest.json').write_text(json.dumps({'sourceSha':os.environ['GITHUB_SHA'],'protectedBase':'7f0cf0cb55467916fb0f57239301a8787fd5cb69','originalProtectedBase':'d691fba430ce80dd2048f84863b4628e1e6713d8','referenceChain':'HOLD_MISSING_SCHEMA_ONLY_150_BASELINE','deployedApiSource':'UNVERIFIED','deployedFrontendSource':'UNVERIFIED','r9Source':'95b1987c7f9f92bf589f2f9aecb0632d5d880325','scope':'PARTIAL_LEADER_CUSTOMER_DRAFT_ONLY','schemaEvidence':'PROTECTED_EF_MODEL_ONLY','actualUatParity':'HOLD','rollbackArtifact':'HOLD_NOT_CAPTURED','deployment':'NOT_AUTHORIZED','migrationRequired':False,'files':files},indent=2))
