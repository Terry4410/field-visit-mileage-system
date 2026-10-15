"""Fail-closed nondeploy validation for the Full Scheme A UAT review package.

This validator never connects to UAT and never executes migration SQL.  It
binds the reviewed dependency manifest to the Recovery source, checks that the
reserved 013 slot is untouched, and proves that runtime feature defaults stay
OFF while the migration package remains outside executable migration roots.
"""

from __future__ import annotations

import hashlib
import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DRAFT = ROOT / "docs/release/optionc-canonical-sql-draft"
MANIFEST = DRAFT / "full-scheme-a/UAT_Migration_Review_Manifest.json"
HISTORICAL = DRAFT / "full-scheme-a/HistoricalFingerprint.readonly.sql"

EXPECTED_SHA256 = {
    "1800_011/Preflight.sql": "5552c502fdae46ad42a210d9e44aa457a96d489c6b36fd1a8289aa6d3ecb87fe",
    "1800_011/Up.sql": "cdb85749fee47f7480c410792879a6cf885ac2aae5107dfefd3418bc8fa2fd6a",
    "1800_011/Verify.sql": "de9375da5e97aa92ab9779e8a02535597c8a52f02174ca86ad1c4b657343a281",
    "1800_012/Preflight.sql": "e6e62a9f4d4cfce976ea8ddca558cc5b8eb616c1a812b6ff33c3e8e9a2685737",
    "1800_012/Up.sql": "84905dc6efccd0a711878e37f0688e011f259b64cb37799967219d7df7d7c7e7",
    "1800_012/Verify.sql": "87726cd3fee1c80e342c8a05135a1cd91675aaa8d9925a4e12dc6543ececfc72",
    "1800_014/Preflight.sql": "c5d8b0cd1ccb583ed674aaf7b19eea3a7e55fe7bf59f5572500bd1f891c653c2",
    "1800_014/Up.sql": "2ced64461913036b77a1416caadf1bdb68460adba2eaf95adeb47e08fc37d69e",
    "1800_014/Backfill.sql": "b59eeed933a7eba6a2e34df02f83091dc92c54633c83e44ae271e290ab8de3ed",
    "1800_014/Verify.sql": "972aeb530436adc66be57f8407a7ef1404747725e267e104a442bbdae836d55c",
    "audit/StageEvidenceAuditEvents.disposable.sql": "59a5bf105f26aaed578e2d47dd7067bf0c6f9abc771a179ef1032dce3b7b3307",
    "leader-review/LeaderReviewDetails.disposable.sql": "e96ee76d1b51612b57881d96087b1ccf06194f48eb437c0f51dbfbeed726076f",
    "create-lowrisk/CreateLowRiskContract.disposable.sql": "55e2ee2af2adb141d68402f1ffc581366aa96f0bd9438aa61cbec5c5f74ddd03",
    "lowrisk-direct/LowRiskDirectContract.disposable.sql": "2b70b713a16b58c14929782f03805984b68a412108df5f45f37e54c5dd769aaa",
    "merge/LocationMergeDetails.disposable.sql": "c03d2f2a877121fcecd6d829c4339c27c659b8d0d1c2243abfcb1c810dee1a09",
    "excel-import/ExcelImportStatusContract.disposable.sql": "d581de8349927872a04586c59796891115df4a1bc0a825da7a2fa78deaa3fe32",
}


class FullSchemeAUatReadinessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))

    def test_actual_uat_baseline_is_bound_to_successful_readonly_evidence(self):
        evidence = self.manifest["authoritativeUatEvidence"]
        self.assertEqual(38707441735, evidence["workflowRunId"])
        self.assertEqual("ce70eddc290e85375c64311b6d96eedc09942dba", evidence["sourceSha"])
        self.assertEqual("1.8.0-010", evidence["latestSchemaVersion"])
        self.assertEqual(
            "665C66D7BA7DE5F75AD855CDDDA10175E8AB31114B6572EC010813799BFCD975",
            evidence["catalogFingerprintSha256"],
        )
        for field in (
            "canonicalRequestTablesPresent",
            "stageAndPublishedVersionsPresent",
            "mergeDetailsPresent",
            "canonicalEvidenceAuditExtended",
        ):
            self.assertIs(evidence[field], False, field)

    def test_reviewed_sources_are_immutable_and_outside_executable_roots(self):
        for relative, expected in EXPECTED_SHA256.items():
            path = DRAFT / relative
            self.assertEqual(expected, hashlib.sha256(path.read_bytes()).hexdigest(), relative)
        for migration in ("1800_011", "1800_012", "1800_014"):
            self.assertFalse((ROOT / "database/migrations" / migration).exists())
            self.assertFalse((ROOT / "database/candidates" / migration).exists())

    def test_order_preserves_reserved_013_and_exposes_unassigned_additives(self):
        design = self.manifest["orderedDesign"]
        self.assertEqual(list(range(1, 11)), [step["order"] for step in design])
        reserved = [step for step in design if step["marker"] == "1.8.0-013"]
        self.assertEqual(1, len(reserved))
        self.assertEqual("RESERVED_DO_NOT_TOUCH", reserved[0]["deployability"])
        self.assertEqual("NO_CHANGE_NO_EXECUTION", self.manifest["frozenRules"]["reservedMigrationAction"])
        self.assertTrue(any(step["deployability"].startswith("HOLD_") for step in design[4:]))

    def test_symbolic_markers_cannot_be_misrepresented_as_uat_ready(self):
        combined = "\n".join(
            (DRAFT / relative).read_text(encoding="utf-8")
            for relative in (
                "leader-review/LeaderReviewDetails.disposable.sql",
                "create-lowrisk/CreateLowRiskContract.disposable.sql",
                "lowrisk-direct/LowRiskDirectContract.disposable.sql",
                "excel-import/ExcelImportStatusContract.disposable.sql",
            )
        )
        self.assertIn("1.8.0-LEADER-REVIEW", combined)
        self.assertIn("1.8.0-CREATE-LOWRISK", combined)
        self.assertIn("1.8.0-LOWRISK-DIRECT", combined)
        self.assertIn("1.8.0-EXCEL-CANONICAL-STATUS", combined)
        statuses = {step["contract"]: step["deployability"] for step in self.manifest["orderedDesign"]}
        self.assertTrue(all("HOLD" in state for name, state in statuses.items() if name in {
            "persistent independent Leader first-level decision",
            "CreateLocation reservation and LowRisk publication contract",
            "Excel canonical submission statuses",
        }))

    def test_historical_fingerprint_is_select_only_and_covers_trip_invariants(self):
        sql = HISTORICAL.read_text(encoding="utf-8")
        upper = re.sub(r"/\*.*?\*/", "", sql, flags=re.S).upper()
        for forbidden in (" INSERT ", " UPDATE ", " DELETE ", " MERGE ", " CREATE ", " ALTER ", " DROP ", " EXEC "):
            self.assertNotIn(forbidden, f" {upper} ", forbidden)
        for table in ("VisitTrips", "VisitTripStops", "VisitTripSnapshots", "VisitTripSnapshotStops"):
            self.assertIn(f"dbo.{table}", sql)
        self.assertIn("ApprovedDistanceKmSnapshot", sql)
        self.assertIn("LocationId", sql)
        self.assertIn("HISTORICAL_FINGERPRINT_COMPLETE_READ_ONLY", sql)
        self.assertIn("INCLUDE_NULL_VALUES", sql)
        self.assertIn("ObservedHighWatermark", sql)
        self.assertIn("EXPECTED_DATABASE", sql)

    def test_runtime_flags_remain_off_and_candidate_boundary_is_present(self):
        appsettings = json.loads((ROOT / "backend/src/FieldVisit.Api/appsettings.json").read_text())
        self.assertIs(appsettings["PackageB"]["B3"]["Enabled"], False)
        self.assertIs(appsettings["OptionC"]["Approval"]["Enabled"], False)
        self.assertIs(appsettings["OptionC"]["Candidate"]["Enabled"], False)
        frontend = (ROOT / "frontend/src/optionc-approval-flags.ts").read_text()
        self.assertIn("OPTIONC_APPROVAL_UI_ENABLED: boolean = false", frontend)
        controller = (ROOT / "backend/src/FieldVisit.Api/Controllers/V180OptionCCandidateController.cs").read_text()
        self.assertIn("[Authorize]", controller)
        service = (ROOT / "backend/src/FieldVisit.Infrastructure/V180OptionCCandidateService.cs").read_text()
        self.assertIn("OPTIONC_CANDIDATE_DISABLED", service)

    def test_isolated_http_sql_security_evidence_remains_in_source(self):
        tests = (ROOT / "backend/tests/FieldVisit.Application.Tests/V180OptionCFinalSecurityGateSqlTests.cs").read_text()
        required = (
            "CreateLocation_http_reserves_pending_identity_stage_and_rejects_duplicate",
            "Create_and_lowrisk_atomic_candidate_requires_independent_leader_and_preserves_history",
            "Frozen_lowrisk_policy_direct_save_uses_live_write_scope_and_atomic_version_audit",
            "Leader_first_level_review_real_http_stage_evidence_and_admin_publication_preserve_history",
            "MergeFields_real_http_submission_approval_reject_and_history_are_atomic",
            "Reviewer_and_requester_hr_role_revocation_after_request_lock_is_fail_closed",
            "Independent_connections_racing_approve_approve_approve_reject_and_reject_reject_commit_exactly_one",
            "Lost_commit_ack_is_reconciled_on_fresh_nonpooled_connection_without_retry",
        )
        for name in required:
            self.assertIn(name, tests)


if __name__ == "__main__":
    unittest.main(verbosity=2)
