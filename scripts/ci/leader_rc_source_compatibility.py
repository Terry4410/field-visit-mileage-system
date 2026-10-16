"""Read Git objects only. Historical source comparison is NOT live schema parity."""
import hashlib,json,subprocess
from pathlib import Path
api='db235938b8b13ca8a65ab3b525de1df10d911645';pages='0899f3996618c4b2bd02ecd659c76fdd07cc91ed'
def read(ref,path):return subprocess.check_output(['git','show',ref+':'+path])
unchanged=['backend/src/FieldVisit.Infrastructure/AppDbContext.cs','backend/src/FieldVisit.Domain','database']
for path in unchanged:
 if subprocess.check_output(['git','diff','--name-only',api,'HEAD','--',path],text=True).strip():raise SystemExit('SOURCE_MODEL_OR_DDL_CHANGED '+path)
contract='backend/src/FieldVisit.Application/V160FinalContracts.cs'
def request(data):return data.decode().split('public sealed record SaveManagedLocationRequest(')[1].split(');')[0]
if request(read(api,contract))!=request(Path(contract).read_bytes()):raise SystemExit('SAVE_LOCATION_REQUEST_CHANGED')
old_app=read(pages,'frontend/src/App.tsx').decode();app=Path('frontend/src/App.tsx').read_text()
old_controller=read(api,'backend/src/FieldVisit.Api/V160FinalController.cs').decode();controller=Path('backend/src/FieldVisit.Api/V160FinalController.cs').read_text()
findings={'visitorRouteRemoved':'path="/locations/manage"' in old_app and 'path="/locations/manage"' not in app,'visitorTeamEndpointRemoved':'managed-locations/visitor-teams' in old_controller and 'managed-locations/visitor-teams' not in controller,'createdByDtoRemoved':'CreatedByUserId' in read(api,contract).decode() and 'CreatedByUserId' not in Path(contract).read_text()}
if not all(findings.values()):raise SystemExit('REVIEWED_SOURCE_REGRESSION_EVIDENCE_CHANGED_REAUDIT_REQUIRED')
Path('artifacts').mkdir(exist_ok=True)
Path('artifacts/source-compatibility.json').write_text(json.dumps({'state':'HOLD_SOURCE_REGRESSIONS','historicalApiSource':api,'observedPagesSource':pages,'unchangedModelAndDdlPaths':unchanged,'saveRequestUnchanged':True,'findings':findings,'actualUatParity':'HOLD'},indent=2)+'\n')
print('MODEL_SOURCE_DIFF_CHECK_PASS; EXISTING_VISITOR_COMPATIBILITY=HOLD_SOURCE_REGRESSIONS; ACTUAL_UAT_SCHEMA_PARITY=HOLD')
