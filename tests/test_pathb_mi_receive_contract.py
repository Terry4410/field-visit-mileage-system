"""Synthetic, offline-only negative tests. No Azure or SSH."""
import hashlib
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts" / "nondeploy"))
from pathb_mi_receive_contract import Hold, receive_once


class ReceiveContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "owned"
        self.root.mkdir(mode=0o700)
        self.payload = b"synthetic-not-a-real-archive"
        self.digest = hashlib.sha256(self.payload).hexdigest()

    def receive(self, **kwargs):
        args = dict(directory=self.root, name="mi-login-candidate.zip",
                    payload=self.payload, expected_sha256=self.digest)
        args.update(kwargs)
        return receive_once(**args)

    def test_valid_synthetic_bytes_created_once(self):
        self.assertEqual(self.receive(), self.digest)
        self.assertEqual((self.root / "mi-login-candidate.zip").read_bytes(), self.payload)
        with self.assertRaises(FileExistsError):
            self.receive()

    def test_digest_mismatch_rejected_without_write(self):
        with self.assertRaises(Hold):
            self.receive(expected_sha256="0" * 64)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_wrong_name_rejected(self):
        with self.assertRaises(Hold):
            self.receive(name="../escape.zip")

    def test_oversize_rejected(self):
        with self.assertRaises(Hold):
            self.receive(payload=b"x" * (16 * 1024 * 1024 + 1))

    def test_symlink_directory_rejected(self):
        alias = Path(self.temp.name) / "alias"
        alias.symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(Hold):
            self.receive(directory=alias)

    def test_world_writable_directory_rejected(self):
        os.chmod(self.root, 0o777)
        with self.assertRaises(Hold):
            self.receive()

    def test_directory_replaced_after_write_is_hold(self):
        original_stat = Path.stat
        calls = {"count": 0}

        def swapping_stat(path, *args, **kwargs):
            if path == self.root:
                calls["count"] += 1
                if calls["count"] == 3:
                    self.root.rename(Path(self.temp.name) / "moved")
                    self.root.mkdir(mode=0o700)
            return original_stat(path, *args, **kwargs)

        with patch.object(Path, "stat", swapping_stat):
            with self.assertRaises(Hold):
                self.receive()

    def test_existing_symlink_target_not_followed(self):
        outside = Path(self.temp.name) / "outside"
        outside.write_bytes(b"untouched")
        (self.root / "mi-login-candidate.zip").symlink_to(outside)
        with self.assertRaises(FileExistsError):
            self.receive()
        self.assertEqual(outside.read_bytes(), b"untouched")


if __name__ == "__main__":
    unittest.main()
