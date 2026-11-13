"""NONDEPLOY SELECT22 pinned SSH status-only receiver (no live entrypoint).

Reads only fixed READY and exact sanitized receipt lines from an already
authenticated/host-key-pinned SSH session. Never logs raw PTY, Token or SQL.
"""
import os
import select
import time

from p3_kudu_transport import TransportHold
from pathb_select22_receipt_bridge_nondeploy import PREFIX, parse_receipt

READY = b"FV_SELECT22_READY"
MAX_BUFFER = 8192


def _read_matching_line(session, match, seconds):
    end = min(session.deadline, time.monotonic() + seconds)
    while time.monotonic() < end:
        session._check_secret_echo()
        cursor = 0
        for raw in session.buffer.splitlines(keepends=True):
            if not raw.endswith(b"\n"):
                break
            cursor += len(raw)
            line = raw[:-1].removesuffix(b"\r")
            if match(line):
                session.buffer = session.buffer[cursor:]
                return line + b"\n"
        ready = select.select([session.fd], [], [], .2)[0]
        if ready:
            try:
                chunk = os.read(session.fd, 65536)
            except OSError:
                raise TransportHold("KUDU_CHANNEL_CLOSED") from None
            if not chunk:
                raise TransportHold("KUDU_CHANNEL_CLOSED")
            session.buffer += chunk
            if len(session.buffer) > MAX_BUFFER:
                raise TransportHold("KUDU_OUTPUT_LIMIT")
        elif session.proc is None or session.proc.poll() is not None:
            raise TransportHold("KUDU_CHANNEL_CLOSED")
    raise TransportHold("KUDU_CHANNEL_TIMEOUT")


def expect_ready(session):
    if session.state.state != "STAGED":
        raise TransportHold("PROTOCOL_STATE_HOLD")
    _read_matching_line(session, lambda line: line == READY, 45)
    session.audit.append("SELECT22_READY")


def read_status(session):
    if session.state.state != "SQL_STARTED":
        raise TransportHold("PROTOCOL_STATE_HOLD")
    raw = _read_matching_line(
        session, lambda line: line.startswith(PREFIX), 120)
    try:
        receipt = parse_receipt(raw)
    except Exception:
        raise TransportHold("PROTOCOL_STATE_HOLD") from None
    session.audit.append("SELECT22_SANITIZED_RECEIPT")
    return receipt


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_SSH_RECEIVER")
