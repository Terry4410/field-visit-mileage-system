"""NONDEPLOY: Claim result ambiguity must never imply an unused one-shot."""
import ast
import inspect
import unittest
from unittest.mock import patch

import p3_blob_guard as guard
import p3_app_installer_runner as runner


class ClaimUnknownTests(unittest.TestCase):
    def _guard(self, request):
        with patch.object(guard, 'validate_owner_storage_token', return_value=True):
            obj = guard.OneTimeInstallBlobGuard('FAKE_NO_TOKEN', request=request)
        obj.predecessors_verified = True
        obj.new_key_available = True
        gates = {name: 'PASS' for name in guard.REQUIRED_PRE_DDL_GATES}
        claim_sha = obj.prepare(gates, operation_id='a'*32,
                                artifact_sha256='b'*64, instance_sha256='c'*64)
        return obj, gates, claim_sha

    def _scenario(self, *, lose_put=False, lose_readback=False):
        calls = []
        def request(method, name, headers, body=b''):
            calls.append(method)
            if method == 'PUT':
                if lose_put:
                    raise OSError('synthetic lost PUT response')
                return guard.Response(201, '"etag1"')
            if method == 'GET':
                if lose_readback:
                    raise OSError('synthetic lost GET response')
                return guard.Response(200, '"etag1"', body=created[0])
            raise AssertionError('unexpected request')
        created = [b'']
        obj, gates, expected = self._guard(request)
        created[0] = obj._prepared
        marker = 'NOT_CONSUMED'
        marker = 'UNKNOWN'  # must happen before any possibly irreversible PUT
        try:
            self.assertEqual(obj.consume(gates), expected)
            marker = 'CONSUMED'
        except guard.InstallGuardHold:
            pass
        self.assertEqual(calls.count('PUT'), 1)
        self.assertTrue(obj.creation_attempted)
        self.assertEqual(marker, 'UNKNOWN' if lose_put or lose_readback else 'CONSUMED')
        self.assertEqual(obj.consumed, not lose_put)
        return calls

    def test_put_response_lost_no_retry(self):
        self.assertEqual(self._scenario(lose_put=True), ['PUT'])

    def test_readback_lost_no_retry(self):
        self.assertEqual(self._scenario(lose_readback=True), ['PUT', 'GET'])

    def test_put_and_readback_success(self):
        self.assertEqual(self._scenario(), ['PUT', 'GET'])

    def test_runner_assigns_unknown_before_consume_and_consumed_after(self):
        # Static ordering check of the actual runner, not merely the mock scenario.
        source = inspect.getsource(runner.run)
        before = source.index("result['marker']='UNKNOWN'")
        call = source.index('guard.consume(gates)')
        after = source.index("result['marker']='CONSUMED'")
        self.assertLess(before, call)
        self.assertLess(call, after)
        self.assertEqual(source.count('guard.consume(gates)'), 1)
        self.assertNotIn("result['marker']='NOT_CONSUMED'", source[source.index("result['marker']='UNKNOWN'"):])


if __name__ == '__main__':
    unittest.main()
