"""Offline-only contract tests for the immutable G1 source distribution.

No network, subprocesses, SQL, tokens, Azure, or original SQL blobs.
"""
import json
import shutil
import tempfile
import unittest
import warnings
from pathlib import Path
from zipfile import ZipFile

import g1_pinned_source_distribution_nondeploy as delivery
import p3_kudu_cloudshell_parent as approved_parent


ROOT = Path(__file__).resolve().parent


class SourceOnlyDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.workspace = Path(self.temp.name)
        self.source = self.workspace / "source"
        self.source.mkdir()
        members, _ = delivery.verified_source_members(ROOT)
        for name, data in members.items():
            (self.source / name).write_bytes(data)

    def test_original_parent_accepts_deliverable(self):
        target = self.workspace / "pinned-source.zip"
        digest, binding_sha, count = delivery.build_source_only(self.source, target)
        binding, parent_sha = approved_parent.validate_binding(ROOT)
        self.assertEqual(binding_sha, parent_sha)
        self.assertEqual(count, len(binding["files"]) + 1)
        self.assertEqual(digest, delivery._digest(target.read_bytes()))
        approved_parent.validate_distribution_archive(target, binding)

    def test_reproducible_bytes(self):
        first = self.workspace / "first.zip"
        second = self.workspace / "second.zip"
        self.assertEqual(delivery.build_source_only(self.source, first),
                         delivery.build_source_only(self.source, second))
        self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_does_not_bundle_original_sql_or_approvals(self):
        target = self.workspace / "pinned-source.zip"
        delivery.build_source_only(self.source, target)
        with ZipFile(target) as archive:
            names = set(archive.namelist())
        self.assertNotIn("original.zip", names)
        self.assertNotIn("original.seal.json", names)
        self.assertFalse(any("authorization" in name.lower() or
                             "token-file" in name.lower() for name in names))

    def test_tampered_sql_preflight_fails_without_writing(self):
        path = self.source / "preflight.sql"
        path.write_bytes(path.read_bytes() + b"\n-- MODIFIED\n")
        target = self.workspace / "never.zip"
        with self.assertRaises(delivery.OfflineHold):
            delivery.build_source_only(self.source, target)
        self.assertFalse(target.exists())

    def test_tampered_frozen_installer_fails_without_writing(self):
        path = self.source / "frozen_install.py.snapshot"
        path.write_bytes(path.read_bytes() + b"\n# UNSAFE CHANGE\n")
        target = self.workspace / "never.zip"
        with self.assertRaises(delivery.OfflineHold):
            delivery.build_source_only(self.source, target)
        self.assertFalse(target.exists())

    def test_altered_pinned_target_fails(self):
        path = self.source / "executor_config.json"
        obj = json.loads(path.read_text())
        obj["database"] = "db-fieldvisit-uat"
        path.write_text(json.dumps(obj))
        with self.assertRaises(delivery.OfflineHold):
            delivery.build_source_only(self.source, self.workspace / "never.zip")

    def test_missing_member_fails(self):
        (self.source / "preflight.sql").unlink()
        with self.assertRaises(delivery.OfflineHold):
            delivery.build_source_only(self.source, self.workspace / "never.zip")

    def test_symlink_member_fails(self):
        victim = self.source / "postcheck.sql"
        outside = self.workspace / "outside.sql"
        shutil.copy2(victim, outside)
        victim.unlink()
        try:
            victim.symlink_to(outside)
        except (OSError, NotImplementedError):
            self.skipTest("symlink unsupported in this filesystem")
        with self.assertRaises(delivery.OfflineHold):
            delivery.build_source_only(self.source, self.workspace / "never.zip")

    def test_cannot_overwrite_existing_distribution(self):
        target = self.workspace / "existing.zip"
        target.write_bytes(b"keep-me")
        with self.assertRaises(delivery.OfflineHold):
            delivery.build_source_only(self.source, target)
        self.assertEqual(target.read_bytes(), b"keep-me")

    def test_extra_zip_entry_rejected_by_original_parent(self):
        target = self.workspace / "candidate.zip"
        delivery.build_source_only(self.source, target)
        with ZipFile(target, mode="a") as archive:
            archive.writestr("extra.txt", "unexpected")
        binding, _ = approved_parent.validate_binding(ROOT)
        with self.assertRaises(approved_parent.Hold):
            approved_parent.validate_distribution_archive(target, binding)

    def test_corrupt_zip_member_rejected_by_original_parent(self):
        target = self.workspace / "candidate.zip"
        delivery.build_source_only(self.source, target)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with ZipFile(target, mode="a") as archive:
                archive.writestr("preflight.sql", b"not the approved query")
        binding, _ = approved_parent.validate_binding(ROOT)
        with self.assertRaises(approved_parent.Hold):
            approved_parent.validate_distribution_archive(target, binding)


if __name__ == "__main__":
    unittest.main()
