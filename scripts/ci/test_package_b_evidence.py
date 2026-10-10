import json
import tempfile
import unittest
from pathlib import Path

from package_b_evidence import collect, parse_junit, parse_trx


class PackageBEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name)
        self.guard = base / "guard.json"
        self.trx = base / "backend.trx"
        self.junit = base / "frontend.xml"
        self.guard.write_text(json.dumps({
            "kind": "PACKAGE_B_B3_B4_NONDEPLOY",
            "head_sha": "abc", "frozen_sha": "frozen",
            "passed": True, "checks": [{"passed": True}]}))
        self.trx.write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '<ResultSummary><Counters total="2" passed="2" failed="0" error="0" '
            'notExecuted="0"/></ResultSummary></TestRun>')
        self.junit.write_text('<testsuites><testsuite tests="2" failures="0" '
                              'errors="0" skipped="0"/></testsuites>')

    def test_valid_offline_evidence_passes(self):
        self.assertEqual("PASS", collect(self.guard, self.trx, self.junit)["result"])
        self.assertEqual(2, parse_trx(self.trx)["passed"])
        self.assertEqual(2, parse_junit(self.junit)["passed"])

    def test_guard_failure_fails_closed(self):
        data = json.loads(self.guard.read_text())
        data["checks"][0]["passed"] = False
        self.guard.write_text(json.dumps(data))
        result = collect(self.guard, self.trx, self.junit)
        self.assertEqual("FAIL_CLOSED", result["result"])

    def test_backend_missing_fails_closed(self):
        self.trx.unlink()
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit)["result"])

    def test_frontend_failure_fails_closed(self):
        self.junit.write_text('<testsuite tests="2" failures="1" '
                              'errors="0" skipped="0"/>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit)["result"])

    def test_skipped_test_fails_closed(self):
        self.trx.write_text(
            '<TestRun><ResultSummary><Counters total="2" passed="1" '
            'failed="0" error="0" notExecuted="1"/></ResultSummary></TestRun>')
        self.assertEqual("FAIL_CLOSED", collect(
            self.guard, self.trx, self.junit)["result"])

    def test_nested_junit_counts_leaf_suites_once(self):
        self.junit.write_text(
            '<testsuites tests="3"><testsuite name="parent" tests="3">'
            '<testsuite name="a" tests="1" failures="0"/>'
            '<testsuite name="b" tests="2" failures="0"/>'
            '</testsuite></testsuites>')
        self.assertEqual(3, parse_junit(self.junit)["total"])


if __name__ == "__main__":
    unittest.main()
