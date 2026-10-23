import tempfile
import unittest
from pathlib import Path

from build_effective_triggers import TriggerSourceError, dynamic_statements, build, EXPECTED


class EffectiveTriggerSourceTests(unittest.TestCase):
    def test_unescape_nested_quote_and_parse_trigger(self):
        sql = """EXEC sys.sp_executesql N'CREATE TRIGGER dbo.TR_T ON dbo.X AFTER UPDATE AS
        BEGIN SELECT N''inside quote''; END';"""
        result = list(dynamic_statements(sql))
        self.assertEqual(len(result), 1)
        self.assertIn("N'inside quote'", result[0])

    def test_malformed_dynamic_sql_fails_closed(self):
        with self.assertRaises(TriggerSourceError):
            list(dynamic_statements("EXEC sys.sp_executesql N'bad"))

    def test_missing_source_is_not_a_partial_pass(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "rc"
            out = Path(tmp) / "out"
            root.mkdir()
            out.mkdir()
            with self.assertRaises(TriggerSourceError):
                build(root, out)

    def test_scope_is_full_001_through_010(self):
        self.assertEqual(sorted(EXPECTED), [f"{i:03d}" for i in range(1, 11)])
        self.assertEqual(sum(EXPECTED.values()), 22)  # includes superseded 005 version


if __name__ == "__main__":
    unittest.main()
