import tempfile
import unittest
from pathlib import Path

from build_optionc_sql_only_tables import CANDIDATES, build
from build_sql_only_tables import SourceContractError


class OptionCSqlOnlyTableContractTests(unittest.TestCase):
    def test_disposable_source_only_fixture(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "rc"
            output = Path(tmp) / "out"
            output.mkdir()
            for rel, tables in CANDIDATES.items():
                path = root / rel
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "/* NONEXECUTABLE DESIGN */\n" +
                    "\n".join(f"CREATE TABLE dbo.{n}(Id int PRIMARY KEY);" for n in tables),
                    encoding="utf-8")
            manifest = build(root, output)
            self.assertEqual(manifest["disposableCandidateTableCount"], 4)
            self.assertEqual((output / "optionc-sql-only-candidates.sql").read_text().count("\nGO\n"), 4)
            self.assertIn("HOLD_", manifest["fullCleanInstall"])

    def test_candidate_without_disposable_status_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "rc"
            output = Path(tmp) / "out"
            output.mkdir()
            for rel, tables in CANDIDATES.items():
                path = root / rel
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("\n".join(
                    f"CREATE TABLE dbo.{n}(Id int PRIMARY KEY);" for n in tables),
                    encoding="utf-8")
            with self.assertRaises(SourceContractError):
                build(root, output)


if __name__ == "__main__":
    unittest.main()
