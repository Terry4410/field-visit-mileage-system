import tempfile
import unittest
from pathlib import Path
from build_001_004_checks import COUNT,SOURCES,build
from build_sql_only_tables import SourceContractError


class Source001004ChecksTests(unittest.TestCase):
    def test_declared_scope_finite_and_unique(self):
        names=[name for mapping in SOURCES.values() for checks in mapping.values() for name in checks]
        self.assertEqual(len(names),COUNT)
        self.assertEqual(len(set(names)),COUNT)
        self.assertEqual(len(SOURCES),4)
        self.assertFalse(any("RouteCalculationAttempts" in n for n in names))

    def test_absent_source_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)/"rc"
            output=Path(tmp)/"out"
            root.mkdir()
            output.mkdir()
            with self.assertRaises(SourceContractError):
                build(root,output)

    def test_generates_seventeen_bounded_check_batches(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)/"rc"
            out=Path(tmp)/"out"
            out.mkdir()
            for relative, mapping in SOURCES.items():
                p=root/relative
                p.parent.mkdir(parents=True,exist_ok=True)
                p.write_text("\n".join(
                    f"ALTER TABLE dbo.{table} ADD CONSTRAINT {name} CHECK (Id >= 0);"
                    for table, checks in mapping.items() for name in checks
                ),encoding="utf-8")
            manifest=build(root,out)
            self.assertEqual(manifest["count"],17)
            self.assertEqual((out/"001_004_source_checks.sql").read_text().count("\nGO\n"),17)
            self.assertIn("HOLD_", manifest["fullCleanInstall"])


if __name__ == "__main__":
    unittest.main()
