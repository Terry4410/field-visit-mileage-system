from __future__ import annotations

import hashlib
import importlib.util
import json
import re
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SQL = ROOT / "docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql"
WORKFLOW = ROOT / ".github/workflows/azure-sql-uat-schema-fidelity-readonly.yml"
VALIDATOR = ROOT / "scripts/ci/uat_schema_fidelity_evidence.ps1"
COMPARATOR = ROOT / "scripts/ci/compare_uat010_schema_inventory.py"
SQL_SHA = "e83552d4b1472e677f61a5e44973b80321d2874322f4a5954c070b8f3fd2c232"
VALIDATOR_SHA = "f2953d17ea364e1191a5cddcbcf797251f9e97b109007aa89607f109875b3b15"


def load_comparator():
    spec = importlib.util.spec_from_file_location("schema_compare", COMPARATOR)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sample_inventory() -> dict:
    return {
        "ContractVersion": "uat-schema-fidelity-v1",
        "SourceSha": "a" * 40,
        "SqlSha256": SQL_SHA,
        "Target": {"ServerName": "sql-fieldvisit-jpe-uat", "DatabaseName": "db-fieldvisit-uat"},
        "SchemaVersion": {"Count": 10, "Latest": "1.8.0-010"},
        "Columns": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts", "Ordinal": 1,
            "ColumnName": "RouteCalculationAttemptId", "SqlType": "bigint",
            "MaxLengthBytesOrCharacters": 8, "NumericPrecision": 19, "NumericScale": 0,
            "IsNullable": False, "IsIdentity": True, "IsComputed": False,
            "IsRowVersion": False, "DefaultDefinition": None,
        }],
        "Indexes": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts", "IndexName": "PK_RouteCalculationAttempts",
            "IndexType": "CLUSTERED", "IsPrimaryKey": True, "IsUniqueConstraint": False,
            "IsUnique": True, "IsDisabled": False, "HasFilter": False, "FilterDefinition": None,
            "KeyOrdinal": 1, "IsIncluded": False, "ColumnName": "RouteCalculationAttemptId", "IsDescending": False,
        }],
        "ForeignKeys": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts", "ForeignKeyName": "FK_RouteCalculationAttempts_VisitTrips",
            "ParentColumn": "VisitTripId", "ReferencedSchema": "dbo", "ReferencedTable": "VisitTrips",
            "ReferencedColumn": "VisitTripId", "ColumnOrdinal": 1, "DeleteAction": "NO_ACTION",
            "UpdateAction": "NO_ACTION", "IsDisabled": False, "IsNotTrusted": False,
        }],
        "Checks": [{
            "SchemaName": "dbo", "TableName": "RouteCalculationAttempts",
            "CheckConstraintName": "CK_RouteCalculationAttempts_Reason",
            "Definition": "CalculationReason IN ('VisitorCalculate','LeaderRetry','CorrectionRecalculate','BackgroundMileageJob')",
            "IsDisabled": False, "IsNotTrusted": False,
        }],
        "HistoricalAggregates": [],
        "CapturedUtc": "2026-10-16T00:00:00Z",
    }


class SchemaFidelityProposalTests(unittest.TestCase):
    def test_sql_is_hash_pinned_exact_target_and_select_only(self):
        source = SQL.read_bytes()
        self.assertEqual(hashlib.sha256(source).hexdigest(), SQL_SHA)
        executable = re.sub(rb"/\*.*?\*/|--[^\n]*", b"", source, flags=re.S).upper()
        for token in (
            b" INSERT ", b" UPDATE ", b" DELETE ", b" MERGE ", b" CREATE ", b" ALTER ",
            b" DROP ", b" TRUNCATE ", b" EXEC ", b" EXECUTE ", b" RESTORE ", b" BACKUP ",
            b" OPENROWSET ", b" OPENDATASOURCE ", b" SELECT *",
        ):
            self.assertNotIn(token, b" " + executable + b" ")
        for required in (b"SYS.COLUMNS", b"SYS.INDEXES", b"SYS.FOREIGN_KEYS", b"SYS.CHECK_CONSTRAINTS", b"IS_NOT_TRUSTED"):
            self.assertIn(required, executable)
        for sensitive in (b"ORIGINAL_LOGIN", b"SUSER_SNAME", b"SYSTEM_USER", b"CURRENT_USER", b"APPLIEDBY"):
            self.assertNotIn(sensitive, executable)

    def test_workflow_is_fixed_target_hash_pinned_and_fail_closed(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("refs/heads/post-uat/v1.8.0", workflow)
        self.assertIn("ref: ${{ github.sha }}", workflow)
        self.assertIn("test \"$(git rev-parse HEAD)\" = \"${GITHUB_SHA}\"", workflow)
        self.assertIn("ApplicationIntent ReadOnly", workflow)
        self.assertIn("environment: uat", workflow)
        self.assertIn("id-token: write", workflow)
        self.assertIn(SQL_SHA, workflow)
        self.assertIn(VALIDATOR_SHA, workflow)
        self.assertNotRegex(workflow, r"workflow_dispatch:\s*\n\s+inputs:")
        self.assertNotIn("database/migrations/", workflow)
        self.assertNotIn("dotnet ef database update", workflow.lower())

    def test_validator_is_hash_pinned_and_exact_shape(self):
        source = VALIDATOR.read_bytes()
        self.assertEqual(hashlib.sha256(source).hexdigest(), VALIDATOR_SHA)
        text = source.decode("utf-8")
        self.assertIn("$tables.Count -ne 7", text)
        self.assertIn("result columns were missing, reordered or unexpected", text)
        self.assertIn("1.8.0-010", text)
        self.assertIn("METADATA_ONLY_NO_ROW_VALUES", text)
        self.assertIn("raw SQL", WORKFLOW.read_text(encoding="utf-8"))

    def test_comparator_detects_full_facet_and_010_drift(self):
        comparator = load_comparator()
        expected = sample_inventory()
        actual = json.loads(json.dumps(expected))
        self.assertEqual(comparator.assert_010_contract(actual), [])
        self.assertEqual(comparator.compare(expected, actual), [])
        actual["Columns"][0]["IsNullable"] = True
        drift = comparator.compare(expected, actual)
        self.assertEqual(len(drift), 1)
        self.assertIn("IsNullable", drift[0])
        actual = json.loads(json.dumps(expected))
        actual["Checks"][0]["IsNotTrusted"] = True
        self.assertIn("010: reason constraint is not trusted", comparator.assert_010_contract(actual))

    def test_comparator_requires_real_reference_and_actual_files(self):
        comparator = load_comparator()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "inventory.json"
            path.write_text(json.dumps(sample_inventory()), encoding="utf-8")
            loaded = comparator.load_inventory(path)
            self.assertEqual(loaded["SchemaVersion"]["Latest"], "1.8.0-010")


if __name__ == "__main__":
    unittest.main(verbosity=2)
