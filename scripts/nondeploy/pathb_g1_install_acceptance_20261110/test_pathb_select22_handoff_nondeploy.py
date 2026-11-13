"""Offline-only SELECT22 transport composition: one stage, one token, one receipt."""
import unittest
from unittest import mock

import pathb_select22_handoff_nondeploy as flow
from pathb_select22_receipt_bridge_nondeploy import EMPTY
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession

TOKEN = "a" * 60 + "." + "b" * 60 + "." + "c" * 60
NONCE = "0123456789abcdef0123456789abcdef"
SHA = "b" * 64


class HandoffTests(unittest.TestCase):
    def setUp(self):
        self.session = Select22StageSession("instance01234567", deadline=10**12)
        self.session.state.begin()
        self.calls = []
        self.addCleanup(self.session.close)

    def stage(self, raw, sha):
        self.calls.append("stage")
        self.assertEqual(raw, b"synthetic ZIP, no Azure")
        self.assertEqual(sha, SHA)
        self.session.state.advance("CHANNEL", "STAGED")

    def send(self, token, nonce):
        self.calls.append("token")
        self.assertEqual((token, nonce), (TOKEN, NONCE))
        self.session.state.advance("STAGED", "TOKENS")

    def test_exact_one_handoff_and_one_status(self):
        with mock.patch.object(self.session, "stage_select22_archive",
                               side_effect=self.stage), \
             mock.patch.object(self.session, "send_public",
                               side_effect=lambda *a: self.calls.append("remote")), \
             mock.patch.object(self.session, "send_select22_frame_once",
                               side_effect=self.send), \
             mock.patch.object(flow, "expect_ready",
                               side_effect=lambda s: self.calls.append("ready")), \
             mock.patch.object(flow, "read_status",
                               side_effect=lambda s: self.calls.append("receipt") or EMPTY):
            result = flow.handoff_once(
                self.session, stage_bytes=b"synthetic ZIP, no Azure",
                stage_sha=SHA, nonce=NONCE, sql_token=TOKEN)
            self.assertEqual(result, EMPTY)
            self.assertEqual(self.calls,
                             ["stage", "remote", "ready", "token", "receipt"])
            self.assertEqual(self.session.state.state, "COMPLETE")
            with self.assertRaisesRegex(flow.HandoffHold, "^CHANNEL_NOT_BOUND$"):
                flow.handoff_once(
                    self.session, stage_bytes=b"synthetic ZIP, no Azure",
                    stage_sha=SHA, nonce=NONCE, sql_token=TOKEN)
            self.assertEqual(self.calls.count("token"), 1)

    def test_transport_failure_after_token_is_terminal_unknown(self):
        with mock.patch.object(self.session, "stage_select22_archive",
                               side_effect=self.stage), \
             mock.patch.object(self.session, "send_public"), \
             mock.patch.object(self.session, "send_select22_frame_once",
                               side_effect=self.send), \
             mock.patch.object(flow, "expect_ready"), \
             mock.patch.object(flow, "read_status",
                               side_effect=TimeoutError("private channel error")):
            with self.assertRaisesRegex(flow.HandoffHold, "^SELECT22_HANDOFF_HOLD$"):
                flow.handoff_once(
                    self.session, stage_bytes=b"synthetic ZIP, no Azure",
                    stage_sha=SHA, nonce=NONCE, sql_token=TOKEN)
            self.assertEqual(self.session.state.state, "UNKNOWN")
            with self.assertRaises(flow.HandoffHold):
                flow.handoff_once(
                    self.session, stage_bytes=b"synthetic ZIP, no Azure",
                    stage_sha=SHA, nonce=NONCE, sql_token=TOKEN)
            self.assertEqual(self.calls.count("token"), 1)

    def test_unstarted_session_refused(self):
        other = Select22StageSession("instance01234567", deadline=10**12)
        self.addCleanup(other.close)
        with self.assertRaisesRegex(flow.HandoffHold, "^CHANNEL_NOT_BOUND$"):
            flow.handoff_once(other, stage_bytes=b"", stage_sha=SHA,
                              nonce=NONCE, sql_token=TOKEN)

    def test_bad_stage_never_sends_token(self):
        with mock.patch.object(self.session, "stage_select22_archive",
                               side_effect=ValueError("invalid artifact")), \
             mock.patch.object(self.session, "send_select22_frame_once") as send:
            with self.assertRaisesRegex(flow.HandoffHold, "^SELECT22_HANDOFF_HOLD$"):
                flow.handoff_once(
                    self.session, stage_bytes=b"bad",
                    stage_sha=SHA, nonce=NONCE, sql_token=TOKEN)
            send.assert_not_called()
            self.assertEqual(self.session.state.state, "UNKNOWN")


if __name__ == "__main__":
    unittest.main()
