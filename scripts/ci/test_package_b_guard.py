import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import package_b_guard as guard


class PackageBGuardNegativeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "backend/src/FieldVisit.Api").mkdir(parents=True)
        (self.root / "backend/src/FieldVisit.Infrastructure").mkdir(parents=True)
        (self.root / "database/migrations").mkdir(parents=True)
        (self.root / ".github/workflows").mkdir(parents=True)
        self.config = self.root / "backend/src/FieldVisit.Api/appsettings.json"
        self.config.write_text(json.dumps({"PackageB": {"B3": {"Enabled": False}}}))
        self.approval = self.root / "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs"
        self.approval.write_text(
            'public async Task<V180B3RequestView> ApproveAsync('
            '{ throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED"); }')
        self.workflow = self.root / ".github/workflows/package-b-b2-b3-controlled-verify.yml"
        self.workflow.write_text(
            "permissions:\n  contents: read\n  persist-credentials: false\n"
            "  python3 scripts/ci/package_b_guard.py\n")
        self.changed_sql = ""
        self.changed_workflows = ""
        self.remote = guard.FROZEN

    def scan(self):
        def fake_git(*args):
            if args[:2] == ("rev-parse", "HEAD"):
                return "candidate"
            if args[:2] == ("rev-parse", "refs/remotes/origin/post-uat/v1.8.0"):
                return self.remote
            if args[:2] == ("diff", "--name-only"):
                return (self.changed_sql if args[-1] == "database/migrations"
                        else self.changed_workflows)
            raise AssertionError("unexpected git operation " + repr(args))
        with patch.object(guard, "ROOT", self.root), \
             patch.object(guard, "git", side_effect=fake_git), \
             patch.object(guard.subprocess, "run", return_value=Mock(returncode=0)), \
             patch.dict(os.environ, {"GITHUB_REF": guard.BRANCH}):
            return guard.scan()

    def failures(self):
        return {x["name"] for x in self.scan() if not x["passed"]}

    def test_clean_candidate_passes_all_gates(self):
        self.assertEqual(set(), self.failures())

    def test_protected_ref_drift_fails(self):
        self.remote = "unexpected"
        self.assertIn("protected_source_frozen", self.failures())

    def test_feature_enablement_fails(self):
        self.config.write_text('{"PackageB":{"B3":{"Enabled":true}}}')
        self.assertIn("feature_flag_off", self.failures())

    def test_approval_write_fails(self):
        self.approval.write_text(self.approval.read_text() + " await db.SaveChangesAsync();")
        self.assertIn("B3_approve_executor_DENY_ALL", self.failures())

    def test_new_011_migration_fails(self):
        (self.root / "database/migrations/1800_011_unauthorized.sql").write_text("SELECT 1")
        self.assertIn("no_executable_011_migration", self.failures())

    def test_edited_010_migration_fails(self):
        self.changed_sql = "database/migrations/1800_010_legacy/Up.sql"
        self.assertIn("all_migrations_unchanged_since_baseline", self.failures())

    def test_disguised_011_workflow_fails(self):
        (self.root / ".github/workflows/neutral-filename.yml").write_text(
            "run: echo 1800_011")
        self.assertIn("no_hidden_011_workflow_dispatch", self.failures())

    def test_unapproved_workflow_change_fails(self):
        self.changed_workflows = ".github/workflows/api-azure.yml"
        self.assertIn("no_unapproved_workflow_edits_since_baseline", self.failures())


if __name__ == "__main__":
    unittest.main()
