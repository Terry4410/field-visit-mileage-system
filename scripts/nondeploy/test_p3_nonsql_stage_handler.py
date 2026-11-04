"""Synthetic P3 non-SQL stage tests. No SSH, credentials, network or DDL."""
import contextlib
import hashlib
import io
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import p3_nonsql_stage_handler as h

class NonSqlStageTests(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.TemporaryDirectory()
        self.addCleanup(self.t.cleanup)
        self.root = Path(self.t.name)

    def stage(self, extra=None, omit=None, symlink=None):
        payload = {k: b'a' for k in h.REQUIRED}
        if omit:
            del payload[omit]
        if extra:
            payload.update(extra)
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
            for name, data in payload.items():
                if name == symlink:
                    info = zipfile.ZipInfo(name)
                    info.create_system = 3
                    info.external_attr = 0o120777 << 16
                    z.writestr(info, data)
                else:
                    z.writestr(name, data)
        return buf.getvalue()

    def check(self, data, sha=None):
        with self.assertRaises(h.Hold):
            h.extract_stage(data, self.root, sha or hashlib.sha256(data).hexdigest())

    def test_valid_staging_contents(self):
        data = self.stage()
        h.extract_stage(data, self.root, hashlib.sha256(data).hexdigest())
        actual = {str(p.relative_to(self.root)) for p in self.root.rglob('*') if p.is_file()}
        self.assertEqual(actual, h.REQUIRED)

    def test_sha_tamper(self): self.check(self.stage(), '0'*64)
    def test_no_authorization_file(self): self.check(self.stage({'owner-authorization.json':b'{}'}))
    def test_no_sql_zip(self): self.check(self.stage({'original.zip':b'zip'}))
    def test_missing_tool(self): self.check(self.stage(omit='debs/msodbcsql18.deb'))
    def test_symlink_rejected(self): self.check(self.stage(symlink='debs/msodbcsql18.deb'))
    def test_unknown_file(self): self.check(self.stage(extra={'bad-file':b'x'}))
    def test_bad_digest_format(self): self.check(self.stage(), 'abc')

    def test_original_archive_binding_reaches_tool_checks(self):
        original = Path(os.environ.get('P3_ORIGINAL_BUNDLE', '/nonexistent-p3-original-bundle'))
        if not (original/'binding-index.json').is_file():
            self.skipTest('set P3_ORIGINAL_BUNDLE to test original sealed source')
        buf=io.BytesIO()
        with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
            for name in h.BOUND_SOURCE:
                z.writestr(name, (original/name).read_bytes())
            for tool in h.REQUIRED_TOOLS:
                z.writestr('debs/'+tool+'.deb', b'not-real-package')
        payload=buf.getvalue()
        stage=self.root/'stage.zip'; stage.write_bytes(payload)
        with patch('p3_nonsql_abi_probe.machine_gate'), \
             patch('p3_nonsql_abi_probe.safe_extract_toolset'), \
             patch('p3_nonsql_abi_probe.elf_gate'):
            self.assertTrue(h.run(stage, hashlib.sha256(payload).hexdigest()))

    def test_only_non_sql_probe_invoked(self):
        data = self.stage()
        stage = self.root/'stage.zip'; stage.write_bytes(data)
        with patch.object(h, 'probe') as probe:
            self.assertTrue(h.run(stage, hashlib.sha256(data).hexdigest()))
            probe.assert_called_once()

    def test_probe_fail_cleans_workspace(self):
        data = self.stage()
        stage = self.root/'stage.zip'; stage.write_bytes(data)
        before = set(Path('/tmp').glob('fv-p3-nonsql-stage-*'))
        with patch.object(h, 'probe', side_effect=h.Hold):
            with self.assertRaises(h.Hold):
                h.run(stage, hashlib.sha256(data).hexdigest())
        self.assertEqual(before, set(Path('/tmp').glob('fv-p3-nonsql-stage-*')))

    def test_no_executable_entry_on_bad_file(self):
        with contextlib.redirect_stdout(io.StringIO()) as out:
            self.assertEqual(h.main(['--stage', str(self.root/'missing.zip'), '--expected-sha256', '0'*64]), 1)
        self.assertIn('HOLD', out.getvalue())
        self.assertIn('NOT_RUN', out.getvalue())

if __name__ == '__main__':
    unittest.main()
