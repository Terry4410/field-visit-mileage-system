"""Option C read-only source provenance and collision safety checks.
No SQL execution, no Azure access, no migration.
"""
from pathlib import Path
import hashlib
import unittest

ROOT = Path(__file__).resolve().parents[2]
DRAFT = ROOT / "docs/release/optionc-canonical-sql-draft"
EXPECTED = {
  "docs/release/optionc-canonical-sql-draft/1800_011/Preflight.sql": "288693164e495705125980aa68b7719f2bc18d68",
  "docs/release/optionc-canonical-sql-draft/1800_011/Up.sql": "480ceeab7b98593a67bdc9813ca057009221ce21",
  "docs/release/optionc-canonical-sql-draft/1800_011/Verify.sql": "03b2f6e2adfe76838ace3a73598aec083a20f4f6",
  "docs/release/optionc-canonical-sql-draft/1800_011/RECOVERY.md": "e48c9dfc95790c419c25351f555e127419939777",
  "docs/release/optionc-canonical-sql-draft/1800_012/Preflight.sql": "f8ec795a2557265b5a44793cf4617c5147513fee",
  "docs/release/optionc-canonical-sql-draft/1800_012/Up.sql": "0906ac48b96729d314cdd06e61fd130043ed464c",
  "docs/release/optionc-canonical-sql-draft/1800_012/Verify.sql": "237c3f5e68f79c18435c6edfc701422cb9b2ee32",
  "docs/release/optionc-canonical-sql-draft/1800_012/RECOVERY.md": "4328f06519c25b1c44818380c33afc7d2f372d11"
}

class OptionCCanonicalSqlDraftTests(unittest.TestCase):
    def test_canonical_source_files_are_byte_identical(self):
        for path, expected in EXPECTED.items():
            data = (ROOT / path).read_bytes()
            blob = b"blob " + str(len(data)).encode() + bytes([0]) + data
            self.assertEqual(hashlib.sha1(blob).hexdigest(), expected, path)

    def test_drafts_are_outside_executable_migration_roots(self):
        self.assertFalse((ROOT / "database/migrations/1800_011_b3_change_requests_candidate").exists())
        self.assertFalse((ROOT / "database/migrations/1800_012_b3_atomic_apply_candidate").exists())
        self.assertFalse((ROOT / "database/candidates/1800_011_b3_change_requests_candidate").exists())
        self.assertFalse((ROOT / "database/candidates/1800_012_b3_atomic_apply_candidate").exists())
        self.assertFalse((ROOT / "database/migrations/1800_011_uat010_location_change_governance").exists())

    def test_one_request_store_and_forward_only_contract(self):
        a = (DRAFT / "1800_011/Up.sql").read_text()
        b = (DRAFT / "1800_012/Up.sql").read_text()
        self.assertIn("CREATE TABLE dbo.ChangeRequests", a)
        self.assertIn("CREATE TABLE dbo.ChangeRequestEvents", a)
        self.assertIn("UX_B3_ChangeRequests_Org_Entity_Pending", a)
        self.assertIn("UX_B3_ChangeRequestEvents_DecisionKey", a)
        self.assertNotIn("LocationChangeRequests", a+b)
        self.assertIn("N'Applied'", b)
        self.assertIn("N'Approved'", b)
        self.assertIn("BEGIN TRANSACTION", a)
        self.assertIn("BEGIN TRANSACTION", b)
        self.assertNotIn("DROP TABLE", b.upper())

    def test_optionc_014_stage_and_ledger_are_nonexecutable_and_preserve_013(self):
        root = DRAFT / "1800_014"
        up = (root / "Up.sql").read_text()
        preflight = (root / "Preflight.sql").read_text()
        verify = (root / "Verify.sql").read_text()
        recovery = (root / "RECOVERY.md").read_text()
        for text in (up, preflight, verify):
            self.assertIn("OptionCIsolated[_]%", text)
        self.assertIn("LocationPublicationStages", up)
        self.assertIn("LocationPublishedVersions", up)
        self.assertIn("UX_OptionC_Published_Current", up)
        self.assertIn("UX_OptionC_Published_Request", up)
        self.assertIn("1.8.0-012", preflight + up)
        self.assertIn("013", recovery)
        self.assertNotIn("LocationChangeRequests", up + preflight + verify)
        self.assertFalse((ROOT / "database/migrations/1800_014").exists())

if __name__ == "__main__":
    unittest.main()
