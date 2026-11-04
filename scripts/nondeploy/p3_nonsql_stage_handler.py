#!/usr/bin/env python3
"""NONDEPLOY: strictly bounded local receiver for a future approved P3 read-only stage.

No SSH server/client, network, Azure, tokens, SQL, DDL or permanent staging.
An independently authorized transport must deliver a stage ZIP before invocation.
"""
import argparse
import hashlib
import io
import os
from pathlib import Path, PurePosixPath
import re
import stat
import tempfile
import zipfile
from p3_nonsql_abi_probe import probe, Hold

REQUIRED_TOOLS = ('msodbcsql18', 'mssql-tools18', 'libltdl7', 'libodbc2', 'libodbcinst2')
MAX_ZIP_BYTES = 128 * 1024 * 1024
MAX_MEMBER_BYTES = 40 * 1024 * 1024
MAX_TOTAL_BYTES = 160 * 1024 * 1024
MAX_ENTRIES = 32
BOUND_SOURCE = frozenset({
    'executor_config.json', 'p3_cloudshell_parent.py', 'p3_app_installer_runner.py',
    'p3_blob_guard.py', 'p3_install_gates.py', 'p3_ssh_stage.py',
    'p3_ssh_transport_contract.py', 'seal_verifier.py', 'token_adapter.py',
    'frozen_install.py.snapshot', 'preflight.sql', 'postcheck.sql',
    'schema-fingerprint.sql', 'binding-index.json',
})
# These are only SHA-verified bytes. They are NEVER executed or imported.
REQUIRED = BOUND_SOURCE | frozenset('debs/' + x + '.deb' for x in REQUIRED_TOOLS)

def require(ok):
    if not ok:
        raise Hold()

def extract_stage(blob, destination, expected_sha):
    require(len(blob) <= MAX_ZIP_BYTES)
    require(re.fullmatch(r'[0-9a-f]{64}', expected_sha or '') is not None)
    require(hashlib.sha256(blob).hexdigest() == expected_sha)
    with zipfile.ZipFile(io.BytesIO(blob), 'r') as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= MAX_ENTRIES)
        names = [x.filename for x in entries]
        require(len(set(names)) == len(names))
        require(set(names) == REQUIRED)
        require(sum(x.file_size for x in entries) <= MAX_TOTAL_BYTES)
        for item in entries:
            require(item.filename in REQUIRED)
            p = PurePosixPath(item.filename)
            require(not p.is_absolute() and '..' not in p.parts)
            require(item.file_size <= MAX_MEMBER_BYTES)
            require(item.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED))
            mode = item.external_attr >> 16
            require(not stat.S_ISLNK(mode) and not stat.S_ISDIR(mode))
            require(not (item.flag_bits & 1))
        for item in entries:
            target = destination.joinpath(*PurePosixPath(item.filename).parts)
            require(target.resolve().is_relative_to(destination.resolve()))
            target.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
            fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'wb') as out, archive.open(item) as src:
                count = 0
                while True:
                    data = src.read(1024 * 1024)
                    if not data:
                        break
                    count += len(data)
                    require(count <= item.file_size and count <= MAX_MEMBER_BYTES)
                    out.write(data)
                require(count == item.file_size)

def run(stage, expected_sha):
    require(stage.is_file() and not stage.is_symlink() and stage.stat().st_nlink == 1)
    require(stage.stat().st_size <= MAX_ZIP_BYTES)
    with tempfile.TemporaryDirectory(prefix='fv-p3-nonsql-stage-', dir='/tmp') as td:
        root = Path(td)
        os.chmod(root, 0o700)
        extract_stage(stage.read_bytes(), root, expected_sha)
        # Child module rechecks the fixed P3 index/config SHA and five .deb SHA.
        probe(root, root / 'debs')
    return True

def main(argv=None):
    ap = argparse.ArgumentParser(description='NONDEPLOY LOCAL P3 STAGE CHECK; NO SSH/SQL')
    ap.add_argument('--stage', type=Path, required=True)
    ap.add_argument('--expected-sha256', required=True)
    args = ap.parse_args(argv)
    try:
        run(args.stage, args.expected_sha256)
        print('P3_NONSQL_STAGE=PASS_STATIC_ONLY')
        print('SSH_TOKEN_SQL_MARKER=NOT_RUN')
        return 0
    except Exception:
        print('P3_NONSQL_STAGE=HOLD')
        print('SSH_TOKEN_SQL_MARKER=NOT_RUN')
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
