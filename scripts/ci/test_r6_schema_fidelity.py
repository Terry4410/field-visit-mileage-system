"""Fail-closed guard for the unexecuted R6 metadata-only UAT preflight."""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
SQL = ROOT / "docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql"
TESTS = ROOT / "backend/tests/FieldVisit.Application.Tests/V180OptionCFinalSecurityGateSqlTests.cs"
MATRIX = ROOT / "docs/release/OPTIONC_CANONICAL_SCHEMA_GAP_MATRIX_R4.md"


class R6SchemaFidelityTests(unittest.TestCase):
    def test_future_uat_inventory_is_exact_target_and_metadata_only(self):
        source = SQL.read_text(encoding="utf-8")
        executable = re.sub(r"/\*.*?\*/|--[^\n]*", "", source, flags=re.S)
        upper = f" {executable.upper()} "
        for word in (" INSERT ", " UPDATE ", " DELETE ", " MERGE ", " CREATE ",
                     " ALTER ", " DROP ", " TRUNCATE ", " EXEC ", " EXECUTE ",
                     " RESTORE ", " BACKUP "):
            self.assertNotIn(word, upper)
        self.assertIn("DB_NAME()<>@EXPECTEDDATABASE", re.sub(r"\s+", "", upper))
        self.assertIn("SQL-FIELDVISIT-JPE-UAT", upper)
        self.assertIn("DB-FIELDVISIT-UAT", upper)
        self.assertIn("SYS.COLUMNS", upper)
        self.assertIn("SYS.INDEXES", upper)
        self.assertIn("SYS.FOREIGN_KEYS", upper)
        self.assertIn("SYS.CHECK_CONSTRAINTS", upper)
        self.assertIn("IS_NOT_TRUSTED", upper)
        self.assertIn("AS ISROWVERSION", re.sub(r"\s+", " ", upper))
        for table in ("VisitTrips", "VisitTripStops", "VisitTripSnapshots", "VisitTripSnapshotStops"):
            self.assertIn(f"dbo.{table}".upper(), upper)
        self.assertNotIn("SELECT *", upper)
        self.assertNotIn("APPLIEDBY", upper)

    def test_inventory_sql_matches_reviewed_immutable_sha256(self):
        import hashlib

        source = SQL.read_bytes()
        self.assertEqual(
            hashlib.sha256(source).hexdigest(),
            "67551978bf16266ef8f38c018be87b7aadc321ee3db84d6d94b52a1538204734",
            "Schema inventory SQL changed; it requires a new independent review.",
        )
        executable = re.sub(
            rb"/\*.*?\*/|--[^\n]*", b"", source, flags=re.S
        ).upper()
        for identity_surface in (
            b"ORIGINAL_LOGIN", b"SUSER_SNAME", b"SYSTEM_USER",
            b"CURRENT_USER", b"APPLIEDBY",
        ):
            self.assertNotIn(identity_surface, executable)
        self.assertNotIn(b"SELECT *", executable)

    def test_real_full_chain_writer_variants_are_bound_to_sql_harness(self):
        tests = TESTS.read_text(encoding="utf-8")
        for name in (
            "R5_full_010_to_020_chain_runs_real_lowrisk_writer_in_same_sql_database",
            "R5_full_010_to_020_chain_runs_excel_create_and_update_writers_in_same_sql_database",
            "R6_full_010_to_020_chain_runs_real_visitor_create_location_writer",
            "R6_full_010_to_020_chain_runs_real_highrisk_leader_evidence_admin_writers",
            "R6_full_010_to_020_chain_runs_real_merge_fields_writer",
        ):
            self.assertIn(name, tests)
        self.assertIn("R6_SAME_DATABASE_HTTP_WRITER_PASS", tests)
        self.assertIn("TryCreateAsync(fullCanonicalChain)", tests)
        self.assertIn("api/v1/optionc-candidate/requests/{pending.RequestPublicId}/geocode-evidence", tests)

    def test_uat_matrix_retains_explicit_schema_fidelity_hold(self):
        matrix = MATRIX.read_text(encoding="utf-8")
        self.assertIn("665C66D7BA7DE5F75AD855CDDDA10175E8AB31114B6572EC010813799BFCD975", matrix)
        self.assertIn("Tables / columns / SQL types / nullability", matrix)
        self.assertIn("Unknown / HOLD", matrix)
        self.assertIn("not yet one sequential business journey in a", matrix)
        self.assertIn("not been run", matrix)


if __name__ == "__main__":
    unittest.main(verbosity=2)
