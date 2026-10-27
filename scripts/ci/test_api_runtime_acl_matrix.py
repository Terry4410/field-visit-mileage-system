"""Source-only guards for the NONDEPLOY API runtime ACL matrix."""
import csv
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROMOTION = ROOT / "docs/release/g1-execution/promotion"
MATRIX = PROMOTION / "API-RUNTIME-SQL-CRUD-MATRIX.csv"
CANDIDATE = PROMOTION / "API-RUNTIME-SQL-GRANT-CANDIDATE.sql"


class ApiRuntimeAclMatrixTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with MATRIX.open(newline="", encoding="utf-8") as stream:
            cls.rows = list(csv.DictReader(stream))

    def test_matrix_paths_and_values_are_closed(self):
        self.assertGreaterEqual(len(self.rows), 20)
        required = {"operation_group", "table", "schema_phase", "SELECT", "INSERT", "UPDATE", "DELETE", "source_path", "source_evidence", "status", "database_dependencies"}
        self.assertEqual(set(self.rows[0]), required)
        names = set()
        for row in self.rows:
            self.assertRegex(row["table"], r"^[A-Za-z][A-Za-z0-9_]{0,127}$")
            self.assertNotIn(row["table"], names, "one row per physical table is required")
            names.add(row["table"])
            self.assertTrue((ROOT / row["source_path"]).is_file(), row["source_path"])
            self.assertTrue(row["operation_group"].strip() and row["source_evidence"].strip() and row["database_dependencies"].strip())
            self.assertEqual(row["status"], "SOURCE_REVIEW_CANDIDATE")
            for permission in ("SELECT", "INSERT", "UPDATE", "DELETE"):
                self.assertIn(row[permission], {"YES", "NO"})

    def test_route_governance_dml_matches_real_api_lifecycle(self):
        by_table = {row["table"]: row for row in self.rows}
        expected = {
            "RouteCalculationAttempts": ("YES", "YES", "YES", "NO"),
            "GeocodingAttempts": ("YES", "YES", "YES", "NO"),
            "MileageGovernanceEvents": ("NO", "YES", "NO", "NO"),
        }
        for table, crud in expected.items():
            self.assertIn(table, by_table)
            self.assertEqual(tuple(by_table[table][p] for p in ("SELECT", "INSERT", "UPDATE", "DELETE")), crud)

    def test_runtime_history_is_read_only_and_complete_protected_set(self):
        protected = [r for r in self.rows if r["schema_phase"].startswith("PROTECTED")]
        self.assertGreaterEqual(len(protected), 3)
        self.assertTrue({"SchemaVersions", "SchemaMigrationDataBaselines", "FieldVisitMigrationLedger"}.issubset({r["table"] for r in protected}))
        for row in protected:
            self.assertEqual(row["SELECT"], "YES")
            self.assertEqual((row["INSERT"], row["UPDATE"], row["DELETE"]), ("NO", "NO", "NO"))

    def test_option_c_is_labeled_as_future_schema_scope(self):
        option_c = [r for r in self.rows if "Option C" in r["operation_group"]]
        self.assertGreaterEqual(len(option_c), 5)
        self.assertTrue(all(r["schema_phase"] in {"POST010_OPTIONC", "010_AND_POST010_OPTIONC"} for r in option_c))

    def test_grant_candidate_is_inert_and_no_broad_writer_grant(self):
        source = CANDIDATE.read_text(encoding="utf-8")
        self.assertRegex(source, r"(?m)^RETURN;\s*$")
        self.assertIn("NOT FOR LIVE EXECUTION", source)
        self.assertNotRegex(source, r"(?im)^\s*ALTER\s+ROLE\s+db_datawriter")
        self.assertNotRegex(source, r"(?im)^\s*GRANT\s+\w+\s+ON\s+SCHEMA::dbo")
        self.assertIn("DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SchemaVersions", source)

    def test_exact_workflow_calls_the_isolated_sql_acl_test(self):
        workflow = (ROOT / ".github/workflows/uat010-integrated-source-nondeploy.yml").read_text(encoding="utf-8")
        self.assertIn("scripts/ci/test_api_runtime_acl_matrix.ps1", workflow)
        self.assertIn("uat010-api-runtime-acl-nondeploy-${{ github.sha }}", workflow)
        self.assertIn("API-RUNTIME-SQL-CRUD-MATRIX.csv", workflow)

    def test_existing_cleanup_guard_remains_default_off(self):
        controller = (ROOT / "backend/src/FieldVisit.Api/Controllers/UatAutomationController.cs").read_text(encoding="utf-8")
        settings = __import__("json").loads((ROOT / "backend/src/FieldVisit.Api/appsettings.json").read_text(encoding="utf-8"))
        self.assertIn('config["UatAutomation:CleanupEnabled"]', controller)
        self.assertIs(settings["UatAutomation"]["CleanupEnabled"], False)


if __name__ == "__main__":
    unittest.main()
