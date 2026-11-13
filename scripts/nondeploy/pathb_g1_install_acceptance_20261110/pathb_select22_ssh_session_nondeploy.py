"""NONDEPLOY single-SQL-token SSH stage adapter, G1 pinned SSH inherited.

No live CLI. The original G1 send_secret_line expects SQL AND Storage tokens,
which is outside SELECT-only scope. This adapter accepts exactly ONE validated
SQL JWT in a nonce-bound frame after verified staging, then irrevocably
advances the same transport state machine. No Azure, SSH, Token on import.
"""
import re
import time

from p3_g1_pinned_ssh import SshExecSession
from p3_kudu_transport import TransportHold
from p3_ssh_transport_contract import validate_owner_token
from pathb_readonly_stdin_frame_20261113 import encode_frame


class Select22SshSession(SshExecSession):
    """The only alteration to the existing pinned App SSH driver is token framing."""

    def send_select22_frame_once(self, token, nonce):
        if (self.state.state != "STAGED" or self._token_count != 0 or
                self._secrets):
            raise TransportHold("ONCE_ONLY_HOLD")
        try:
            if not isinstance(token, str) or not token.isascii():
                raise ValueError("TOKEN_SHAPE_HOLD")
            validate_owner_token(token, "sql", time.time())
            if not isinstance(nonce, str) or re.fullmatch("[a-f0-9]{32}", nonce) is None:
                raise ValueError("NONCE_HOLD")
            frame = encode_frame(token, nonce)
        except Exception:
            raise TransportHold("TOKEN_HANDOFF_HOLD") from None
        # Mark spent BEFORE any bytes are sent: an ambiguous partial write
        # must never be dispatched a second time on this session.
        self._token_count = 1
        secret = bytearray(token.encode("ascii"))
        self._secrets.append(secret)
        try:
            self._write_all(frame)
            self.state.advance("STAGED", "TOKENS")
            self.audit.append("SELECT22_SINGLE_TOKEN_FRAME_SENT")
        except Exception:
            self.state.unknown()
            raise TransportHold("TOKEN_HANDOFF_HOLD") from None

    def send_secret_line(self, *args, **kwargs):
        """Prevent accidental use of the original two-token installer protocol."""
        raise TransportHold("TOKEN_HANDOFF_HOLD")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_SSH_ENTRYPOINT")
