"""NONDEPLOY handoff envelope negative tests. No Azure/SSH/Token/SQL/DDL."""
import hashlib
import io
import json
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch
import zipfile

import p3_nonsql_handoff_builder as h

class HandoffTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.src = self.root / 'src'
        self.src.mkdir()
        self.stage = self.root / 'stage.zip'
        for name in h.SCRIPTS:
            (self.src / name).write_bytes(b'synthetic script only')
        self.stage.write_bytes(b'synthetic stage only')
        self.blob = h.git_blob_sha1(b'synthetic script only')

    def test_git_blob_digest_matches_known_git_object(self):
        self.assertEqual(h.git_blob_sha1(b'hello'),
                         'b6fc4c620b67d95f953a5c1c1230aaab5db5a1b0')

    def test_bounded_handoff_manifest(self):
        with patch.object(h, 'inspect_stage'), patch.dict(h.SCRIPTS, {
            k:self.blob for k in h.SCRIPTS}):
            payload = h.build(self.src, self.stage)
        with zipfile.ZipFile(io.BytesIO(payload)) as z:
            self.assertEqual(set(z.namelist()),
                             {'stage.zip', 'manifest.json'} | set(h.SCRIPTS))
            manifest = json.loads(z.read('manifest.json'))
            self.assertEqual(manifest['live_execution_authorized'], False)
            self.assertEqual(manifest['stage_sha256'],
                             hashlib.sha256(self.stage.read_bytes()).hexdigest())
            self.assertEqual(manifest['remote_sql_token_ddl'], 'FORBIDDEN')

    def test_unknown_script_digest_holds(self):
        # Bypass only stage-format decoding so this test reaches source pinning.
        with patch.object(h, 'inspect_stage'), self.assertRaises(h.Hold):
            h.build(self.src, self.stage)

    def test_stage_malformed_holds_before_building(self):
        with self.assertRaises((h.Hold, zipfile.BadZipFile)):
            h.build(self.src, self.stage)

    def test_missing_script_holds(self):
        (self.src / 'p3_nonsql_abi_probe.py').unlink()
        with patch.object(h, 'inspect_stage'), self.assertRaises(h.Hold):
            h.build(self.src, self.stage)

    def test_symlink_source_holds(self):
        (self.src / 'p3_nonsql_abi_probe.py').unlink()
        (self.src / 'p3_nonsql_abi_probe.py').symlink_to('p3_nonsql_stage_handler.py')
        with patch.object(h, 'inspect_stage'), self.assertRaises(h.Hold):
            h.build(self.src, self.stage)

    def test_forbid_overwrite(self):
        output = self.root / 'output.zip'
        output.write_bytes(b'existing')
        self.assertEqual(h.main(['--source-dir',str(self.src),
                                 '--stage',str(self.stage),
                                 '--output',str(output)]), 1)
        self.assertEqual(output.read_bytes(), b'existing')

if __name__ == '__main__':
    unittest.main()
