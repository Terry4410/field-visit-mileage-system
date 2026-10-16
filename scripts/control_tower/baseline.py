"""Select prior successful Hub artifact producer; no download or mutations."""
import os
from hub import Client

BRANCH = 'work/control-tower-hub-20261016'
try:
    client = Client(os.environ.get('GH_TOKEN'))
    runs = client.get('actions/workflows/control-tower-hub.yml/runs?branch=' + BRANCH + '&status=success&per_page=100')
    current = int(os.environ['GITHUB_RUN_ID'])
    candidates = [r for r in runs['workflow_runs'] if int(r['id']) < current
                  and r.get('event') == 'push' and r.get('head_branch') == BRANCH
                  and r.get('repository', {}).get('full_name') == 'Terry4410/field-visit-mileage-system']
    selected = ''
    for run in sorted(candidates, key=lambda x: int(x['id']), reverse=True):
        artifacts = client.get(f"actions/runs/{int(run['id'])}/artifacts?per_page=100")
        valid = [a for a in artifacts['artifacts'] if a['name'] == 'control-tower-state' and not a['expired']]
        if valid:
            selected = str(int(run['id']))
            break
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        output.write('run_id=' + selected + '\n')
except Exception:
    print('HUB_BASELINE_FAILED_HOLD')
    raise SystemExit(1)
