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

    @staticmethod
    def _trusted_non_sql_gates(folder, *, tool_ready=True, source_ready=True):
        (Path(folder) / "pathb_connection_only_tool_stage_nondeploy.py").write_text(
            "def load_bound_config(): return {}\n"
            "def stage_offline(cfg, debs):\n"
            + ("    return 'PASS'\n" if tool_ready else "    raise ValueError('unavailable')\n")
        )
        (Path(folder) / "pathb_readonly_isolated_gate.py").write_text(
            "def verify(): return {\n"
            + ("'SOURCE_BINDING':'PASS', 'PINNED_SQL':'PASS', 'EXACT_TARGET':'PASS'}\n"
               if source_ready else
               "'SOURCE_BINDING':'HOLD', 'PINNED_SQL':'PASS', 'EXACT_TARGET':'PASS'}\n")
        )

    def test_tool_precheck_failure_prevents_ready_and_token_request(self):
        with tempfile.TemporaryDirectory() as temp:
            self._trusted_non_sql_gates(temp, tool_ready=False)
            (Path(temp) / "pathb_select22_receipt_bridge_nondeploy.py").write_text(
                "def app_receive_after_external_claim(*args, **kwargs):\n"
                "    raise AssertionError('should never dispatch')\n"
            )
            completed = subprocess.run(
                [sys.executable, "-c", candidate._source(NONCE, temp)],
                input=encode_frame(TOKEN, NONCE),
                capture_output=True, timeout=5,
            )
            self.assertEqual(completed.returncode, 0)
            self.assertEqual(completed.stdout, candidate.HOLD_WIRE)
            self.assertEqual(completed.stderr, b"")
            self.assertNotIn(b"FV_SELECT22_READY", completed.stdout)
            self.assertNotIn(TOKEN.encode(), completed.stdout)

    def test_source_binding_failure_prevents_ready(self):
        with tempfile.TemporaryDirectory() as temp:
            self._trusted_non_sql_gates(temp, source_ready=False)
            (Path(temp) / "pathb_select22_receipt_bridge_nondeploy.py").write_text(
                "def app_receive_after_external_claim(*args, **kwargs):\n"
                "    raise AssertionError('must not send')\n"
            )
            completed = subprocess.run(
                [sys.executable, "-c", candidate._source(NONCE, temp)],
                input=encode_frame(TOKEN, NONCE),
                capture_output=True, timeout=5,
            )
            self.assertEqual(completed.returncode, 0)
            self.assertEqual(completed.stdout, candidate.HOLD_WIRE)
            self.assertEqual(completed.stderr, b"")

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
            self._trusted_non_sql_gates(temp)
            mock_module = Path(temp) / "pathb_select22_receipt_bridge_nondeploy.py"
            mock_module.write_text(
                "def app_receive_after_external_claim(frame, nonce, **kwargs):\n"
                "    assert frame.endswith(b'\\n')\n"
                "    assert nonce == " + repr(NONCE) + "\n"
                "    assert kwargs['debs_dir'].endswith('/debs')\n"
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
                candidate.HOLD_WIRE)
            self.assertNotIn(TOKEN.encode(), completed.stdout)
            self.assertEqual(completed.stderr, b"")

    def test_bad_remote_module_cannot_expose_exception(self):
        with tempfile.TemporaryDirectory() as temp:
            self._trusted_non_sql_gates(temp)
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
