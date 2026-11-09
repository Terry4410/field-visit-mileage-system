#!/usr/bin/env python3
"""NONDEPLOY receipt store for a NEW, separately authorized Path B catalog attempt.

No Azure/SQL entrypoint. Write on the app's persistent /home volume, then read
in a separately authorized read-only recovery. NEVER reuse consumed R3 paths.
An operation's terminal receipt is unique and never overwritten or deleted.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat

ROOT = Path('/home/LogFiles')
PREFIX = 'fv-pathb-owner-catalog-r4-'
ALLOWED = frozenset({
    'EMPTY_CATALOG_CANDIDATE',
    'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED',
    'HOLD_SQL_LOGIN_OR_QUERY', 'HOLD_OPERATION', 'HOLD_PRECHECK',
    'HOLD_RESULT_SHAPE', 'HOLD_SQL_TARGET_OR_METADATA',
    'HOLD_PARTIAL_OR_INCONSISTENT', 'HOLD_RESULT_INCONSISTENT',
    'HOLD_DURABLE_ONCE_GUARD',
})
CONTRACT = 'FV_OWNER_CATALOG_DURABLE_RECEIPT_V1'


class ReceiptHold(Exception):
    pass


def _require(v):
    if not v:
        raise ReceiptHold('HOLD_RECEIPT')


def _paths(operation_id, root):
    _require(isinstance(operation_id, str) and re.fullmatch(r'[0-9a-f]{32}', operation_id))
    root = Path(root)
    _require(root.is_absolute() and not root.is_symlink() and root.is_dir())
    st = root.stat()
    _require(stat.S_ISDIR(st.st_mode) and st.st_uid == os.geteuid())
    name = PREFIX + operation_id + '.json'
    _require(len(name) <= 255)
    return root / name


def _canonical(payload):
    return (json.dumps(payload, sort_keys=True, separators=(',', ':'), ensure_ascii=True) + '\n').encode('ascii')


def _validate(record, operation_id, expected_candidate):
    _require(isinstance(record, dict) and set(record) == {
        'contract', 'operation_id', 'candidate_sha256', 'status', 'sql_attempted',
        'installer', 'ddl', 'retry',
    })
    _require(record['contract'] == CONTRACT and record['operation_id'] == operation_id)
    _require(isinstance(expected_candidate, str) and re.fullmatch(r'[0-9a-f]{64}', expected_candidate))
    _require(record['candidate_sha256'] == expected_candidate)
    _require(record['status'] in ALLOWED and type(record['sql_attempted']) is bool)
    _require(record['installer'] == 'NOT_RUN' and record['ddl'] == 'NO'
             and record['retry'] == 'FORBIDDEN')
    return record


def store(operation_id, candidate_sha, status, sql_attempted, root=ROOT):
    """Create exactly one durable, status-only receipt, O_EXCL + fsync.

    Filesystem sync provides best effort on mounted /home; no guarantee across
    provider outage. Caller MUST report HOLD if persistence fails, never replay.
    """
    path = _paths(operation_id, root)
    record = _validate({
        'contract': CONTRACT, 'operation_id': operation_id,
        'candidate_sha256': candidate_sha, 'status': status,
        'sql_attempted': sql_attempted, 'installer': 'NOT_RUN', 'ddl': 'NO',
        'retry': 'FORBIDDEN',
    }, operation_id, candidate_sha)
    raw = _canonical(record)
    _require(len(raw) < 2048)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(fd, 'wb') as f:
            f.write(raw)
            f.flush()
            os.fsync(f.fileno())
        # Flush directory entry as well. Some Azure mounted volumes cannot
        # guarantee this; failure is terminal HOLD, not permission to retry.
        dfd = os.open(Path(root), os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            os.fsync(dfd)
        finally:
            os.close(dfd)
    except Exception:
        # Never unlink even after uncertain persistence; quarantine the name.
        raise ReceiptHold('HOLD_RECEIPT_PERSISTENCE') from None
    return hashlib.sha256(raw).hexdigest()


def read_once(operation_id, candidate_sha, root=ROOT):
    """Read-only recovery; caller must have separate permission to access App.

    Missing/unknown is NOT evidence that SQL was never attempted.
    """
    path = _paths(operation_id, root)
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            _require(stat.S_ISREG(st.st_mode) and st.st_uid == os.geteuid()
                     and st.st_nlink == 1 and st.st_size < 2048)
            raw = f.read(2048)
        rec = json.loads(raw)
        _validate(rec, operation_id, candidate_sha)
        _require(_canonical(rec) == raw)
        return {'status': rec['status'], 'sql_attempted': rec['sql_attempted'],
                'receipt_sha256': hashlib.sha256(raw).hexdigest()}
    except (OSError, ValueError, UnicodeError, TypeError, ReceiptHold):
        raise ReceiptHold('HOLD_RECEIPT_UNKNOWN') from None


def main():
    print('OWNER_CATALOG_DURABLE_RECEIPT=HOLD_REVIEW_ONLY_NOT_EXECUTABLE')
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
