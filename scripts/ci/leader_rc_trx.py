from pathlib import Path
import xml.etree.ElementTree as ET
required=['backend.trx','leader-http-sql.trx','browser-sql-probe.trx']
for name in required:
 p=Path('artifacts/test-results')/name
 if not p.exists(): raise SystemExit('EVIDENCE_MISSING '+name)
 counters=next(x for x in ET.parse(p).getroot().iter() if x.tag.endswith('Counters'))
 a=counters.attrib
 if int(a['total'])<=0 or a['executed']!=a['total'] or int(a.get('failed','0')) or int(a.get('passed','0'))!=int(a['total']): raise SystemExit('EVIDENCE_FAILED '+name)
 print('TRX_PASS',name,'executed='+a['executed'])
worker=Path('artifacts/test-results/worker-sql.trx')
if not worker.exists(): raise SystemExit('EVIDENCE_MISSING worker-sql.trx')
worker_root=ET.parse(worker).getroot()
worker_counters=next(x for x in worker_root.iter() if x.tag.endswith('Counters')).attrib
worker_results=[x for x in worker_root.iter() if x.tag.endswith('UnitTestResult')]
if (int(worker_counters.get('total','0')) != 13 or worker_counters.get('executed') != '13'
        or int(worker_counters.get('passed','0')) != 13
        or any(int(worker_counters.get(k,'0')) for k in ('failed','skipped','inconclusive','notExecuted'))):
    raise SystemExit('EVIDENCE_FAILED worker-sql.trx expected=13-executed=13-passed=13')
if len(worker_results) != 13 or any(x.attrib.get('outcome') != 'Passed' for x in worker_results):
    raise SystemExit('EVIDENCE_FAILED worker-sql.trx result-set-is-not-13-passed')
required_worker_methods = (
    'Worker_sql_controlled_interleavings',
    'Worker_sql_provider_inflight_changes_fail_closed',
    'Worker_sql_real_fault_rolls_back_and_recovers',
    'Worker_sql_provider_sensitive_payload_never_reaches_storage_or_http',
)
worker_names='\n'.join(x.attrib.get('testName','') for x in worker_results)
if any(method not in worker_names for method in required_worker_methods):
    raise SystemExit('EVIDENCE_FAILED worker-sql.trx required-scenario-missing')
if 'DISPOSABLE_SQL_NOT_CONFIGURED' in worker.read_text(errors='replace'):
    raise SystemExit('EVIDENCE_FAILED worker-sql.trx disposable SQL was not configured')
print('TRX_PASS worker-sql.trx executed=13')
print('ACTUAL_UAT_SCHEMA_PARITY=HOLD DEPLOYMENT=HOLD')
