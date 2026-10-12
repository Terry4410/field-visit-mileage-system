import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import package_b_it_handoff as ledger


class ItHandoffNonreleaseTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for path in ledger.SOURCE_FILES:
            file = self.root / path
            file.parent.mkdir(parents=True, exist_ok=True)
            if path == ledger.IT_FILE:
                file.write_text("\n".join(gate for gate, _, _ in ledger.GATE_OWNERS),
                                encoding="utf-8")
            else:
                file.write_text("DRAFT / WORK ONLY / NO-GO\n", encoding="utf-8")
        config = self.root / "backend/src/FieldVisit.Api/appsettings.json"
        config.parent.mkdir(parents=True)
        config.write_text(json.dumps({"PackageB": {"B3": {"Enabled": False}}}))
        self.service = (self.root /
                        "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs")
        self.service.parent.mkdir(parents=True, exist_ok=True)
        self.service.write_text(
            'public async Task<V180B3RequestView> ApproveAsync('
            '{throw new InvalidOperationException('
            '"B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");}')
        (self.root / "database/migrations").mkdir(parents=True)
        (self.root / ".github/workflows").mkdir(parents=True)

    def collect(self):
        return ledger.collect(self.root, "a" * 40)

    def test_complete_handoff_preparation_is_explicitly_release_hold(self):
        proof = self.collect()
        self.assertEqual("IT_HANDOFF_PREP_PASS_RELEASE_HOLD", proof["status"])
        self.assertEqual("NO_GO", proof["release_decision"])
        self.assertEqual("DENY_ALL", proof["approve_apply"])
        self.assertEqual((0, 22),
                         (proof["formal_b4_accepted"], proof["formal_b4_total"]))
        self.assertTrue(all(x["status"] == "BLOCKED_UNSIGNED"
                            for x in proof["release_gates"]))
        self.assertFalse(proof["uat_migration_011_authorized"])
        self.assertFalse(proof["uat_migration_012_authorized"])
        self.assertFalse(proof["uat_deployment_authorized"])

    def test_handoff_file_hash_is_real_and_source_bound(self):
        proof = self.collect()
        file = ledger.SOURCE_FILES[1]
        digest = next(x["sha256"] for x in proof["review_artifact_hashes"]
                      if x["file"] == file)
        self.assertEqual(hashlib.sha256((self.root/file).read_bytes()).hexdigest(),
                         digest)

    def test_missing_IT_review_field_fails_closed(self):
        (self.root/ledger.IT_FILE).write_text("DDL-01\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "HANDOFF_GATE_UNLISTED"):
            self.collect()

    def test_missing_source_file_cannot_be_marked_as_ready(self):
        (self.root/ledger.SOURCE_FILES[2]).unlink()
        with self.assertRaisesRegex(ValueError, "HANDOFF_REQUIRED_SOURCE_MISSING"):
            self.collect()

    def test_unauthorized_011_sql_migration_fails_closed(self):
        file = self.root/"database/migrations/1800_011_foo/Up.sql"
        file.parent.mkdir()
        file.write_text("SELECT 1")
        with self.assertRaisesRegex(ValueError, "HANDOFF_UNAUTHORIZED_MIGRATION"):
            self.collect()

    def test_unauthorized_012_sql_migration_fails_closed(self):
        file = self.root/"database/migrations/1800_012_foo/Verify.sql"
        file.parent.mkdir()
        file.write_text("SELECT 1")
        with self.assertRaisesRegex(ValueError, "HANDOFF_UNAUTHORIZED_MIGRATION"):
            self.collect()

    def test_unauthorized_012_workflow_fails_closed(self):
        (self.root/".github/workflows/azure-sql-1800_012.yml").write_text(
            "name: unauthorized")
        with self.assertRaisesRegex(ValueError, "HANDOFF_UNAUTHORIZED_MIGRATION"):
            self.collect()

    def test_activation_or_missing_denial_is_not_handoff_success(self):
        cfg = self.root/"backend/src/FieldVisit.Api/appsettings.json"
        cfg.write_text(json.dumps({"PackageB": {"B3": {"Enabled": True}}}))
        with self.assertRaisesRegex(ValueError, "HANDOFF_FEATURE_NOT_OFF"):
            self.collect()
        cfg.write_text(json.dumps({"PackageB": {"B3": {"Enabled": False}}}))
        self.service.write_text("public async Task<V180B3RequestView> ApproveAsync() { }")
        with self.assertRaisesRegex(ValueError, "HANDOFF_APPROVE_NOT_DENY_ALL"):
            self.collect()

    def test_rejects_non_sha_or_unverified_source(self):
        with self.assertRaisesRegex(ValueError, "HANDOFF_INVALID_WORK_SHA"):
            ledger.collect(self.root, "pending")

    def test_markdown_is_explicitly_no_go(self):
        doc = ledger.render(self.collect())
        self.assertIn("Release: NO_GO", doc)
        self.assertIn("B4: 0/22", doc)
        self.assertIn("BLOCKED_UNSIGNED", doc)
        self.assertIn("HOLD", doc)


if __name__ == "__main__":
    unittest.main()
