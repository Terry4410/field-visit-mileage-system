"""Synthetic HTTPS transport tests: NEVER issue real network requests."""
import unittest
from unittest import mock
import pathb_select22_storage_https_nondeploy as https


class HttpsContractTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.token = "synthetic-only-not-a-real-token"
        self.put = {"If-None-Match": "*", "x-ms-blob-type": "BlockBlob",
                    "Content-Type": "application/json", "Content-Length": "2"}
        self.get = {"If-Match": '"etag"'}
        self.clock = lambda: 0

    def _transport(self, method, url, headers, body, timeout):
        self.calls.append((method, url, headers, body, timeout))
        return 201 if method == "PUT" else 200, {"ETag": '"etag"'}, b"{}"

    def _call(self, method="PUT", headers=None, body=b"{}", transport=None,
              key=https.KEY):
        with mock.patch.object(https, "validate_owner_token", return_value=True):
            return https.exact_https_response(
                method, https.ACCOUNT, https.CONTAINER, key,
                self.put if headers is None else headers, body,
                token=self.token, clock=self.clock,
                transport=self._transport if transport is None else transport)

    def test_fixed_https_put_headers_and_endpoint(self):
        response = self._call()
        self.assertEqual(response.status, 201)
        self.assertEqual(len(self.calls), 1)
        method, url, headers, body, timeout = self.calls[0]
        self.assertEqual(method, "PUT")
        self.assertEqual(url, https.URL)
        self.assertEqual(headers["If-None-Match"], "*")
        self.assertEqual(timeout, 20)

    def test_other_key_rejected_before_network(self):
        with self.assertRaises(https.StorageTransportHold):
            self._call(key="another")
        self.assertEqual(self.calls, [])

    def test_nonconditional_put_rejected_before_network(self):
        invalid = dict(self.put, **{"If-None-Match": "ignored"})
        with self.assertRaises(https.StorageTransportHold):
            self._call(headers=invalid)
        self.assertEqual(self.calls, [])

    def test_get_requires_etag(self):
        with self.assertRaises(https.StorageTransportHold):
            self._call(method="GET", headers={}, body=b"")
        self.assertEqual(self.calls, [])

    def test_bad_status_fails_no_retry(self):
        def wrong(*args):
            self.calls.append("attempt")
            return 500, {"ETag": '"etag"'}, b""
        with self.assertRaises(https.StorageTransportHold):
            self._call(transport=wrong)
        self.assertEqual(self.calls, ["attempt"])

    def test_token_validation_before_transport(self):
        with self.assertRaises(https.StorageTransportHold):
            https.exact_https_response(
                "PUT", https.ACCOUNT, https.CONTAINER, https.KEY,
                self.put, b"{}", token=self.token, clock=self.clock,
                transport=self._transport)
        self.assertEqual(self.calls, [])


if __name__ == "__main__":
    unittest.main()
