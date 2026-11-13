"""SELECT22 Owner Parent contract, NONDEPLOY. No live CLI, network or tokens.

Only independently audited Owner-side adapters can establish authentic live
evidence. These caller-supplied values are NOT themselves proof of approval.
"""
from dataclasses import dataclass
import hashlib
import re
from pathb_select22_handoff_nondeploy import handoff_after_storage_claim_once
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession
from pathb_select22_storage_once_nondeploy import OnceGuard

STAGE_SHA = "ff63702ae5c5fdbd63ed0ecffe609463824ccb3f96fb7441c7a231638a588b8b"
OWNER_SHA = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"
TARGET = ("rg-fieldvisit-uat", "api-fieldvisit-pathb-uat",
          "sql-fieldvisit-jpe-pathb-uat.database.windows.net",
          "db-fieldvisit-pathb-uat")


class ParentHold(Exception):
    pass


@dataclass(frozen=True)
class Evidence:
    target: tuple
    owner_oid_sha256: str
    stage_sha256: str
    instance_sha256: str
    tenant_verified: bool
    subscription_verified: bool
    unique_instance_verified: bool
    ssh_host_key_pinned: bool
    private_network_verified: bool
    storage_https_rbac_verified: bool
    exact_new_blob_grant_verified: bool
    exact_sql_select_grant_verified: bool


def preflight(evidence, stage_bytes, session, storage_guard, sql_provider):
    """Reject ANY missing gate before SSH staging/Blob/SQL Token."""
    if not isinstance(evidence, Evidence):
        raise ParentHold("PARENT_EVIDENCE_HOLD")
    flags = (evidence.tenant_verified, evidence.subscription_verified,
             evidence.unique_instance_verified, evidence.ssh_host_key_pinned,
             evidence.private_network_verified,
             evidence.storage_https_rbac_verified,
             evidence.exact_new_blob_grant_verified,
             evidence.exact_sql_select_grant_verified)
    good = (
        evidence.target == TARGET and evidence.owner_oid_sha256 == OWNER_SHA and
        evidence.stage_sha256 == STAGE_SHA and type(stage_bytes) is bytes and
        hashlib.sha256(stage_bytes).hexdigest() == STAGE_SHA and
        isinstance(evidence.instance_sha256, str) and
        re.fullmatch("[0-9a-f]{64}", evidence.instance_sha256) is not None and
        isinstance(session, Select22StageSession) and
        session.state.state == "CHANNEL" and session.state.attempts == 1 and
        hashlib.sha256(session.instance.lower().encode()).hexdigest()
        == evidence.instance_sha256 and
        isinstance(storage_guard, OnceGuard) and callable(sql_provider) and
        all(value is True for value in flags)
    )
    if not good:
        raise ParentHold("PARENT_PREGATE_HOLD")


def handoff_candidate(evidence, *, stage_bytes, session, nonce,
                      storage_guard, sql_token_provider):
    """Only composed after externally authenticated, exact Owner checks.

    This is NOT a runnable Live Owner Parent. It has no authentication,
    SSH connection setup, Azure Storage HTTPS client or token provider.
    Calling it with fabricated Evidence is unsafe and never authorized.
    """
    preflight(evidence, stage_bytes, session, storage_guard, sql_token_provider)
    return handoff_after_storage_claim_once(
        session, stage_bytes=stage_bytes, stage_sha=STAGE_SHA,
        nonce=nonce, instance_sha256=evidence.instance_sha256,
        storage_guard=storage_guard, sql_token_provider=sql_token_provider,
        owner_storage_approved=True)


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_PARENT")
