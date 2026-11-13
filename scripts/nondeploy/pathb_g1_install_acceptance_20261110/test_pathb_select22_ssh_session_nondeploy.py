"""Offline-only SELECT22 single-token G1 SSH adapter contract."""
import unittest
from unittest import mock

import pathb_select22_ssh_session_nondeploy as subject
from p3_kudu_transport import TransportHold
from pathb_readonly_stdin_frame_20261113 import decode_frame

TOKEN = "a" * 60 + "." + "b" * 60 + "." + "c" * 60
NONCE = "1" * 32


class TokenAdapterTests(unittest.TestCase):
    def setUp(self):
        self.session = subject.Select22SshSession("instance01234567", deadline=10**12)
        self.session.state.begin()
        self.session.state.advance("CHANNEL", "STAGED")
        self.payloads = []
        self.writes = mock.patch.object(self.session, "_write_all",
                                        side_effect=self.payloads.append)
        self.auth = mock.patch.object(subject, "validate_owner_token", return_value=True)
        self.writes.start()
        self.auth.start()
        self.addCleanup(self.session.close)
        self.addCleanup(self.writes.stop)
        self.addCleanup(self.auth.stop)

    def test_one_sql_token_framed_and_no_storage_token(self):
        self.session.send_select22_frame_once(TOKEN, NONCE)
        self.assertEqual(self.session.state.state, "TOKENS")
        self.assertEqual(self.session._token_count, 1)
        self.assertEqual(len(self.payloads), 1)
        self.assertEqual(decode_frame(self.payloads[0], NONCE), TOKEN)
        self.assertEqual(subject.validate_owner_token.call_args.args[1], "sql")
        self.assertEqual(self.session.audit[-1], "SELECT22_SINGLE_TOKEN_FRAME_SENT")

    def test_second_dispatch_rejected_without_writing(self):
        self.session.send_select22_frame_once(TOKEN, NONCE)
        with self.assertRaises(TransportHold):
            self.session.send_select22_frame_once(TOKEN, NONCE)
        self.assertEqual(len(self.payloads), 1)

    def test_original_g1_storage_token_api_is_disabled(self):
        with self.assertRaises(TransportHold):
            self.session.send_secret_line("any", validator=lambda a: True,
                                          audit_code="STORAGE_TOKEN_SENT")
        self.assertEqual(len(self.payloads), 0)

    def test_write_ambiguity_is_terminal_hold(self):
        self.writes.stop()
        with mock.patch.object(self.session, "_write_all",
                               side_effect=TimeoutError("secret debug info")):
            with self.assertRaisesRegex(TransportHold, "^TOKEN_HANDOFF_HOLD$"):
                self.session.send_select22_frame_once(TOKEN, NONCE)
        self.assertEqual(self.session.state.state, "UNKNOWN")
        with self.assertRaises(TransportHold):
            self.session.send_select22_frame_once(TOKEN, NONCE)

    def test_wrong_nonce_rejected_without_dispatch(self):
        with self.assertRaises(TransportHold):
            self.session.send_select22_frame_once(TOKEN, "INVALID")
        self.assertEqual(self.session._token_count, 0)
        self.assertEqual(len(self.payloads), 0)

    def test_invalid_sql_identity_rejected(self):
        self.auth.stop()
        with mock.patch.object(subject, "validate_owner_token",
                               side_effect=ValueError("forbidden claims")):
            with self.assertRaisesRegex(TransportHold, "^TOKEN_HANDOFF_HOLD$"):
                self.session.send_select22_frame_once(TOKEN, NONCE)
        self.assertEqual(len(self.payloads), 0)

    def test_reused_g1_ssh_pinned_session(self):
        self.assertTrue(issubclass(subject.Select22SshSession,
                                   subject.SshExecSession))
        self.assertFalse(hasattr(subject.Select22SshSession, "execute"))
        self.assertFalse(hasattr(subject.Select22SshSession, "main"))

    def test_secret_buffer_is_scrubbed_on_close(self):
        self.session.send_select22_frame_once(TOKEN, NONCE)
        ref = self.session._secrets[0]
        self.session.close()
        self.assertTrue(all(byte == 0 for byte in ref))


if __name__ == "__main__":
    unittest.main()
