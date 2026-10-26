"""GET-only previous-success selection; immutable per-run/attempt artifact names."""
import os
import re
from hub import BRANCH, Client, REPO


def select(client, current):
    runs = client.get('actions/workflows/control-tower-hub.yml/runs?branch=' + BRANCH + '&status=success&per_page=100')
    candidates = [r for r in runs['workflow_runs'] if int(r['id']) < current
                  and r.get('event') == 'push' and r.get('head_branch') == BRANCH
                  and r.get('conclusion') == 'success' and r.get('status') == 'completed'
                  and r.get('repository', {}).get('full_name') == REPO]
    for run in sorted(candidates, key=lambda r: int(r['id']), reverse=True):
        rid = int(run['id'])
        valid = []
        for artifact in client.pages(f'actions/runs/{rid}/artifacts', key='artifacts'):
            name = artifact.get('name', '')
            match = re.fullmatch(r'control-tower-state-' + str(rid) + r'-([1-9][0-9]*)', name)
            if (artifact.get('expired') is False and (name == 'control-tower-state' or match)
                    and artifact.get('workflow_run', {}).get('head_sha') == run['head_sha']):
                valid.append((int(match[1]) if match else 0, name))
        if valid:
            return str(rid), max(valid)[1]
    return '', ''


def main():
    try:
        rid, name = select(Client(os.environ.get('GH_TOKEN')), int(os.environ['GITHUB_RUN_ID']))
        with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
            output.write('run_id=' + rid + '\nartifact_name=' + name + '\n')
    except Exception:
        print('HUB_BASELINE_FAILED_HOLD')
        raise SystemExit(1)

if __name__ == '__main__':
    main()
