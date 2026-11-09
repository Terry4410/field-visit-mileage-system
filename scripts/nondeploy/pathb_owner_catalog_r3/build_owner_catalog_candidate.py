#!/usr/bin/env python3
"""Build deterministic NONDEPLOY candidate ZIP and binding JSON."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

from owner_catalog_azure_adapter_REVIEW_ONLY import (
    APP, BINDING_CONTRACT, CONTRACT, DATABASE, MARKER, MAX_DURATION, RELEASE,
    RG, SQL_SERVER,
)

FILES = ('owner_sql_catalog_child_REVIEW_ONLY.py',
         'pathb_catalog_readonly_REVIEW_ONLY.sql',
         'owner_catalog_durable_receipt_REVIEW_ONLY.py')
CONTROLS = ('owner_catalog_azure_adapter_REVIEW_ONLY.py',
            'owner_catalog_integration_REVIEW_ONLY.py',
            'owner_catalog_parent_REVIEW_ONLY.py')


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(',', ':')) + '\n').encode()


def member(name, raw):
    info = zipfile.ZipInfo(name, (2026, 11, 9, 0, 0, 0))
    info.compress_type = zipfile.ZIP_STORED
    info.create_system = 3
    info.external_attr = 0o100600 << 16
    return info, raw


def build(source, candidate_path, binding_path):
    records = {name: (source / name).read_bytes() for name in FILES}
    file_hashes = {name: sha(raw) for name, raw in records.items()}
    manifest = {
        'contract': CONTRACT, 'approved_release': RELEASE,
        'resource_group': RG, 'app': APP, 'sql_server': SQL_SERVER,
        'database': DATABASE, 'marker': MARKER, 'sql_calls_max': 1,
        'storage_puts_max': 1, 'ddl_dml': 'FORBIDDEN',
        'automatic_retry': False, 'files': file_hashes,
    }
    with zipfile.ZipFile(candidate_path, 'w') as archive:
        info, raw = member('manifest.json', canonical(manifest))
        archive.writestr(info, raw)
        for name in FILES:
            info, raw = member(name, records[name])
            archive.writestr(info, raw)
    candidate_raw = candidate_path.read_bytes()
    controls = {name: sha((source / name).read_bytes()) for name in CONTROLS}
    binding = {
        'contract': BINDING_CONTRACT, 'approved_release': RELEASE,
        'resource_group': RG, 'app': APP, 'sql_server': SQL_SERVER,
        'database': DATABASE, 'marker': MARKER,
        'max_duration_seconds': MAX_DURATION, 'max_ssh_sessions': 2,
        'max_storage_tokens': 1, 'max_sql_tokens': 1,
        'max_storage_puts': 1, 'max_sql_calls': 1,
        'automatic_retry': False, 'candidate_sha256': sha(candidate_raw),
        'files': file_hashes, 'control_files': controls,
    }
    binding_path.write_bytes(canonical(binding))
    return sha(candidate_raw), sha(binding_path.read_bytes())


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--binding', type=Path, required=True)
    args = parser.parse_args(argv)
    candidate_sha, binding_sha = build(args.source, args.candidate, args.binding)
    print('CANDIDATE_SHA256=' + candidate_sha)
    print('BINDING_SHA256=' + binding_sha)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
