"""GitHub-only v3 source/consent/history guard. Never resets v2's history."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.request


REPO = 'Terry4410/field-visit-mileage-system'
WORKFLOW = 'g1-version-observation-v3.yml'
BASE = '0f81cc0878b3e29e90512c5a58ad2ee5afa8713e'
PREFIX = 'G1_D0_V3_DIAGNOSTIC_APPROVED'
ALLOWED = {'.github/workflows/' + WORKFLOW, '.github/workflows/g1-d0-v3-offline.yml', '.github/workflows/g1-d0-v3-worker.yml',
           *('scripts/ci/g1_d0_v3/' + f for f in
             ('d0.py', 'remote.py', 'guard.py', 'test_d0.py', 'test_guard.py', 'checksums.sha256')),
           'docs/release/g1-d0-v3/REVIEW.md', 'docs/release/g1-d0-v3/RECOVERY-INVENTORY.json',
           'docs/release/g1-d0-v3/main-registration.yml'}


def valid_approval(comment, sha):
    line = f'{PREFIX} {sha} {WORKFLOW} ONE_WSS ZERO_SQL NO_RETRY'
    return (comment.get('user', {}).get('login') == 'Terry4410' and
            comment.get('user', {}).get('id') == 170161890 and
            comment.get('author_association') == 'OWNER' and
            comment.get('issue_url') == f'https://api.github.com/repos/{REPO}/issues/41' and
            line in comment.get('body', '').splitlines())


def valid_context(e):
    sha = e.get('GITHUB_SHA', '')
    return (e.get('GITHUB_ACTIONS') == 'true' and e.get('GITHUB_REPOSITORY') == REPO and
            e.get('GITHUB_REF') == 'refs/heads/post-uat/v1.8.0' and
            e.get('GITHUB_EVENT_NAME') == 'workflow_dispatch' and e.get('GITHUB_ACTOR') == 'Terry4410' and
            e.get('GITHUB_TRIGGERING_ACTOR') == 'Terry4410' and e.get('GITHUB_RUN_ATTEMPT') == '1' and
            e.get('RUNNER_DEBUG') != '1' and bool(re.fullmatch('[a-f0-9]{40}', sha)) and
            e.get('APPROVED_SHA') == sha and e.get('G1_MODE') == 'D0_V3_DIAGNOSTIC' and
            e.get('ACCEPT_COST') == 'false')


def valid_delta(changed):
    lines = changed.splitlines()
    return (len(lines) == len(ALLOWED) and {line.removeprefix('A\t') for line in lines} == ALLOWED and
            all(line.startswith('A\t') for line in lines))


def valid_runs(runs, e):
    values = runs.get('workflow_runs')
    if runs.get('total_count') != 1 or not isinstance(values, list) or len(values) != 1:
        return False
    run = values[0]
    return (str(run.get('id')) == e.get('GITHUB_RUN_ID') and run.get('head_sha') == e.get('GITHUB_SHA') and
            run.get('head_branch') == 'post-uat/v1.8.0' and run.get('event') == 'workflow_dispatch' and
            run.get('path') == '.github/workflows/' + WORKFLOW and run.get('run_attempt') == 1 and
            run.get('actor', {}).get('login') == 'Terry4410' and
            run.get('triggering_actor', {}).get('login') == 'Terry4410')


def get(path):
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):
            raise ValueError()
    url = 'https://api.github.com/repos/' + REPO + path
    request = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
                                    'Accept': 'application/vnd.github+json'}, method='GET')
    with urllib.request.build_opener(NoRedirect()).open(request, timeout=20) as response:
        if response.status != 200 or response.geturl() != url or 'rel="next"' in response.headers.get('Link', ''):
            raise ValueError()
        data = response.read(1048577)
    if len(data) > 1048576:
        raise ValueError()
    return json.loads(data)


def main():
    try:
        e = os.environ
        if not valid_context(e):
            raise ValueError()
        comment_id = e.get('APPROVAL_COMMENT', '')
        if not re.fullmatch('[0-9]{1,20}', comment_id) or comment_id == '6378991235':
            raise ValueError()
        subprocess.check_call(['git', 'merge-base', '--is-ancestor', BASE, 'HEAD'],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
        changed = subprocess.check_output(['git', 'diff', '--name-status', BASE, 'HEAD'], text=True, timeout=15)
        if not valid_delta(changed):
            raise ValueError()
        comment = get('/issues/comments/' + comment_id)
        if not valid_approval(comment, e['GITHUB_SHA']):
            raise ValueError()
        runs = get('/actions/workflows/' + WORKFLOW + '/runs?event=workflow_dispatch&per_page=100')
        if not valid_runs(runs, e):
            raise ValueError()
        print('V3OwnerSource=PASS; FreshExactV3Consent=PASS; PriorV3Dispatches=0; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
        return 0
    except Exception:
        print('V3OwnerSource=HOLD; Details=SUPPRESSED; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
        return 1


if __name__ == '__main__':
    sys.exit(main())
