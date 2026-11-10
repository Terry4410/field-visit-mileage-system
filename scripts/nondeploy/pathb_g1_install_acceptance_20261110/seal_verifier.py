#!/usr/bin/env python3
"""Offline verifier for the immutable Path B ZIP and its matching seal.

The seal's packageFile is the original package basename.  The private Blob
object has a versioned name, and the local download may have any filename.
These three names are deliberately bound separately.
"""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path, PurePosixPath


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def safe_name(name):
    return (isinstance(name, str) and name != '' and '\\' not in name
            and not PurePosixPath(name).is_absolute()
            and all(part not in ('.', '..') for part in name.split('/'))
            and str(PurePosixPath(name)) == name)


def verify(package, seal, config, blob_object_name):
    """Return count-only evidence. No network, SQL, writes, or artifact repair."""
    require(blob_object_name == config['blob_object_name'], 'BLOB_NAME')
    raw = Path(package).read_bytes()
    seal_raw = Path(seal).read_bytes()
    require(sha(raw) == config['package_sha256'], 'PACKAGE_HASH')
    require(sha(seal_raw) == config['seal_sha256'], 'SEAL_HASH')
    record = json.loads(seal_raw)
    require(record['packageFile'] == config['seal_package_file'], 'SEAL_INNER_NAME')
    require(record['packageSha256'] == sha(raw), 'SEAL_PACKAGE_HASH')
    require(record['packageBytes'] == len(raw), 'SEAL_PACKAGE_SIZE')
    require(record['sourceSha'] == config['release'], 'SEAL_RELEASE')
    with zipfile.ZipFile(package) as archive:
        infos = archive.infolist()
        names = [item.filename for item in infos]
        require(len(names) == len(set(names)) == 34 and all(safe_name(n) for n in names)
                and all(not item.is_dir() for item in infos), 'ZIP_MEMBERS')
        manifest = archive.read('standalone-install-manifest.json')
        require(sha(manifest) == config['manifest_sha256'] == record['manifestSha256'],
                'MANIFEST_HASH')
        plan = json.loads(manifest)
        require(plan['candidateSourceSha'] == config['release'], 'MANIFEST_RELEASE')
        mode = plan['pathb-uat']
        require((mode['server'], mode['database'], mode['schemaProfile']) ==
                (config['server'], config['database'], 'PathBNewEmpty'), 'MANIFEST_TARGET')
        require(sha(archive.read('install.py')) == config['installer_sha256'],
                'INSTALLER_HASH')
        sequence = mode['sequence']
        require(len(sequence) == 14 and
                [{'file': x['file'], 'sha256': x['sha256']} for x in sequence]
                == config['layers'], 'SQL_ORDER')
        require(all(sha(archive.read(x['file'])) == x['sha256'] for x in sequence),
                'SQL_LAYER_HASH')
        sealed = record['sealedFiles']
        require(len(sealed) == 34 and
                {x['path'] for x in sealed} == set(names) and
                len({x['path'] for x in sealed}) == 34, 'SEAL_MEMBERS')
        require(all(safe_name(x['path']) and sha(archive.read(x['path'])) == x['sha256']
                    for x in sealed), 'SEAL_FILE_HASH')
        index = json.loads(archive.read('PACKAGE-CHECKSUMS.json'))['files']
        require(len(index) == 33 and len({x['path'] for x in index}) == 33 and
                {x['path'] for x in index} == set(names) - {'PACKAGE-CHECKSUMS.json'},
                'INDEX_MEMBERS')
        require(all(safe_name(x['path']) and
                    sha(archive.read(x['path'])) == x['sha256'] and
                    len(archive.read(x['path'])) == x['bytes'] for x in index),
                'INDEX_FILE_HASH_SIZE')
    return {'package': 'PASS', 'seal_files': 34, 'checksum_files': 33,
            'installer': 'PASS', 'sql_layers': 14, 'release': config['release']}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--package', required=True)
    p.add_argument('--seal', required=True)
    p.add_argument('--config', required=True)
    p.add_argument('--blob-object-name', required=True)
    args = p.parse_args()
    try:
        result = verify(args.package, args.seal,
                        json.loads(Path(args.config).read_bytes()),
                        args.blob_object_name)
        print('OFFLINE_ORIGINAL_BINDING_PASS ' + json.dumps(result, sort_keys=True))
    except Exception as error:
        print('OFFLINE_ORIGINAL_BINDING_HOLD ' +
              (str(error) if isinstance(error, ValueError) else 'UNKNOWN'))
        raise SystemExit(1)
