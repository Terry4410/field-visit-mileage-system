"""Synthetic NEGATIVE tests only: never contact ARM or GitHub.
All provider errors/identities are forbidden in printed evidence.
"""
import base64
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import arm6

TENANT = '11111111-1111-1111-1111-111111111111'
SUB = '22222222-2222-2222-2222-222222222222'
CLIENT = '33333333-3333-3333-3333-333333333333'
OID = '44444444-4444-4444-4444-444444444444'
ROLE = '55555555-5555-5555-5555-555555555555'
ROOT = '/subscriptions/' + SUB + '/resourceGroups/rg-fieldvisit-uat/providers/Microsoft.Web/sites/api-fieldvisit-uat'
ROLE_ID = '/subscriptions/' + SUB + '/providers/Microsoft.Authorization/roleDefinitions/' + ROLE


def token(changes=None):
    claims = {'aud': 'https://management.azure.com/', 'tid': TENANT, 'appid': CLIENT, 'oid': OID, 'idtyp': 'app'}
    claims.update(changes or {})
    payload = base64.urlsafe_b64encode(json.dumps(claims).encode()).decode().rstrip('=')
    return 'aaa.' + payload + '.signature'


def env():
    return dict(GITHUB_ACTIONS='true', GITHUB_REPOSITORY='Terry4410/field-visit-mileage-system',
                GITHUB_REF='refs/heads/post-uat/v1.8.0', GITHUB_EVENT_NAME='workflow_dispatch',
                GITHUB_ACTOR='Terry4410', GITHUB_TRIGGERING_ACTOR='Terry4410',
                GITHUB_RUN_ATTEMPT='1', GITHUB_SHA='f' * 40, APPROVED_SHA='f' * 40,
                RUNNER_DEBUG='0', G1_MODE='SCM_ARM6_READONLY', G1_ARM6_GUARD='PASS',
                ACCEPT_COST='false', AZURE_TENANT_ID=TENANT, AZURE_SUBSCRIPTION_ID=SUB,
                AZURE_CLIENT_ID=CLIENT)


def responses():
    return [
        {'id': ROOT, 'name': 'api-fieldvisit-uat', 'properties': {'reserved': True, 'state': 'Running', 'publicNetworkAccess': 'Enabled'}},
        {'id': ROOT + '/config/web', 'name': 'web', 'type': 'Microsoft.Web/sites/config',
         'properties': {'scmIpSecurityRestrictionsUseMain': False,
                        'scmIpSecurityRestrictionsDefaultAction': 'Deny',
                        'scmIpSecurityRestrictions': [{'action': 'Allow', 'ipAddress': 'SENSITIVE_IP'}], 'scmMinTlsVersion': '1.2'}},
        {'value': [{'properties': {'principalId': OID, 'principalType': 'ServicePrincipal',
                                  'roleDefinitionId': ROLE_ID, 'condition': None, 'conditionVersion': None}}]},
        {'value': [{'id': ROLE_ID, 'properties': {'permissions': [{'actions': ['Microsoft.Web/sites/publish/Action'], 'notActions': []}]}}]},
        {'value': []},
        {'value': [{'actions': ['Microsoft.Web/sites/publish/Action'], 'notActions': []}]},
    ]


class Response:
    def __init__(self, uri, value, status=200, redirect=None, url=None, raw=None):
        self.url, self.value, self.status = uri if url is None else url, value, status
        self.raw = raw
        self.headers = {'Location': redirect} if redirect is not None else {}

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def geturl(self):
        return self.url

    def read(self, *_):
        return json.dumps(self.value).encode() if self.raw is None else self.raw


class Transport:
    def __init__(self, values=None, fail_index=None, failure=None, response_options=None):
        self.values = responses() if values is None else values
        self.fail_index = fail_index
        self.failure = failure
        self.response_options = response_options or {}
        self.calls = []

    def open(self, request, timeout):
        i = len(self.calls)
        self.calls.append((request.get_method(), request.full_url, timeout))
        if self.fail_index == i:
            raise self.failure if self.failure is not None else OSError('SECRET_PROVIDER_ERROR SECRET_TOKEN')
        return Response(request.full_url, self.values[i], **self.response_options.get(i, {}))


class Arm6Tests(unittest.TestCase):
    def setUp(self):
        self.env = env()
        self.pinned = mock.patch.object(arm6, 'CONTEXT_SHA256', hashlib.sha256((TENANT + '\n' + SUB + '\n' + CLIENT).encode()).hexdigest())
        self.pinned.start()
        self.argv_patch = mock.patch.object(sys, 'argv', ['arm6.py'])
        self.argv_patch.start()

    def tearDown(self):
        self.argv_patch.stop()
        self.pinned.stop()

    def execute(self, transport=None, given_token=None, e=None):
        transport = transport or Transport()
        sink = io.StringIO()
        with contextlib.redirect_stdout(sink):
            rc = arm6.run(opener=transport, token_factory=lambda scope: given_token or token(), env=e or self.env)
        body = sink.getvalue()
        for secret in ('SENSITIVE_IP', 'SECRET_PROVIDER_ERROR', 'SECRET_TOKEN', 'SECRET_RESPONSE', TENANT, SUB, CLIENT, OID, ROLE, 'Bearer', 'management.azure.com'):
            self.assertNotIn(secret, body)
        return rc, json.loads(body), transport

    def assert_stopped(self, result, transport, step):
        self.assertEqual(result['status'], 'HOLD')
        self.assertEqual((result['GET_ATTEMPTS'], result['GET_COMPLETED']), (step + 1, step))
        self.assertEqual(len(transport.calls), step + 1)
        self.assertEqual([result['steps'][k] for k in arm6.STEPS[:step]], ['PASS'] * step)
        self.assertEqual(result['steps'][arm6.STEPS[step]], 'ATTEMPTED')
        self.assertEqual([result['steps'][k] for k in arm6.STEPS[step+1:]], ['SKIPPED'] * (5 - step))

    def test_site_missing_public_network_access_fails_closed(self):
        values = responses()
        del values[0]['properties']['publicNetworkAccess']
        rc, out, tr = self.execute(Transport(values=values))
        self.assertEqual((rc, out['target']), (1, 'UNKNOWN'))
        self.assert_stopped(out, tr, 0)

    def test_site_unexpected_public_network_access_fails_closed(self):
        for invalid in (None, '', 'SECRET_RESPONSE', 'enabled', True, 1, [], {}):
            with self.subTest():
                values = responses()
                values[0]['properties']['publicNetworkAccess'] = invalid
                rc, out, tr = self.execute(Transport(values=values))
                self.assertEqual((rc, out['target']), (1, 'UNKNOWN'))
                self.assert_stopped(out, tr, 0)

    def test_site_disabled_is_metadata_only_not_scm_reachability(self):
        values = responses()
        values[0]['properties']['publicNetworkAccess'] = 'Disabled'
        rc, out, tr = self.execute(Transport(values=values))
        self.assertEqual((rc, out['target'], out['scmNetworkFeasibility']), (0, 'CONFIRMED', 'UNKNOWN'))

    def test_site_config_missing_or_foreign_binding_fails_closed(self):
        for field in ('id', 'name', 'type'):
            for missing in (True, False):
                with self.subTest(field=field, missing=missing):
                    values = responses()
                    if missing:
                        del values[1][field]
                    else:
                        values[1][field] = 'SECRET_RESPONSE'
                    rc, out, tr = self.execute(Transport(values=values))
                    self.assertEqual((rc, out['scmUseMain'], out['scmDefault']), (1, 'UNKNOWN', 'UNKNOWN'))
                    self.assert_stopped(out, tr, 1)

    def test_missing_response_shape_stops_at_each_fixed_get(self):
        for step in range(6):
            for missing in (None, {}, [], {'properties': None, 'SECRET_RESPONSE': 'SECRET_TOKEN'}):
                with self.subTest(step=step):
                    values = responses()
                    values[step] = missing
                    rc, out, tr = self.execute(Transport(values=values))
                    self.assertEqual(rc, 1)
                    self.assert_stopped(out, tr, step)

    def test_each_role_collection_pagination_stops_without_followup(self):
        for step in range(2, 6):
            for field in ('nextLink', 'nextlink'):
                with self.subTest(step=step, field=field):
                    values = responses()
                    values[step][field] = 'SECRET_RESPONSE'
                    rc, out, tr = self.execute(Transport(values=values))
                    self.assertEqual(rc, 1)
                    self.assert_stopped(out, tr, step)

    def test_http_status_stops_without_retry_or_next_get(self):
        for step in (0, 1):
            for status in (301, 302, 307, 308, 401, 403, 500):
                with self.subTest(step=step, status=status):
                    rc, out, tr = self.execute(Transport(response_options={step: {'status': status}}))
                    self.assertEqual(rc, 1)
                    self.assert_stopped(out, tr, step)

    def test_redirect_location_or_response_url_mismatch_stops(self):
        for step in (0, 1):
            for options in ({'redirect': 'SECRET_RESPONSE'}, {'url': 'SECRET_RESPONSE'}):
                with self.subTest(step=step):
                    rc, out, tr = self.execute(Transport(response_options={step: options}))
                    self.assertEqual(rc, 1)
                    self.assert_stopped(out, tr, step)

    def test_production_redirect_handler_refuses_followup(self):
        for status in (301, 302, 303, 307, 308):
            with self.subTest(status=status), self.assertRaises(arm6.Stop):
                arm6.NoRedirect().redirect_request(None, None, status, 'SECRET_RESPONSE', {}, 'SECRET_RESPONSE')

    def test_invalid_or_oversize_json_stops_without_raw_output(self):
        for raw in (b'SECRET_RESPONSE SECRET_TOKEN', b'SECRET_RESPONSE' * 90000):
            rc, out, tr = self.execute(Transport(response_options={0: {'raw': raw}}))
            self.assertEqual((rc, out['target']), (1, 'UNKNOWN'))
            self.assert_stopped(out, tr, 0)

    def test_happy_path_gets_six_exact_once_sanitized(self):
        rc, result, t = self.execute()
        self.assertEqual(rc, 0)
        self.assertEqual(result['status'], 'PASS_METADATA_ONLY')
        self.assertEqual((result['GET_ATTEMPTS'], result['GET_COMPLETED']), (6, 6))
        self.assertEqual(result['oidcPublishAction'], 'YES')
        self.assertEqual(result['scmDefault'], 'DENY')
        self.assertEqual(result['scmNetworkFeasibility'], 'UNKNOWN')
        self.assertEqual([x[1] for x in t.calls], ['https://management.azure.com' + p for p in arm6.paths(SUB)])
        self.assertTrue(all(x[0] == 'GET' for x in t.calls))
        self.assertEqual(list(result['steps'].values()), ['PASS'] * 6)

    def test_pre_gate_denials_consume_no_requests(self):
        for key, wrong in [('GITHUB_REF', 'refs/heads/main'), ('GITHUB_RUN_ATTEMPT', '2'),
                           ('GITHUB_ACTOR', 'intruder'), ('GITHUB_TRIGGERING_ACTOR', 'intruder'),
                           ('GITHUB_SHA', 'f' * 39), ('APPROVED_SHA', '0' * 40),
                           ('ACCEPT_COST', 'true'), ('G1_MODE', 'D0_VERSION_ONLY'),
                           ('G1_ARM6_GUARD', 'not-pass'), ('RUNNER_DEBUG', '1'),
                           ('AZURE_CLIENT_ID', 'incorrect')]:
            with self.subTest(key=key):
                e = self.env.copy()
                e[key] = wrong
                rc, result, t = self.execute(e=e)
                self.assertEqual((rc, result['GET_ATTEMPTS'], len(t.calls)), (1, 0, 0))

    def test_token_bound_to_pinned_enterprise_app(self):
        for mismatch in ({'appid': '55555555-5555-5555-5555-555555555555'}, {'oid': ''},
                         {'aud': 'https://appservice.azure.com/'}, {'tid': 'not-tenant'}, {'idtyp': 'user'}):
            with self.subTest(mismatch=mismatch):
                rc, out, tr = self.execute(given_token=token(mismatch))
                self.assertEqual((rc, out['GET_ATTEMPTS'], len(tr.calls)), (1, 0, 0))

    def test_bad_token_no_jwt_claim_leak(self):
        rc, out, tr = self.execute(given_token='malformed-oauth-token')
        self.assertEqual((rc, out['GET_ATTEMPTS'], len(tr.calls)), (1, 0, 0))

    def test_failure_consumes_exactly_one_attempt(self):
        for step in range(6):
            with self.subTest(step=step):
                rc, out, tr = self.execute(Transport(fail_index=step))
                self.assertEqual(rc, 1)
                self.assertEqual((out['GET_ATTEMPTS'], out['GET_COMPLETED']), (step + 1, step))
                self.assertEqual(len(tr.calls), step + 1)
                self.assertEqual(out['steps'][arm6.STEPS[step]], 'ATTEMPTED')
                self.assertTrue(all(x == 'SKIPPED' for x in [out['steps'][k] for k in arm6.STEPS[step+1:]]))

    def test_pagination_and_untrusted_json_stop_without_followup(self):
        for problem in ({'value': [], 'nextLink': 'https://attacker'},
                        {'value': [], 'nextlink': 'secret'}, {'wrong': True},
                        {'value': [123]}, {'value': [None]}):
            values = responses()
            values[2] = problem
            rc, out, tr = self.execute(Transport(values=values))
            self.assertEqual((rc, out['GET_ATTEMPTS'], out['GET_COMPLETED']), (1, 3, 2))
            self.assertEqual(len(tr.calls), 3)

    def test_site_target_or_state_mismatch_stops_after_first_get(self):
        for newfield, value in [('id', 'wrong'), ('name', 'wrong')]:
            values = responses()
            values[0][newfield] = value
            rc, out, tr = self.execute(Transport(values=values))
            self.assertEqual((rc, len(tr.calls)), (1, 1))

    def test_scm_policy_no_ip_data_escape_and_missing_field_fails(self):
        values = responses()
        values[1]['properties']['scmIpSecurityRestrictionsUseMain'] = 'false'
        rc, out, tr = self.execute(Transport(values=values))
        self.assertEqual((rc, len(tr.calls)), (1, 2))

    def test_permission_notactions_wildcard_and_denies(self):
        values = responses()
        values[5]['value'][0]['notActions'] = ['Microsoft.Web/sites/publish/*']
        rc, out, tr = self.execute(Transport(values=values))
        self.assertEqual((rc, out['oidcPublishAction']), (0, 'NO'))
        values = responses()
        values[4]['value'] = [{'properties': {'permissions': [{'actions': ['*'], 'notActions': []}]}}]
        rc, out, tr = self.execute(Transport(values=values))
        self.assertEqual((rc, out['oidcPublishAction'], out['denyOrConditionCoverage']), (1, 'UNKNOWN', 'INCOMPLETE'))

    def test_uncertain_group_condition_or_missing_definition_never_yes(self):
        variants = []
        a = responses(); a[2]['value'].append({'properties': {'principalId': 'other', 'principalType': 'Group'}}); variants.append(a)
        b = responses(); b[2]['value'][0]['properties']['condition'] = 'unsafe'; variants.append(b)
        c = responses(); c[3]['value'] = []; variants.append(c)
        for values in variants:
            rc, out, tr = self.execute(Transport(values=values))
            self.assertEqual((rc, out['oidcPublishAction']), (1, 'UNKNOWN'))

    def test_fails_closed_on_second_read_and_not_retry(self):
        vals = responses()
        vals[1]['properties']['scmIpSecurityRestrictionsDefaultAction'] = 'anything'
        rc, out, tr = self.execute(Transport(values=vals))
        self.assertEqual((rc, len(tr.calls), out['GET_ATTEMPTS']), (1, 2, 2))

    def test_url_paths_are_only_arm_and_fixed(self):
        pp = arm6.paths(SUB)
        self.assertEqual(len(set(pp)), 6)
        self.assertTrue(all(x.startswith('/subscriptions/' + SUB + '/resourceGroups/rg-fieldvisit-uat/providers/Microsoft.Web/sites/api-fieldvisit-uat') for x in pp))
        self.assertTrue(all('scm.' not in x and '/exec/' not in x and '/sql/' not in x for x in pp))
        self.assertTrue(all(x.count('api-version=') == 1 for x in pp))

    def test_sequence_cannot_be_replayed_or_out_of_order(self):
        t = Transport()
        r = arm6.SixGets(token(), arm6.paths(SUB), opener=t)
        with self.assertRaises(arm6.Stop):
            r.get(1)
        r.get(0)
        with self.assertRaises(arm6.Stop):
            r.get(0)
        self.assertEqual(len(t.calls), 1)


if __name__ == '__main__':
    unittest.main()
