"""NONDEPLOY Owner SELECT22 shared-stage protocol planner.

Pure immutable protocol rules for a *future*, independently audited Live Parent.
Does NOT accept token providers, sessions, HTTP clients, SQL runners or secrets.
A caller cannot use this as an execution path. Previous Kudu and main App
integrity PASS may be reused, but is not cryptographic Owner-session proof.

Required external boundaries: fresh Owner delegation for ONE new Blob key and
ONE SQL SELECT; authenticated Owner principal; pinned existing App instance;
fixed shared stage and SQL SHA; separately trusted session transport with
non-echoed token stdin, provenance, time/timeout and no replay. The Portal's
browser SSH view alone is not a programmatically controlled token channel.
"""
from dataclasses import dataclass

from pathb_select22_owner_shared_receiver_nondeploy import (
    ROOT as SHARED_ROOT, APP, SQL_SHA, MANIFEST_SHA,
)
from pathb_select22_owner_parent_nondeploy import OWNER_SHA, TARGET, STAGE_SHA
from pathb_select22_storage_once_nondeploy import ACCOUNT, CONTAINER, KEY

STEPS = (
    "OWNER_INDEPENDENT_AUTH_AND_NEW_EXACT_GRANT",
    "BOUND_EXISTING_APP_INSTANCE_AND_PRIVATE_CHANNEL",
    "RECEIVER_READY_SHARED_STAGE_SHA",
    "BLOB_CONDITIONAL_PUT_ONCE",
    "BLOB_ETAG_AND_EXACT_BODY_GET_ONCE",
    "SQL_TOKEN_AFTER_BLOB_READBACK_ONCE",
    "SELECT22_FIXED_SQL_ONCE",
    "SANITIZED_RECEIPT_THEN_STOP",
)
# No executable entrypoint. This indicates *planned* sequencing only.


class SharedParentHold(Exception):
    pass


@dataclass(frozen=True)
class FrozenPlan:
    app: str
    shared_root: str
    stage_sha: str
    manifest_sha: str
    sql_sha: str
    owner_sha: str
    blob_account: str
    blob_container: str
    blob_key: str
    target: tuple


def plan():
    """Expected static binding contract. No authorization or cloud access."""
    return FrozenPlan(APP, SHARED_ROOT, STAGE_SHA, MANIFEST_SHA, SQL_SHA,
                      OWNER_SHA, ACCOUNT, CONTAINER, KEY, TARGET)


def review_frozen(candidate):
    """Prevent drift and explicitly refuse to release any LIVE action."""
    if type(candidate) is not FrozenPlan or candidate != plan():
        raise SharedParentHold("OWNER_SHARED_PARENT_SCOPE_HOLD")
    return {
        "static_bindings": "PASS",
        "reuse_main_app_stage": "REQUIRED",
        "reupload_or_restage": "FORBIDDEN",
        "owner_live_identity": "NOT_ATTESTED",
        "owner_new_one_shot_approval": "NOT_GRANTED",
        "trusted_secret_stdin_transport": "NOT_IMPLEMENTED",
        "blob_data_plane_authority": "NOT_PROVEN",
        "sql_select_capability": "NOT_PROVEN",
        "blob_first": "REQUIRED",
        "live_parent": "HOLD",
    }


def release_live(*args, **kwargs):
    raise SharedParentHold("OWNER_SHARED_LIVE_PARENT_NOT_RELEASED")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_OWNER_SHARED_LIVE_ENTRYPOINT")
