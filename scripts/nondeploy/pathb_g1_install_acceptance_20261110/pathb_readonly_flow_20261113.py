"""NONDEPLOY orchestration: durable claim -> one SSH frame -> sanitized receipt.

This is a dependency-injected library, NOT a live SSH/SQL entrypoint.
The transport must be independently authenticated, host-key pinned, deadline
bounded and supplied by a separately reviewed parent. The receipt parser must
reject raw/unexpected results. No Azure, subprocess or credential acquisition.
"""
from pathb_readonly_durable_once_20261113 import consume_once
from pathb_readonly_oneshot_transport_20261113 import OneShotTransport
from pathb_readonly_receipt_parser import parse_preflight

class ReadonlyFlowHold(Exception):
    pass

def execute_once(private_dir, token, nonce, transport, *, receipt_parser=parse_preflight):
    # Durable claim is consumed before even constructing the token frame.
    try:
        consume_once(private_dir)
        response = OneShotTransport(transport).dispatch(token, nonce)
        return receipt_parser(response)
    except Exception:
        # No raw SSH, SQL, JWT or parser error can reach operator output.
        raise ReadonlyFlowHold("READONLY_FLOW_HOLD") from None

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_EXECUTION")
