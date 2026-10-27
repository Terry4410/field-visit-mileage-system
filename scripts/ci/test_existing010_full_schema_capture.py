from __future__ import annotations

import re
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SQL_PATH = ROOT / "docs/release/g1-execution/promotion/EXISTING010-FULL-SCHEMA-READONLY-ONCE.sql"
COMPARATOR_PATH = ROOT / "scripts/ci/compare_uat010_schema_inventory.py"


def load_comparator():
    spec = importlib.util.spec_from_file_location("existing010_schema_compare", COMPARATOR_PATH)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def raw_capture_fixture():
    comparator = load_comparator()
    rows = [
        {field: None for field in fields}
        for fields in comparator.RAW_SET_FIELDS
    ]
    rows[0].update({
        "ServerName": "sql-fieldvisit-jpe-uat", "DatabaseName": "db-fieldvisit-uat",
        "EvidenceMode": "METADATA_ONLY_NO_BUSINESS_ROW_VALUES", "DboTableCount": 63,
        "SchemaVersionCount": 26, "LatestVersion": "1.8.0-010",
    })
    rows[1].update({"SchemaName": "dbo", "TableName": "Organizations", "TemporalType": "NON_TEMPORAL_TABLE",
                    "IsMemoryOptimized": False, "IsFileTable": False, "LockEscalation": "TABLE"})
    rows[2].update({"SchemaName": "dbo", "TableName": "Organizations", "Ordinal": 1, "ColumnName": "OrganizationId",
                    "TypeSchema": "sys", "SqlType": "int", "MaxLengthBytes": 4, "NumericPrecision": 10,
                    "NumericScale": 0, "IsNullable": False, "IsIdentity": True, "IdentitySeed": 1,
                    "IdentityIncrement": 1, "IsComputed": False, "ComputedDefinition": None, "IsPersisted": False,
                    "IsRowGuid": False, "IsSparse": False, "IsColumnSet": False, "GeneratedAlwaysType": "NOT_APPLICABLE",
                    "CollationName": None, "DefaultDefinition": None})
    rows[3].update({"SchemaName": "dbo", "TableName": "Organizations", "IndexName": "PK_Organizations",
                    "IndexType": "CLUSTERED", "IsPrimaryKey": True, "IsUniqueConstraint": False, "IsUnique": True,
                    "IsDisabled": False, "IsHypothetical": False, "IgnoreDuplicateKey": False, "HasFilter": False,
                    "FilterDefinition": None, "KeyOrdinal": 1, "IsIncluded": False, "ColumnName": "OrganizationId",
                    "IsDescending": False})
    rows[4].update({"SchemaName": "dbo", "TableName": "Teams", "ForeignKeyName": "FK_Teams_Organizations",
                    "ParentColumn": "OrganizationId", "ReferencedSchema": "dbo", "ReferencedTable": "Organizations",
                    "ReferencedColumn": "OrganizationId", "ColumnOrdinal": 1, "DeleteAction": "NO_ACTION",
                    "UpdateAction": "NO_ACTION", "IsDisabled": False, "IsNotTrusted": False, "IsNotForReplication": False})
    rows[5].update({"SchemaName": "dbo", "TableName": "Teams", "CheckConstraintName": "CK_Teams_EffectiveDates",
                    "Definition": "([EffectiveTo] IS NULL OR\n ([EffectiveFrom] IS NULL OR [EffectiveTo]>=[EffectiveFrom]))",
                    "IsDisabled": False, "IsNotTrusted": False, "IsNotForReplication": False})
    rows[6].update({"SchemaName": "dbo", "TableName": "TeamCenterAssignments", "TriggerName": "TR_TeamCenterAssignments_NoOverlap",
                    "IsDisabled": False, "IsInsteadOfTrigger": False, "IsNotForReplication": False,
                    "IsMicrosoftShipped": False, "CreatedAt": "2026-01-01T00:00:00", "ModifiedAt": "2026-01-01T00:00:00",
                    "EventType": "INSERT", "UsesAnsiNulls": True, "UsesQuotedIdentifier": True,
                    "Definition": "CREATE TRIGGER ...\nAS\nBEGIN\n  SELECT 1;\nEND"})
    result_sets = [{"rows": [row]} for row in rows]
    result_sets[1]["rows"] = [rows[1] | {"TableName": f"T{i:02}"} for i in range(63)]
    result_sets[2]["rows"] = [rows[2]]
    return {
        "format": comparator.RAW_CAPTURE_FORMAT, "status": "COMPLETE",
        "sourceCommit": "723868922d229d557130b3c8b2aad35acbc1803a",
        "sourceQuerySha256": comparator.RAW_SQL_SHA256, "capturedUtc": "2026-10-26T03:37:38Z",
        "connectionAttempts": 1, "queryExecutedOnce": True, "queryErrorNumber": None,
        "resultSets": result_sets,
    }


class Existing010FullSchemaCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sql = SQL_PATH.read_text(encoding="utf-8")
        cls.executable = "\n".join(line for line in cls.sql.splitlines() if not line.lstrip().startswith("--"))

    def test_fixed_target_and_predecessor_guards(self):
        for token in ("EngineEdition", "sql-fieldvisit-jpe-uat", "db-fieldvisit-uat", "VIEW DEFINITION",
                      "COUNT(*) FROM sys.tables", "COUNT_BIG(*) FROM dbo.SchemaVersions",
                      "VersionNumber=N'1.8.0-010'", "<>63", "<>26"):
            self.assertIn(token, self.executable)

    def test_full_schema_facets(self):
        for token in ("sys.tables", "sys.schemas", "sys.columns", "sys.types", "sys.identity_columns",
                      "sys.computed_columns", "sys.default_constraints", "collation_name", "is_sparse",
                      "generated_always_type_desc", "sys.indexes", "sys.index_columns", "is_primary_key",
                      "is_unique_constraint", "filter_definition", "sys.foreign_keys", "sys.foreign_key_columns",
                      "is_not_trusted", "delete_referential_action_desc", "sys.check_constraints",
                      "sys.triggers", "sys.trigger_events", "sys.sql_modules", "m.definition"):
            self.assertIn(token, self.executable)

    def test_no_dml_ddl_execution_or_dynamic_sql(self):
        forbidden = re.compile(r"(?im)^\s*(?:INSERT|UPDATE|DELETE|MERGE|GRANT|REVOKE|DENY|CREATE|ALTER|DROP|TRUNCATE|EXEC(?:UTE)?|DBCC)\b")
        self.assertIsNone(forbidden.search(self.executable))
        self.assertNotRegex(self.executable, r"(?i)sp_executesql|EXEC\s*\(")

    def test_metadata_only_output(self):
        self.assertIn("METADATA_ONLY_NO_BUSINESS_ROW_VALUES", self.executable)
        self.assertNotIn("SELECT *", self.executable.upper())
        self.assertNotIn("PASSWORD", self.executable.upper())
        self.assertNotIn("CONNECTION STRING", self.executable.upper())

    def test_exact_seven_result_sets_normalize_to_complete_v2_contract(self):
        comparator = load_comparator()
        normalized = comparator.normalize_raw_capture(raw_capture_fixture())
        self.assertEqual(normalized["ContractVersion"], "uat-schema-fidelity-v2")
        self.assertEqual(list(normalized["Tables"]), raw_capture_fixture()["resultSets"][1]["rows"])
        self.assertEqual(len(normalized["Tables"]), 63)
        self.assertIn("TableName", normalized["Tables"][0])
        self.assertIn("Triggers", normalized)
        self.assertEqual(
            normalized["Triggers"][0]["Definition"],
            "CREATE TRIGGER ...\nAS\nBEGIN\n  SELECT 1;\nEND",
        )

    def test_v2_comparison_includes_table_inventory_and_exact_multiline_trigger(self):
        comparator = load_comparator()
        expected = comparator.normalize_raw_capture(raw_capture_fixture())
        actual = json.loads(json.dumps(expected))
        self.assertEqual(comparator.compare(expected, actual), [])
        actual["Tables"].pop()
        actual["Triggers"][0]["Definition"] = actual["Triggers"][0]["Definition"].replace("  SELECT", " SELECT")
        differences = comparator.compare(expected, actual)
        self.assertTrue(any(item.startswith("Tables: missing") for item in differences))
        self.assertTrue(any(item.startswith("Triggers: facet drift") and "Definition" in item for item in differences))

    def test_raw_capture_rejects_wrong_query_retry_and_incomplete_sets(self):
        comparator = load_comparator()
        capture = raw_capture_fixture()
        bad = json.loads(json.dumps(capture)); bad["sourceQuerySha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "query SHA-256"):
            comparator.normalize_raw_capture(bad)
        bad = json.loads(json.dumps(capture)); bad["queryExecutedOnce"] = False
        with self.assertRaisesRegex(ValueError, "one successful query"):
            comparator.normalize_raw_capture(bad)
        bad = json.loads(json.dumps(capture)); bad["resultSets"].pop()
        with self.assertRaisesRegex(ValueError, "exactly seven"):
            comparator.normalize_raw_capture(bad)

    def test_raw_capture_can_be_loaded_directly_and_saved_as_normalized_contract(self):
        comparator = load_comparator()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "live-capture.json"
            path.write_text(json.dumps(raw_capture_fixture()), encoding="utf-8")
            loaded = comparator.load_inventory(path)
            self.assertEqual(loaded["ContractVersion"], "uat-schema-fidelity-v2")


if __name__ == "__main__":
    unittest.main()
