"""Sanitized acceptance claims derived from mandatory exact-run test outputs, never UAT acceptance."""
import json,os,xml.etree.ElementTree as ET
from pathlib import Path
root=Path('artifacts'); required={'backend.trx':None,'leader-http-sql.trx':'Leader_draft_real_http_sql_and_negative_gates_without_011_020','browser-sql-probe.trx':'Browser_write_sql_probe_when_requested'}
counts={}
for name,method in required.items():
 tree=ET.parse(root/'test-results'/name);results=tree.findall('.//{*}UnitTestResult')
 if not results or any(x.attrib.get('outcome')!='Passed' for x in results):raise SystemExit('ACCEPTANCE_TRX_NOT_PASSED')
 if method and not any(method in x.attrib.get('testName','') for x in results):raise SystemExit('ACCEPTANCE_REQUIRED_TEST_MISSING')
 counts[name]=len(results)
browser=json.loads((root/'browser-results.json').read_text())
def specs(suite):
 for x in suite.get('specs',[]):yield x
 for x in suite.get('suites',[]):yield from specs(x)
found={s['title']:s for suite in browser['suites'] for s in specs(suite)}
expected=['Leader eight-field draft create and update against real isolated API','Visitor existing navigation search create and ownership edit use real API SQL']
for title in expected:
 if title not in found or not found[title].get('ok') or any(t.get('status')!='expected' for t in found[title]['tests']):raise SystemExit('ACCEPTANCE_BROWSER_NOT_PASSED')
report={'contract':'work-c-visitor-acceptance-v1','sourceSha':os.environ['GITHUB_SHA'],'authorization':'NONDEPLOY_ONLY','testCounts':counts,'realBrowserScenarios':len(expected),'fixture':'DISPOSABLE_PROTECTED_EF_MODEL_NOT_GENUINE_CHAIN','matrix':{k:'PASS_ISOLATED' for k in ['VisitorEntryNavigationRoute','VisitorSearchCreateEdit','VisitorCreatedByAndTeamsDto','VisitorOwnershipScopeDenials','VisitorLiveRoleHrTeamAccountDenials','LeaderEightFieldsAndLiveScope','AdminCreateEdit','AnonymousAndUnauthorizedDenials','PublishedRowPreservation','BrowserApiSqlPersistenceAndAudit']},'releaseGates':{k:'HOLD' for k in ['ActualDeployedVisitorBusinessHistory','LegacyPublishedMaintenanceCompatibility','Genuine001To010SchemaFidelity','ApplicationPrincipalPermissions','ActiveApiBinaryIdentity','MatchedRollbackPair','CrossLaneRuntimeIntegration','OwnerReleaseDecision']}}
(root/'visitor-acceptance.json').write_text(json.dumps(report,indent=2)+'\n');print('VISITOR_ACCEPTANCE_PASS_ISOLATED RELEASE_HOLD')
