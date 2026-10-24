import hashlib
import tempfile
import unittest
from pathlib import Path

from build_sql_only_tables import SOURCES, SourceContractError, build, extract_table


class SqlOnlyTableExtractionTests(unittest.TestCase):
    def test_balanced_nested_checks_and_quoted_parentheses(self):
        sql = """CREATE TABLE dbo.NotificationSettings (
            Id int NOT NULL,
            Name nvarchar(40) CHECK (Name IN (N'alpha ) beta', N'gamma')),
            CONSTRAINT PK_T PRIMARY KEY (Id)
        ); DECLARE @ignore int = 1;"""
        ddl = extract_table(sql, "NotificationSettings")
        self.assertIn("CHECK (Name IN", ddl)
        self.assertNotIn("DECLARE @ignore", ddl)
        self.assertTrue(ddl.endswith(";"))

    def test_missing_duplicate_or_unterminated_fail_closed(self):
        self.assertRaises(SourceContractError, extract_table, "SELECT 1", "X")
        self.assertRaises(SourceContractError, extract_table,
                          "CREATE TABLE dbo.X(Id int); CREATE TABLE dbo.X(Id int);", "X")
        self.assertRaises(SourceContractError, extract_table,
                          "CREATE TABLE dbo.X(Id int NOT NULL", "X")

    def test_source_manifest_and_hash_in_disposable_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "rc"
            out = Path(directory) / "out"
            out.mkdir()
            for path, names in SOURCES.items():
                file = root / path
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text("\n".join(
                    f"CREATE TABLE dbo.{name}(Id int NOT NULL PRIMARY KEY);"
                    for name in names
                ), encoding="utf-8")
            manifest = build(root, out)
            sql = (out / "sql-only-core-tables.sql").read_bytes()
            self.assertEqual(manifest["requiredSqlOnlyTableCount"], 7)
            self.assertEqual(manifest["sqlSha256"], hashlib.sha256(sql).hexdigest())
            self.assertEqual(sql.count(b"\nGO\n"), 7)
            self.assertTrue(manifest["fullCleanInstall"].startswith("HOLD_"))


if __name__ == "__main__":
    unittest.main()
