#!/usr/bin/env python3
"""Exact-root, no-write, no-network local filesystem presence gate.

This is NOT a remote operator, active-process scanner, cleanup authorization,
or a substitute for separately authorized Azure/SQL preflight. Suppress paths,
identity and error details in output. Existing original journal is untouched.
"""
import argparse
import json
import os
import re
import stat
from pathlib import Path

PREFIX = '/tmp/fv-p3-nonsql-'


def result(op, *, root_base='/tmp'):
    # Caller must separately prove which historical operation id is relevant.
    if re.fullmatch(r'[0-9a-f]{32}', op or '') is None or root_base != '/tmp':
        return 'HOLD_INPUT'
    root = Path(PREFIX + op)
    try:
        parent = os.lstat('/tmp')
        if not stat.S_ISDIR(parent.st_mode) or stat.S_ISLNK(parent.st_mode):
            return 'HOLD_PARENT'
        try:
            st = os.lstat(root)
        except FileNotFoundError:
            return 'ABSENT'
        if stat.S_ISLNK(st.st_mode) or not stat.S_ISDIR(st.st_mode):
            return 'HOLD_UNEXPECTED_TYPE'
        if st.st_uid != os.geteuid() or stat.S_IMODE(st.st_mode) != 0o700:
            return 'HOLD_UNEXPECTED_OWNER_MODE'
        return 'OWNED_ROOT_PRESENT'
    except OSError:
        return 'UNKNOWN'


def main(argv=None):
    p = argparse.ArgumentParser()
    p.add_argument('--operation-id', required=True)
    a = p.parse_args(argv)
    outcome = result(a.operation_id)
    print('FV_P3_OLD_ROOT=' + json.dumps({'status': outcome}, separators=(',', ':'), sort_keys=True))
    # ABSENT proves only directory absence, never no active process or remote
    # cleanup PASS; the independent install safety review remains mandatory.
    return 0 if outcome == 'ABSENT' else 2


if __name__ == '__main__':
    raise SystemExit(main())
