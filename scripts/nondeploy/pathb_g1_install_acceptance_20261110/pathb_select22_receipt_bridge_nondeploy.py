"""NONDEPLOY two-sided SELECT22 status protocol (no CLI, SSH, SQL, or Azure).

Connect the existing read-only stdin frame, App-side one-shot SELECT and
redacted schema receipt to a STRICT status-only wire format. Never transmit
the original SQL row, SID, token, connection string, or error text.
All live transport, exact App instance binding and independently reviewed
staging remain OUTSIDE this module. No install path exists.
"""
import json

from pathb_readonly_stdin_frame_20261113 import decode_frame
from pathb_select22_app_once_candidate import run_once


PREFIX = b"FV_SELECT22_SANITIZED_V1="
EMPTY = {"status": "PASS", "sql_target": "PASS", "connection": "PASS",
         "schema": "EMPTY_CANDIDATE",
         "next_action": "SEPARATE_INSTALL_AUTH_REQUIRED"}
NONEMPTY = {"status": "PASS", "sql_target": "PASS", "connection": "PASS",
            "schema": "NONEMPTY_OR_PARTIAL",
            "next_action": "HOLD_MANUAL_RECONCILIATION"}
HOLD = {"status": "HOLD"}


class ReceiptBridgeHold(Exception):
    pass


def _validate(payload):
    if type(payload) is not dict:
        raise ReceiptBridgeHold("RECEIPT_SHAPE_HOLD")
    if payload not in (EMPTY, NONEMPTY, HOLD):
        raise ReceiptBridgeHold("RECEIPT_UNTRUSTED_HOLD")
    return payload


def encode_receipt(payload):
    """Single line, canonical redacted JSON, no arbitrary fields."""
    clean = _validate(payload)
    return PREFIX + json.dumps(clean, separators=(",", ":"), sort_keys=True).encode("ascii") + b"\n"


def parse_receipt(raw):
    if (type(raw) is not bytes or len(raw) > 256 or
            not raw.startswith(PREFIX) or not raw.endswith(b"\n") or
            raw.count(b"\n") != 1):
        raise ReceiptBridgeHold("RECEIPT_FRAMING_HOLD")
    try:
        def unique(pairs):
            value = {}
            for key, field in pairs:
                if key in value:
                    raise ValueError("duplicate")
                value[key] = field
            return value
        obj = json.loads(raw[len(PREFIX):-1].decode("ascii"),
                         object_pairs_hook=unique)
        return _validate(obj)
    except (ValueError, UnicodeError, TypeError):
        raise ReceiptBridgeHold("RECEIPT_UNTRUSTED_HOLD") from None


def app_receive_once(frame, expected_nonce, *, debs_dir, claim_dir,
                     invoke_sql=None):
    """Offline-tested App receiver: once-only fixed SELECT22, sanitized receipt.

    This is an injected *library* with NO live stdin/shell entrypoint.
    The caller must bind exact stage, instance, SSH host key, and durable
    operation claim before passing the confidential frame.
    """
    try:
        token = decode_frame(frame, expected_nonce)
        receipt = run_once(debs_dir=debs_dir, claim_dir=claim_dir,
                           sql_token=token, invoke_sql=invoke_sql)
        clean = {
            "status": "PASS", "sql_target": receipt.sql_target,
            "connection": receipt.connection, "schema": receipt.schema,
            "next_action": receipt.next_action,
        }
        return encode_receipt(clean)
    except Exception:
        # Never expose raw token, SQL row, session SID, or transport error.
        return encode_receipt(HOLD)


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_ENTRYPOINT")
