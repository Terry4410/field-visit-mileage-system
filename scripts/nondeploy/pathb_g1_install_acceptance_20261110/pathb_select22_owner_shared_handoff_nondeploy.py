"""NONDEPLOY Owner SELECT22 shared-stage handoff choreography.

This is an injected *library*, not an authenticated runnable Live Parent.
It does not fetch credentials, open SSH, read Azure identity or authorize Blob
writes. A future separately audited Owner Parent must pin the authentic
Owner principal, exact App instance, private authenticated SSH channel,
Storage data-plane access and fresh exact one-shot Owner grant BEFORE calling.

Unlike the old stage handoff, this reuses the immutable SHA-verified /home
stage and never restages/uploads/extracts the SELECT22 archive.
"""
import hashlib
import re

from pathb_select22_ssh_session_nondeploy import Select22SshSession
from pathb_select22_owner_shared_receiver_nondeploy import command
from pathb_select22_session_receipt_nondeploy import expect_ready, read_status
from pathb_select22_storage_once_nondeploy import OnceGuard, prepare
from pathb_select22_owner_shared_parent_nondeploy import plan, review_frozen


class SharedHandoffHold(Exception):
    pass


def handoff_shared_after_external_claim_once(
        session, *, nonce, instance_sha256, storage_guard,
        sql_token_provider, owner_new_grant_attested=False):
    """One shared receiver → READY → one Blob PUT/GET → one SQL token → receipt.

    Caller-provided bool is an additional *fail-closed* contract bit, never
    independent Owner authorization or an Azure principal identity proof.
    Only a separately reviewed authenticated Parent may supply this function
    a real Select22SshSession plus real credential providers.
    NEVER call directly on an untrusted/Portal browser-SSH session.
    """
    review_frozen(plan())
    if (owner_new_grant_attested is not True
            or not isinstance(session, Select22SshSession)
            or session.state.state != "CHANNEL"
            or session.state.attempts != 1
            or session._token_count != 0
            or session._secrets
            or not isinstance(storage_guard, OnceGuard)
            or not callable(sql_token_provider)
            or type(nonce) is not str
            or re.fullmatch(r"[a-f0-9]{32}", nonce) is None
            or type(instance_sha256) is not str
            or hashlib.sha256(session.instance.lower().encode()).hexdigest()
               != instance_sha256):
        raise SharedHandoffHold("SHARED_OWNER_HOFF_PRECONDITION_HOLD")
    try:
        # This command carries no token. No archive re-stage or dpkg call.
        session.send_public(command(nonce), "SELECT22_SHARED_RECEIVER_SENT")
        session.state.advance("CHANNEL", "STAGED")
        # Failure before READY: absolutely no Blob or SQL Token.
        expect_ready(session)
        spec = prepare(stage_sha256=plan().stage_sha,
                       instance_sha256=instance_sha256)
        proof = storage_guard.consume(spec)
        if (type(proof) is not dict or proof.get("claim") != "PASS"
                or proof.get("readback") != "PASS"
                or proof.get("sha256") != spec.sha256
                or proof.get("sql_token_request") != "NO"
                or proof.get("sql_calls") != 0):
            raise SharedHandoffHold("SHARED_OWNER_BLOB_UNVERIFIED")
        # The provider MUST be called only after exact Blob PUT+GET proof.
        token = sql_token_provider()
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
        # No retry, fallback claim, second Token or alternative transport.
        raise SharedHandoffHold("SHARED_OWNER_TERMINAL_HOLD") from None


def release_live(*args, **kwargs):
    """No default live authentication/transport path exists in this module."""
    raise SharedHandoffHold("OWNER_LIVE_PARENT_NOT_RELEASED")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_AZURE_SSH_TOKEN_BLOB_SQL_ENTRYPOINT")
