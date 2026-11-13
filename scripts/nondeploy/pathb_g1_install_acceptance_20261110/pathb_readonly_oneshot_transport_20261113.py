"""Offline-only, injected one-shot SSH transport boundary.

The callable transport is injected by a test or a separately reviewed live
parent. No SSH subprocess, Azure, token retrieval, SQL or filesystem writes.
The same object refuses a second attempt, including after ambiguous failure.
"""
from pathb_readonly_stdin_frame_20261113 import encode_frame, FrameHold

class TransportHold(Exception):
    pass

class OneShotTransport:
    def __init__(self, transport):
        if not callable(transport):
            raise TransportHold("TRANSPORT_NOT_INJECTED")
        self._transport = transport
        self._spent = False

    def dispatch(self, sql_token, nonce):
        if self._spent:
            raise TransportHold("ATTEMPT_ALREADY_CONSUMED")
        self._spent = True  # consume before framing or invoking
        try:
            frame = encode_frame(sql_token, nonce)
            # A live parent must enforce authenticated SSH, fixed remote command,
            # channel confidentiality, output suppression and deadline externally.
            response = self._transport(frame)
            if not isinstance(response, bytes) or len(response) > 8192:
                raise TransportHold("RESPONSE_SHAPE_HOLD")
            return response
        except Exception:
            # Never propagate raw transport errors, which may contain credentials.
            raise TransportHold("TRANSPORT_HOLD") from None

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_SSH_ENTRYPOINT")
