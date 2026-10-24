"""Capped ARM-only SCM/RBAC read. New approval needed AFTER source merge.

No SCM, WSS, SQL, shell, privilege changes or raw provider outputs.
"""
import base64
import fnmatch
import hashlib
import json
import os
import re
import sys
import time
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'g1_bounded'))
from arm import APP, RG, CONTEXT_SHA256, Stop, context_valid, exchange_token

WORKFLOW_MODE = 'SCM_ARM6_READONLY'
PUBLISH = 'microsoft.web/sites/publish/action'
STEPS = ('SITE', 'SCM_CONFIG', 'ROLE_ASSIGNMENTS', 'ROLE_DEFINITIONS', 'DENY_ASSIGNMENTS', 'CALLER_PERMISSIONS')


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        raise Stop()


def paths(subscription):
    root = '/subscriptions/' + subscription + '/resourceGroups/' + RG + '/providers/Microsoft.Web/sites/' + APP
    auth = root + '/providers/Microsoft.Authorization/'
    return (
        root + '?api-version=2024-04-01',
        root + '/config/web?api-version=2024-11-01',
        auth + 'roleAssignments?api-version=2022-04-01&$filter=atScope()',
        auth + 'roleDefinitions?api-version=2022-04-01',
        auth + 'denyAssignments?api-version=2022-04-01&$filter=atScope()',
        auth + 'permissions?api-version=2022-04-01',
    )


def bound_oid(token, tenant, client):
    """Claims are inspected only from the HTTPS Entra exchange for pinned client.

    Not a general JWT verifier and never accepts a caller-provided token source.
    """
    if not isinstance(token, str) or token.count('.') != 2:
        raise Stop()
    chunk = token.split('.')[1]
    if len(chunk) > 16000 or not re.fullmatch('[A-Za-z0-9_-]+', chunk):
        raise Stop()
    claims = json.loads(base64.urlsafe_b64decode(chunk + '=' * (-len(chunk) % 4)))
    if (not isinstance(claims, dict) or str(claims.get('tid', '')).lower() != tenant or
            str(claims.get('appid', claims.get('azp', ''))).lower() != client or
            str(claims.get('idtyp', 'app')) not in ('app', '') or
            str(claims.get('aud', '')).lower() not in ('https://management.azure.com/', 'https://management.azure.com')):
        raise Stop()
    oid = str(claims.get('oid', '')).lower()
    if not re.fullmatch('[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}', oid):
        raise Stop()
    return oid


class SixGets:
    def __init__(self, token, paths_, opener=None, clock=time.monotonic):
        if len(paths_) != 6 or len(set(paths_)) != 6:
            raise Stop()
        self.token, self.paths = token, tuple(paths_)
        self.next = 0
        self.attempted = 0
        self.completed = 0
        self.started, self.clock = clock(), clock
        self.opener = opener if opener is not None else urllib.request.build_opener(NoRedirect(), urllib.request.ProxyHandler({}))

    def get(self, step):
        if (type(step) is not int or step != self.next or step >= 6 or self.attempted >= 6 or
                self.clock() - self.started > 120):
            raise Stop()
        self.next += 1
        self.attempted += 1  # attempt consumed even on connection/HTTP failure
        uri = 'https://management.azure.com' + self.paths[step]
        req = urllib.request.Request(uri, method='GET', headers={'Authorization': 'Bearer ' + self.token})
        with self.opener.open(req, timeout=20) as response:
            if response.status != 200 or response.geturl() != uri or response.headers.get('Location'):
                raise Stop()
            raw = response.read(1048577)
        if len(raw) > 1048576:
            raise Stop()
        value = json.loads(raw)
        if not isinstance(value, dict) or value.get('nextLink') or value.get('nextlink'):
            raise Stop()
        return value


def wildcard(pattern, action=PUBLISH):
    return isinstance(pattern, str) and fnmatch.fnmatchcase(action, pattern.lower())


def permission_entry(entry):
    if not isinstance(entry, dict):
        raise Stop()
    actions, exclusions = entry.get('actions'), entry.get('notActions')
    if not isinstance(actions, list) or not isinstance(exclusions, list) or any(not isinstance(x, str) for x in actions + exclusions):
        raise Stop()
    return any(wildcard(x) for x in actions) and not any(wildcard(x) for x in exclusions)


def list_items(value):
    if set(value) != {'value'} or not isinstance(value['value'], list) or len(value['value']) > 250:
        raise Stop()
    return value['value']


def site_projection(value, sub):
    expected = '/subscriptions/' + sub + '/resourceGroups/' + RG + '/providers/Microsoft.Web/sites/' + APP
    props = value.get('properties')
    if (not isinstance(props, dict) or str(value.get('id', '')).lower() != expected.lower() or
            str(value.get('name', '')).lower() != APP.lower() or props.get('reserved') is not True or
            props.get('state') != 'Running'):
        raise Stop()
    return props.get('publicNetworkAccess') in ('Enabled', 'Disabled')


def scm_projection(value):
    props = value.get('properties')
    if not isinstance(props, dict):
        raise Stop()
    use = props.get('scmIpSecurityRestrictionsUseMain')
    default = props.get('scmIpSecurityRestrictionsDefaultAction')
    rules = props.get('ipSecurityRestrictions') if use is True else props.get('scmIpSecurityRestrictions')
    default = props.get('ipSecurityRestrictionsDefaultAction') if use is True else default
    if type(use) is not bool or default not in ('Allow', 'Deny') or not isinstance(rules, list):
        raise Stop()
    actions = []
    for rule in rules:
        if not isinstance(rule, dict) or rule.get('action') not in ('Allow', 'Deny'):
            raise Stop()
        actions.append(rule['action'])  # DO NOT read rule IPs, names or headers
    policy = ('MIXED' if len(set(actions)) == 2 else
              'RESTRICTIVE' if default == 'Deny' or 'Deny' in actions else 'NONRESTRICTIVE')
    tls = props.get('scmMinTlsVersion')
    return dict(scmUseMain='YES' if use else 'NO', scmDefault=default.upper(), scmRulePolicy=policy,
                scmTls='ACCEPTED' if tls in ('1.2', '1.3') else 'UNVERIFIED')


def assignments_projection(value, oid):
    """Any unknown membership/conditions forces UNKNOWN rather than unsafe YES."""
    direct_ids = set()
    ambiguous = False
    for row in list_items(value):
        p = row.get('properties') if isinstance(row, dict) else None
        if not isinstance(p, dict):
            raise Stop()
        if p.get('condition') or p.get('conditionVersion'):
            ambiguous = True
        principal = str(p.get('principalId', '')).lower()
        if principal == oid:
            role_id = str(p.get('roleDefinitionId', '')).lower()
            if not re.fullmatch(r'/subscriptions/[a-f0-9-]{36}/providers/microsoft.authorization/roledefinitions/[a-f0-9-]{36}', role_id):
                ambiguous = True
            else:
                direct_ids.add(role_id)
        elif p.get('principalType') in ('Group', None, 'Unknown'):
            ambiguous = True
    return direct_ids, ambiguous


def definition_projection(value, role_ids):
    definitions = {}
    for row in list_items(value):
        if not isinstance(row, dict) or not isinstance(row.get('properties'), dict):
            raise Stop()
        key = str(row.get('id', '')).lower()
        if key not in role_ids:
            continue
        perms = row['properties'].get('permissions')
        if not isinstance(perms, list):
            raise Stop()
        definitions[key] = any(permission_entry(x) for x in perms)
    return role_ids.issubset(definitions), any(definitions.values())


def deny_projection(value, oid):
    """Return complete only if all relevant denies are unambiguously inapplicable."""
    for row in list_items(value):
        props = row.get('properties') if isinstance(row, dict) else None
        if not isinstance(props, dict):
            raise Stop()
        perms = props.get('permissions')
        if not isinstance(perms, list):
            raise Stop()
        relevant = False
        for perm in perms:
            if not isinstance(perm, dict):
                raise Stop()
            actions, not_actions = perm.get('actions'), perm.get('notActions')
            if not isinstance(actions, list) or not isinstance(not_actions, list):
                raise Stop()
            relevant |= any(wildcard(x) for x in actions) and not any(wildcard(x) for x in not_actions)
        if relevant:
            # Group memberships, SystemDefined and exclusions may be unknown.
            return 'INCOMPLETE'
    return 'COMPLETE'


def caller_permissions(value):
    return any(permission_entry(x) for x in list_items(value))


def blank(source):
    return dict(protocol='SCM_ARM6_READONLY', sourceSha=source if isinstance(source, str) and re.fullmatch('[a-f0-9]{40}', source) else 'UNKNOWN',
                status='HOLD', preGate='HOLD', target='UNKNOWN', GET_ATTEMPTS=0, GET_COMPLETED=0,
                steps={k: 'SKIPPED' for k in STEPS}, scmUseMain='UNKNOWN', scmDefault='UNKNOWN',
                scmRulePolicy='UNKNOWN', scmTls='UNKNOWN', scmNetworkFeasibility='UNKNOWN',
                oidcIdentityBinding='UNKNOWN', oidcPublishAction='UNKNOWN',
                denyOrConditionCoverage='UNKNOWN', sql='NOT_ACCESSED', scm='NOT_ACCESSED', write='NONE')


def run(opener=None, token_factory=None, env=None):
    e = os.environ if env is None else env
    out = blank(e.get('GITHUB_SHA'))
    reader = None
    try:
        if (not context_valid(e) or e.get('G1_MODE') != WORKFLOW_MODE or e.get('G1_ARM6_GUARD') != 'PASS' or
                e.get('ACCEPT_COST') != 'false' or sys.argv[1:]):
            raise Stop()
        ctx = [e.get(k, '').lower() for k in ('AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_CLIENT_ID')]
        if hashlib.sha256('\n'.join(ctx).encode()).hexdigest() != CONTEXT_SHA256:
            raise Stop()
        # The trusted pinned federated token exchange is the sole source of caller identity.
        exchange = exchange_token if token_factory is None else token_factory
        token = exchange('https://management.azure.com/.default')
        oid = bound_oid(token, ctx[0], ctx[2])
        out['oidcIdentityBinding'] = 'VERIFIED'
        out['preGate'] = 'PASS'
        reader = SixGets(token, paths(ctx[1]), opener=opener)
        vals = []
        for i, key in enumerate(STEPS):
            out['steps'][key] = 'ATTEMPTED'
            response = reader.get(i)
            if i == 0:
                site_projection(response, ctx[1])
                out['target'] = 'CONFIRMED'
            if i == 1:
                out.update(scm_projection(response))
            if i == 2:
                role_ids, ambiguity = assignments_projection(response, oid)
            if i == 3:
                definition_complete, direct_grant = definition_projection(response, role_ids)
            if i == 4:
                deny_complete = deny_projection(response, oid)
            if i == 5:
                effective_allow = caller_permissions(response)
            reader.completed += 1  # validated fixed-step response; never count malformed as complete
            out['steps'][key] = 'PASS'
        out['denyOrConditionCoverage'] = ('COMPLETE' if not ambiguity and definition_complete and deny_complete == 'COMPLETE' else 'INCOMPLETE')
        if out['denyOrConditionCoverage'] == 'COMPLETE':
            # A caller-effective Allow can be confirmed only with direct applicable grants.
            out['oidcPublishAction'] = 'YES' if direct_grant and effective_allow else 'NO' if not effective_allow else 'UNKNOWN'
        out['scmNetworkFeasibility'] = 'UNKNOWN'  # no GitHub Runner reachability test permitted
        out['status'] = 'PASS_METADATA_ONLY' if out['oidcPublishAction'] in ('YES', 'NO') else 'HOLD'
    except Exception:
        # No raw provider errors or arbitrary object reprs. On incomplete data HOLD.
        out['status'] = 'HOLD'
    finally:
        if reader is not None:
            out['GET_ATTEMPTS'] = reader.attempted
            out['GET_COMPLETED'] = reader.completed
    print(json.dumps(out, sort_keys=True, separators=(',', ':')))
    return 0 if out['status'] == 'PASS_METADATA_ONLY' else 1


if __name__ == '__main__':
    sys.exit(run())
