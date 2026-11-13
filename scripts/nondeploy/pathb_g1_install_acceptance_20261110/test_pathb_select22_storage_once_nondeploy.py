"""Offline SELECT22 conditional Blob claim: no Azure/Token/SQL/network calls."""
import json
from pathlib import Path
import unittest

import pathb_select22_storage_once_nondeploy as claimlib

STAGE = "a" * 64
INSTANCE = "b" * 64


class FakeStore:
    def __init__(self):
        self.requests = []
        self.payload = None
        self.etag = '"fixed-test-etag"'

    def call(self, method, account, container, key, headers, body):
        self.requests.append((method, account, container, key, headers, body))
        if (account, container, key) != (claimlib.ACCOUNT, claimlib.CONTAINER, claimlib.KEY):
            raise AssertionError("target drift")
        if method == "PUT":
            if headers.get("If-None-Match") != "*":
                raise AssertionError("condition absent")
            if headers.get("x-ms-blob-type") != "BlockBlob":
                raise AssertionError("blob type absent")
            if self.payload is not None:
                return claimlib.Response(412)
            self.payload = body
            return claimlib.Response(201, self.etag)
        if method == "GET":
            if headers != {"If-Match": self.etag} or body != b"":
                raise AssertionError("unbound read")
            return claimlib.Response(200, self.etag, self.payload)
        raise AssertionError("unexpected operation")


class ClaimTests(unittest.TestCase):
    def setUp(self):
        self.spec = claimlib.prepare(stage_sha256=STAGE, instance_sha256=INSTANCE)

    def test_fixed_exact_new_namespace_no_legacy_marker(self):
        self.assertTrue(claimlib.KEY.startswith("operator-once/pathb-select22-"))
        self.assertNotIn("install", claimlib.KEY)
        self.assertNotIn("manual-v2", claimlib.KEY)
        self.assertNotIn("review-v3", claimlib.KEY)
        obj = json.loads(self.spec.payload)
        self.assertEqual(obj["status"], "CLAIMED_BEFORE_SQL_TOKEN_OR_SQL")
        self.assertEqual(obj["query_sha256"], claimlib.QUERY_SHA)
        self.assertEqual(obj["stage_sha256"], STAGE)
        self.assertEqual(obj["instance_sha256"], INSTANCE)
        self.assertNotIn("token", obj)
        self.assertEqual(self.spec.key, claimlib.KEY)
        self.assertEqual(len(self.spec.sha256), 64)

    def test_exact_one_put_then_one_readback_and_nonsecret_receipt(self):
        remote = FakeStore()
        g = claimlib.OnceGuard(remote.call)
        result = g.consume(self.spec)
        self.assertEqual([x[0] for x in remote.requests], ["PUT", "GET"])
        self.assertEqual(result["claim"], "PASS")
        self.assertEqual(result["readback"], "PASS")
        self.assertEqual(result["sql_token_request"], "NO")
        self.assertEqual(result["sql_calls"], 0)
        self.assertTrue(g._attempted)

    def test_second_call_same_guard_terminal(self):
        remote = FakeStore()
        g = claimlib.OnceGuard(remote.call)
        g.consume(self.spec)
        with self.assertRaises(claimlib.StorageOnceHold):
            g.consume(self.spec)
        self.assertEqual([x[0] for x in remote.requests], ["PUT", "GET"])

    def test_second_process_same_blob_rejected_even_if_new_guard(self):
        remote = FakeStore()
        claimlib.OnceGuard(remote.call).consume(self.spec)
        g2 = claimlib.OnceGuard(remote.call)
        with self.assertRaisesRegex(claimlib.StorageOnceHold, "REPLAY_BLOCKED"):
            g2.consume(self.spec)
        self.assertEqual([x[0] for x in remote.requests], ["PUT", "GET", "PUT"])

    def test_put_timeout_must_not_retry_or_get(self):
        calls = []
        def timeout(*args):
            calls.append(args)
            raise TimeoutError("possibly already created, secret details")
        g = claimlib.OnceGuard(timeout)
        with self.assertRaisesRegex(claimlib.StorageOnceHold, "^SELECT22_STORAGE_CLAIM_OUTCOME_UNKNOWN$"):
            g.consume(self.spec)
        self.assertEqual(len(calls), 1)
        with self.assertRaises(claimlib.StorageOnceHold):
            g.consume(self.spec)
        self.assertEqual(len(calls), 1)

    def test_invalid_put_response_never_attempts_get(self):
        for status in (200, 202, 403, 404, 409, 412, 500):
            with self.subTest(status=status):
                calls = []
                def fake(method, *args):
                    calls.append(method)
                    return claimlib.Response(status, '"etag"')
                with self.assertRaises(claimlib.StorageOnceHold):
                    claimlib.OnceGuard(fake).consume(self.spec)
                self.assertEqual(calls, ["PUT"])

    def test_put_201_but_missing_etag_terminal(self):
        calls = []
        def fake(method, *args):
            calls.append(method)
            return claimlib.Response(201, "")
        with self.assertRaises(claimlib.StorageOnceHold):
            claimlib.OnceGuard(fake).consume(self.spec)
        self.assertEqual(calls, ["PUT"])

    def test_readback_timeout_is_terminal_unknown_not_permission_to_retry(self):
        remote = FakeStore()
        calls = []
        def broken(method, *args):
            calls.append(method)
            if method == "GET":
                raise TimeoutError("storage read failed")
            return remote.call(method, *args)
        g = claimlib.OnceGuard(broken)
        with self.assertRaisesRegex(claimlib.StorageOnceHold, "^SELECT22_STORAGE_READBACK_UNKNOWN$"):
            g.consume(self.spec)
        self.assertEqual(calls, ["PUT", "GET"])
        self.assertIsNotNone(remote.payload)
        with self.assertRaises(claimlib.StorageOnceHold):
            g.consume(self.spec)
        self.assertEqual(calls, ["PUT", "GET"])

    def test_wrong_readback_etag_or_content_terminal(self):
        for kind in ("etag", "body", "status"):
            with self.subTest(kind=kind):
                remote = FakeStore()
                def fake(method, *args):
                    obj = remote.call(method, *args)
                    if method == "GET":
                        if kind == "etag":
                            return claimlib.Response(200, '"wrong"', obj.body)
                        if kind == "body":
                            return claimlib.Response(200, obj.etag, b"incorrect")
                        return claimlib.Response(206, obj.etag, obj.body)
                    return obj
                with self.assertRaises(claimlib.StorageOnceHold):
                    claimlib.OnceGuard(fake).consume(self.spec)
                self.assertEqual(len(remote.requests), 2)

    def test_invalid_sha_rejected_before_request(self):
        for invalid in ("", "X" * 64, "1" * 63, "../random"):
            with self.subTest(invalid=invalid):
                with self.assertRaises(claimlib.StorageOnceHold):
                    claimlib.prepare(stage_sha256=invalid, instance_sha256=INSTANCE)

    def test_bad_claim_integrity_rejected_before_network(self):
        remote = FakeStore()
        forged = claimlib.Claim(self.spec.key, self.spec.payload + b"X", self.spec.sha256)
        g = claimlib.OnceGuard(remote.call)
        with self.assertRaises(claimlib.StorageOnceHold):
            g.consume(forged)
        self.assertEqual(remote.requests, [])

    def test_no_live_entrypoint_no_network_or_delete_code(self):
        source = Path(claimlib.__file__).read_text()
        self.assertNotIn("import http", source)
        self.assertNotIn("import requests", source)
        self.assertNotIn("import urllib", source)
        self.assertNotIn("subprocess", source)
        self.assertNotIn('"DELETE"', source)
        self.assertNotIn('"HEAD"', source)
        self.assertIn("NONDEPLOY_NO_STORAGE_OR_SQL_ENTRYPOINT", source)


if __name__ == "__main__":
    unittest.main()
