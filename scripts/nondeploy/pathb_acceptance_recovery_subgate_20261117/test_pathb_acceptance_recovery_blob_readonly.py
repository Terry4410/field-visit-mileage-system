#!/usr/bin/env python3
"""Offline-only failure classification tests for the recovered wrapper."""
from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
import sys
import tempfile
import types
import unittest
from dataclasses import dataclass
from pathlib import Path
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
WRAPPER = HERE / "pathb_acceptance_recovery_blob_readonly.py"
ORIGINAL = HERE / "original_pathb_acceptance_recovery_blob_readonly.py"
ORIGINAL_SHA256 = "d7cbdbe3dc942170c7614ed43b7ed16f4cd5465939807e3a1001f648f6ad0347"


def load_wrapper():
    spec = importlib.util.spec_from_file_location("recovery_wrapper_under_test", WRAPPER)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


wrapper = load_wrapper()


def ident(value):
    return hashlib.sha256(str(value).lower().encode()).hexdigest()


@dataclass(frozen=True)
class Receipt:
    key: str
    payload: bytes
    digest: str


class FakeResponse:
    def __init__(self, status, body=b"", etag='"etag"', error_code=None):
        self.status = status
        self._body = body
        self._etag = etag
        self._error_code = error_code

    def read(self, _limit):
        return self._body

    def getheader(self, name, default=""):
        if name == "ETag":
            return self._etag
        if name == "x-ms-error-code":
            return self._error_code if self._error_code is not None else default
        return default


class RecoverySubgateTests(unittest.TestCase):
    operation_id = "ab" * 16
    owner_id = "owner-object-id"
    instance = {"name": "instance-1"}
    token = "SECRET_STORAGE_TOKEN_MUST_NOT_APPEAR"

    def setUp(self):
        self.claim = {
            "operation_id": self.operation_id,
            "instance_sha256": ident(self.instance),
            "artifact_sha256": wrapper.SOURCE_SHA,
        }
        self.claim_raw = json.dumps(self.claim, separators=(",", ":")).encode()
        self.claim_sha = hashlib.sha256(self.claim_raw).hexdigest()
        self.fingerprint = "cd" * 32
        self.http_plan = []
        self.http_calls = []
        self.token_requests = 0
        self.fail_at = None

    def fake_modules(self):
        ledger = types.ModuleType("g1_blob_call_journal_REVIEW_ONLY")
        ledger.PREFIX = "operator-once/pathb-g1-install-call-journal/"
        ledger.JournalHold = type("JournalHold", (Exception,), {})

        def canonical_postcheck(operation_id, claim_sha, fingerprint):
            raw = (json.dumps({
                "operation_id": operation_id,
                "claim_sha256": claim_sha,
                "schema_fingerprint_sha256": fingerprint,
            }, sort_keys=True, separators=(",", ":")) + "\n").encode()
            key = ledger.PREFIX + operation_id + "/postcheck.json"
            return Receipt(key, raw, hashlib.sha256(raw).hexdigest())

        ledger.construct_postcheck = canonical_postcheck
        ledger.construct = lambda *args, **kwargs: Receipt("never-used", b"", "00" * 32)
        ledger.recover_exact = lambda *_args, **_kwargs: "00" * 32

        recovery = types.ModuleType("g1_readonly_recovery_REVIEW_ONLY")

        def verify_original_claim(raw, operation_id, artifact_sha, instance_sha):
            if self.fail_at == "claim_content_verifier":
                raise RuntimeError("RAW_CLAIM_SECRET_MUST_NOT_APPEAR")
            if (raw != self.claim_raw or operation_id != self.operation_id
                    or artifact_sha != wrapper.SOURCE_SHA
                    or instance_sha != self.claim["instance_sha256"]):
                raise RuntimeError("claim mismatch")
            return self.claim_sha

        recovery.verify_original_claim = verify_original_claim

        gates = types.ModuleType("p3_install_gates")
        gates.PAYLOAD_SHA256 = ["11" * 32] * 16
        parent = types.ModuleType("p3_kudu_cloudshell_parent")
        parent.ident = ident
        transport = types.ModuleType("p3_kudu_transport")

        def select_single_instance(_items):
            if self.fail_at == "instance":
                raise RuntimeError("RAW_INSTANCE_ERROR_MUST_NOT_APPEAR")
            return self.instance

        transport.select_single_instance = select_single_instance
        contract = types.ModuleType("p3_ssh_transport_contract")
        contract.OWNER_SHA = ident(self.owner_id)

        def check_owner_account(_account):
            if self.fail_at == "owner_account":
                raise RuntimeError("RAW_OWNER_ERROR_MUST_NOT_APPEAR")

        def validate_owner_token(_token, _kind, _now):
            if self.fail_at == "token_validation":
                raise RuntimeError("RAW_TOKEN_ERROR_MUST_NOT_APPEAR")

        contract.check_owner_account = check_owner_account
        contract.validate_owner_token = validate_owner_token
        return {
            ledger.__name__: ledger,
            recovery.__name__: recovery,
            gates.__name__: gates,
            parent.__name__: parent,
            transport.__name__: transport,
            contract.__name__: contract,
        }

    def az_json(self, *args):
        if args[:2] == ("account", "show"):
            return {"tenantId": "redacted"}
        if args[:4] == ("ad", "signed-in-user", "show"):
            return {"id": self.owner_id}
        if args[:2] == ("webapp", "list-instances"):
            return [self.instance]
        if args[:2] == ("account", "get-access-token"):
            self.token_requests += 1
            if self.fail_at == "token_request":
                raise RuntimeError("RAW_TOKEN_REQUEST_ERROR_MUST_NOT_APPEAR")
            if self.fail_at == "token_shape":
                return {"accessToken": None}
            return {"accessToken": self.token}
        raise AssertionError("unexpected az invocation")

    def connection_factory(self, host, **kwargs):
        test = self

        class Connection:
            def request(self, method, path, headers):
                test.http_calls.append((method, host, path, dict(headers)))
                if not test.http_plan:
                    raise AssertionError("unexpected Blob request")
                outcome = test.http_plan.pop(0)
                if isinstance(outcome, Exception):
                    raise outcome
                self.outcome = outcome

            def getresponse(self):
                return self.outcome

            def close(self):
                pass

        return Connection()

    def run_wrapper(self, fail_at=None, http_plan=()):
        self.fail_at = fail_at
        self.http_plan = list(http_plan)

        class FakeZip:
            def __init__(self, _path):
                pass

            def __enter__(self):
                return self

            def __exit__(self, *_args):
                return False

            def read(self, name):
                if name != "binding-index.json":
                    raise AssertionError("unexpected archive member")
                return b"binding"

            def extractall(self, _root):
                pass

        def fake_digest(raw):
            if raw == b"source":
                return wrapper.SOURCE_SHA
            if raw == b"binding":
                return wrapper.BINDING_SHA
            return hashlib.sha256(raw).hexdigest()

        with tempfile.TemporaryDirectory() as raw_home:
            home = Path(raw_home)
            (home / wrapper.SOURCE_NAME).write_bytes(b"source")
            stdout = io.StringIO()
            with patch.dict(sys.modules, self.fake_modules(), clear=False), \
                    patch.object(wrapper.Path, "home", return_value=home), \
                    patch.object(wrapper, "ZipFile", FakeZip), \
                    patch.object(wrapper, "digest", side_effect=fake_digest), \
                    patch.object(wrapper, "az_json", side_effect=self.az_json), \
                    patch.object(wrapper.http.client, "HTTPSConnection",
                                 side_effect=self.connection_factory), \
                    contextlib.redirect_stdout(stdout):
                self.assertEqual(wrapper.main(), 0)
        output = stdout.getvalue()
        values = dict(line.split("=", 1) for line in output.splitlines())
        for name in (
            "OWNER_INSTANCE_BINDING", "STORAGE_TOKEN_REQUEST",
            "STORAGE_TOKEN_VALIDATION", "CLAIM_GET",
            "CLAIM_CONTENT_VALIDATION",
        ):
            self.assertIn(values[name], {"PASS", "HOLD", "NOT_RUN"})
        for forbidden in (
            self.token, "RAW_OWNER", "RAW_INSTANCE", "RAW_TOKEN",
            "RAW_CLAIM", "redacted",
        ):
            self.assertNotIn(forbidden, output)
        return values

    def test_recovered_source_sha256_is_exact(self):
        self.assertEqual(hashlib.sha256(ORIGINAL.read_bytes()).hexdigest(),
                         ORIGINAL_SHA256)

    def test_import_is_offline_and_does_not_invoke_azure_or_blob(self):
        self.assertEqual(self.token_requests, 0)
        self.assertEqual(self.http_calls, [])

    def test_owner_or_instance_failure_stops_before_token_and_blob(self):
        for failure in ("owner_account", "instance"):
            with self.subTest(failure=failure):
                self.setUp()
                values = self.run_wrapper(fail_at=failure)
                self.assertEqual(values["OWNER_INSTANCE_BINDING"], "HOLD")
                self.assertEqual(values["STORAGE_TOKEN_REQUEST"], "NOT_RUN")
                self.assertEqual(self.token_requests, 0)
                self.assertEqual(self.http_calls, [])

    def test_storage_token_request_failure_is_classified_and_terminal(self):
        values = self.run_wrapper(fail_at="token_request")
        self.assertEqual(values["OWNER_INSTANCE_BINDING"], "PASS")
        self.assertEqual(values["STORAGE_TOKEN_REQUEST"], "HOLD")
        self.assertEqual(values["STORAGE_TOKEN_VALIDATION"], "NOT_RUN")
        self.assertEqual(self.token_requests, 1)
        self.assertEqual(self.http_calls, [])

    def test_storage_token_validation_failures_are_classified_and_terminal(self):
        for failure in ("token_shape", "token_validation"):
            with self.subTest(failure=failure):
                self.setUp()
                values = self.run_wrapper(fail_at=failure)
                self.assertEqual(values["STORAGE_TOKEN_REQUEST"], "PASS")
                self.assertEqual(values["STORAGE_TOKEN_VALIDATION"], "HOLD")
                self.assertEqual(values["CLAIM_GET"], "NOT_RUN")
                self.assertEqual(self.token_requests, 1)
                self.assertEqual(self.http_calls, [])

    def test_claim_get_transport_and_404_are_distinct_safe_outcomes(self):
        values = self.run_wrapper(
            http_plan=[RuntimeError("RAW_BLOB_TRANSPORT_ERROR_MUST_NOT_APPEAR")]
        )
        self.assertEqual(values["CLAIM_GET"], "HOLD")
        self.assertEqual(values["CLAIM"], "UNKNOWN")
        self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "NOT_RUN")
        self.assertEqual(self.token_requests, 1)
        self.assertEqual(len(self.http_calls), 1)

        self.setUp()
        values = self.run_wrapper(http_plan=[FakeResponse(404, error_code="BlobNotFound")])
        self.assertEqual(values["CLAIM_GET"], "HOLD")
        self.assertEqual(values["CLAIM"], "ABSENT")
        self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "NOT_RUN")
        self.assertEqual(self.token_requests, 1)
        self.assertEqual(len(self.http_calls), 1)

    def test_generic_404_does_not_claim_blob_absence(self):
        for error_code in (None, "ContainerNotFound", "AuthorizationFailure"):
            with self.subTest(error_code=error_code):
                self.setUp()
                values = self.run_wrapper(
                    http_plan=[FakeResponse(404, error_code=error_code)]
                )
                self.assertEqual(values["CLAIM_GET"], "HOLD")
                self.assertEqual(values["CLAIM"], "UNKNOWN")
                self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "NOT_RUN")
                self.assertEqual(self.token_requests, 1)
                self.assertEqual(len(self.http_calls), 1)

    def test_live_app_recycle_keeps_valid_historical_claim(self):
        prior_instance_sha = self.claim["instance_sha256"]
        self.instance = {"name": "recycled-instance-2"}
        self.assertNotEqual(ident(self.instance), prior_instance_sha)
        ledger = self.fake_modules()["g1_blob_call_journal_REVIEW_ONLY"]
        postcheck = ledger.construct_postcheck(
            self.operation_id, self.claim_sha, self.fingerprint
        ).payload
        values = self.run_wrapper(http_plan=[
            FakeResponse(200, self.claim_raw), FakeResponse(200, postcheck),
        ])
        self.assertEqual(values["OWNER_INSTANCE_BINDING"], "PASS")
        self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "PASS")
        self.assertEqual(values["ACCESS"], "PASS")
        self.assertEqual(values["DURABLE_RECEIPT"], "PASS")
        self.assertEqual(self.token_requests, 1)
        self.assertEqual(len(self.http_calls), 2)

    def test_malformed_historical_instance_digest_stops_before_second_get(self):
        raw = json.dumps(dict(self.claim, instance_sha256="not-a-sha"),
                         separators=(",", ":")).encode()
        values = self.run_wrapper(http_plan=[FakeResponse(200, raw)])
        self.assertEqual(values["CLAIM_GET"], "PASS")
        self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "HOLD")
        self.assertEqual(values["ACCESS"], "HOLD")
        self.assertEqual(self.token_requests, 1)
        self.assertEqual(len(self.http_calls), 1)

    def test_generic_postcheck_404_is_unknown_not_absent(self):
        for error_code in (None, "ContainerNotFound"):
            with self.subTest(error_code=error_code):
                self.setUp()
                values = self.run_wrapper(http_plan=[
                    FakeResponse(200, self.claim_raw),
                    FakeResponse(404, error_code=error_code),
                ])
                self.assertEqual(values["CLAIM"], "PASS")
                self.assertEqual(values["ACCESS"], "PASS")
                self.assertEqual(values["DURABLE_RECEIPT"], "UNKNOWN")
                self.assertEqual(values["EXTERNAL_LEDGER"], "UNKNOWN")
                self.assertEqual(self.token_requests, 1)
                self.assertEqual(len(self.http_calls), 2)

    def test_claim_content_failure_stops_after_single_exact_claim_get(self):
        bad_claim = json.dumps({"operation_id": self.operation_id}).encode()
        values = self.run_wrapper(http_plan=[FakeResponse(200, bad_claim)])
        self.assertEqual(values["CLAIM_GET"], "PASS")
        self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "HOLD")
        self.assertEqual(len(self.http_calls), 1)
        method, _host, path, _headers = self.http_calls[0]
        self.assertEqual(method, "GET")
        self.assertEqual(path, "/" + wrapper.CONTAINER + "/" + wrapper.CLAIM_KEY)

    def test_verifier_failure_is_redacted_and_terminal(self):
        values = self.run_wrapper(
            fail_at="claim_content_verifier",
            http_plan=[FakeResponse(200, self.claim_raw)],
        )
        self.assertEqual(values["CLAIM_CONTENT_VALIDATION"], "HOLD")
        self.assertEqual(len(self.http_calls), 1)

    def test_all_subgates_pass_with_exact_two_get_allowlist(self):
        ledger = self.fake_modules()["g1_blob_call_journal_REVIEW_ONLY"]
        post = ledger.construct_postcheck(
            self.operation_id, self.claim_sha, self.fingerprint
        ).payload
        values = self.run_wrapper(http_plan=[
            FakeResponse(200, self.claim_raw), FakeResponse(200, post),
        ])
        for name in (
            "OWNER_INSTANCE_BINDING", "STORAGE_TOKEN_REQUEST",
            "STORAGE_TOKEN_VALIDATION", "CLAIM_GET",
            "CLAIM_CONTENT_VALIDATION",
        ):
            self.assertEqual(values[name], "PASS")
        self.assertEqual(values["ACCESS"], "PASS")
        self.assertEqual(values["DURABLE_RECEIPT"], "PASS")
        self.assertEqual(self.token_requests, 1)
        self.assertEqual(len(self.http_calls), 2)
        self.assertTrue(all(call[0] == "GET" for call in self.http_calls))
        paths = [call[2] for call in self.http_calls]
        self.assertEqual(paths[0], "/" + wrapper.CONTAINER + "/" + wrapper.CLAIM_KEY)
        self.assertEqual(
            paths[1],
            "/" + wrapper.CONTAINER + "/operator-once/pathb-g1-install-call-journal/"
            + self.operation_id + "/postcheck.json",
        )


if __name__ == "__main__":
    unittest.main()
