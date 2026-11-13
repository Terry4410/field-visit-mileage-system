"""Offline-only frame tests; synthetic tokens, no Azure."""
import unittest
from pathb_readonly_stdin_frame_20261113 import encode_frame, decode_frame, FrameHold

NONCE = "a" * 32
FAKE = "a" * 60 + "." + "b" * 60 + "." + "c" * 60

class FrameTests(unittest.TestCase):
    def test_roundtrip(self):
        self.assertEqual(decode_frame(encode_frame(FAKE, NONCE), NONCE), FAKE)
    def test_wrong_nonce(self):
        with self.assertRaises(FrameHold):
            decode_frame(encode_frame(FAKE, NONCE), "b" * 32)
    def test_reject_storage_token(self):
        with self.assertRaises(FrameHold):
            encode_frame("not-a-jwt", NONCE)
    def test_reject_injected_fields(self):
        raw = encode_frame(FAKE, NONCE).replace(b'"sql_token":', b'"storage_token":"secret","sql_token":')
        with self.assertRaises(FrameHold):
            decode_frame(raw, NONCE)
    def test_reject_oversized_frame(self):
        with self.assertRaises(FrameHold):
            decode_frame(b"x" * 17001, NONCE)
    def test_reject_multiline(self):
        with self.assertRaises(FrameHold):
            decode_frame(encode_frame(FAKE, NONCE) + b"anything\n", NONCE)
    def test_no_sensitive_data_in_exception(self):
        try:
            decode_frame(encode_frame(FAKE, NONCE), "b" * 32)
        except FrameHold as exc:
            self.assertNotIn(FAKE, str(exc))

if __name__ == "__main__":
    unittest.main()
