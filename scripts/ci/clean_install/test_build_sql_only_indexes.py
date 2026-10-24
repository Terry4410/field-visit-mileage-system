import tempfile
import unittest
from pathlib import Path

from build_sql_only_indexes import INDEX_SOURCES, extract_index, build
from build_sql_only_tables import SourceContractError


class SqlOnlyIndexTests(unittest.TestCase):
    def test_unique_filtered_index_and_boundary(self):
        s = """CREATE UNIQUE INDEX IX_A ON dbo.MailOutbox(Id) WHERE Id IS NOT NULL;
        INSERT dbo.Fake VALUES(1);"""
        self.assertEqual(
            extract_index(s, "IX_A", "MailOutbox"),
            "CREATE UNIQUE INDEX IX_A ON dbo.MailOutbox(Id) WHERE Id IS NOT NULL;",
        )
        with self.assertRaises(SourceContractError):
            extract_index(s, "IX_A", "Other")

    def test_reject_nonterminated_source(self):
        with self.assertRaises(SourceContractError):
            extract_index("CREATE INDEX IX_A ON dbo.MailOutbox(Id)", "IX_A", "MailOutbox")

    def test_generated_manifest_is_bounded(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "rc"
            out = Path(tmp) / "out"
            out.mkdir()
            for relative, indexes in INDEX_SOURCES.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("\n".join(
                    f"CREATE INDEX {name} ON dbo.{table}(Id);"
                    for name, table in indexes.items()), encoding="utf-8")
            m = build(root, out)
            self.assertEqual(m["indexCount"], 13)
            self.assertEqual((out / "sql-only-extra-indexes.sql").read_text().count("\nGO\n"), 13)
            self.assertIn("HOLD_", m["fullCleanInstall"])


if __name__ == "__main__":
    unittest.main()
