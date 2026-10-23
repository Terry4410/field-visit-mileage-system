import tempfile
import unittest
from pathlib import Path
from build_optionc_final_checks import CONTRACT, COUNT, ROOT, CANDIDATE_SOURCE_IDS, build
from build_sql_only_tables import SourceContractError


class OptionCFinalCheckContractTests(unittest.TestCase):
    def test_final_names_and_supersession_are_bounded(self):
        all_items = [(table, name, mid) for table, names in CONTRACT.items()
                     for name, mid in names.items()]
        self.assertEqual(len(all_items), COUNT)
        self.assertEqual(len({x[1] for x in all_items}), COUNT)
        self.assertEqual(next(mid for _, name, mid in all_items
                              if name == "CK_B3_ChangeRequests_ReviewState"), "018")
        self.assertEqual(next(mid for _, name, mid in all_items
                              if name == "CK_B3_ChangeRequestEvents_EventType"), "018")
        self.assertEqual(next(mid for _, name, mid in all_items
                              if name == "CK_OptionC_Published_Origin"), "017")
        self.assertEqual(next(mid for _, name, mid in all_items
                              if name == "CK_ImportBatchItems_Status"), "020")

    def test_generate_source_pinned_disposable_fixtures(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "rc"
            out = Path(tmp) / "out"
            out.mkdir()
            for mid in CANDIDATE_SOURCE_IDS:
                path = root / ROOT / f"1800_{mid}" / "Up.sql"
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("\n".join(
                    f"ALTER TABLE dbo.{table} ADD CONSTRAINT {name} CHECK (Id>=0);"
                    for table, mapping in CONTRACT.items()
                    for name, version in mapping.items() if version == mid
                ), encoding="utf-8")
            manifest = build(root, out)
            self.assertEqual(manifest["checkCount"], COUNT)
            self.assertEqual((out / "optionc-final-target-checks.sql").read_text().count("\nGO\n"), COUNT)
            self.assertEqual(manifest["fullCleanInstall"].startswith("HOLD_"), True)

    def test_missing_approved_source_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "rc"
            out = Path(tmp) / "out"
            root.mkdir()
            out.mkdir()
            with self.assertRaises((SourceContractError, OSError)):
                build(root, out)


if __name__ == "__main__":
    unittest.main()
