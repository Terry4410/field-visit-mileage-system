"""No Azure, SQL or credential access: source-closure negative tests."""
import hashlib
import importlib.util
import json
import pathlib
import re
import unittest

SCRIPT = pathlib.Path(__file__).with_name("uat010_014_readonly.py")
spec = importlib.util.spec_from_file_location("uat010_014_readonly", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
source = (module.ROOT / module.BACKFILL).read_bytes()
manifest = json.loads((module.ROOT / module.MANIFEST).read_text())


class SourcePinned014ReadOnlyTests(unittest.TestCase):
    def test_source_pin_valid_and_two_approved_only_predicates(self):
        self.assertEqual(hashlib.sha256(source).hexdigest(), manifest["sourceHashes"][module.BACKFILL])
        sql = module.build_sql(source, manifest)
        self.assertEqual(sql.count("WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1"), 2)
        self.assertIn("l.TeamId IS NOT NULL", sql)
        self.assertIn("u.OrganizationId<>l.OrganizationId", sql)
        self.assertIn("t.OrganizationId<>l.OrganizationId", sql)
        self.assertIn("l.RowVersion IS NULL", sql)

    def test_sql_is_pure_read_only_and_no_row_values(self):
        sql = module.build_sql(source, manifest)
        self.assertNotRegex(sql, r"\b(?:INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|CREATE|GRANT|REVOKE|EXEC)\b")
        self.assertIn("SELECT code,status FROM", sql)
        self.assertIn("WHEN EXISTS(", sql)
        self.assertNotIn("SELECT l.*", sql)
        self.assertNotIn("FOR JSON", sql)

    def test_requires_exact_010_predecessor_and_fixed_target_or_disposable(self):
        sql = module.build_sql(source, manifest)
        for word in ("sql-fieldvisit-jpe-uat", "db-fieldvisit-uat", "OptionCIsolated_",
                     "FieldVisit.G1.NonDeploy", "1.8.0-010", "1.8.0-013",
                     "G1_014_TARGET_STOP", "G1_014_PREDECESSOR_STOP"):
            self.assertIn(word, sql)

    def test_source_hash_changes_fail_closed(self):
        corrupted = source.replace(b"u.OrganizationId<>l.OrganizationId",
                                   b"u.OrganizationId=l.OrganizationId")
        self.assertNotEqual(corrupted, source)
        with self.assertRaises(module.SourceHold):
            module.build_sql(corrupted, manifest)

    def test_dropped_source_approval_fail_closed(self):
        changed = dict(manifest)
        changed["approval"] = "GO"
        with self.assertRaises(module.SourceHold):
            module.build_sql(source, changed)

    def test_omit_authority_check_fails_even_with_rebound_digest(self):
        corrupted = source.replace(b"l.TeamId IS NOT NULL", b"l.TeamId IS NULL")
        self.assertNotEqual(corrupted, source)
        fake = dict(manifest)
        fake["sourceHashes"] = dict(manifest["sourceHashes"])
        fake["sourceHashes"][module.BACKFILL] = hashlib.sha256(corrupted).hexdigest()
        with self.assertRaises(module.SourceHold):
            module.build_sql(corrupted, fake)

    def test_requires_each_predicate_once(self):
        sql = module.build_sql(source, manifest)
        self.assertEqual(sql.count("CASE WHEN EXISTS("), 2)
        self.assertEqual(sql.count("AS v(code,status)"), 1)


if __name__ == "__main__":
    unittest.main()
