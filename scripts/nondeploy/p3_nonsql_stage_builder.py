#!/usr/bin/env python3
"""NONDEPLOY P3 non-SQL stage builder; no SSH, Azure, token, SQL or installer."""
import argparse
import hashlib
import io
import os
from pathlib import Path
import tempfile
import zipfile
from p3_nonsql_abi_probe import fixed_bundle, regular, require, Hold, sha_file, EXPECTED_NAMES
from p3_nonsql_stage_handler import BOUND_SOURCE, REQUIRED, MAX_ZIP_BYTES, MAX_TOTAL_BYTES, MAX_MEMBER_BYTES, extract_stage

def build(bundle: Path, debs: Path):
    cfg = fixed_bundle(bundle)
    require(debs.is_dir() and not debs.is_symlink())
    files = {}
    for name in BOUND_SOURCE:
        p = bundle / name
        regular(p)
        require(p.stat().st_size <= MAX_MEMBER_BYTES)
        files[name] = p.read_bytes()
    for entry in cfg['tools']:
        name = entry['name']
        require(name in EXPECTED_NAMES)
        p = debs / (name + '.deb')
        regular(p)
        require(p.stat().st_size <= MAX_MEMBER_BYTES)
        require(sha_file(p) == entry['sha256'])
        files['debs/' + name + '.deb'] = p.read_bytes()
    require(set(files) == REQUIRED)
    require(sum(map(len, files.values())) <= MAX_TOTAL_BYTES)
    out = io.BytesIO()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_STORED) as z:
        for name in sorted(files):
            info = zipfile.ZipInfo(name)
            info.external_attr = (0o100600 << 16)
            z.writestr(info, files[name])
    data = out.getvalue()
    require(len(data) <= MAX_ZIP_BYTES)
    with tempfile.TemporaryDirectory(prefix='fv-p3-stage-build-', dir='/tmp') as tmp:
        extract_stage(data, Path(tmp), hashlib.sha256(data).hexdigest())
    return data

def main(argv=None):
    p = argparse.ArgumentParser(description='offline-only build; never SSH/SQL/DDL')
    p.add_argument('--bundle', type=Path, required=True)
    p.add_argument('--debs', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args(argv)
    try:
        require(not args.output.exists() and not args.output.is_symlink())
        require(args.output.parent.is_dir())
        result = build(args.bundle, args.debs)
        fd = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, 'wb') as f:
            f.write(result)
        print('P3_NONSQL_STAGE_BUILD=PASS_OFFLINE_ONLY')
        print('P3_STAGE_SHA256=' + hashlib.sha256(result).hexdigest())
        print('REMOTE_SSH_TOKEN_SQL=NOT_RUN')
        return 0
    except Exception:
        print('P3_NONSQL_STAGE_BUILD=HOLD')
        print('REMOTE_SSH_TOKEN_SQL=NOT_RUN')
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
