#!/usr/bin/env python3
"""Source-only negative and phase-separation checks; no Azure/SQL connections."""
import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("g1_pre010_source_contract.py")
spec = importlib.util.spec_from_file_location("pre010_source", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class Pre010SourceTests(unittest.TestCase):
    def setUp(self):
        self.root = Path("candidate")

    def test_fixed_source_and_all_effective_triggers(self):
        result = mod.derive(self.root)
        self.assertEqual(result["ContractVersion"], "g1-source-derived-pre010-scope-v1")
        self.assertEqual(len(result["Triggers"]), 21)
        self.assertEqual(result["ReleaseGate"], "HOLD")
        self.assertEqual(result["ActualUat010"], "UNKNOWN")
        self.assertTrue(all(t["DefinitionSha256"] is None for t in result["Triggers"]))
        self.assertEqual(
            next(t for t in result["Triggers"] if t["Name"] == "TR_MileageRateRules_ProtectSeries")["SourceMigration"], "008"
        )
        self.assertEqual([v["Name"] for v in result["Checks"]], ["CK_RouteCalculationAttempts_Reason"])
        self.assertEqual(result["Post010ChecksExcluded"],
                         ["CK_ImportBatchItems_Status", "CK_ImportBatches_Status"])

    def test_failed_trigger_parse_fails_closed(self):
        with self.assertRaises(mod.ContractStop):
            mod.parse_triggers(b"CREATE TRIGGER dbo.TR_X ON dbo.X AFTER INSERT AS SELECT 1;", "004")
        with self.assertRaises(mod.ContractStop):
            mod.parse_triggers(b"CREATE TRIGGER dbo.TR_X ON dbo.X INSTEAD OF INSERT AS SELECT 1;", "004")

    def test_no_raw_sql_or_schema_claim_from_source_only(self):
        result = mod.derive(self.root)
        self.assertEqual(result["Base15ColumnFacetParity"], "UNKNOWN")
        self.assertEqual(result["HistoryPreservation"], "UNKNOWN")
        self.assertEqual(result["RuntimeApiPrincipal"], "UNKNOWN")
        for t in result["Triggers"]:
            self.assertEqual(t["DefinitionProof"], "UNKNOWN_PENDING_DISPOSABLE_SQL_SERVER")
            self.assertEqual(set(t["Events"].split(",")) &
                             {"INSERT", "UPDATE", "DELETE"}, set(t["Events"].split(",")))

    def test_no_unpinned_reference_accepted(self):
        with self.assertRaises(Exception):
            mod.derive(Path("/path-not-present-in-ci"))

if __name__ == "__main__":
    unittest.main()
