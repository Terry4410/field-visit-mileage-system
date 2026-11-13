"""NONDEPLOY pure-code framing for a future stdin-only read-only SSH channel.

No live entrypoint, subprocess, network, Azure, or token acquisition.
This module NEVER prints or persists a token. Framing is not authentication:
the caller must separately validate Azure-issued JWT and SSH host key.
"""
import json
import re

class FrameHold(Exception):
    pass

TOKEN_PATTERN = re.compile(r"[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\Z")
NONCE_PATTERN = re.compile(r"[a-f0-9]{32}\Z")
PROTOCOL = "FV_PATHB_READONLY_SQL_V1"

def encode_frame(token, nonce):
    if (not isinstance(token, str) or len(token) > 16384
            or not TOKEN_PATTERN.fullmatch(token)):
        raise FrameHold("TOKEN_SHAPE_HOLD")
    if not isinstance(nonce, str) or not NONCE_PATTERN.fullmatch(nonce):
        raise FrameHold("NONCE_SHAPE_HOLD")
    return (json.dumps({"protocol": PROTOCOL, "nonce": nonce,
                        "sql_token": token}, separators=(",", ":")) + "\n").encode("ascii")

def _unique_fields(pairs):
    obj = {}
    for key, value in pairs:
        if key in obj:
            raise ValueError("duplicate field")
        obj[key] = value
    return obj

def decode_frame(raw, expected_nonce):
    if not isinstance(raw, bytes) or len(raw) > 17000 or not raw.endswith(b"\n"):
        raise FrameHold("FRAME_SHAPE_HOLD")
    try:
        obj = json.loads(raw.decode("ascii"), object_pairs_hook=_unique_fields)
    except (UnicodeError, ValueError) as exc:
        raise FrameHold("FRAME_PARSE_HOLD") from None
    if (not isinstance(obj, dict)
            or set(obj) != {"protocol", "nonce", "sql_token"}
            or obj["protocol"] != PROTOCOL
            or obj["nonce"] != expected_nonce):
        raise FrameHold("FRAME_BINDING_HOLD")
    # Revalidate before returning to the isolated caller; never log the object.
    encode_frame(obj["sql_token"], obj["nonce"])
    return obj["sql_token"]

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_TRANSPORT")
