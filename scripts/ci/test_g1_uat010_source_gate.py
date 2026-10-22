import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import g1_uat010_source_gate as gate


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1] if (HERE.parents[1] / gate.MANIFEST).is_file() else HERE


class Uat010SourceGateTests(unittest.TestCase):
    def setUp(self):
        self.has_repo = (ROOT / gate.MANIFEST).is_file() and (ROOT / gate.PROMOTION_REFERENCE).is_file() and (ROOT / gate.NECESSARY).is_file()
        if self.has_repo:
            self.manifest = json.loads((ROOT / gate.MANIFEST).read_text(encoding="utf-8"))
            self.promotion_reference = json.loads((ROOT / gate.PROMOTION_REFERENCE).read_text(encoding="utf-8"))
            self.necessary = json.loads((ROOT / gate.NECESSARY).read_text(encoding="utf-8"))
        else:
            self.manifest = {
                "contract": "g1-fixed-target-promotion-v1", "rcSource": gate.RC,
                "reviewedStagingCommit": gate.STAGING, "reviewedStagingParent": gate.STAGING_PARENT,
                "approval": "PROPOSED_NOT_AUTHORIZED",
                "schema010SourceBoundBaseline": {"status": "HOLD", "syntheticReferenceAcceptedAsUatBaseline": False},
                "steps": [{"proposedId": value} for value in gate.EXPECTED_IDS],
            }
            self.promotion_reference = {"originRc": gate.RC, "fixture": "SYNTHETIC_SCOPED_MIGRATION_CONTRACT_NOT_UAT_OR_150_BASELINE"}
            self.necessary = {"rc": gate.RC, "scope": "CANDIDATE_NECESSARY_CONTRACT_NOT_OBSERVED_UAT", "inputs": {}}

    def test_current_reference_is_explicitly_not_observed_uat(self):
        if not self.has_repo:
            self.skipTest("repository files are available in the exact-SHA CI checkout")
        gate.validate_reference(self.manifest, self.promotion_reference, self.necessary)
        self.assertIn("SYNTHETIC", self.promotion_reference["fixture"])
        self.assertEqual(self.manifest["schema010SourceBoundBaseline"]["status"], "HOLD")

    def test_synthetic_reference_cannot_be_relabelled_as_uat(self):
        m = dict(self.manifest)
        m["schema010SourceBoundBaseline"] = dict(m["schema010SourceBoundBaseline"], syntheticReferenceAcceptedAsUatBaseline=True)
        with self.assertRaises(gate.GateError):
            gate.validate_reference(m, self.promotion_reference, self.necessary)

    def test_wrong_synthetic_reference_source_stops(self):
        reference = dict(self.promotion_reference, originRc="0" * 40)
        with self.assertRaises(gate.GateError):
            gate.validate_reference(self.manifest, reference, self.necessary)

    def test_missing_or_extra_migration_marker_id_stops(self):
        m = dict(self.manifest)
        m["steps"] = list(m["steps"]) + [{"proposedId": "1.8.0-013"}]
        with self.assertRaises(gate.GateError):
            gate.validate_reference(m, self.promotion_reference, self.necessary)

    def test_004_and_007_trigger_source_sets_are_pinned(self):
        if not self.has_repo:
            self.skipTest("repository files are available in the exact-SHA CI checkout")
        by_migration = {}
        for dirname in gate.EXPECTED_TRIGGERS:
            path = next(p for p in self.necessary["inputs"] if p.startswith(f"database/migrations/{dirname}/") and p.endswith("/Up.sql"))
            by_migration[dirname] = (ROOT / path).read_text(encoding="utf-8")
        gate.validate_trigger_texts(by_migration)

    def test_complete_source_and_candidate_sql_hash_maps(self):
        if not self.has_repo:
            self.skipTest("repository files are available in the exact-SHA CI checkout")
        gate.verify_source_files(ROOT, self.manifest, self.necessary)

    def test_trigger_source_drift_stops(self):
        fixture = {name: "CREATE TRIGGER dbo.TR_Unexpected ON dbo.X AFTER UPDATE AS SELECT 1" for name in gate.EXPECTED_TRIGGERS}
        with self.assertRaises(gate.GateError):
            gate.validate_trigger_texts(fixture)

    def test_teamless_014_requires_org_creator_and_nonnull_team_scope(self):
        source = ((ROOT / gate.BACKFILL).read_text(encoding="utf-8") if self.has_repo else
                  "AND (o.OrganizationId IS NULL OR u.OrganizationId<>l.OrganizationId OR (l.TeamId IS NOT NULL AND (t.OrganizationId<>l.OrganizationId)))")
        gate.validate_teamless_policy(source)

    def test_teamless_without_creator_scope_stops(self):
        with self.assertRaises(gate.GateError):
            gate.validate_teamless_policy("AND (l.TeamId IS NOT NULL AND t.OrganizationId<>l.OrganizationId)")

    def test_public_result_never_promotes_live_gates(self):
        result = gate.sanitized_result("a" * 40)
        self.assertEqual(result["SourceClosure"], "PASS")
        for key in ("ObservedUAT010", "ApiPrincipal", "RuntimeProvenance", "Recovery"):
            self.assertEqual(result[key], "UNKNOWN")
        self.assertEqual(result["Necessary010Acceptance"], "HOLD")
        self.assertEqual(result["TriggerEventParity"], "UNKNOWN")
        self.assertEqual(result["LiveOperation"], "NOT_EXECUTED")
        self.assertEqual(set(result), {"SourceSHA", "SourceClosure", "Necessary010Acceptance", "TriggerEventParity", "ObservedUAT010", "ApiPrincipal", "RuntimeProvenance", "Recovery", "LiveOperation"})

    def test_public_result_rejects_non_sha_source_identity(self):
        with self.assertRaises(gate.GateError):
            gate.sanitized_result("not-a-source")


if __name__ == "__main__":
    unittest.main()
