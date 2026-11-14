"""Offline-only proof of fail-closed persistence guard (no Azure/SQL)."""
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest import mock

import pathb_select22_persistent_claim_gate_nondeploy as gate


def mount(fs):
    return "36 29 0:30 / /home rw,relatime - " + fs + " source rw\n"


class ClaimGateTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.claim = Path(self.dir.name) / "fv-pathb-select22-claims"
        self.claim.mkdir(mode=0o700)
        os.chmod(self.claim, 0o700)
        self.pin = mock.patch.object(gate, "CLAIM_DIR", self.claim)
        self.pin.start()
        self.addCleanup(self.pin.stop)

    def test_verified_mount_and_exact_owner_private_directory(self):
        self.assertEqual(gate.verify_persistent_claim_dir(
            self.claim, mountinfo=mount("cifs")), self.claim)

    def test_ephemeral_tmpfs_never_satisfies_durability(self):
        for fs in ("tmpfs", "overlay", "ext4", "nfs"):
            with self.subTest(fs=fs), self.assertRaisesRegex(
                    gate.PersistentClaimHold, "PERSISTENT_HOME_NOT_PROVEN"):
                gate.verify_persistent_claim_dir(self.claim, mountinfo=mount(fs))

    def test_mount_not_present_or_duplicate_holds(self):
        for info in ("", mount("cifs") + mount("cifs")):
            with self.subTest(info=info), self.assertRaises(
                    gate.PersistentClaimHold):
                gate.verify_persistent_claim_dir(self.claim, mountinfo=info)

    def test_wrong_claim_path_holds(self):
        with self.assertRaisesRegex(gate.PersistentClaimHold, "CLAIM_PATH_HOLD"):
            gate.verify_persistent_claim_dir(
                self.claim / "other", mountinfo=mount("cifs"))

    def test_not_owner_private_holds(self):
        os.chmod(self.claim, 0o755)
        with self.assertRaisesRegex(gate.PersistentClaimHold, "CLAIM_MODE_HOLD"):
            gate.verify_persistent_claim_dir(self.claim, mountinfo=mount("cifs"))

    def test_symlinked_claim_dir_rejected(self):
        link = self.claim.parent / "link"
        link.symlink_to(self.claim, target_is_directory=True)
        with mock.patch.object(gate, "CLAIM_DIR", link):
            with self.assertRaisesRegex(gate.PersistentClaimHold, "CLAIM_PATH_HOLD"):
                gate.verify_persistent_claim_dir(link, mountinfo=mount("cifs"))

    def test_no_unverified_fallback_or_file_write(self):
        with mock.patch.object(gate.Path, "read_text",
                               side_effect=OSError("unreadable mount")):
            with self.assertRaisesRegex(gate.PersistentClaimHold, "PERSISTENCE_UNVERIFIED_HOLD"):
                gate.verify_persistent_claim_dir(self.claim)
        self.assertFalse((self.claim / "readonly-sql-attempt-spent").exists())


if __name__ == "__main__":
    unittest.main()
