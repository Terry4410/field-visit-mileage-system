#!/usr/bin/env python3
"""Source parser adversarial checks; no network or SQL."""
import importlib.util
import unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location("ddl",Path(__file__).with_name("g1_pre010_trigger_ddl.py"))
ddl = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ddl)

class SourceDdlParserTests(unittest.TestCase):
    def test_source_pinned_dynamic_definition(self):
        rows=ddl.extract(Path("candidate"))
        self.assertEqual(len(rows),21)
        self.assertEqual(rows["TR_MileageRateRules_ProtectSeries"]["SourceMigration"],"008")
        self.assertEqual(rows["TR_Locations_SelectedGeocodingAttempt"]["SourceMigration"],"007")
        self.assertIn("N'Succeeded'",rows["TR_Locations_SelectedGeocodingAttempt"]["Ddl"])
        self.assertNotIn("N''Succeeded''",rows["TR_Locations_SelectedGeocodingAttempt"]["Ddl"])
        self.assertEqual(rows["TR_Locations_SelectedGeocodingAttempt"]["SqlServerProof"],"UNKNOWN_UNTIL_DISPOSABLE_SQL")
        self.assertEqual(len({v["Utf16LeSha256"] for v in rows.values()}),21)
    def test_quote_escaping(self):
        self.assertEqual(list(ddl.dynamic_statements("EXEC sys.sp_executesql N'CREATE TRIGGER dbo.T ON dbo.X AFTER UPDATE AS SELECT N''A'';'")),["CREATE TRIGGER dbo.T ON dbo.X AFTER UPDATE AS SELECT N'A';"])
    def test_unbalanced_source_fails_closed(self):
        with self.assertRaises(ddl.Stop):
            list(ddl.dynamic_statements("EXEC sys.sp_executesql N'abc"))
    def test_nontrigger_not_used(self):
        self.assertFalse(ddl.TRIGGER_DDL.match("SELECT 'CREATE TRIGGER dbo.FAKE';"))
        self.assertFalse(ddl.TRIGGER_DDL.match("ALTER TABLE dbo.X ADD Col int;"))
if __name__=="__main__":
    unittest.main()
