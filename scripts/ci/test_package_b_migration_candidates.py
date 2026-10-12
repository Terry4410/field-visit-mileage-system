"""Static controls for the WORK-only executable 1800_011/012 candidates.

These tests do not execute SQL. GitHub's disposable SQL fixture independently
tests the matching schema/transaction contracts. The purpose here is to stop a
candidate from silently becoming an auto-discovered UAT migration or losing its
preflight, verification and forward-recovery controls.
"""
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
CANDIDATES = ROOT / "database" / "candidates"
WORKFLOW = ROOT / ".github" / "workflows" / "package-b-b2-b3-controlled-verify.yml"


class PackageBMigrationCandidateControls(unittest.TestCase):
    def candidate(self, number: str) -> Path:
        matches = sorted(CANDIDATES.glob(f"{number}_*"))
        self.assertEqual(1, len(matches), f"missing or ambiguous {number} candidate")
        return matches[0]

    def text(self, number: str, file: str) -> str:
        return (self.candidate(number) / file).read_text(encoding="utf-8")

    def test_candidates_have_control_envelope_and_remain_outside_runner(self):
        for number in ("1800_011", "1800_012"):
            path = self.candidate(number)
            self.assertFalse((ROOT / "database" / "migrations" / path.name).exists())
            for name in ("Preflight.sql", "Up.sql", "Verify.sql", "RECOVERY.md"):
                self.assertTrue((path / name).is_file(), f"{number} missing {name}")
            self.assertIn("NOT", self.text(number, "RECOVERY.md").upper())
            self.assertIn("THROW", self.text(number, "Preflight.sql"))
            self.assertIn("BEGIN TRANSACTION", self.text(number, "Up.sql"))
            self.assertIn("ROLLBACK TRANSACTION", self.text(number, "Up.sql"))
            self.assertIn("VerifyPassed", self.text(number, "Verify.sql"))

    def test_011_is_exact_predecessor_create_only_and_has_audited_contract(self):
        preflight = self.text("1800_011", "Preflight.sql")
        up = self.text("1800_011", "Up.sql")
        verify = self.text("1800_011", "Verify.sql")
        for token in ("1.8.0-010", "dbo.Locations", "dbo.Organizations", "dbo.Teams", "dbo.Users"):
            self.assertIn(token, preflight)
        for token in ("CREATE TABLE dbo.ChangeRequests", "CREATE TABLE dbo.ChangeRequestEvents",
                      "UX_B3_ChangeRequests_Org_Entity_Pending",
                      "UX_B3_ChangeRequestEvents_DecisionKey", "ON DELETE NO ACTION",
                      "CK_B3_ChangeRequests_ReviewState", "1.8.0-011"):
            self.assertIn(token, up)
        # 011 retains the physical AppliedAt column for forward compatibility,
        # but its request-state constraint must not yet permit Applied.
        self.assertIn("[Status] IN (N'Pending',N'Rejected')", up)
        self.assertIn("B3 unique-index contract drift", verify)

    def test_012_is_forward_only_with_approved_applied_audit_pair(self):
        preflight = self.text("1800_012", "Preflight.sql")
        up = self.text("1800_012", "Up.sql")
        verify = self.text("1800_012", "Verify.sql")
        self.assertIn("1.8.0-011", preflight)
        for token in ("N'Applied'", "N'Approved'", "CK_B3_ChangeRequests_ReviewState",
                      "CK_B3_ChangeRequestEvents_DecisionState", "1.8.0-012"):
            self.assertIn(token, up)
        self.assertIn("trigger is prohibited", verify)
        self.assertNotIn("DROP TABLE", up.upper())
        self.assertNotIn("DELETE FROM", up.upper())

    def test_nondeploy_workflow_never_mentions_or_executes_candidate_sql(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertNotRegex(workflow, r"(?i)1800[_-]01[12]")
        self.assertNotRegex(workflow, r"(?i)sqlcmd|database update|azure/login")


if __name__ == "__main__":
    unittest.main()
