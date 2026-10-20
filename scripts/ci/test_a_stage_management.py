"""Synthetic transport only. Never authenticates, sleeps, or opens a socket."""
import base64
import copy
import json
import unittest
from unittest.mock import patch
import a_stage_management as a

SUB = '11111111-1111-1111-1111-111111111111'
TENANT = '22222222-2222-2222-2222-222222222222'
CLIENT = '33333333-3333-3333-3333-333333333333'
TOKENS = {'management.azure.com': 'SYNTHETIC', 'api.github.com': 'SYNTHETIC'}

def response(data, status=200, headers=None):
    raw = json.dumps(data).encode()
    return status, headers or {'content-type': 'application/json', 'content-length': str(len(raw))}, raw

def fixture():
    p = a.endpoints(SUB)
    return {
        p['APP'][0]: response(dict(id=p['APP'][1], name='api-fieldvisit-uat', properties={})),
        p['DATABASE'][0]: response(dict(id=p['DATABASE'][1], name='db-fieldvisit-uat', properties={'status': 'Online'})),
        p['PITR'][0]: response(dict(id=p['PITR'][1], name='default', properties={'retentionDays': 7})),
        p['DEPLOYMENTS'][0]: response({'value': []}),
        p['PAGES'][0]: response({'html_url': 'https://terry4410.github.io/field-visit-mileage-system/'}),
        p['RC_ARTIFACT'][0]: response({'total_count': 1, 'artifacts': [{'id': a.ARTIFACT, 'digest': a.DIGEST,
            'expired': False, 'workflow_run': {'head_sha': a.RC}}]}),
    }

class Channel(unittest.TestCase):
    def run_fixture(self, data=None):
        values = data or fixture()
        calls = []
        def get(url, token, timeout):
            self.assertGreater(timeout, 0)
            self.assertLessEqual(timeout, 10)
            calls.append(url)
            return values[url]
        return a.collect(SUB, TOKENS, get), calls

    def test_positive_six_gets_still_hold(self):
        result, calls = self.run_fixture()
        self.assertEqual(len(calls), 6)
        self.assertEqual(result['G1'], 'HOLD')
        self.assertEqual(result['API_PRINCIPAL_BINDING'], 'UNKNOWN')
        self.assertEqual(result['RECOVERY_PAIR'], 'UNKNOWN')

    def test_wrong_subscription_resource(self):
        data = fixture();url, resource = a.endpoints(SUB)['APP']
        data[url] = response(dict(id=resource.replace(SUB, TENANT), name='api-fieldvisit-uat', properties={}))
        with self.assertRaises(a.Stop): self.run_fixture(data)

    def test_unauthorized_get_no_retry(self):
        calls = []
        def denied(*args): calls.append(1);return response({}, 403)
        with self.assertRaises(a.Stop): a.collect(SUB, TOKENS, denied)
        self.assertEqual(len(calls), 1)

    def test_redirect_budget(self):
        url = a.endpoints(SUB)['APP'][0];calls = []
        def redirect(*args): calls.append(1);return 302, {'location': url}, b''
        budget = a.Budget([url], redirect)
        with self.assertRaises(a.Stop): budget.get(url, TOKENS)
        self.assertEqual((budget.count, len(calls)), (20, 20))

    def test_redirect_unknown_origin(self):
        url = a.endpoints(SUB)['APP'][0]
        with self.assertRaises(a.Stop):
            a.Budget([url], lambda *args: (302, {'location': 'https://invalid.example/'}, b'')).get(url, TOKENS)

    def test_secret_bearing_response(self):
        for data in ({'password': 'SYNTHETIC'}, {'url': 'https://invalid.example/?sig=SYNTHETIC'}, {'connectionStrings': [{'value': 'SYNTHETIC'}]}):
            with self.subTest(), self.assertRaises(a.Stop): a.secret_free(data)

    def test_timeout_no_retry(self):
        calls = []
        def timeout(*args): calls.append(1);raise TimeoutError()
        with self.assertRaises(TimeoutError): a.collect(SUB, TOKENS, timeout)
        self.assertEqual(len(calls), 1)

    def test_transient_error_not_retried(self):
        calls = []
        def transient(*args): calls.append(1);return response({}, 503)
        with self.assertRaises(a.Stop): a.collect(SUB, TOKENS, transient)
        self.assertEqual(len(calls), 1)

    def test_total_deadline(self):
        now = [0];url = a.endpoints(SUB)['APP'][0]
        def late(*args): now[0] = 601;return response({})
        with self.assertRaises(a.Stop): a.Budget([url], late, lambda: now[0]).get(url, TOKENS)

    def test_truncated_or_paginated(self):
        url = a.endpoints(SUB)['APP'][0]
        for reply in (response({}, headers={'content-type': 'application/json', 'content-length': '100'}),
                      response({'nextLink': 'https://invalid.example/'}),
                      response({}, headers={'content-type': 'application/json', 'link': 'SYNTHETIC'}),
                      (200, {'content-type': 'application/json'}, b'{')):
            with self.subTest(), self.assertRaises((a.Stop, ValueError)):
                a.Budget([url], lambda *args: reply).get(url, TOKENS)

    def test_unknown_package(self):
        data = fixture();url = a.endpoints(SUB)['RC_ARTIFACT'][0]
        data[url] = response({'total_count': 1, 'artifacts': [{'id': 999}]})
        with self.assertRaises(a.Stop): self.run_fixture(data)

    def test_missing_authenticated_identity(self):
        with self.assertRaises(a.Stop): a.identity('', TENANT, CLIENT, 0)

    def test_wrong_identity_claims(self):
        claims = {'tid': TENANT, 'appid': CLIENT, 'aud': 'https://management.azure.com/', 'exp': 10000}
        def token(c): return 'e30.' + base64.urlsafe_b64encode(json.dumps(c).encode()).decode().rstrip('=') + '.synthetic'
        a.identity(token(claims), TENANT, CLIENT, 0)
        for key, value in [('tid', SUB), ('appid', SUB), ('aud', 'https://database.windows.net/'), ('exp', 1)]:
            wrong = dict(claims);wrong[key] = value
            with self.subTest(), self.assertRaises(a.Stop): a.identity(token(wrong), TENANT, CLIENT, 0)

    def test_unexpected_endpoint_never_sent(self):
        calls = []
        budget = a.Budget([a.endpoints(SUB)['APP'][0]], lambda *args: calls.append(1))
        with self.assertRaises(a.Stop): budget.get('https://management.azure.com/unapproved', TOKENS)
        self.assertEqual(calls, [])

    def test_paused_no_wake(self):
        data = fixture();url, resource = a.endpoints(SUB)['DATABASE']
        data[url] = response(dict(id=resource, name='db-fieldvisit-uat', properties={'status': 'Paused'}))
        result, calls = self.run_fixture(data)
        self.assertEqual(result['SQL_OPERATIONAL'], 'HOLD')
        self.assertEqual(len(calls), 6)

    def test_unauthorized_entry_never_sends(self):
        with patch.object(a, 'network_get', side_effect=AssertionError('NETWORK_FORBIDDEN')) as send:
            with self.assertRaises(a.Stop): a.main({})
            send.assert_not_called()

if __name__ == '__main__': unittest.main()
