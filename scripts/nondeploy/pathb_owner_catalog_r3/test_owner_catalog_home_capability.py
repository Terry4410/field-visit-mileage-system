import importlib.util
from pathlib import Path
import tempfile
import unittest

p = Path(__file__).with_name('owner_catalog_home_capability_REVIEW_ONLY.py')
spec = importlib.util.spec_from_file_location('homeprobe', p)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class Tests(unittest.TestCase):
    def test_local_temp_directory_supported(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(m.inspect(d), 'DIRECTORY_FSYNC_SUPPORTED_ONLY')

    def test_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            a = Path(d) / 'l'
            a.symlink_to('/tmp')
            self.assertEqual(m.inspect(a), 'HOLD_TARGET')

    def test_missing_directory_rejected(self):
        self.assertEqual(m.inspect('/this-path-does-not-exist-12345'), 'HOLD_DIRECTORY_FSYNC_OR_ACCESS')

    def test_wrong_owner_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(m.inspect(d, uid=999999), 'HOLD_OWNER_OR_DIRECTORY')

    def test_no_directory_fsync_is_hold(self):
        with tempfile.TemporaryDirectory() as d:
            def boom(_): raise OSError('unsupported')
            self.assertEqual(m.inspect(d, fsync_fn=boom), 'HOLD_DIRECTORY_FSYNC_OR_ACCESS')

    def test_no_creation_or_azure_imports(self):
        source = p.read_text()
        for phrase in ['subprocess', 'urllib', 'requests', 'os.mkdir(', 'os.unlink(', 'os.remove(', 'os.rmdir(', 'os.O_CREAT']:
            self.assertNotIn(phrase, source)


if __name__ == '__main__': unittest.main()
