"""Integrated SELECT22 Blob-first protocol SIMULATOR — NONDEPLOY ONLY.

Uses actual frozen Claim/OnceGuard and stdin framing/receipt code with a
hardcoded synthetic in-memory Storage transport. No external token provider,
network, Azure, SSH, App file read, SQL execution, installer or CLI exists.
Critically, this is NOT and cannot become a live Parent by supplying flags.
"""
from pathb_select22_storage_once_nondeploy import prepare, OnceGuard, Response
from pathb_readonly_stdin_frame_20261113 import encode_frame, decode_frame
from pathb_select22_receipt_bridge_nondeploy import (
    EMPTY, HOLD, encode_receipt, parse_receipt,
)
from pathb_select22_owner_shared_parent_nondeploy import plan, review_frozen, STEPS

NONCE = "0123456789abcdef0123456789abcdef"
FAKE_TOKEN = "synthetic" + "." + "only" + "." + "never-live"
INSTANCE_HASH = "2" * 64
SCENARIOS = frozenset({"success_empty", "put_timeout", "readback_mismatch",
                       "receiver_hold", "bad_nonce"})


class SimulationHold(Exception):
    pass


def simulate(scenario):
    """Offline-only fixed trace; no injection seam or live operation."""
    if type(scenario) is not str or scenario not in SCENARIOS:
        raise SimulationHold("SIMULATION_SCENARIO_HOLD")
    review_frozen(plan())
    events = []
    saved = {"payload": None}

    def fake_storage(method, account, container, key, headers, body):
        # Fixed synthetic response; no network or bearer token possible.
        events.append(method)
        if method == "PUT":
            assert headers["If-None-Match"] == "*"
            if scenario == "put_timeout":
                raise TimeoutError("synthetic only")
            saved["payload"] = body
            return Response(201, '"synthetic-etag"')
        if method == "GET":
            assert headers == {"If-Match": '"synthetic-etag"'}
            data = saved["payload"]
            if scenario == "readback_mismatch":
                data = b"synthetic-corruption"
            return Response(200, '"synthetic-etag"', data)
        raise AssertionError("invalid synthetic method")

    guard = OnceGuard(fake_storage)
    # Intent is prepared in RAM using original pinned reference; NOT a claim.
    intent = prepare(stage_sha256=plan().stage_sha,
                     instance_sha256=INSTANCE_HASH)
    events.append("SYNTHETIC_READY")

    try:
        proof = guard.consume(intent)
        if (proof["claim"] != "PASS" or proof["readback"] != "PASS"
                or proof["sql_token_request"] != "NO"
                or proof["sql_calls"] != 0
                or proof["sha256"] != intent.sha256):
            raise SimulationHold("SIMULATION_CLAIM_HOLD")

        events.append("SYNTHETIC_SQL_TOKEN")
        frame = encode_frame(FAKE_TOKEN, NONCE)
        receiver_nonce = "f" * 32 if scenario == "bad_nonce" else NONCE
        try:
            token = decode_frame(frame, receiver_nonce)
        except Exception:
            events.append("SYNTHETIC_RECEIVER_HOLD")
            raise SimulationHold("SIMULATION_FRAME_HOLD") from None
        if token != FAKE_TOKEN:
            raise SimulationHold("SIMULATION_TOKEN_HOLD")
        events.append("SYNTHETIC_SELECT_ONLY")
        receipt = parse_receipt(encode_receipt(
            HOLD if scenario == "receiver_hold" else EMPTY))
        if receipt != EMPTY:
            raise SimulationHold("SIMULATION_RECEIPT_HOLD")
        events.append("SYNTHETIC_RECEIPT")
        return {"outcome": "SYNTHETIC_PASS", "events": tuple(events),
                "live": "HOLD", "sql": "NOT_EXECUTED",
                "blob": "NOT_EXECUTED", "token": "SYNTHETIC_ONLY"}
    except Exception:
        # An ambiguous PUT/readback is terminal. Do not call guard again.
        return {"outcome": "SYNTHETIC_HOLD", "events": tuple(events),
                "live": "HOLD", "sql": "NOT_EXECUTED",
                "blob": "NOT_EXECUTED", "token": "SYNTHETIC_ONLY"}


def release_live(*args, **kwargs):
    raise SimulationHold("SIMULATOR_CANNOT_RELEASE_LIVE")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_SYNTHETIC_ONLY")
