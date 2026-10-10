import json
import os
import tempfile
import unittest
from pathlib import Path

from package_b_evidence import (collect, parse_junit, parse_trx,
                                REQUIRED_SECURITY_GATES, FROZEN_PROTECTED_SHA)


class PackageBEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name)
        self.guard = base / "guard.json"
        self.trx = base / "backend.trx"
        self.junit = base / "frontend.xml"
        self.matrix = base / "matrix.json"
        self.matrix.write_text(json.dumps({
            "passed": True, "status": "DOCUMENTED_ONLY_SQL_RUNTIME_NOT_TESTED",
            "candidate_sha": os.environ.get("GITHUB_SHA") or ("a" * 40),
            "present": 22, "required": 22,
            "ids": [f"B4-SQL-{i:02d}" for i in range(1, 23)]
        }))
        self.guard.write_text(json.dumps({
            "kind": "PACKAGE_B_B3_B4_NONDEPLOY",
            "head_sha": os.environ.get("GITHUB_SHA") or ("a" * 40),
            "frozen_sha": FROZEN_PROTECTED_SHA,
            "passed": True,
            "checks": [{"name": name, "passed": True}
                       for name in sorted(REQUIRED_SECURITY_GATES)]}))
        self.trx.write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '<ResultSummary><Counters total="2" passed="2" failed="0" error="0" '
            'notExecuted="0"/></ResultSummary></TestRun>')
        self.junit.write_text('<testsuites><testsuite tests="2" failures="0" '
                              'errors="0" skipped="0"/></testsuites>')

    def test_missing_B4_matrix_fails_closed(self):
        self.matrix.unlink()
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_B4_matrix_sha_mismatch_fails_closed(self):
        data = json.loads(self.matrix.read_text())
        data["candidate_sha"] = "f" * 40
        self.matrix.write_text(json.dumps(data))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_B4_matrix_fabricated_execution_status_fails_closed(self):
        data = json.loads(self.matrix.read_text())
        data["status"] = "SQL_SERVER_SUCCESS"
        self.matrix.write_text(json.dumps(data))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_valid_offline_evidence_passes(self):
        self.assertEqual("PASS", collect(self.guard, self.trx, self.junit, self.matrix)["result"])
        self.assertEqual(2, parse_trx(self.trx)["passed"])
        self.assertEqual(2, parse_junit(self.junit)["passed"])

    def test_missing_named_security_gate_is_not_a_pass(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"] = [c for c in payload["checks"]
                             if c["name"] != "B3_approve_executor_DENY_ALL"]
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_duplicate_named_security_gate_is_not_a_pass(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"].append(payload["checks"][0])
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_false_protected_sha_is_not_a_pass(self):
        payload = json.loads(self.guard.read_text())
        payload["frozen_sha"] = "b" * 40
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_non_boolean_success_flag_fails_closed(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"][0]["passed"] = "true"
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_extra_fake_check_fails_closed(self):
        payload = json.loads(self.guard.read_text())
        payload["checks"].append({"name": "fake_ok", "passed": True})
        self.guard.write_text(json.dumps(payload))
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_sha_mismatch_fails_closed(self):
        payload = json.loads(self.guard.read_text())
        payload["head_sha"] = "an-intentionally-incorrect-sha"
        self.guard.write_text(json.dumps(payload))
        # Explicitly simulates the real GitHub Actions SHA guard.
        from unittest.mock import patch
        with patch.dict(os.environ, {"GITHUB_SHA": "the-real-head-sha"}):
            self.assertEqual("FAIL_CLOSED", collect(
                self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_guard_failure_fails_closed(self):
        data = json.loads(self.guard.read_text())
        data["checks"][0]["passed"] = False
        self.guard.write_text(json.dumps(data))
        result = collect(self.guard, self.trx, self.junit, self.matrix)
        self.assertEqual("FAIL_CLOSED", result["result"])

    def test_backend_missing_fails_closed(self):
        self.trx.unlink()
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_frontend_failure_fails_closed(self):
        self.junit.write_text('<testsuite tests="2" failures="1" '
                              'errors="0" skipped="0"/>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_skipped_test_fails_closed(self):
        self.trx.write_text(
            '<TestRun><ResultSummary><Counters total="2" passed="1" '
            'failed="0" error="0" notExecuted="1"/></ResultSummary></TestRun>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit, self.matrix)["result"])

    def test_nested_junit_counts_leaf_suites_once(self):
        self.junit.write_text(
            '<testsuites tests="3"><testsuite name="parent" tests="3">'
            '<testsuite name="a" tests="1" failures="0"/>'
            '<testsuite name="b" tests="2" failures="0"/>'
            '</testsuite></testsuites>')
        self.assertEqual(3, parse_junit(self.junit)["total"])


if __name__ == "__main__":
    unittest.main()
