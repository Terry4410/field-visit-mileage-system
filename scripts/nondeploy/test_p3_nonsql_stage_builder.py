"""Offline-only P3 non-SQL stage builder tests. No Azure or SQL."""
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import p3_nonsql_stage_builder as b
from p3_nonsql_abi_probe import Hold, EXPECTED_NAMES
from p3_nonsql_stage_handler import BOUND_SOURCE

class TestBuilder(unittest.TestCase):
    def setUp(self):
        self.t=tempfile.TemporaryDirectory()
        self.addCleanup(self.t.cleanup)
        self.root=Path(self.t.name)
        self.bundle=self.root/'bundle';self.bundle.mkdir()
        self.debs=self.root/'debs';self.debs.mkdir()
        for name in BOUND_SOURCE:(self.bundle/name).write_bytes(b'v')
        self.cfg={'tools':[{'name':name,'sha256':'f'*64} for name in EXPECTED_NAMES]}
        for name in EXPECTED_NAMES:(self.debs/(name+'.deb')).write_bytes(b'x')
    def fake_sha(self, p):
        return 'f'*64
    def test_archive_matches_receiver(self):
        with patch.object(b,'fixed_bundle',return_value=self.cfg),patch.object(b,'sha_file',side_effect=self.fake_sha):
            raw=b.build(self.bundle,self.debs)
        self.assertGreater(len(raw),0)
    def test_missing_tool_holds(self):
        (self.debs/'libodbc2.deb').unlink()
        with patch.object(b,'fixed_bundle',return_value=self.cfg),patch.object(b,'sha_file',side_effect=self.fake_sha), self.assertRaises(Hold):
            b.build(self.bundle,self.debs)
    def test_bad_tool_hash_holds(self):
        with patch.object(b,'fixed_bundle',return_value=self.cfg), self.assertRaises(Hold):
            b.build(self.bundle,self.debs)
    def test_extra_tool_name_holds(self):
        self.cfg['tools'][0]['name']='unknown'
        with patch.object(b,'fixed_bundle',return_value=self.cfg),self.assertRaises(Hold):
            b.build(self.bundle,self.debs)
    def test_no_overwrite(self):
        output=self.root/'a.zip';output.write_bytes(b'keep')
        with patch('sys.stdout',new_callable=io.StringIO):
            self.assertEqual(b.main(['--bundle',str(self.bundle),'--debs',str(self.debs),'--output',str(output)]),1)
        self.assertEqual(output.read_bytes(),b'keep')
if __name__=='__main__':unittest.main()
