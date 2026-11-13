"""Offline-only tests: no Azure, SQL, Token, installer, deployment or deb install."""
import ast
import contextlib
import hashlib
import io
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import pathb_connection_only_tool_stage_nondeploy as stage


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


class OfflineStageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.debs = self.root / "debs"
        self.debs.mkdir()
        self.payload = {}
        tools = []
        for name in stage.TOOL_NAMES:
            raw = ("FAKE_DEB_" + name).encode()
            p = self.debs / (name + ".deb")
            p.write_bytes(raw)
            tools.append({"name": name, "sha256": digest(raw)})
        for name in stage.TOOL_FILE_NAMES:
            self.payload[name] = ("FAKE_BINARY_" + name).encode()
        self.cfg = {
            "tools": tools,
            "tool_files": {name: digest(data) for name, data in self.payload.items()},
        }
        self.extractions = []

    def extract(self, package, dest):
        self.extractions.append(package.name)
        for name, data in self.payload.items():
            p = dest / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)

    def test_bound_original_g1_manifest_matches_exact_hash(self):
        cfg = stage.load_bound_config()
        self.assertEqual([x["name"] for x in cfg["tools"]], list(stage.TOOL_NAMES))
        self.assertEqual(set(cfg["tool_files"]), set(stage.TOOL_FILE_NAMES))

    def test_offline_stage_succeeds_and_cleans(self):
        with mock.patch.object(stage.tempfile, "TemporaryDirectory",
                               wraps=tempfile.TemporaryDirectory) as tmp:
            result = stage.stage_offline(self.cfg, self.debs, extractor=self.extract)
            self.assertEqual(result, "PASS")
            self.assertEqual(len(self.extractions), 5)
            self.assertEqual(tmp.call_count, 1)

    def test_missing_package_fails_before_extraction(self):
        (self.debs / "libodbc2.deb").unlink()
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_DEBS_ABSENT"):
            stage.stage_offline(self.cfg, self.debs, extractor=self.extract)
        self.assertEqual(self.extractions, [])

    def test_bad_package_hash_fails_before_extraction(self):
        (self.debs / "libodbc2.deb").write_bytes(b"TAMPERED")
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_DEB_HASH"):
            stage.stage_offline(self.cfg, self.debs, extractor=self.extract)
        self.assertEqual(self.extractions, [])

    def test_symlinked_deb_rejected_before_extraction(self):
        p = self.debs / "libodbc2.deb"
        p.unlink()
        p.symlink_to(self.debs / "msodbcsql18.deb")
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_DEBS_ABSENT"):
            stage.stage_offline(self.cfg, self.debs, extractor=self.extract)
        self.assertEqual(self.extractions, [])

    def test_binary_hash_mismatch_fails(self):
        def bad_extractor(package, dest):
            self.extract(package, dest)
            (dest / stage.TOOL_FILE_NAMES[1]).write_bytes(b"WRONG")
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_BINARY_HASH"):
            stage.stage_offline(self.cfg, self.debs, extractor=bad_extractor)

    def test_absolute_extracted_symlink_fails(self):
        def bad_extractor(package, dest):
            self.extract(package, dest)
            (dest / "absolute-escape").symlink_to("/etc")
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_EXTRACTED_LINK"):
            stage.stage_offline(self.cfg, self.debs, extractor=bad_extractor)

    def test_relative_escape_link_fails(self):
        def bad_extractor(package, dest):
            self.extract(package, dest)
            (dest / "relative-escape").symlink_to("../../../../../etc")
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_EXTRACTED_LINK"):
            stage.stage_offline(self.cfg, self.debs, extractor=bad_extractor)

    def test_unexpected_binary_list_fails(self):
        self.cfg["tool_files"]["unexpected/path"] = digest(b"foo")
        with self.assertRaisesRegex(stage.OfflineHold, "HOLD_BINARY_LIST"):
            stage.stage_offline(self.cfg, self.debs, extractor=self.extract)

    def test_missing_debs_dir_cli_fails_closed(self):
        with contextlib.redirect_stdout(io.StringIO()) as out:
            rc = stage.main(["--offline-stage", "--debs-dir",
                             str(self.root / "missing")])
        self.assertEqual(rc, 1)
        self.assertIn("HOLD_DEBS_ABSENT", out.getvalue())
        self.assertIn("SQL_CONNECTION=NO", out.getvalue())

    def test_manifest_only_cli_passes(self):
        with contextlib.redirect_stdout(io.StringIO()) as out:
            rc = stage.main(["--check-config"])
        self.assertEqual(rc, 0)
        self.assertIn("CONFIG_BINDING=PASS", out.getvalue())
        self.assertIn("SQL_CONNECTION=NO", out.getvalue())

    def test_source_has_no_live_client_or_installer_call(self):
        source = Path(stage.__file__).read_text(encoding="utf-8")
        tree = ast.parse(source)
        imports = {
            node.module.split(".")[0]
            for node in ast.walk(tree)
            if isinstance(node, ast.ImportFrom) and node.module
        }
        imports |= {
            alias.name.split(".")[0]
            for node in ast.walk(tree) if isinstance(node, ast.Import)
            for alias in node.names
        }
        for forbidden in ("socket", "pyodbc", "pymssql", "requests", "urllib",
                          "azure", "p3_app_installer_runner", "p3_kudu_cloudshell_parent"):
            self.assertNotIn(forbidden, imports)
        calls = [
            node for node in ast.walk(tree)
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
            and isinstance(node.func.value, ast.Name)
            and node.func.value.id == "subprocess"
        ]
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0].func.attr, "run")
        self.assertIn('["dpkg-deb", "-x"', source)
        self.assertNotIn("sql_call(", source)
        self.assertNotIn("install.py", source)


if __name__ == "__main__":
    unittest.main()
