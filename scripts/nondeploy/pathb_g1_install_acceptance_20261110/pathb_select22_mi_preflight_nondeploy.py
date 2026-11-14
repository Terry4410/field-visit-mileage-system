"""Path B MI SELECT22 policy boundary: pure NONDEPLOY, no live release.

An Owner's approval of MI *design* never authorizes execution. The App MI
is a distinct service principal, not the Portal Owner. Client-supplied values
are NOT authenticated Azure identity, data-plane RBAC or SQL grants.
"""
from dataclasses import dataclass
import hashlib
import re

RG = "rg-fieldvisit-uat"
APP = "api-fieldvisit-pathb-uat"
SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DATABASE = "db-fieldvisit-pathb-uat"
ACCOUNT = "stfvsc261026a"
CONTAINER = "schema-evidence"
CLAIM_KEY = "operator-once/pathb-select22-schema-readonly-once-20261113.json"
QUERY_SHA = "e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add"
OWNER_SHA = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"
STORAGE_AUD = "https://storage.azure.com"
SQL_AUD = "https://database.windows.net"
REQUIRED_ORDER = ("OWNER_APPROVAL", "AUTHENTICATE_MI", "STORAGE_DATA_GRANT",
                  "SQL_SELECT_GRANT", "READY", "BLOB_PUT_ONCE",
                  "BLOB_READBACK", "SQL_TOKEN_ONCE", "SELECT22_ONCE")


class MiPreflightHold(Exception):
    """Constant sanitized refusal, never raw principal details."""


@dataclass(frozen=True)
class MiReviewInputs:
    resource_group: str
    app: str
    server: str
    database: str
    storage_account: str
    storage_container: str
    claim_key: str
    sql_sha256: str
    owner_oid_sha256: str
    mi_principal_id: str
    mi_tenant_id: str
    storage_audience: str
    sql_audience: str


def offline_review(inputs):
    """Verify STATIC expected scope, not authenticated identity or live grants."""
    if type(inputs) is not MiReviewInputs:
        raise MiPreflightHold("MI_STATIC_SCOPE_HOLD")
    expected = (RG, APP, SERVER, DATABASE, ACCOUNT, CONTAINER, CLAIM_KEY,
                QUERY_SHA, OWNER_SHA, STORAGE_AUD, SQL_AUD)
    observed = (inputs.resource_group, inputs.app, inputs.server, inputs.database,
                inputs.storage_account, inputs.storage_container, inputs.claim_key,
                inputs.sql_sha256, inputs.owner_oid_sha256,
                inputs.storage_audience, inputs.sql_audience)
    if observed != expected:
        raise MiPreflightHold("MI_STATIC_SCOPE_HOLD")
    # Format check alone must never prove that MI exists or has permissions.
    uuid = r"[a-fA-F0-9]{8}-(?:[a-fA-F0-9]{4}-){3}[a-fA-F0-9]{12}"
    if (not isinstance(inputs.mi_principal_id, str)
            or re.fullmatch(uuid, inputs.mi_principal_id) is None
            or not isinstance(inputs.mi_tenant_id, str)
            or re.fullmatch(uuid, inputs.mi_tenant_id) is None
            or hashlib.sha256(inputs.mi_principal_id.lower().encode()).hexdigest()
            == OWNER_SHA):
        raise MiPreflightHold("MI_PRINCIPAL_SHAPE_HOLD")
    return {"static_scope": "PASS", "principal_kind": "APP_MI_NOT_OWNER",
            "owner_design_approval": "RECORDED_NOT_LIVE_AUTHORIZATION",
            "mi_authenticated": "NOT_PROVEN",
            "storage_data_plane_grant": "NOT_PROVEN",
            "sql_select_grant": "NOT_PROVEN",
            "sql_sha256": "PASS", "blob_first_order": "REQUIRED",
            "live_release": "HOLD"}


def release_live(*args, **kwargs):
    """Even fabricated flags cannot authorize a network/SQL operation."""
    raise MiPreflightHold("MI_LIVE_PARENT_NOT_RELEASED")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_MI_TOKEN_BLOB_SQL_ENTRYPOINT")
