import unittest
from pathlib import Path

from package_b_schema_parity import analyze, DDL, MODEL, TRIGGERS, CHECKS, TABLES, UNIQUE


class PackageBSchemaParityStaticTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sql = DDL.read_text(encoding="utf-8")
        cls.ef = MODEL.read_text(encoding="utf-8")

    def test_1800_003_stays_explicitly_nonparity(self):
        report = analyze(self.sql, self.ef, "a" * 40)
        self.assertEqual("PASS_WITH_KNOWN_DIFFERENCES", report["status"])
        self.assertEqual("NOT_VERIFIED", report["schema_parity"])
        self.assertFalse(report["migration_executed"])
        self.assertEqual("0/22", report["b4_formal_accepted"])
        self.assertEqual("HOLD", report["business_uat"])
        self.assertEqual("HARD_HOLD", report["production"])

    def test_all_seven_migration_triggers_are_reported_as_differences(self):
        rows = analyze(self.sql, self.ef)["rows"]
        triggers = {r["name"]: r["status"] for r in rows if r["kind"] == "TRIGGER"}
        self.assertEqual(set(TRIGGERS), set(triggers))
        self.assertEqual({"DIFFERENCE"}, set(triggers.values()))

    def test_four_date_checks_are_never_mistaken_for_ensurecreated_checks(self):
        rows = analyze(self.sql, self.ef)["rows"]
        checks = {r["name"]: r["status"] for r in rows if r["kind"] == "DATE_CHECK"}
        self.assertEqual(set(CHECKS), set(checks))
        self.assertEqual({"DIFFERENCE"}, set(checks.values()))

    def test_missing_migration_trigger_fails_closed(self):
        corrupted = self.sql.replace("CREATE TRIGGER dbo." + TRIGGERS[0],
                                     "CREATE TRIGGER dbo.DELETED_SCHEMA_TRIGGER", 1)
        r = analyze(corrupted, self.ef)
        self.assertEqual("FAIL_CLOSED", r["status"])
        self.assertTrue(any(TRIGGERS[0] in v for v in r["failures"]))

    def test_missing_ef_mapping_fails_closed(self):
        entity = TABLES["DeploymentSites"]
        corrupted = self.ef.replace("Entity<" + entity + ">(e =>",
                                    "Entity<MissingSiteMapping>(e =>", 1)
        r = analyze(self.sql, corrupted)
        self.assertEqual("FAIL_CLOSED", r["status"])
        self.assertTrue(any("DeploymentSites" in v for v in r["failures"]))

    def test_missing_unique_name_fails_closed(self):
        corrupted = self.ef.replace('HasDatabaseName("' + UNIQUE[0] + '")',
                                    'HasDatabaseName("DRIFTED")', 1)
        r = analyze(self.sql, corrupted)
        self.assertEqual("FAIL_CLOSED", r["status"])
        self.assertTrue(any(UNIQUE[0] in v for v in r["failures"]))

    def test_no_runtime_db_or_azure_migration_is_invoked_by_inventory(self):
        source = Path(__file__).with_name("package_b_schema_parity.py").read_text(encoding="utf-8")
        self.assertNotIn("SqlConnection(", source)
        self.assertNotIn("ExecuteSqlRaw", source)
        self.assertNotIn("subprocess.run", source)
        self.assertNotIn("DROP DATABASE", source)


if __name__ == "__main__":
    unittest.main()
