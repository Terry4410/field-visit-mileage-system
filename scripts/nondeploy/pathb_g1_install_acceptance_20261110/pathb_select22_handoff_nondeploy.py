"""NONDEPLOY final SELECT22 handoff choreography, no Azure or live CLI.

Accepts only a pre-established, owner-reviewed SELECT22 pinned-SSH session.
No connection setup, Token acquisition, filesystem staging from remote or SQL
directly in this module. An independent live parent would have to check
Owner, exact App instance, source artifact SHA, stage/claims and authorization.
Never reuse the original G1 installer parent.
"""
from p3_kudu_transport import TransportHold
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession
from pathb_select22_remote_command_nondeploy import select22_remote_command
from pathb_select22_session_receipt_nondeploy import expect_ready, read_status


class HandoffHold(Exception):
    pass


def handoff_once(session, *, stage_bytes, stage_sha, nonce, sql_token):
    """One fixed stage, one remote receiver, one nonce-bound SQL Token frame.

    Session start/identity/SSH trust and Token acquisition must ALREADY
    have been approved and independently bound. No automatic retry.
    """
    if (not isinstance(session, Select22StageSession)
            or session.state.state != "CHANNEL"
            or session.state.attempts != 1):
        raise HandoffHold("CHANNEL_NOT_BOUND")
    try:
        # Stage SHA is verified against actual bytes BEFORE any stage transfer.
        session.stage_select22_archive(stage_bytes, stage_sha)
        session.send_public(select22_remote_command(nonce),
                            "SELECT22_REMOTE_COMMAND_SENT")
        expect_ready(session)
        session.send_select22_frame_once(sql_token, nonce)
        session.mark_sql_started()
        receipt = read_status(session)
        # COMPLETE means sanitized receipt obtained, NOT schema/SQL success.
        session.mark_complete()
        return receipt
    except Exception:
        session.state.unknown()
        raise HandoffHold("SELECT22_HANDOFF_HOLD") from None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_PARENT")
