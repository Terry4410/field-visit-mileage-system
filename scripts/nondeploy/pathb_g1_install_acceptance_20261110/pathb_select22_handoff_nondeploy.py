"""NONDEPLOY final SELECT22 handoff choreography, no Azure or live CLI.

Accepts only a pre-established, owner-reviewed SELECT22 pinned-SSH session.
No connection setup, Token acquisition, filesystem staging from remote or SQL
directly in this module. An independent live parent would have to check
Owner, exact App instance, source artifact SHA, stage/claims and authorization.
Never reuse the original G1 installer parent.
"""
import hashlib
from p3_kudu_transport import TransportHold
from pathb_select22_storage_once_nondeploy import prepare as prepare_storage_claim, OnceGuard
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession
from pathb_select22_remote_command_nondeploy import select22_remote_command
from pathb_select22_session_receipt_nondeploy import expect_ready, read_status


class HandoffHold(Exception):
    pass


def handoff_once(*args, **kwargs):
    """Obsolete direct-Token flow is prohibited: no durable Blob Claim proof.

    This retained symbol only fails closed for compatibility with offline
    callers. Never stage, request a token or execute SQL through this path.
    """
    raise HandoffHold("EXTERNAL_STORAGE_CLAIM_REQUIRED")


def handoff_after_storage_claim_once(
        session, *, stage_bytes, stage_sha, nonce,
        instance_sha256, storage_guard, sql_token_provider,
        owner_storage_approved=False):
    """The only planned external-claim handoff: STAGE -> BLOB -> SQL Token.

    NO live CLI, Azure, credentials, Blob request or SQL exists in this
    module. storage_guard and sql_token_provider must be supplied by an
    independently authenticated/pinned Owner Parent with fresh explicit
    Storage write grant. owner_storage_approved is an extra fail-closed
    preparation switch, NOT proof of real Owner authorization. The real
    Parent must verify exact signed-off grant and artifact before setting it.
    """
    if (owner_storage_approved is not True
            or not isinstance(session, Select22StageSession)
            or session.state.state != "CHANNEL"
            or session.state.attempts != 1
            or not isinstance(storage_guard, OnceGuard)
            or not callable(sql_token_provider)
            or not isinstance(instance_sha256, str)
            or hashlib.sha256(session.instance.lower().encode()).hexdigest()
               != instance_sha256):
        raise HandoffHold("EXTERNAL_CLAIM_PRECONDITION_HOLD")
    try:
        spec = prepare_storage_claim(stage_sha256=stage_sha,
                                     instance_sha256=instance_sha256)
        session.stage_select22_archive(stage_bytes, stage_sha)
        session.send_public(select22_remote_command(nonce),
                            "SELECT22_REMOTE_COMMAND_SENT")
        expect_ready(session)
        # Immediately before SQL Token request, commit the external claim:
        # one conditional PUT and one strict GET verification.
        proof = storage_guard.consume(spec)
        if (proof.get("claim") != "PASS" or proof.get("readback") != "PASS"
                or proof.get("sha256") != spec.sha256
                or proof.get("sql_token_request") != "NO"):
            raise HandoffHold("EXTERNAL_CLAIM_UNVERIFIED_HOLD")
        token = sql_token_provider()  # at most once, only after claim proven
        try:
            session.send_select22_frame_once(token, nonce)
        finally:
            token = None
        session.mark_sql_started()
        receipt = read_status(session)
        session.mark_complete()
        return receipt
    except Exception:
        session.state.unknown()
        # No retry or fallback marker; no SQL Token on Blob HOLD.
        raise HandoffHold("EXTERNAL_CLAIM_HANDOFF_HOLD") from None

if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_PARENT")
