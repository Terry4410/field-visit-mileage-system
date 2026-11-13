"""Direct TLS transport tests: injected fake connection, no network."""
import unittest
from email.message import Message
from pathb_select22_direct_https_nondeploy import (
    direct_https_once, DirectHttpsHold)
from pathb_select22_storage_https_nondeploy import URL, HOST


class FakeResponse:
    def __init__(self, status=201, body=b"", etags=('"tag"',)):
        self.status = status
        self.data = body
        self.headers = Message()
        for tag in etags:
            self.headers.add_header("ETag", tag)
    def read(self, limit):
        return self.data[:limit]


class FakeConnection:
    def __init__(self, status=201, body=b"", etags=('"tag"',)):
        self.response = FakeResponse(status, body, etags)
        self.calls = []
        self.closed = False
    def request(self, *args, **kwargs):
        self.calls.append(("request", args, kwargs))
    def getresponse(self):
        self.calls.append(("getresponse",))
        return self.response
    def close(self):
        self.closed = True


class DirectHttpsContract(unittest.TestCase):
    def setUp(self):
        self.headers = {"Authorization": "Bearer synthetic-only", "x-ms-version": "2023-11-03",
                        "If-None-Match": "*", "x-ms-blob-type": "BlockBlob",
                        "Content-Type": "application/json", "Content-Length": "2"}
        self.conn = FakeConnection()
        self.factories = []
    def factory(self, host, port, timeout):
        self.factories.append((host, port, timeout))
        return self.conn
    def call(self, **kwargs):
        d = dict(method="PUT", url=URL, headers=self.headers, body=b"{}",
                 timeout_seconds=20, connection_factory=self.factory)
        d.update(kwargs)
        return direct_https_once(**d)

    def test_exact_once_put_and_close(self):
        result = self.call()
        self.assertEqual(result[0:2], (201, {"ETag": '"tag"'}))
        self.assertEqual(self.factories, [(HOST, 443, 20)])
        self.assertEqual([x[0] for x in self.conn.calls],
                         ["request", "getresponse"])
        self.assertTrue(self.conn.closed)

    def test_get_etag_matching_path(self):
        self.conn = FakeConnection(200, b"{}")
        h = {"Authorization": "Bearer synthetic-only",
             "x-ms-version": "2023-11-03", "If-Match": '"tag"'}
        self.assertEqual(self.call(method="GET", headers=h, body=b"")[0], 200)

    def test_redirect_is_terminal_not_followed(self):
        self.conn = FakeConnection(307, b"")
        with self.assertRaises(DirectHttpsHold):
            self.call()
        self.assertEqual(len(self.factories), 1)
        self.assertEqual(len(self.conn.calls), 2)
        self.assertTrue(self.conn.closed)

    def test_wrong_host_or_url_fails_before_connection(self):
        for url in ("http://" + HOST + "/", "https://attacker.example/",
                    URL + "?snapshot=other"):
            with self.subTest(url=url):
                with self.assertRaises(DirectHttpsHold):
                    self.call(url=url)
        self.assertEqual(self.factories, [])

    def test_duplicate_etag_is_terminal(self):
        self.conn = FakeConnection(201, b"", ('"one"', '"two"'))
        with self.assertRaises(DirectHttpsHold):
            self.call()
        self.assertEqual(len(self.factories), 1)

    def test_oversize_response_is_terminal(self):
        self.conn = FakeConnection(200, b"x" * 1025)
        h = {"Authorization": "Bearer synthetic-only",
             "x-ms-version": "2023-11-03", "If-Match": '"tag"'}
        with self.assertRaises(DirectHttpsHold):
            self.call(method="GET", headers=h, body=b"")
        self.assertEqual(len(self.factories), 1)

    def test_injected_request_failure_no_retry(self):
        class Failing(FakeConnection):
            def request(self, *args, **kwargs):
                self.calls.append(("request",))
                raise OSError("never expose")
        self.conn = Failing()
        with self.assertRaisesRegex(DirectHttpsHold, "^SELECT22_DIRECT_HTTPS_HOLD$"):
            self.call()
        self.assertEqual(self.conn.calls, [("request",)])
        self.assertTrue(self.conn.closed)

    def test_proxy_and_redirect_headers_forbidden(self):
        h = dict(self.headers, **{"Proxy-Authorization": "test"})
        with self.assertRaises(DirectHttpsHold):
            self.call(headers=h)
        self.assertEqual(self.factories, [])


if __name__ == "__main__":
    unittest.main()
