"""Offline fixed-status SSH line parsing tests, no sockets or secrets."""
import unittest
from unittest import mock

import pathb_select22_session_receipt_nondeploy as subject
from pathb_select22_receipt_bridge_nondeploy import encode_receipt, EMPTY, HOLD
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession
from p3_kudu_transport import TransportHold


class ReceiptReaderTests(unittest.TestCase):
    def setUp(self):
        self.s = Select22StageSession("instance01234567", deadline=10**12)
        self.s.state.begin()
        self.s.state.advance("CHANNEL", "STAGED")
        self.addCleanup(self.s.close)

    def test_ready_marker_must_be_exact_line(self):
        self.s.buffer = b"shell\nFV_SELECT22_READY\n"
        subject.expect_ready(self.s)
        self.assertEqual(self.s.buffer, b"")
        self.assertEqual(self.s.audit[-1], "SELECT22_READY")

    def test_remote_rejects_pre_ready_hold_before_storage_claim(self):
        # An App that fails to import or validate tools must not burn a Blob claim.
        self.s.buffer = b'FV_SELECT22_SANITIZED_V1={"status":"HOLD"}\n'
        with self.assertRaisesRegex(TransportHold, "^PROTOCOL_STATE_HOLD$"):
            subject.expect_ready(self.s)
        self.assertNotIn("SELECT22_READY", self.s.audit)

    def test_fake_ready_substring_cannot_pass(self):
        self.s.buffer = b"prefix-FV_SELECT22_READY\n"
        # The next read is simulated disconnected; no second protocol attempt.
        with self.assertRaises(TransportHold), \
             mock.patch.object(subject.select, "select", return_value=([], [], [])):
            subject.expect_ready(self.s)

    def test_receipt_exact_empty_candidate(self):
        self.s.state.advance("STAGED", "TOKENS")
        self.s.mark_sql_started()
        self.s.buffer = b"prompt\n" + encode_receipt(EMPTY)
        result = subject.read_status(self.s)
        self.assertEqual(result, EMPTY)
        self.assertEqual(self.s.audit[-1], "SELECT22_SANITIZED_RECEIPT")

    def test_receipt_hold(self):
        self.s.state.advance("STAGED", "TOKENS")
        self.s.mark_sql_started()
        self.s.buffer = encode_receipt(HOLD)
        self.assertEqual(subject.read_status(self.s), HOLD)

    def test_secret_echo_detected_before_receipt(self):
        self.s.state.advance("STAGED", "TOKENS")
        self.s.mark_sql_started()
        self.s._secrets.append(bytearray(b"PRIVATE_TOKEN"))
        self.s.buffer = b"PRIVATE_TOKEN\n" + encode_receipt(EMPTY)
        with self.assertRaisesRegex(TransportHold, "^KUDU_SECRET_ECHO$"):
            subject.read_status(self.s)

    def test_invalid_receipt_holds_even_when_status_prefix_exists(self):
        self.s.state.advance("STAGED", "TOKENS")
        self.s.mark_sql_started()
        self.s.buffer = b'FV_SELECT22_SANITIZED_V1={"status":"PASS","sid":"fake"}\n'
        with self.assertRaisesRegex(TransportHold, "^PROTOCOL_STATE_HOLD$"):
            subject.read_status(self.s)

    def test_receipt_before_sql_start_not_accepted(self):
        self.s.buffer = encode_receipt(EMPTY)
        with self.assertRaises(TransportHold):
            subject.read_status(self.s)


if __name__ == "__main__":
    unittest.main()
