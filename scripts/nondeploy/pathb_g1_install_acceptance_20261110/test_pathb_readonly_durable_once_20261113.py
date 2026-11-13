"""Offline durability and crash/replay tests. Never contacts Azure."""
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from pathb_readonly_durable_once_20261113 import consume_once, DurableHold, CLAIM_NAME

class DurableTests(unittest.TestCase):
    def test_replay_denied_across_processes(self):
        with tempfile.TemporaryDirectory() as tmp:
            os.chmod(tmp, 0o700)
            self.assertEqual(consume_once(tmp), "SPENT")
            claim = pathlib.Path(tmp) / CLAIM_NAME
            self.assertEqual(claim.read_bytes(), b"SPENT\n")
            with self.assertRaises(DurableHold):
                consume_once(tmp)
            script = "from pathb_readonly_durable_once_20261113 import consume_once; consume_once(__import__('sys').argv[1])"
            child = subprocess.run([sys.executable, "-c", script, tmp],
                                   capture_output=True, text=True, timeout=5)
            self.assertNotEqual(child.returncode, 0)
            self.assertEqual(claim.read_bytes(), b"SPENT\n")
    def test_unsafe_directory_denied(self):
        with tempfile.TemporaryDirectory() as tmp:
            os.chmod(tmp, 0o755)
            with self.assertRaises(DurableHold):
                consume_once(tmp)
            self.assertFalse((pathlib.Path(tmp)/CLAIM_NAME).exists())

if __name__ == "__main__":
    unittest.main()
