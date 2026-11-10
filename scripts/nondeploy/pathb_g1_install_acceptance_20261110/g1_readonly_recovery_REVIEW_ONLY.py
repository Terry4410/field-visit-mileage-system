#!/usr/bin/env python3
"""Read-only interpretation of a previously consumed G1 install claim/ledger.

Pure injection: claim_body is supplied from a separately-authorized read-only
Blob GET; `request` can only GET the fixed 16 immutable call-pairs and final
acceptance. Never connects, acquires tokens, retries, repairs, or writes.
"""
import hashlib
import json
import re

import g1_blob_call_journal_REVIEW_ONLY as ledger
from p3_install_gates import PAYLOAD_SHA256, RELEASE, SERVER, DATABASE, INSTALLER_SHA
from p3_blob_guard import CONTRACT as CLAIM_CONTRACT, RUN_ID


class RecoveryHold(Exception):
    pass


def _require(ok):
    if not ok:raise RecoveryHold('HOLD_G1_RECOVERY_UNCERTAIN')


def verify_original_claim(raw, operation_id, artifact_sha, instance_sha):
    _require(isinstance(raw,bytes) and 0 < len(raw) < 4096)
    _require(re.fullmatch(r'[a-f0-9]{32}',str(operation_id)) is not None)
    _require(re.fullmatch(r'[a-f0-9]{64}',str(artifact_sha)) is not None)
    _require(re.fullmatch(r'[a-f0-9]{64}',str(instance_sha)) is not None)
    try:obj=json.loads(raw)
    except Exception:raise RecoveryHold('HOLD_G1_RECOVERY_UNCERTAIN') from None
    _require(isinstance(obj,dict) and
        obj.get('contract')==CLAIM_CONTRACT and
        obj.get('operation_id')==operation_id and
        obj.get('artifact_sha256')==artifact_sha and
        obj.get('instance_sha256')==instance_sha and
        obj.get('release')==RELEASE and
        obj.get('server')==SERVER and obj.get('database')==DATABASE and
        obj.get('installer_sha256')==INSTALLER_SHA and
        obj.get('run_id')==RUN_ID and
        obj.get('status')=='CONSUMED_BEFORE_INSTALLER_SQL')
    return hashlib.sha256(raw).hexdigest()


def read_progress(operation_id, claim_raw, artifact_sha, instance_sha, request):
    """Return only a fixed status/count. Never infer that unobserved SQL did not run."""
    claim_sha=verify_original_claim(claim_raw,operation_id,artifact_sha,instance_sha)
    # Positive evidence from 16 fixed call pairs, before postcheck.
    successes=0
    for i,want in enumerate(PAYLOAD_SHA256):
        before=ledger.construct(operation_id,claim_sha,i,'BEFORE',want)
        after=ledger.construct(operation_id,claim_sha,i,'AFTER',want,'SUCCESS')
        try:
            if ledger.recover_exact(before,request)!=before.digest:
                raise RecoveryHold('HOLD_G1_RECOVERY_UNCERTAIN')
            if ledger.recover_exact(after,request)!=after.digest:
                raise RecoveryHold('HOLD_G1_RECOVERY_UNCERTAIN')
        except (RecoveryHold,ledger.JournalHold):
            return {'status':'HOLD_IRREVERSIBLE_OR_UNKNOWN',
                    'externally_confirmed_successful_calls':successes,
                    'next_unconfirmed_index':i,'sql_replay':'FORBIDDEN'}
        successes+=1
    # POSTCHECK cannot be guessed from 16 successful calls: must have a valid
    # actual fingerprint observed after the original installed schema readback.
    prefix=ledger.PREFIX+operation_id+'/postcheck.json'
    try:
        status,_etag,raw=request('GET',prefix,b'',{})
        _require(status==200 and isinstance(raw,bytes) and len(raw)<1024)
        rec=json.loads(raw)
        _require(isinstance(rec,dict) and
                 rec.get('contract')==ledger.CONTRACT and
                 rec.get('operation_id')==operation_id and
                 rec.get('claim_sha256')==claim_sha and
                 rec.get('installer_sha256')==INSTALLER_SHA and
                 rec.get('release')==RELEASE and
                 rec.get('server')==SERVER and rec.get('database')==DATABASE and
                 rec.get('sql_calls')==19 and rec.get('tables')==69 and
                 rec.get('schema_versions')==3 and
                 re.fullmatch(r'[0-9a-f]{64}',str(rec.get('schema_fingerprint_sha256',''))) is not None and
                 rec.get('status')=='SCHEMA_VERIFIED')
        expected=ledger.construct_postcheck(operation_id,claim_sha,
                                              rec['schema_fingerprint_sha256'])
        _require(raw==expected.payload)
    except Exception:
        return {'status':'HOLD_POSTCHECK_UNKNOWN',
                'externally_confirmed_successful_calls':16,
                'sql_replay':'FORBIDDEN'}
    return {'status':'SQL_SCHEMA_VERIFIED_EVIDENCE',
            'externally_confirmed_successful_calls':16,
            'schema_fingerprint_sha256':rec['schema_fingerprint_sha256'],
            'sql_replay':'FORBIDDEN'}


def main():
    print('G1_RECOVERY=REVIEW_ONLY_NO_NETWORK')
    return 1

if __name__=='__main__':raise SystemExit(main())
