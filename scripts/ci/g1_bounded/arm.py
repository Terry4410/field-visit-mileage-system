"""Fixed-target, no-retry ARM inventory. Raw responses/tokens stay in memory."""
import hashlib
import json
import os
import subprocess
import sys
import time
import urllib.request

REPO = 'Terry4410/field-visit-mileage-system'
REF = 'refs/heads/post-uat/v1.8.0'
RG, APP, SERVER, DB = 'rg-fieldvisit-uat', 'api-fieldvisit-uat', 'sql-fieldvisit-jpe-uat', 'db-fieldvisit-uat'
CONTEXT_SHA256 = '7aa60b6067a7cbc77f6d7c8ccb92883ae02e75dadd0d30741e0ba838c82cb31e'


class Stop(Exception):
    pass


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise Stop()


def context_valid(env):
    return (env.get('GITHUB_ACTIONS') == 'true' and env.get('GITHUB_REPOSITORY') == REPO
            and env.get('GITHUB_REF') == REF and env.get('GITHUB_ACTOR') == 'Terry4410'
            and env.get('GITHUB_TRIGGERING_ACTOR') == 'Terry4410'
            and env.get('GITHUB_EVENT_NAME') == 'workflow_dispatch'
            and env.get('GITHUB_RUN_ATTEMPT') == '1' and env.get('RUNNER_DEBUG') != '1'
            and env.get('APPROVED_SHA') == env.get('GITHUB_SHA')
            and len(env.get('GITHUB_SHA', '')) == 40)


def az(*args):
    # No shell, no console forwarding, no retry.
    p = subprocess.run(['az', *args], capture_output=True, text=True, timeout=25, check=True)
    return p.stdout.strip()


def paths(sub):
    root = f'/subscriptions/{sub}/resourceGroups/{RG}'
    server = f'{root}/providers/Microsoft.Sql/servers/{SERVER}'
    app = f'{root}/providers/Microsoft.Web/sites/{APP}'
    return {
        'group': root + '?api-version=2021-04-01',
        'api': app + '?api-version=2024-04-01',
        'server': server + '?api-version=2023-08-01',
        'database': server + f'/databases/{DB}?api-version=2023-08-01',
        'retention': server + f'/databases/{DB}/backupShortTermRetentionPolicies/default?api-version=2023-08-01',
    }


class Reader:
    def __init__(self, plan, token, opener=None, clock=time.monotonic):
        self.allowed = set(plan.values())
        self.token, self.calls, self.clock = token, 0, clock
        self.started = clock()
        self.opener = opener or urllib.request.build_opener(NoRedirect())

    def get(self, path):
        if path not in self.allowed or self.calls >= 5 or self.clock() - self.started >= 120:
            raise Stop()
        self.calls += 1  # Errors consume the request; never retry.
        url = 'https://management.azure.com' + path
        req = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + self.token}, method='GET')
        with self.opener.open(req, timeout=20) as r:
            if r.status != 200 or r.geturl() != url:
                raise Stop()
            raw = r.read(262145)
        if len(raw) > 262144:
            raise Stop()
        d = json.loads(raw)
        if not isinstance(d, dict) or d.get('nextLink') or str(d.get('id', '')).lower() != path.split('?')[0].lower():
            raise Stop()
        return d


def main():
    try:
        if not context_valid(os.environ):
            raise Stop()
        ctx = [os.environ.get(k, '').lower() for k in ('AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_CLIENT_ID')]
        if hashlib.sha256('\n'.join(ctx).encode()).hexdigest() != CONTEXT_SHA256:
            raise Stop()
        if sys.argv[1:] == ['--prelogin']:
            print('PinnedOidcContext=PASS; Azure=NOT_ACCESSED;')
            return 0
        if sys.argv[1:]:
            raise Stop()
        account = json.loads(az('account', 'show', '-o', 'json'))
        if str(account.get('tenantId', '')).lower() != ctx[0] or str(account.get('id', '')).lower() != ctx[1]:
            raise Stop()
        token = az('account', 'get-access-token', '--resource', 'https://management.azure.com/', '--query', 'accessToken', '-o', 'tsv')
        plan = paths(ctx[1])
        reader = Reader(plan, token)
        pages = {k: reader.get(p) for k, p in plan.items()}
        if pages['server'].get('properties', {}).get('fullyQualifiedDomainName') != SERVER + '.database.windows.net':
            raise Stop()
        api = pages['api'].get('properties', {})
        db = pages['database'].get('properties', {})
        retention = pages['retention'].get('properties', {}).get('retentionDays')
        print('Target=PASS; ArmCollectorGets=5; Redirects=DENIED; Retries=0;')
        print('ApiState=' + ('RUNNING' if api.get('state') == 'Running' else 'OTHER') + ';')
        print('DatabaseState=' + ('PAUSED' if db.get('status') == 'Paused' else 'ONLINE' if db.get('status') == 'Online' else 'OTHER') + ';')
        print('ManagedIdentity=' + ('CONFIGURED_NOT_SQL_PROOF' if pages['api'].get('identity', {}).get('principalId') else 'UNKNOWN') + ';')
        print('PitrMetadata=' + ('PRESENT_NOT_RESTORE_PROOF' if type(retention) is int and 1 <= retention <= 35 and db.get('earliestRestoreDate') else 'UNKNOWN') + ';')
        print('RuntimeBinary=UNKNOWN; FrontendBinary=UNKNOWN; Flags=UNKNOWN; RecoveryPair=UNKNOWN; LiveG1=HOLD;')
        return 0
    except Exception:
        print('Target=HOLD; Details=SUPPRESSED; Sql=NOT_ACCESSED; LiveG1=HOLD;')
        return 1


if __name__ == '__main__':
    sys.exit(main())
