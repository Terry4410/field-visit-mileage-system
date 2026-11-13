"""Offline synthetic lazy token/Blob guard composition, no live services."""
import unittest
from unittest import mock
import pathb_select22_storage_claim_bridge_nondeploy as bridge
from pathb_select22_storage_once_nondeploy import prepare


class LazyBridgeTests(unittest.TestCase):
    def setUp(self):
        self.events = []
        self.claim = prepare(stage_sha256="a"*64, instance_sha256="b"*64)
        self.secret = "synthetic-test-token"
        self.saved_body = None

    def provider(self):
        self.events.append("storage_token")
        return self.secret

    def transport(self, method, url, headers, body, timeout):
        self.events.append(method)
        if method == "PUT":
            self.saved_body = body
            return 201, {"ETag": '"exact"'}, b""
        return 200, {"ETag": '"exact"'}, self.saved_body

    def guard(self, transport=None):
        return bridge.make_storage_once_guard(
            self.provider, lambda: 0, transport=transport or self.transport)

    def test_constructor_does_not_get_token(self):
        self.guard()
        self.assertEqual(self.events, [])

    def test_one_token_after_put_begins_and_before_get(self):
        with mock.patch("pathb_select22_storage_https_nondeploy.validate_owner_token",
                        return_value=True):
            result = self.guard().consume(self.claim)
        self.assertEqual(result["claim"], "PASS")
        self.assertEqual(self.events, ["storage_token", "PUT", "GET"])

    def test_uncertain_put_terminal_no_retry_or_get(self):
        def fail(*args):
            self.events.append("PUT")
            raise TimeoutError("unknown")
        with mock.patch("pathb_select22_storage_https_nondeploy.validate_owner_token",
                        return_value=True):
            guard = self.guard(fail)
            with self.assertRaises(Exception):
                guard.consume(self.claim)
            with self.assertRaises(Exception):
                guard.consume(self.claim)
        self.assertEqual(self.events, ["storage_token", "PUT"])

    def test_token_provider_failure_prevents_http(self):
        def no_token():
            self.events.append("storage_token")
            raise ValueError("invalid")
        guard = bridge.make_storage_once_guard(no_token, lambda: 0,
                                               transport=self.transport)
        with self.assertRaises(Exception):
            guard.consume(self.claim)
        self.assertEqual(self.events, ["storage_token"])

    def test_wrong_put_status_blocks_get(self):
        def rejected(method, url, headers, body, timeout):
            self.events.append(method)
            return 412, {"ETag": '"existing"'}, b""
        with mock.patch("pathb_select22_storage_https_nondeploy.validate_owner_token",
                        return_value=True):
            with self.assertRaises(Exception):
                self.guard(rejected).consume(self.claim)
        self.assertEqual(self.events, ["storage_token", "PUT"])


if __name__ == "__main__":
    unittest.main()
