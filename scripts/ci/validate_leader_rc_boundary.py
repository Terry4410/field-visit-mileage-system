from pathlib import Path
import subprocess
base='b1a507c90c577fb2c0cdd6a98ef33481d3a5f790'
changed=subprocess.check_output(['git','diff','--name-only',base,'HEAD'],text=True).splitlines()
for p in changed:
 if p.startswith(('database/','demos/')): raise SystemExit('OUT_OF_SCOPE '+p)
page=Path('frontend/src/pages/LeaderDraftLocationPage.tsx').read_text()
for forbidden in ['optionc-candidate','OPTIONC_','imports/','jobs/','mock']:
 if forbidden in page.lower() if forbidden=='mock' else forbidden in page: raise SystemExit('FORBIDDEN_UI_ROUTE '+forbidden)
for required in ['teamId','locationName','city','district','address','plusCode','taxId','masterNote','rowVersion','isActive:false','locationType:"Customer"']:
 if required not in page: raise SystemExit('FIELD_CONTRACT_MISSING '+required)
workflow=Path('.github/workflows/leader-uat010-rc-nondeploy.yml').read_text()
for forbidden in ['azure/login','azure/webapps-deploy','deploy-pages','secrets.','environment: uat','id-token: write']:
 if forbidden in workflow: raise SystemExit('NONDEPLOY_BOUNDARY '+forbidden)
print('PROTECTED_MODEL_CONTRACT_PASS; ACTUAL_UAT_SCHEMA_PARITY=HOLD')
