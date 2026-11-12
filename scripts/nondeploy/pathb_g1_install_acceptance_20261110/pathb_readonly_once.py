"""Isolated, NONDEPLOY read-only execution orchestration.

No live entrypoint. A future separately approved executor must inject a single
reviewed callable. This module cannot spawn, connect, retrieve tokens, install,
write Blob, or replay a Claim. The injected callable is invoked at most once.
"""
from pathlib import Path
from pathb_readonly_isolated_gate import verify, HERE
from pathb_readonly_sql_contract import sqlcmd_argv
from pathb_readonly_receipt_parser import parse_preflight, ReceiptHold

class ExecutionHold(Exception):
    pass

def evaluate_once(invoke, binary, token_path, root=HERE):
    """Return sanitized receipt, never raw SQL output or secrets."""
    if not callable(invoke):
        raise ExecutionHold('EXECUTOR_NOT_INJECTED')
    try:
        pins=verify(root)
        if pins['READONLY_OFFLINE'] if 'READONLY_OFFLINE' in pins else False:
            raise ExecutionHold('PIN_CONTRACT_HOLD')
        sql=(Path(root)/'preflight.sql').read_bytes()
        args=sqlcmd_argv(binary, token_path, sql)
        # The transport must be supplied by an independent authorization boundary.
        # Never retry, even after an exception or ambiguous completion.
        raw=invoke(tuple(args), sql)
        return parse_preflight(raw)
    except Exception as exc:
        raise ExecutionHold('READONLY_EXECUTION_HOLD') from exc

if __name__ == '__main__':
    raise SystemExit('NONDEPLOY_NO_LIVE_ENTRYPOINT')
