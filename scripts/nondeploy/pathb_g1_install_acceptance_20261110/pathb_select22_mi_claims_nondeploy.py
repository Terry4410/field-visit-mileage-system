"""NONDEPLOY SELECT22 App-MI claims contract; NO live authentication.

A decoded/hand-constructed JWT payload is UNTRUSTED DATA. These pure checks
NEVER verify JWT signatures, issuers, metadata provenance, token possession,
Azure RBAC, SQL grants or an Owner execution grant. No token input, network,
filesystem, Azure, Blob or SQL capability. A future independently audited
live parent MUST perform cryptographic verification and bind the actual App MI
principal + tenant from Azure management, before any Blob or SQL attempt.
"""
from dataclasses import dataclass
from datetime import datetime
import hashlib
import re

from pathb_select22_mi_preflight_nondeploy import (
    MiReviewInputs, MiPreflightHold, OWNER_SHA, STORAGE_AUD, SQL_AUD,
    offline_review,
)

AUDIENCES = {"storage": STORAGE_AUD, "sql": SQL_AUD}
# A token may use an audience with/without the final slash.
MAX_TIME_TO_LIVE = 24 * 60 * 60
MIN_REMAINING_SECONDS = 300


class MiClaimsHold(Exception):
    pass


@dataclass(frozen=True)
class UntrustedClaims:
    """Synthetic/decoded values ONLY; never a verified identity assertion."""
    oid: str
    tid: str
    aud: str
    nbf: int
    exp: int


def offline_claims_review(scope, claims, *, audience, now):
    """Check untrusted claims against a fixed App-MI scope, always HOLD live.

    This is NOT a JWT verifier; never call it a token verification PASS.
    Actual MI tokens must later be checked cryptographically by a trusted
    authenticated adapter, including issuer, signature, principal binding,
    resource/audience, token lifetime and app identity provenance.
    """
    try:
        if type(scope) is not MiReviewInputs or type(claims) is not UntrustedClaims:
            raise ValueError()
        offline_review(scope)
        if audience not in AUDIENCES or type(now) is not int or now < 0:
            raise ValueError()
        if (type(claims.oid) is not str or type(claims.tid) is not str
                or type(claims.aud) is not str
                or type(claims.nbf) is not int or type(claims.exp) is not int):
            raise ValueError()
        if (claims.oid.lower() != scope.mi_principal_id.lower()
                or claims.tid.lower() != scope.mi_tenant_id.lower()
                or claims.aud.rstrip("/") != AUDIENCES[audience]
                or claims.nbf > now + 30
                or claims.exp - now < MIN_REMAINING_SECONDS
                or claims.exp - claims.nbf > MAX_TIME_TO_LIVE
                or claims.exp <= claims.nbf
                or hashlib.sha256(claims.oid.lower().encode()).hexdigest() == OWNER_SHA):
            raise ValueError()
        return {
            "static_scope": "PASS",
            "untrusted_claims_shape": "PASS",
            "principal_kind": "APP_MI_NOT_OWNER",
            "jwt_signature": "NOT_VERIFIED",
            "issuer_and_key_provenance": "NOT_VERIFIED",
            "azure_mi_principal_binding": "NOT_PROVEN",
            "owner_execution_grant": "NOT_AUTHORIZED",
            "storage_data_plane_grant": "NOT_PROVEN",
            "sql_select_grant": "NOT_PROVEN",
            "blob_claim": "NOT_ATTEMPTED",
            "sql_token": "NOT_REQUESTED",
            "live_release": "HOLD",
        }
    except (MiPreflightHold, ValueError, TypeError, OverflowError, AttributeError):
        raise MiClaimsHold("MI_UNTRUSTED_CLAIMS_HOLD") from None


def release_live(*args, **kwargs):
    """No caller-supplied object can unlock Blob/SQL from this module."""
    raise MiClaimsHold("MI_LIVE_AUTHENTICATION_NOT_IMPLEMENTED")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_MI_TOKEN_BLOB_SQL_ENTRYPOINT")
