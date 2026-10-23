import tempfile
import unittest
from pathlib import Path
from build_007_checks import (TABLES, EXPECTED_COUNT, SourceContractError,
                              extract_balanced_check, build)


class Source007CheckTests(unittest.TestCase):
    def test_nested_quotes_preserve_inner_expression(self):
        code = """CONSTRAINT CK_Example CHECK ((Reason IN (N'Accepted',N'Bad ) test'))
              OR (Reason=N'Double''Quote')),"""
        self.assertEqual(
            extract_balanced_check(code, "CK_Example"),
            """CONSTRAINT CK_Example CHECK ((Reason IN (N'Accepted',N'Bad ) test'))
              OR (Reason=N'Double''Quote'))"""
        )

    def test_malformed_or_duplicate_does_not_generate_sql(self):
        for sql in ("CONSTRAINT CK_X CHECK (Status IN ('A')",
                    "CONSTRAINT CK_X CHECK (Status=1); CONSTRAINT CK_X CHECK (Status=2)"):
            with self.assertRaises(SourceContractError):
                extract_balanced_check(sql, "CK_X")

    def test_contract_excludes_superseded_009_010(self):
        names=[name for rows in TABLES.values() for name in rows]
        self.assertEqual(len(names),EXPECTED_COUNT)
        self.assertEqual(len(set(names)), EXPECTED_COUNT)
        self.assertNotIn("CK_RouteCalculationAttempts_StopCount", names)
        self.assertNotIn("CK_RouteCalculationAttempts_Reason", names)
        self.assertFalse(any(n.startswith("CK_RouteCalculationAttempts_") for n in names))

    def test_missing_real_migration_is_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)/"source"
            output=Path(tmp)/"artifact"
            root.mkdir()
            output.mkdir()
            with self.assertRaises(SourceContractError):
                build(root,output)


if __name__ == "__main__":
    unittest.main()
