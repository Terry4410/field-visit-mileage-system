"""ARM6 independent one-shot GitHub consent and immutable source delta guard.

GitHub GETs only; invoked before any Azure OIDC or ARM token request.
"""
import json
import os
import re
import subprocess
import sys
import urllib.request

REPO = 'Terry4410/field-visit-mileage-system'
WORKFLOW = 'g1-scm-arm6-readonly.yml'
BASE = '0e4ede50141e5d7456f84fde9000db8c01c3b59e'
PREFIX = 'G1_ARM6_EXECUTION_APPROVED'
ALLOWED = {
    '.github/workflows/' + WORKFLOW,
    '.github/workflows/g1-arm6-worker.yml',
    '.github/workflows/g1-arm6-offline.yml',
    *('scripts/ci/g1_arm6/' + f for f in ('arm6.py', 'guard.py', 'test_arm6.py', 'test_guard.py', 'checksums.sha256')),
    'docs/release/g1-arm6/REVIEW.md', 'docs/release/g1-arm6/main-registration.yml',
}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        raise ValueError()


def context(e):
    sha = e.get('GITHUB_SHA', '')
    return (e.get('GITHUB_ACTIONS') == 'true' and e.get('GITHUB_REPOSITORY') == REPO and
            e.get('GITHUB_REF') == 'refs/heads/post-uat/v1.8.0' and
            e.get('GITHUB_EVENT_NAME') == 'workflow_dispatch' and e.get('GITHUB_ACTOR') == 'Terry4410' and
            e.get('GITHUB_TRIGGERING_ACTOR') == 'Terry4410' and e.get('GITHUB_RUN_ATTEMPT') == '1' and
            e.get('RUNNER_DEBUG') != '1' and re.fullmatch('[a-f0-9]{40}', sha) is not None and
            e.get('APPROVED_SHA') == sha and e.get('G1_MODE') == 'SCM_ARM6_READONLY' and
            e.get('ACCEPT_COST') == 'false')


def approval(comment, sha):
    exact = f'{PREFIX} {sha} {WORKFLOW} MAX6_GET ZERO_SCM ZERO_SQL NO_RETRY'
    return (comment.get('user', {}).get('login') == 'Terry4410' and
            comment.get('user', {}).get('id') == 170161890 and
            comment.get('author_association') == 'OWNER' and
            comment.get('issue_url') == f'https://api.github.com/repos/{REPO}/issues/41' and
            exact in comment.get('body', '').splitlines())


def delta(changed):
    lines = changed.splitlines()
    return (len(lines) == len(ALLOWED) and all(x.startswith('A\t') for x in lines) and
            {x[2:] for x in lines} == ALLOWED)


def one_shot(history, env):
    runs = history.get('workflow_runs')
    return (history.get('total_count') == 1 and isinstance(runs, list) and len(runs) == 1 and
            str(runs[0].get('id')) == env.get('GITHUB_RUN_ID') and
            runs[0].get('head_sha') == env.get('GITHUB_SHA') and
            runs[0].get('event') == 'workflow_dispatch' and
            runs[0].get('head_branch') == 'post-uat/v1.8.0' and
            runs[0].get('path') == '.github/workflows/' + WORKFLOW and
            runs[0].get('run_attempt') == 1 and
            runs[0].get('actor', {}).get('login') == 'Terry4410' and
            runs[0].get('triggering_actor', {}).get('login') == 'Terry4410')


def get(path, token):
    url = 'https://api.github.com/repos/' + REPO + path
    req = urllib.request.Request(url, method='GET', headers={
        'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json'})
    with urllib.request.build_opener(NoRedirect(), urllib.request.ProxyHandler({})).open(req, timeout=20) as r:
        if r.status != 200 or r.geturl() != url or 'rel="next"' in r.headers.get('Link', ''):
            raise ValueError()
        raw = r.read(1048577)
    if len(raw) > 1048576:
        raise ValueError()
    return json.loads(raw)


def main():
    try:
        e = os.environ
        if not context(e) or not re.fullmatch('[0-9]{1,20}', e.get('APPROVAL_COMMENT', '')):
            raise ValueError()
        # Old read-only approval is not permission to run a NEW source/workflow.
        if int(e['APPROVAL_COMMENT']) <= 6380488858:
            raise ValueError()
        subprocess.check_call(['git', 'merge-base', '--is-ancestor', BASE, 'HEAD'],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
        changed = subprocess.check_output(['git', 'diff', '--name-status', BASE, 'HEAD'], text=True, timeout=15)
        if not delta(changed):
            raise ValueError()
        if not approval(get('/issues/comments/' + e['APPROVAL_COMMENT'], e['GH_TOKEN']), e['GITHUB_SHA']):
            raise ValueError()
        if not one_shot(get('/actions/workflows/' + WORKFLOW + '/runs?event=workflow_dispatch&per_page=100', e['GH_TOKEN']), e):
            raise ValueError()
        print('ARM6Source=PASS; FreshARM6Consent=PASS; PriorARM6Dispatches=0; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
        return 0
    except Exception:
        print('ARM6Source=HOLD; Details=SUPPRESSED; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
        return 1


if __name__ == '__main__':
    sys.exit(main())
