"""Offline-only exact SELECT22 isolated ZIP tests, no real Azure or SQL."""
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

import pathb_select22_isolated_stage_nondeploy as stage
from pathb_connection_only_tool_stage_nondeploy import load_bound_config


class StageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.debs = Path(self.temp.name)
        self.cfg = load_bound_config()
        self.cfg["tools"] = [dict(v) for v in self.cfg["tools"]]
        for item in self.cfg["tools"]:
            raw = ("FAKE_DEBIAN_" + item["name"]).encode()
            (self.debs / (item["name"] + ".deb")).write_bytes(raw)
            item["sha256"] = hashlib.sha256(raw).hexdigest()

    def build(self):
        return stage.build_stage(self.debs, config_loader=lambda: self.cfg)

    def test_stage_exact_packages_sources_and_status(self):
        raw, digest = self.build()
        result = stage.verify_stage(raw, digest, config_loader=lambda: self.cfg)
        self.assertEqual(result["packages"], 5)
        self.assertEqual(result["source_files"], len(stage.SOURCE_FILES))
        self.assertEqual(result["sha256"], digest)
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            names = z.namelist()
            self.assertNotIn("p3_app_installer_runner.py", names)
            self.assertNotIn("frozen_install.py.snapshot", names)
            self.assertNotIn("original.zip", names)
            self.assertNotIn("owner-authorization.json", names)
            self.assertNotIn("install.py", names)
            self.assertEqual(len(names), len(stage.SOURCE_FILES) + 6)

    def test_stage_is_deterministic(self):
        a, h = self.build()
        b, g = self.build()
        self.assertEqual(a, b)
        self.assertEqual(h, g)

    def test_missing_deb_fail_before_stage(self):
        (self.debs / "libodbc2.deb").unlink()
        with self.assertRaises(Exception):
            self.build()

    def test_modified_zip_rejected_by_trusted_sha(self):
        raw, sha = self.build()
        with self.assertRaisesRegex(stage.StageHold, "^STAGE_EXTERNAL_SHA_HOLD$"):
            stage.verify_stage(raw + b"mutated", sha, config_loader=lambda: self.cfg)

    def test_missing_pinned_manifest_entry_fails_even_with_recomputed_sha(self):
        raw, _ = self.build()
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            vals = {n: z.read(n) for n in z.namelist()}
        vals.pop("preflight.sql")
        modified = stage._zip_bytes(vals)
        with self.assertRaisesRegex(stage.StageHold, "^STAGE_MEMBER_SET_HOLD$"):
            stage.verify_stage(modified, stage._digest(modified),
                               config_loader=lambda: self.cfg)

    def test_extra_installer_member_rejected(self):
        raw, _ = self.build()
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            vals = {n: z.read(n) for n in z.namelist()}
        vals["install.py"] = b"NOT EXECUTED"
        modified = stage._zip_bytes(vals)
        with self.assertRaisesRegex(stage.StageHold, "^STAGE_MEMBER_SET_HOLD$"):
            stage.verify_stage(modified, stage._digest(modified),
                               config_loader=lambda: self.cfg)

    def test_traversal_member_rejected(self):
        raw, _ = self.build()
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            vals = {n: z.read(n) for n in z.namelist()}
        vals["../escape"] = b"no"
        modified = stage._zip_bytes(vals)
        with self.assertRaisesRegex(stage.StageHold, "^STAGE_MEMBER_SET_HOLD$"):
            stage.verify_stage(modified, stage._digest(modified),
                               config_loader=lambda: self.cfg)

    def test_modified_sql_rejected_even_with_self_consistent_manifest(self):
        raw, _ = self.build()
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            vals = {n: z.read(n) for n in z.namelist()}
        vals["preflight.sql"] = b"SELECT 1;"
        manifest = json.loads(vals[stage.MANIFEST])
        manifest["files"]["preflight.sql"] = stage._digest(vals["preflight.sql"])
        vals[stage.MANIFEST] = json.dumps(
            manifest, sort_keys=True, separators=(",", ":")).encode()
        modified = stage._zip_bytes(vals)
        with self.assertRaisesRegex(stage.StageHold, "^STAGE_SQL_PIN_HOLD$"):
            stage.verify_stage(modified, stage._digest(modified),
                               config_loader=lambda: self.cfg)


if __name__ == "__main__":
    unittest.main()
