"""End-to-end in-memory SELECT22 one-use protocol tests, no external I/O."""
import unittest
import pathb_select22_owner_flow_simulation_nondeploy as f


class SyntheticFlowTests(unittest.TestCase):
    def test_claim_blob_readback_before_synthetic_token(self):
        r = f.simulate("success_empty")
        self.assertEqual(r["outcome"], "SYNTHETIC_PASS")
        self.assertEqual(r["events"], (
            "SYNTHETIC_READY", "PUT", "GET",
            "SYNTHETIC_SQL_TOKEN", "SYNTHETIC_SELECT_ONLY",
            "SYNTHETIC_RECEIPT"))
        self.assertEqual(r["blob"], "NOT_EXECUTED")
        self.assertEqual(r["sql"], "NOT_EXECUTED")
        self.assertEqual(r["live"], "HOLD")

    def test_ambiguous_put_never_requests_sql_token(self):
        r = f.simulate("put_timeout")
        self.assertEqual(r["outcome"], "SYNTHETIC_HOLD")
        self.assertEqual(r["events"], ("SYNTHETIC_READY", "PUT"))

    def test_bad_readback_no_sql_token_or_retry(self):
        r = f.simulate("readback_mismatch")
        self.assertEqual(r["outcome"], "SYNTHETIC_HOLD")
        self.assertEqual(r["events"], ("SYNTHETIC_READY", "PUT", "GET"))

    def test_broken_nonce_never_reaches_select(self):
        r = f.simulate("bad_nonce")
        self.assertEqual(r["outcome"], "SYNTHETIC_HOLD")
        self.assertNotIn("SYNTHETIC_SELECT_ONLY", r["events"])

    def test_hold_receipt_never_becomes_success(self):
        r = f.simulate("receiver_hold")
        self.assertEqual(r["outcome"], "SYNTHETIC_HOLD")
        self.assertEqual(r["events"].count("SYNTHETIC_SQL_TOKEN"), 1)
        self.assertEqual(r["events"].count("SYNTHETIC_SELECT_ONLY"), 1)

    def test_invalid_scenario_rejects_before_use(self):
        with self.assertRaises(f.SimulationHold):
            f.simulate("real_storage")

    def test_caller_cannot_release_live(self):
        with self.assertRaisesRegex(f.SimulationHold, "SIMULATOR_CANNOT_RELEASE_LIVE"):
            f.release_live(owner_approved=True, storage_ready=True,
                           sql_token="not-allowed")


if __name__ == "__main__":
    unittest.main()
