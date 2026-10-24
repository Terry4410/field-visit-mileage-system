"""Fixed-target, no-retry ARM inventory. Raw responses/tokens stay in memory."""
import hashlib
import json
import os
import sys
import time
import urllib.request
import urllib.parse
from pathlib import Path

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


def exchange_token(scope, opener=None):
    """Existing federation only; zero ARM discovery or CLI/SDK retries."""
    if scope not in ('https://management.azure.com/.default', 'https://database.windows.net/.default'):
        raise Stop()
    url = os.environ.get('ACTIONS_ID_TOKEN_REQUEST_URL', '')
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != 'https' or parsed.username or parsed.password or parsed.fragment or parsed.port not in (None,443):
        raise Stop()
    # GitHub-hosted Actions OIDC request endpoint, never a caller-supplied URL.
    if not parsed.hostname or not parsed.hostname.endswith('.actions.githubusercontent.com'):
        raise Stop()
    params = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
    if 'audience' in params:
        raise Stop()
    url += ('&' if parsed.query else '?') + urllib.parse.urlencode({'audience':'api://AzureADTokenExchange'})
    op = opener or urllib.request.build_opener(NoRedirect())
    def read(req):
        with op.open(req,timeout=20) as r:
            if r.status != 200 or r.geturl() != req.full_url:
                raise Stop()
            raw=r.read(262145)
        if len(raw)>262144:
            raise Stop()
        return json.loads(raw)
    assertion=read(urllib.request.Request(url,headers={'Authorization':'Bearer '+os.environ['ACTIONS_ID_TOKEN_REQUEST_TOKEN']},method='GET'))['value']
    tenant=os.environ['AZURE_TENANT_ID']
    token_url='https://login.microsoftonline.com/'+tenant+'/oauth2/v2.0/token'
    data=urllib.parse.urlencode(dict(client_id=os.environ['AZURE_CLIENT_ID'],scope=scope,
        grant_type='client_credentials',client_assertion_type='urn:ietf:params:oauth:client-assertion-type:jwt-bearer',
        client_assertion=assertion)).encode()
    token=read(urllib.request.Request(token_url,data=data,headers={'Content-Type':'application/x-www-form-urlencoded'},method='POST'))['access_token']
    if not isinstance(token,str) or not 100<len(token)<65536:
        raise Stop()
    return token


def coverage_ready():
    """An unavailable main-app channel blocks publication, before any OIDC."""
    doc=(Path(__file__).parents[3]/'docs/release/g1-bounded/REVIEW.md').read_text()
    marker='<!-- G1_COMPLETION_ROUTES -->'
    if doc.count(marker)!=2:
        return False
    plan=json.loads(doc.split(marker)[1])
    required={'runtime','apiPrincipal','flags','recovery'}
    if set(plan)!=required:
        return False
    root=Path(__file__).parents[3].resolve()
    for route in plan.values():
        if route.get('state')!='IMPLEMENTED_AND_OFFLINE_VERIFIED':
            return False
        name=route.get('sourcePath')
        if not isinstance(name,str) or not name.startswith('scripts/ci/g1_bounded/'):
            return False
        p=(root/name).resolve()
        if root not in p.parents or not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=route.get('sourceSha256'):
            return False
    return True


def final_gate(receipts, now, expected):
    """Join controller-verified measurements, never a CI-success/boolean shortcut.

    This function is an acceptance contract, not a producer for missing facts.
    Callers may supply only independently reviewed collector receipts. The live
    source guard remains blocked while any corresponding producer is absent.
    """
    gates={'target','runtime','schema','apiPrincipal','flags','recovery'}
    if (not isinstance(expected,dict) or set(expected)!={'sourceSha','producers'} or
        not isinstance(expected['sourceSha'],str) or len(expected['sourceSha'])!=40 or
        not isinstance(expected['producers'],dict) or set(expected['producers'])!=gates or
        any(not isinstance(p,str) or len(p)!=64 or any(c not in '0123456789abcdef' for c in p) for p in expected['producers'].values())):
        return 'HOLD'
    if not isinstance(receipts,dict) or set(receipts)!=gates:
        return 'HOLD'
    for name,r in receipts.items():
        if not isinstance(r,dict) or set(r)!= {'measurementSha256','sourceSha','targetBinding','observedAt','outcome','producer'}:
            return 'HOLD'
        if (r['outcome']!='MATCH' or r['producer']!=expected['producers'].get(name)
            or r['sourceSha']!=expected['sourceSha'] or r['targetBinding']!=CONTEXT_SHA256
            or type(r['observedAt']) is not int or not 0<=now-r['observedAt']<=600
            or not isinstance(r['measurementSha256'],str) or len(r['measurementSha256'])!=64
            or any(c not in '0123456789abcdef' for c in r['measurementSha256'])):
            return 'HOLD'
    return 'PASS'


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
        if sys.argv[1:]==['--sql-token-pipe']:
            # Token is consumed over a private redirected pipe by sql.ps1;
            # never write it to a file, GitHub output, environment or console.
            if sys.stdout.isatty():
                raise Stop()
            sys.stdout.write(exchange_token('https://database.windows.net/.default'))
            return 0
        if sys.argv[1:]:
            raise Stop()
        token = exchange_token('https://management.azure.com/.default')
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
        print('Inventory=COLLECTED; CompleteGate=REQUIRES_VERIFIED_RECEIPTS;')
        return 0
    except Exception:
        print('Target=HOLD; Details=SUPPRESSED; Sql=NOT_ACCESSED; LiveG1=HOLD;')
        return 1


if __name__ == '__main__':
    if sys.argv[1:]==['--coverage']:
        ready=coverage_ready()
        print('CompletionCoverage='+('READY' if ready else 'HOLD')+'; Azure=NOT_ACCESSED; SQL=NOT_ACCESSED;')
        sys.exit(0 if ready else 2)
    sys.exit(main())
