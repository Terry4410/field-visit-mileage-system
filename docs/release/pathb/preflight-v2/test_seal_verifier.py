#!/usr/bin/env python3
"""Synthetic-only negative cases. No Azure, SQL, real tokens or private Blob."""
import copy
import hashlib
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from seal_verifier import verify


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(obj):
    return json.dumps(obj, sort_keys=True, separators=(',', ':')).encode()


class SealFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.package = self.root / 'arbitrary-local-download.zip'
        self.seal = self.root / 'matching-seal.json'
        self.config = {
            'release': 'synthetic-release', 'server': 'sql-fieldvisit-jpe-pathb-uat.database.windows.net',
            'database': 'db-fieldvisit-pathb-uat',
            'seal_package_file': 'pathb-sql-install-package.zip',
            'blob_object_name': 'pathb-sql-install-package-d6c220877b9976b6-20261027.zip',
        }
        self.files = {'install.py': b'SYNTHETIC INSTALLER\n'}
        layers = [{'file': 'pathb-uat-sql/layer-%02d.sql' % n,
                   'sha256': sha(('SYNTHETIC SQL %d' % n).encode())} for n in range(14)]
        self.config['layers'] = layers
        for n, layer in enumerate(layers):
            self.files[layer['file']] = ('SYNTHETIC SQL %d' % n).encode()
        for n in range(17):
            self.files['evidence/filler-%02d.txt' % n] = ('SYNTHETIC %d' % n).encode()
        plan = {'candidateSourceSha': self.config['release'], 'pathb-uat': {
            'server': self.config['server'], 'database': self.config['database'],
            'schemaProfile': 'PathBNewEmpty', 'sequence': layers}}
        self.files['standalone-install-manifest.json'] = encoded(plan)
        self._build()

    def tearDown(self):
        self.temp.cleanup()

    def _build(self, *, bad_index_size=False, bad_member=None):
        files = dict(self.files)
        index = [{'path': n, 'sha256': sha(data), 'bytes': len(data)}
                 for n, data in sorted(files.items())]
        if bad_index_size:
            index[0]['bytes'] += 1
        files['PACKAGE-CHECKSUMS.json'] = encoded({'files': index})
        if bad_member is not None:
            files[bad_member] += b'TAMPER'
        with zipfile.ZipFile(self.package, 'w', zipfile.ZIP_STORED) as archive:
            for name, data in sorted(files.items()):
                archive.writestr(name, data)
        raw = self.package.read_bytes()
        record = {'packageFile': self.config['seal_package_file'],
                  'packageSha256': sha(raw), 'packageBytes': len(raw),
                  'sourceSha': self.config['release'],
                  'manifestSha256': sha(files['standalone-install-manifest.json']),
                  'sealedFiles': [{'path': n, 'sha256': sha(data)}
                                  for n, data in sorted(files.items())]}
        self.seal.write_bytes(encoded(record))
        self.config.update(package_sha256=sha(raw), seal_sha256=sha(self.seal.read_bytes()),
                           manifest_sha256=sha(files['standalone-install-manifest.json']),
                           installer_sha256=sha(files['install.py']))

    def _verify(self):
        return verify(self.package, self.seal, self.config, self.config['blob_object_name'])

    def _edit_seal(self, change):
        record = json.loads(self.seal.read_bytes())
        change(record)
        self.seal.write_bytes(encoded(record))
        self.config['seal_sha256'] = sha(self.seal.read_bytes())

    def test_different_blob_inner_and_download_names_pass(self):
        evidence = self._verify()
        self.assertEqual((evidence['seal_files'], evidence['checksum_files'], evidence['sql_layers']),
                         (34, 33, 14))

    def test_package_and_seal_hash_tampering_rejected(self):
        self.package.write_bytes(self.package.read_bytes() + b'TAMPER')
        with self.assertRaisesRegex(ValueError, 'PACKAGE_HASH'): self._verify()
        self._build()
        self.seal.write_bytes(self.seal.read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'SEAL_HASH'): self._verify()

    def test_size_and_manifest_binding_rejected(self):
        self._edit_seal(lambda r: r.update(packageBytes=r['packageBytes'] + 1))
        with self.assertRaisesRegex(ValueError, 'SEAL_PACKAGE_SIZE'): self._verify()
        self._build()
        self._edit_seal(lambda r: r.update(manifestSha256='0' * 64))
        with self.assertRaisesRegex(ValueError, 'MANIFEST_HASH'): self._verify()

    def test_member_content_and_path_rejected(self):
        self._build(bad_member='evidence/filler-00.txt')
        with self.assertRaisesRegex(ValueError, 'INDEX_FILE_HASH_SIZE'): self._verify()
        self._build()
        self._edit_seal(lambda r: r['sealedFiles'][0].update(sha256='0' * 64))
        with self.assertRaisesRegex(ValueError, 'SEAL_FILE_HASH'): self._verify()
        self._build()
        self._edit_seal(lambda r: r['sealedFiles'][0].update(path='../escape'))
        with self.assertRaisesRegex(ValueError, 'SEAL_MEMBERS'): self._verify()

    def test_index_size_and_sql_layer_rejected(self):
        self._build(bad_index_size=True)
        with self.assertRaisesRegex(ValueError, 'INDEX_FILE_HASH_SIZE'): self._verify()
        self._build(bad_member='pathb-uat-sql/layer-00.sql')
        with self.assertRaisesRegex(ValueError, 'SQL_LAYER_HASH'): self._verify()

    def test_cross_version_and_target_rejected(self):
        with self.assertRaisesRegex(ValueError, 'BLOB_NAME'):
            verify(self.package, self.seal, self.config, 'different-version.zip')
        other = copy.deepcopy(self.config)
        other['release'] = 'wrong-release'
        with self.assertRaisesRegex(ValueError, 'SEAL_RELEASE'):
            verify(self.package, self.seal, other, other['blob_object_name'])
        other = copy.deepcopy(self.config)
        other['database'] = 'db-fieldvisit-uat'
        with self.assertRaisesRegex(ValueError, 'MANIFEST_TARGET'):
            verify(self.package, self.seal, other, other['blob_object_name'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
