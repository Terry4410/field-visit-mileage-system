#!/usr/bin/env python3
"""Non-live orchestrator contract for Path B Owner catalog readback.

Integration seam only: a real Azure adapter must be independently reviewed and
approved. The CLI is deliberately non-executable. Existing transport and child
are not modified or unlocked.
"""
from dataclasses import dataclass
from enum import Enum
import hashlib
import re

class Hold(Exception):
    pass

class Phase(str, Enum):
    INITIAL='INITIAL'
    VERIFIED='VERIFIED'
    CLAIM_UNKNOWN='CLAIM_UNKNOWN'
    CLAIM_CONSUMED='CLAIM_CONSUMED'
    STAGED='STAGED'
    READY='READY'
    TOKEN_SENT='TOKEN_SENT'
    TERMINAL='TERMINAL'
    HOLD='HOLD'

TERMINALS=frozenset({'EMPTY_CATALOG_CANDIDATE', 'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED',
'HOLD_SQL_LOGIN_OR_QUERY','HOLD_OPERATION','HOLD_PRECHECK','HOLD_RESULT_SHAPE',
'HOLD_SQL_TARGET_OR_METADATA','HOLD_PARTIAL_OR_INCONSISTENT',
'HOLD_RESULT_INCONSISTENT','HOLD_DURABLE_ONCE_GUARD'})

@dataclass(frozen=True)
class Report:
    phase: str
    receipt: str
    consumed: bool
    sql_token_sent: bool
    cleanup: str

class Controller:
    """One-shot fail-closed sequence, using a separate, injected Azure adapter.

    Adapter contract: verify_owner/target must NOT request a token or run SQL;
    claim_blob uses If-None-Match:* atomically and returns True ONLY on HTTP 201;
    stage only transfers pinned bytes; ready must verify SSH host fingerprint and
    terminal no-echo ACK, and never run SQL; sql_token only after ready, sourced
    from authenticated Owner Azure CLI; transmit once to noecho TTY; receipt must
    be a fixed, authenticated child result; cleanup only removes verified owned
    temporary stage and token material, NEVER the durable claim or old artifacts.
    """
    def __init__(self, adapter, pinned_sha):
        if not re.fullmatch(r'[0-9a-f]{64}',pinned_sha):
            raise Hold('BAD_PIN')
        self.adapter=adapter
        self.sha=pinned_sha
        self.phase=Phase.INITIAL
        self.consumed=False
        self.token_sent=False
        self.started=False

    def run(self, payload):
        if self.started:
            raise Hold('NO_REPLAY')
        self.started=True
        result='HOLD_OPERATION'
        cleanup='NOT_STARTED'
        try:
            if hashlib.sha256(payload).hexdigest()!=self.sha:
                raise Hold('PAYLOAD_SHA')
            if self.adapter.verify_owner_target() is not True:
                raise Hold('OWNER_TARGET')
            self.phase=Phase.VERIFIED
            self.adapter.stage(payload)
            self.phase=Phase.STAGED
            if self.adapter.ready_noecho() is not True:
                raise Hold('NOECHO_HANDSHAKE')
            self.phase=Phase.READY
            # The durable service-side claim is deliberately created only after
            # the exact instance has staged the pinned payload and proved its
            # terminal is no-echo ready, but always before requesting a SQL
            # token or allowing a SQL connection.  An uncertain PUT consumes
            # the attempt and is never retried.
            self.phase=Phase.CLAIM_UNKNOWN
            self.consumed=True
            claimed=self.adapter.claim_blob(self.sha)
            if claimed is not True:
                raise Hold('CLAIM_NOT_PROVEN')
            self.phase=Phase.CLAIM_CONSUMED
            token=self.adapter.sql_token()
            try:
                if not isinstance(token,str) or not 100<=len(token)<=4096 or '\n' in token or '\r' in token:
                    raise Hold('TOKEN_SHAPE')
                # no persistence or token logging by controller; adapter may only
                # write to a pinned no-echo SSH TTY (never argv, file or logs).
                self.token_sent=True  # on unknown transmission, considered consumed
                self.phase=Phase.TOKEN_SENT
                self.adapter.transmit_once(token)
            finally:
                token=None
            receipt=self.adapter.receive_terminal()
            if receipt not in TERMINALS:
                raise Hold('RECEIPT_CONTRACT')
            self.phase=Phase.TERMINAL
            result=receipt
        except Exception:
            self.phase=Phase.HOLD
            result='HOLD_OPERATION'
        finally:
            # Cleanup best effort; never trigger a retry or clear consumed claim.
            try:
                cleanup='PASS' if self.adapter.cleanup_verified_owned() is True else 'HOLD'
            except Exception:
                cleanup='HOLD'
        return Report(self.phase.value,result,self.consumed,self.token_sent,cleanup)

def main():
    print('OWNER_CATALOG_INTEGRATION=HOLD_REVIEW_ONLY_NOT_EXECUTABLE')
    print('AZURE=NOT_CONNECTED; TOKEN=NOT_REQUESTED; SQL=NOT_RUN; BLOB=NOT_WRITTEN')
    return 1

if __name__=='__main__':
    raise SystemExit(main())
