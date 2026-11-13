"""NONDEPLOY App-side read-only SSH stdin receiver.

Pure injected runner: never launches sqlcmd, opens Azure, or obtains tokens.
The caller must separately establish a pinned authenticated SSH channel and
inject a reviewed, bounded SQL runner. No executable CLI entrypoint.
"""
from pathb_readonly_stdin_frame_20261113 import decode_frame
from pathb_readonly_receipt_parser import parse_preflight

class ReceiverHold(Exception):
    pass

def receive_once(frame, nonce, sql_runner):
    if not callable(sql_runner):
        raise ReceiverHold("SQL_RUNNER_NOT_INJECTED")
    try:
        token = decode_frame(frame, nonce)
        # Runner MUST use the fixed SHA-verified preflight, isolated sqlcmd,
        # ephemeral credential handling, a hard deadline and no retry.
        raw = sql_runner(token)
        if not isinstance(raw, (bytes, str)) or len(raw) > 16384:
            raise ReceiverHold("SQL_OUTPUT_SHAPE")
        receipt = parse_preflight(raw)
        # Return only a sanitized structured receipt; raw output never escapes.
        return receipt
    except Exception:
        raise ReceiverHold("RECEIVER_HOLD") from None

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_SQL_RECEIVER")
