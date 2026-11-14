"""Only synthetic tests; never dispatch real SSH, SQL, Storage, Token or Blob."""
import os
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import pathb_select22_owner_shared_receiver_nondeploy as r

NONCE = "1" * 32


class SharedReceiverTests(unittest.TestCase):
    def test_generated_command_no_secret_or_restage(self):
        code = r.source(NONCE)
        self.assertIn(r.ROOT, code)
        self.assertIn(r.MANIFEST_SHA, code)
        self.assertIn(r.SQL_SHA, code)
        self.assertNotIn("dpkg-deb", code)
        self.assertNotIn("stage_offline", code)
        self.assertNotIn("az account", code)
        self.assertNotIn("subprocess", code)
        self.assertNotIn("Blob PUT", code)
        self.assertTrue(r.command(NONCE).startswith(b'python3 -c "'))

    def test_bad_nonce_blocks_code_generation(self):
        for n in ("", "A"*32, "../", "a"*31, None):
            with self.subTest(n=n), self.assertRaises(r.OwnerSharedReceiverHold):
                r.source(n)

    def test_missing_shared_root_fails_before_ready_or_token(self):
        with tempfile.TemporaryDirectory() as t:
            bogus = os.path.join(t, "absent")
            with mock.patch.object(r, "ROOT", bogus):
                out = subprocess.run([sys.executable, "-B", "-c", r.source(NONCE)],
                                     input=b"synthetic-confidential-token", capture_output=True,
                                     timeout=5, env={**os.environ, "WEBSITE_SITE_NAME": r.APP})
            self.assertEqual(out.returncode, 0)
            self.assertEqual(out.stdout, r.HOLD)
            self.assertNotIn(b"READY", out.stdout)
            self.assertNotIn(b"confidential", out.stdout)

    def test_wrong_app_fails_before_root_read(self):
        with tempfile.TemporaryDirectory() as t:
            with mock.patch.object(r, "ROOT", t):
                out = subprocess.run([sys.executable, "-B", "-c", r.source(NONCE)],
                                     capture_output=True, timeout=5,
                                     env={**os.environ, "WEBSITE_SITE_NAME": "wrong-app"})
            self.assertEqual(out.stdout, r.HOLD)

    def test_no_live_release_on_fake_owner_flags(self):
        with self.assertRaisesRegex(r.OwnerSharedReceiverHold, "OWNER_SHARED_LIVE_PARENT_NOT_RELEASED"):
            r.release_live(owner_approved=True, blob_readback=True, sql_token=True)


if __name__ == "__main__":
    unittest.main()
