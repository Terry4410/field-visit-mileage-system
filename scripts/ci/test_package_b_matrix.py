import unittest

from package_b_matrix import validate_matrix


def valid_matrix():
    lines = [
        "# Package B runtime validation checklist",
        "## Required B4 negative cases (NOT YET PASSED in SQL Server runtime)",
        "UAT and Production remain HOLD",
    ]
    for i in range(1, 23):
        lines.append(f"| B4-SQL-{i:02d} | Negative situation with access validation {i} "
                     f"| Expected DENY with no modification {i} |")
    lines.append("NONEXECUTABLE REVIEW MATRIX")
    return "\n".join(lines)


class PackageBMatrixTests(unittest.TestCase):
    def test_all_22_documented_is_not_sql_server_pass(self):
        result = validate_matrix(valid_matrix())
        self.assertEqual(22, result["present"])
        self.assertEqual("DOCUMENTED_ONLY_SQL_RUNTIME_NOT_TESTED", result["status"])

    def test_missing_case_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "IDs mismatch"):
            validate_matrix(valid_matrix().replace("| B4-SQL-17 |", "| OMITTED-17 |"))

    def test_duplicate_case_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "duplicate"):
            validate_matrix(valid_matrix().replace(
                "| B4-SQL-02 |", "| B4-SQL-01 |"))

    def test_claimed_pass_without_runtime_disclaimer_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "runtime completion"):
            validate_matrix(valid_matrix().replace(
                "NOT YET PASSED in SQL Server runtime", "SQL Server PASS"))

    def test_missing_release_hard_hold_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "HOLD"):
            validate_matrix(valid_matrix().replace(
                "UAT and Production remain HOLD", "UAT Production GO"))

    def test_missing_nonexecution_disclaimer_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "non-execution"):
            validate_matrix(valid_matrix().replace(
                "NONEXECUTABLE REVIEW MATRIX", "Executed and approved"))


if __name__ == "__main__":
    unittest.main()
