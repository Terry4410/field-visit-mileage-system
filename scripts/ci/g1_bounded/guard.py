"""No Azure: validate explicit exact-SHA Owner approval and refuse every rerun."""
import json
import os
import re
import subprocess
import sys
import urllib.request
from arm import coverage_ready

REPO = 'Terry4410/field-visit-mileage-system'
WORKFLOW = 'g1-bounded-readonly-v2.yml'
D0_WORKFLOW='g1-version-observation-v2.yml'
BASE = 'd919f8a107e3dbaee919b8c4e3b463240674eaf4'
ALLOWED = {'.github/workflows/' + WORKFLOW,
           '.github/workflows/' + D0_WORKFLOW,
           '.github/workflows/g1-bounded-readonly-worker-v2.yml',
           'docs/release/g1-bounded/REVIEW.md',
           *('scripts/ci/g1_bounded/' + p for p in ('arm.py', 'guard.py', 'sql.ps1', 'catalog.sql', 'test_boundary.py', 'checksums.sha256',
             'main_probe.py','main_channel.py','frontend.py','test_main_channel.py','offline_memory_probe.py','d0.py',
             'probe/Program.cs','probe/G1Probe.csproj'))}


def valid_approval(comment, sha, d0=False):
    # Exact machine-readable line must be posted after separate source review.
    line = f'G1_V2_DISPATCH_APPROVED {sha} {WORKFLOW} ONE_CONNECTION'
    if d0:line=f'G1_D0_OBSERVATION_APPROVED {sha} {D0_WORKFLOW} ONE_WSS ZERO_SQL'
    return (comment.get('user', {}).get('login') == 'Terry4410'
            and comment.get('user', {}).get('id') == 170161890
            and comment.get('author_association') == 'OWNER'
            and comment.get('issue_url') == f'https://api.github.com/repos/{REPO}/issues/41'
            and line in comment.get('body', '').splitlines())


def get(path):
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *a, **k):
            raise ValueError()
    url = 'https://api.github.com/repos/' + REPO + path
    req = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'], 'Accept': 'application/vnd.github+json'}, method='GET')
    with urllib.request.build_opener(NoRedirect()).open(req, timeout=20) as r:
        if r.status != 200 or r.geturl() != url or 'rel="next"' in r.headers.get('Link', ''):
            raise ValueError()
        data = r.read(1048577)
    if len(data) > 1048576:
        raise ValueError()
    return json.loads(data)


def main():
    try:
        e = os.environ
        mode=e.get('G1_MODE','FULL_G1')
        if mode not in ('FULL_G1','D0_VERSION_ONLY'):raise ValueError()
        d0=mode=='D0_VERSION_ONLY'
        sha = e.get('GITHUB_SHA', '')
        if (e.get('GITHUB_ACTIONS') != 'true' or e.get('GITHUB_REPOSITORY') != REPO
                or e.get('GITHUB_REF') != 'refs/heads/post-uat/v1.8.0'
                or e.get('GITHUB_EVENT_NAME') != 'workflow_dispatch'
                or e.get('GITHUB_ACTOR') != 'Terry4410' or e.get('GITHUB_TRIGGERING_ACTOR') != 'Terry4410'
                or e.get('GITHUB_RUN_ATTEMPT') != '1' or e.get('RUNNER_DEBUG') == '1'
                or not re.fullmatch('[0-9a-f]{40}', sha) or e.get('APPROVED_SHA') != sha
                or e.get('ACCEPT_COST') != ('false' if d0 else 'true')):
            raise ValueError()
        comment_id = e.get('APPROVAL_COMMENT', '')
        if not comment_id.isdecimal() or comment_id in ('6376598983', '6376257812', '6376423455'):
            raise ValueError()
        changed = subprocess.check_output(['git', 'diff', '--name-status', BASE, 'HEAD'], text=True, timeout=15).splitlines()
        if {line.removeprefix('A\t') for line in changed} != ALLOWED or any(not line.startswith('A\t') for line in changed):
            raise ValueError()
        comment = get('/issues/comments/' + comment_id)
        if not valid_approval(comment, sha,d0):
            raise ValueError()
        runs = get('/actions/workflows/' + (D0_WORKFLOW if d0 else WORKFLOW) + '/runs?event=workflow_dispatch&per_page=100')
        values = runs.get('workflow_runs')
        if not isinstance(values, list) or len(values) != 1 or str(values[0].get('id')) != e.get('GITHUB_RUN_ID'):
            raise ValueError()
        if runs.get('total_count') != 1 or values[0].get('head_sha') != sha or values[0].get('run_attempt') != 1:
            raise ValueError()
        if not d0 and not coverage_ready():
            print('DispatchReadiness=HOLD; Missing=REVIEWED_COMPLETION_PRODUCERS; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
            return 1
        print('OwnerSource=PASS; FreshExactDispatchConsent=PASS; PriorDispatches=0; Azure=NOT_ACCESSED;')
        return 0
    except Exception:
        print('OwnerSource=HOLD; Details=SUPPRESSED; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
        return 1


if __name__ == '__main__':
    sys.exit(main())
