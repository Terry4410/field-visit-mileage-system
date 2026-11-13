"""Offline execution of the *generated command source* with a mocked receiver."""
import base64
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import pathb_select22_remote_command_nondeploy as candidate
from pathb_readonly_stdin_frame_20261113 import encode_frame

TOKEN = "a" * 60 + "." + "b" * 60 + "." + "c" * 60
NONCE = "fedcba9876543210fedcba9876543210"


class CommandTests(unittest.TestCase):
    def test_command_uses_only_isolated_stage(self):
        command = candidate.select22_remote_command(NONCE)
        self.assertTrue(command.startswith(b'python3 -c "'))
        self.assertNotIn(b"p3_app_installer_runner", command)
        self.assertNotIn(TOKEN.encode(), command)
        self.assertNotIn(b"storage_token", command)
        self.assertNotIn(b"az account", command)

    def test_invalid_nonce_rejected(self):
        for n in ("", "ABC", "../", "0" * 31, "g" * 32):
            with self.subTest(n=n), self.assertRaises(candidate.RemoteCommandHold):
                candidate.select22_remote_command(n)

    def test_remote_source_reads_one_frame_and_returns_status(self):
        with tempfile.TemporaryDirectory() as temp:
            mock_module = Path(temp) / "pathb_select22_receipt_bridge_nondeploy.py"
            mock_module.write_text(
                "def app_receive_once(frame, nonce, **kwargs):\n"
                "    assert frame.endswith(b'\\n')\n"
                "    assert nonce == " + repr(NONCE) + "\n"
                "    assert kwargs['claim_dir']=='/home/fv-pathb-select22-claims'\n"
                "    return b'FV_SELECT22_SANITIZED_V1={\"status\":\"HOLD\"}\\n'\n")
            code = candidate._source(NONCE, temp)
            completed = subprocess.run(
                [sys.executable, "-c", code],
                input=encode_frame(TOKEN, NONCE),
                capture_output=True, timeout=5,
            )
            self.assertEqual(completed.returncode, 0)
            self.assertEqual(
                completed.stdout,
                b'FV_SELECT22_READY\nFV_SELECT22_SANITIZED_V1={"status":"HOLD"}\n')
            self.assertNotIn(TOKEN.encode(), completed.stdout)
            self.assertEqual(completed.stderr, b"")

    def test_bad_remote_module_cannot_expose_exception(self):
        with tempfile.TemporaryDirectory() as temp:
            (Path(temp) / "pathb_select22_receipt_bridge_nondeploy.py").write_text(
                "raise RuntimeError('private error')\n")
            code = candidate._source(NONCE, temp)
            completed = subprocess.run(
                [sys.executable, "-c", code],
                input=encode_frame(TOKEN, NONCE),
                capture_output=True, timeout=5)
            self.assertEqual(completed.returncode, 0)
            self.assertNotIn(b"private error", completed.stdout)
            self.assertEqual(
                completed.stdout,
                b'FV_SELECT22_READY\nFV_SELECT22_SANITIZED_V1={"status":"HOLD"}\n')


if __name__ == "__main__":
    unittest.main()
