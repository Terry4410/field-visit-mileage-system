from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SQL_PATH = ROOT / "docs/release/g1-execution/promotion/EXISTING010-FULL-SCHEMA-READONLY-ONCE.sql"


class Existing010FullSchemaCaptureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sql = SQL_PATH.read_text(encoding="utf-8")
        cls.executable = "\n".join(line for line in cls.sql.splitlines() if not line.lstrip().startswith("--"))

    def test_fixed_target_and_predecessor_guards(self):
        for token in ("EngineEdition", "sql-fieldvisit-jpe-uat", "db-fieldvisit-uat", "VIEW DEFINITION",
                      "COUNT(*) FROM sys.tables", "COUNT_BIG(*) FROM dbo.SchemaVersions",
                      "[Version]=N'1.8.0-010'", "<>63", "<>26"):
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


if __name__ == "__main__":
    unittest.main()
