"""A-stage proposed collector. Import/tests never authenticate or access a network.
Live entry requires separately approved protected source and already-issued tokens.
No Azure login, CLI, SQL, SDK retries, settings, backup blobs or business endpoints.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

REPO = 'Terry4410/field-visit-mileage-system'
RC = 'f29affb3299828f45b1f131321779e8bea2f86a8'
ARTIFACT = 12236590943
DIGEST = 'sha256:6a604b212e88fb6039c449f9a6701d8aa0c68d7c8feec9e7b1e2600a4ddedb7b'
MAX_BYTES = 2 * 1024 * 1024
GUID = re.compile(r'^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')

class Stop(Exception):
    pass

def require(ok):
    if not ok:
        raise Stop()

def endpoints(subscription):
    require(bool(GUID.fullmatch(subscription)))
    root = f'/subscriptions/{subscription}/resourceGroups/rg-fieldvisit-uat/providers/'
    app = root + 'Microsoft.Web/sites/api-fieldvisit-uat'
    db = root + 'Microsoft.Sql/servers/sql-fieldvisit-jpe-uat/databases/db-fieldvisit-uat'
    return {
        'APP': ('https://management.azure.com' + app + '?api-version=2025-03-01', app),
        'DEPLOYMENTS': ('https://management.azure.com' + app + '/deployments?api-version=2025-03-01', app),
        'DATABASE': ('https://management.azure.com' + db + '?api-version=2023-08-01', db),
        'PITR': ('https://management.azure.com' + db + '/backupShortTermRetentionPolicies/default?api-version=2023-08-01', db + '/backupShortTermRetentionPolicies/default'),
        'PAGES': ('https://api.github.com/repos/' + REPO + '/pages', None),
        'RC_ARTIFACT': ('https://api.github.com/repos/' + REPO + '/actions/runs/39357993766/artifacts?per_page=100', None),
    }

def secret_free(value):
    if isinstance(value, dict):
        for k, v in value.items():
            require(not (v and re.search(r'password|secret|token|connectionstring|storageaccounturl|sasurl', k, re.I)))
            secret_free(v)
    elif isinstance(value, list):
        for v in value:
            secret_free(v)
    elif isinstance(value, str):
        require(not re.search(r'[?&](?:sig|access_token|token)=|(?:Password|AccountKey)\s*=|\beyJ[\w-]+\.[\w-]+\.[\w-]+', value, re.I))

def pairs(items):
    result = {}
    for k, v in items:
        require(k not in result)
        result[k] = v
    return result

def identity(token, tenant, client, now):
    # Claims are a pre-request binding check, not a local signature verification.
    # ARM must authenticate the bearer; any non-200 response is fatal.
    require(bool(token) and bool(GUID.fullmatch(tenant)) and bool(GUID.fullmatch(client)))
    parts = token.split('.')
    require(len(parts) == 3)
    claims = json.loads(base64.urlsafe_b64decode(parts[1] + '=' * (-len(parts[1]) % 4)), object_pairs_hook=pairs)
    require(claims.get('tid') == tenant)
    require(claims.get('appid', claims.get('azp')) == client)
    require(claims.get('aud') in ('https://management.azure.com/', 'https://management.core.windows.net/'))
    require(type(claims.get('exp')) is int and claims['exp'] > now + 600)

class Budget:
    def __init__(self, allowed, transport, clock=time.monotonic):
        self.allowed, self.transport, self.clock = set(allowed), transport, clock
        self.deadline, self.count = clock() + 600, 0

    def get(self, url, tokens):
        # Every outbound GET, including a redirect hop, consumes the same budget.
        while True:
            require(url in self.allowed and self.clock() < self.deadline and self.count < 20)
            self.count += 1
            origin = urllib.parse.urlsplit(url).netloc
            require(origin in tokens and tokens[origin])
            timeout = min(10, self.deadline - self.clock())
            status, headers, raw = self.transport(url, tokens[origin], timeout)
            require(self.clock() < self.deadline)
            if status in (301, 302, 303, 307, 308):
                dest = urllib.parse.urljoin(url, headers.get('location', ''))
                require(dest in self.allowed and urllib.parse.urlsplit(dest).netloc == origin)
                url = dest
                continue
            require(status == 200 and len(raw) <= MAX_BYTES)
            require('application/json' in headers.get('content-type', '').lower())
            require('link' not in headers)  # No automatic or silent partial pagination.
            if 'content-length' in headers:
                require(int(headers['content-length']) == len(raw))
            data = json.loads(raw, object_pairs_hook=pairs)
            secret_free(data)
            require(not (isinstance(data, dict) and data.get('nextLink')))
            return data

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None

def network_get(url, bearer, timeout):
    # Socket timeouts alone reset on each read. A real wall-clock alarm bounds
    # the complete request/body read, including a slowly streaming response.
    require(hasattr(signal, 'setitimer') and 0 < timeout <= 10)
    require(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0))
    def expired(*args):
        raise Stop()
    previous = signal.signal(signal.SIGALRM, expired)
    try:
        signal.setitimer(signal.ITIMER_REAL, timeout)
        return _network_get(url, bearer, timeout)
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous)

def _network_get(url, bearer, timeout):
    # No SDK, proxy credentials, redirect following, retry handler or cookie jar.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    req = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + bearer,
        'Accept': 'application/json', 'User-Agent': 'G1-A-stage'}, method='GET')
    try:
        response = opener.open(req, timeout=timeout)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.code, {k.lower(): v for k, v in response.headers.items()}, response.read(MAX_BYTES + 1)

def collect(subscription, tokens, transport, clock=time.monotonic):
    plan = endpoints(subscription)
    budget = Budget([v[0] for v in plan.values()], transport, clock)
    output = {}
    for name, (url, resource) in plan.items():
        data = budget.get(url, tokens)
        require(isinstance(data, dict))
        if name in ('APP', 'DATABASE', 'PITR'):
            require(data.get('id') == resource)
            require(data.get('name') == {'APP': 'api-fieldvisit-uat', 'DATABASE': 'db-fieldvisit-uat', 'PITR': 'default'}[name])
            props = data.get('properties')
            require(isinstance(props, dict))
            if name == 'DATABASE':
                state = props.get('status')
                require(state in ('Online', 'Paused', 'Resuming'))
                # Management state observation only. Paused never invokes SQL/wake.
                output['SQL_OPERATIONAL'] = 'PASS' if state == 'Online' else 'HOLD'
            elif name == 'PITR':
                days = props.get('retentionDays')
                require(type(days) is int and 1 <= days <= 35)
                output['PITR_METADATA'] = 'PASS'
            else:
                output['APP_TARGET'] = 'PASS'
        elif name == 'DEPLOYMENTS':
            require(isinstance(data.get('value'), list) and len(data['value']) <= 100)
            for item in data['value']:
                require(isinstance(item, dict) and item.get('id', '').startswith(resource + '/deployments/'))
            # Deployment IDs/messages/stamps are not running-binary proof.
            output['API_PACKAGE_ALIGNMENT'] = 'UNKNOWN'
        elif name == 'PAGES':
            require(data.get('html_url') == 'https://terry4410.github.io/field-visit-mileage-system/')
            output['PAGES_TARGET'] = 'PASS'
            output['FRONTEND_PACKAGE_ALIGNMENT'] = 'UNKNOWN'
        elif name == 'RC_ARTIFACT':
            require(data.get('total_count') == 1 and isinstance(data.get('artifacts'), list) and len(data['artifacts']) == 1)
            artifact = data['artifacts'][0]
            require(artifact.get('id') == ARTIFACT and artifact.get('digest') == DIGEST and artifact.get('expired') is False)
            require(artifact.get('workflow_run', {}).get('head_sha') == RC)
            output['RC_ARTIFACT_METADATA'] = 'PASS'
    output.update(API_PRINCIPAL_BINDING='UNKNOWN', RECOVERY_PAIR='UNKNOWN', G1='HOLD')
    require(all(v in ('PASS', 'HOLD', 'UNKNOWN') for v in output.values()))
    return output

def main(env):
    # No token acquisition, workflow or live dispatch is supplied by this module.
    require(env.get('A_STAGE_AUTHORIZATION') == 'ONE_A_STAGE_MANAGEMENT_GET')
    require(env.get('GITHUB_REPOSITORY') == REPO and env.get('GITHUB_EVENT_NAME') == 'workflow_dispatch')
    require(env.get('GITHUB_REF') == 'refs/heads/post-uat/v1.8.0')
    source = env.get('GITHUB_SHA', '')
    require(bool(re.fullmatch('[0-9a-f]{40}', source)) and source == env.get('A_STAGE_APPROVED_SHA'))
    require(not any(env.get(k, '').lower() in ('true', '1') for k in ('RUNNER_DEBUG', 'ACTIONS_STEP_DEBUG', 'ACTIONS_RUNNER_DEBUG')))
    require(hashlib.sha256(Path(__file__).read_bytes()).hexdigest() == env.get('A_STAGE_COLLECTOR_SHA256'))
    token = env.get('A_STAGE_ARM_TOKEN', '')
    identity(token, env.get('AZURE_TENANT_ID', ''), env.get('AZURE_CLIENT_ID', ''), time.time())
    subscription = env.get('AZURE_SUBSCRIPTION_ID', '')
    # Subscription must be independently reviewed, not inferred from token audience.
    require(subscription == env.get('A_STAGE_APPROVED_SUBSCRIPTION') and bool(GUID.fullmatch(subscription)))
    tokens = {'management.azure.com': token, 'api.github.com': env.get('A_STAGE_GITHUB_READ_TOKEN', '')}
    result = collect(subscription, tokens, network_get)
    print(json.dumps({'source': source, 'rc': RC, 'results': result}, sort_keys=True))

if __name__ == '__main__':
    try:
        main(os.environ)
    except Exception:
        # No external exception, response, identity, URL, token or claim is printed.
        print('A_STAGE=HOLD; Details=SUPPRESSED;')
        sys.exit(1)
