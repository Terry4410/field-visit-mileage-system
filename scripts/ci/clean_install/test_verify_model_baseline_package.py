import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from verify_model_baseline_package import ContractError, EXPECTED_RC, verify


class ModelBaselinePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.rc = self.root / "rc"
        self.output = self.root / "output"
        self.context = self.rc / "backend/src/FieldVisit.Infrastructure/AppDbContext.cs"
        self.context.parent.mkdir(parents=True)
        self.context.write_bytes(b"synthetic context bytes\n")
        migration = self.rc / "database/migrations/1800_001_example/Up.sql"
        migration.parent.mkdir(parents=True)
        migration.write_text(
            "CREATE TABLE dbo.ModelTable(Id int NOT NULL);\n"
            "CREATE TABLE dbo.SqlOnlyTable(Id int NOT NULL);\n"
            "CREATE OR ALTER TRIGGER dbo.TR_Synthetic ON dbo.ModelTable AFTER INSERT AS RETURN;\n",
            encoding="utf-8",
        )
        self.output.mkdir()
        sql = self.output / "model-baseline.sql"
        sql.write_bytes(b"CREATE TABLE [dbo].[ModelTable] ([Id] int NOT NULL);\n")
        manifest = {
            "format": "field-visit-clean-install-model-baseline-v1",
            "sourceRcSha": EXPECTED_RC,
            "appDbContextSha256": hashlib.sha256(self.context.read_bytes()).hexdigest(),
            "generatedSqlSha256": hashlib.sha256(sql.read_bytes()).hexdigest(),
            "sqlServerMajorVersion": 16,
            "mappedTableCount": 1,
            "mappedTables": ["ModelTable"],
        }
        (self.output / "model-baseline-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def test_verified_model_package_reports_sql_only_hold(self):
        result = verify(EXPECTED_RC, self.rc, self.output)
        inventory = json.loads((self.output / "sql-only-object-inventory.json").read_text())
        self.assertEqual(result["fullCleanInstallStatus"], "HOLD_SQL_ONLY_DDL_AND_TRIGGER_REVIEW_REQUIRED")
        self.assertEqual(inventory["sqlOnlyTables"], [{
            "name": "SqlOnlyTable",
            "definedAt": [{
                "source": "database/migrations/1800_001_example/Up.sql",
                "migration": "1800_001_example",
                "sourceSha256": hashlib.sha256((self.rc / "database/migrations/1800_001_example/Up.sql").read_bytes()).hexdigest(),
            }],
        }])
        self.assertEqual(inventory["triggerDefinitionCount"], 1)

    def test_generated_sql_tampering_is_rejected(self):
        (self.output / "model-baseline.sql").write_bytes(b"ALTER TABLE dbo.ModelTable ADD Secret int;\n")
        with self.assertRaisesRegex(ContractError, "GENERATED_SQL_HASH_MISMATCH"):
            verify(EXPECTED_RC, self.rc, self.output)

    def test_wrong_rc_is_rejected(self):
        with self.assertRaisesRegex(ContractError, "RC_SOURCE_PIN_MISMATCH"):
            verify("0" * 40, self.rc, self.output)


if __name__ == "__main__":
    unittest.main()
