"""Offline-only SELECT22 two-sided secret isolation and schema envelope tests."""
import unittest
from unittest import mock

import pathb_select22_receipt_bridge_nondeploy as bridge
from pathb_readonly_stdin_frame_20261113 import encode_frame
from pathb_readonly_receipt_parser import Receipt


TOKEN = "a" * 64 + "." + "b" * 64 + "." + "c" * 64
NONCE = "0123456789abcdef0123456789abcdef"


class BridgeTests(unittest.TestCase):
    def frame(self):
        return encode_frame(TOKEN, NONCE)

    def test_empty_schema_only_status_allowed(self):
        with mock.patch.object(bridge, "run_once", return_value=Receipt(
            "PASS", "PASS", "EMPTY_CANDIDATE",
            "SEPARATE_INSTALL_AUTH_REQUIRED")) as runner:
            wire = bridge.app_receive_once(self.frame(), NONCE,
                debs_dir="/invalid", claim_dir="/invalid")
        self.assertEqual(bridge.parse_receipt(wire), bridge.EMPTY)
        self.assertEqual(runner.call_count, 1)
        self.assertEqual(runner.call_args.kwargs["sql_token"], TOKEN)
        self.assertNotIn(TOKEN.encode(), wire)
        self.assertNotIn(b"SID", wire)

    def test_nonempty_schema_is_hold_for_install(self):
        with mock.patch.object(bridge, "run_once", return_value=Receipt(
            "PASS", "PASS", "NONEMPTY_OR_PARTIAL",
            "HOLD_MANUAL_RECONCILIATION")):
            wire = bridge.app_receive_once(self.frame(), NONCE,
                debs_dir="/invalid", claim_dir="/invalid")
        self.assertEqual(bridge.parse_receipt(wire), bridge.NONEMPTY)

    def test_error_cannot_disclose_token_or_sql(self):
        with mock.patch.object(bridge, "run_once",
                               side_effect=RuntimeError(TOKEN + " SID secret")):
            wire = bridge.app_receive_once(self.frame(), NONCE,
                debs_dir="/invalid", claim_dir="/invalid")
        self.assertEqual(bridge.parse_receipt(wire), bridge.HOLD)
        self.assertNotIn(TOKEN.encode(), wire)
        self.assertNotIn(b"secret", wire)

    def test_replay_nonce_is_refused_before_sql(self):
        with mock.patch.object(bridge, "run_once") as runner:
            wire = bridge.app_receive_once(self.frame(), "f" * 32,
                debs_dir="/invalid", claim_dir="/invalid")
        self.assertEqual(bridge.parse_receipt(wire), bridge.HOLD)
        runner.assert_not_called()

    def test_bad_frame_rejected(self):
        for frame in (b"", b"bad token\n", self.frame()[:-1],
                      self.frame() + b"another-frame\n"):
            with self.subTest(frame=frame[:10]), mock.patch.object(
                    bridge, "run_once") as runner:
                wire = bridge.app_receive_once(frame, NONCE,
                    debs_dir="/invalid", claim_dir="/invalid")
                self.assertEqual(bridge.parse_receipt(wire), bridge.HOLD)
                runner.assert_not_called()

    def test_reject_extra_fields_or_raw_identity(self):
        bad = (b'FV_SELECT22_SANITIZED_V1={"status":"PASS","sid":"sensitive"}\n')
        with self.assertRaises(bridge.ReceiptBridgeHold):
            bridge.parse_receipt(bad)

    def test_reject_duplicate_json_fields(self):
        with self.assertRaises(bridge.ReceiptBridgeHold):
            bridge.parse_receipt(
                b'FV_SELECT22_SANITIZED_V1={"status":"HOLD","status":"HOLD"}\n')

    def test_reject_multiple_lines(self):
        with self.assertRaises(bridge.ReceiptBridgeHold):
            bridge.parse_receipt(bridge.encode_receipt(bridge.HOLD) + b"extra\n")

    def test_reject_invalid_status_combination(self):
        payload = dict(bridge.EMPTY)
        payload["next_action"] = "INSTALL_NOW"
        with self.assertRaises(bridge.ReceiptBridgeHold):
            bridge.encode_receipt(payload)

    def test_receipt_canonical_round_trip(self):
        for value in (bridge.EMPTY, bridge.NONEMPTY, bridge.HOLD):
            self.assertEqual(bridge.parse_receipt(bridge.encode_receipt(value)), value)


if __name__ == "__main__":
    unittest.main()
