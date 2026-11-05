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
print('ACTUAL_UAT_SCHEMA_PARITY=HOLD DEPLOYMENT=HOLD')
