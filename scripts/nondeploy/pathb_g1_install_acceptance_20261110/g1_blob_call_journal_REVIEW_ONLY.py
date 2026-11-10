#!/usr/bin/env python3
"""Strict, NONDEPLOY, external append-by-unique-object journal for the ORIGINAL
Path B installer 16 SQL client calls. Must be integrated behind a new explicit
Owner DDL approval; this module has NO live executable entrypoint.

One immutable conditional-creation Blob per BEFORE/AFTER phase. A missing AFTER
never means SQL did not execute. A confirmed BEFORE prohibits SQL replay.
"""
import hashlib
import json
import re
from dataclasses import dataclass

RELEASE = 'd6c220877b9976b6478d85b5171d4e06293595ae'
SERVER = 'sql-fieldvisit-jpe-pathb-uat.database.windows.net'
DATABASE = 'db-fieldvisit-pathb-uat'
INSTALLER_SHA256 = 'b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f'
PREFIX = 'operator-once/pathb-g1-install-call-journal/'
BLOB_ACCOUNT = 'stfvsc261026a'
CONTAINER = 'schema-evidence'
CONTRACT = 'FIELDVISIT_PATHB_G1_INSTALL_CALL_LEDGER_V1'


class JournalHold(Exception):
    pass


def _check(ok):
    if not ok:
        raise JournalHold('HOLD_G1_JOURNAL')


def _canonical(obj):
    return (json.dumps(obj, sort_keys=True, separators=(',', ':'), ensure_ascii=True) + '\n').encode('ascii')


@dataclass(frozen=True)
class Receipt:
    key: str
    payload: bytes
    digest: str


def construct(operation_id, claim_sha256, index, phase, sql_input_sha256, exit_status=None):
    """Construct exact allowlisted nonsecret receipt. No raw SQL/errors/tokens."""
    _check(type(operation_id) is str and re.fullmatch('[0-9a-f]{32}', operation_id))
    _check(type(claim_sha256) is str and re.fullmatch('[0-9a-f]{64}', claim_sha256))
    _check(type(sql_input_sha256) is str and re.fullmatch('[0-9a-f]{64}', sql_input_sha256))
    _check(type(index) is int and 0 <= index < 16)
    _check(phase in ('BEFORE', 'AFTER'))
    _check((phase == 'BEFORE' and exit_status is None) or
           (phase == 'AFTER' and exit_status in ('SUCCESS', 'FAIL', 'UNKNOWN')))
    # A common operation ID is *not* a new attempt permission.
    key = PREFIX + operation_id + '/%02d-' % index + phase.lower() + '.json'
    obj = {'contract': CONTRACT, 'operation_id': operation_id,
           'claim_sha256': claim_sha256, 'release': RELEASE,
           'server': SERVER, 'database': DATABASE,
           'installer_sha256': INSTALLER_SHA256,
           'index': index, 'phase': phase,
           'sql_input_sha256': sql_input_sha256}
    if phase == 'AFTER':
        obj['exit_status'] = exit_status
    raw = _canonical(obj)
    _check(len(raw) < 1024)
    return Receipt(key, raw, hashlib.sha256(raw).hexdigest())


def construct_postcheck(operation_id, claim_sha256, fingerprint_sha256):
    """Immutable SQL-only acceptance receipt, after full 19-call postcheck.

    Cleanup is separate: this attests SQL validation, not App deployment/UAT GO.
    """
    _check(type(operation_id) is str and re.fullmatch('[0-9a-f]{32}', operation_id))
    _check(type(claim_sha256) is str and re.fullmatch('[0-9a-f]{64}', claim_sha256))
    _check(type(fingerprint_sha256) is str and re.fullmatch('[0-9a-f]{64}', fingerprint_sha256))
    key = PREFIX + operation_id + '/postcheck.json'
    raw = _canonical({'contract':CONTRACT,'operation_id':operation_id,
            'claim_sha256':claim_sha256,'release':RELEASE,
            'server':SERVER,'database':DATABASE,'installer_sha256':INSTALLER_SHA256,
            'sql_calls':19,'tables':69,'schema_versions':3,
            'schema_fingerprint_sha256':fingerprint_sha256,
            'status':'SCHEMA_VERIFIED'})
    _check(len(raw)<1024)
    return Receipt(key,raw,hashlib.sha256(raw).hexdigest())


def persist_exact(receipt, request):
    """Exactly one conditional PUT + one GET readback, no retries.

    request(method, key, body, headers) -> (http_status, etag, response_body).
    A non-201 PUT or absent matching GET is terminal HOLD, NEVER permission
    for a caller to attempt the SQL operation again.
    """
    _check(isinstance(receipt, Receipt) and receipt.key.startswith(PREFIX))
    headers = {'If-None-Match': '*', 'x-ms-blob-type': 'BlockBlob',
               'Content-Type': 'application/json'}
    try:
        status, etag, body = request('PUT', receipt.key, receipt.payload, headers)
    except Exception:
        raise JournalHold('HOLD_G1_JOURNAL_UNCERTAIN') from None
    if status != 201 or not isinstance(etag, str) or not etag:
        raise JournalHold('HOLD_G1_JOURNAL_UNCERTAIN')
    try:
        read_status, read_etag, observed = request('GET', receipt.key, b'', {})
    except Exception:
        raise JournalHold('HOLD_G1_JOURNAL_UNCERTAIN') from None
    if read_status != 200 or read_etag != etag or observed != receipt.payload:
        raise JournalHold('HOLD_G1_JOURNAL_UNCERTAIN')
    return receipt.digest


def recover_exact(receipt, request):
    """Read-only recovery. A missing or mismatched blob is UNKNOWN, NOT NO SQL."""
    _check(isinstance(receipt, Receipt) and receipt.key.startswith(PREFIX))
    try:
        status, _etag, raw = request('GET', receipt.key, b'', {})
    except Exception:
        raise JournalHold('HOLD_G1_JOURNAL_RECOVERY_UNKNOWN') from None
    if status != 200 or raw != receipt.payload:
        raise JournalHold('HOLD_G1_JOURNAL_RECOVERY_UNKNOWN')
    return receipt.digest


def main():
    print('G1_BLOB_CALL_JOURNAL=NONDEPLOY_REVIEW_ONLY')
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
