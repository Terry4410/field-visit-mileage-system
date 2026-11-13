"""NONDEPLOY SELECT22 exact Azure management response verifier.

Pure, offline validation only. The snapshot MUST be supplied by a separately
audited, authenticated read-only Azure management adapter. This module never
calls az, HTTP, Blob, SSH, SQL, or token services, and is NOT authorization.
"""
import hashlib
from dataclasses import dataclass
from p3_kudu_transport import select_single_instance
from p3_ssh_transport_contract import check_owner_account, OWNER_SHA, RG, APP

SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
SQL_RESOURCE = "sql-fieldvisit-jpe-pathb-uat"


class SnapshotHold(Exception):
    pass


def _hash(value):
    return hashlib.sha256(str(value).lower().encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class VerifiedManagement:
    """Value-level cross-binding only; NOT proof of CLI provenance or grant."""
    instance: str
    instance_sha256: str
    app: str
    sql_server: str
    read_only_verified: bool = True


def verify_snapshot(*, account, signed_in_owner, app, instances, sql_server):
    """Reject ambiguous/inconsistent exact resources and any weak network gate.

    No app settings, raw identity, secret or underlying exception is returned.
    Caller independently validates freshness, exact subscription and access.
    """
    try:
        check_owner_account(account)
        if (not isinstance(signed_in_owner, dict)
                or _hash(signed_in_owner.get("id", "")) != OWNER_SHA):
            raise ValueError()
        if (not isinstance(app, dict)
                or app.get("name") != APP
                or app.get("resourceGroup", "").lower() != RG.lower()
                or app.get("state") != "Running"
                or not app.get("virtualNetworkSubnetId")):
            raise ValueError()
        instance = select_single_instance(instances)
        if (not isinstance(sql_server, dict)
                or sql_server.get("name") != SQL_RESOURCE
                or sql_server.get("fullyQualifiedDomainName") != SERVER
                or sql_server.get("publicNetworkAccess") != "Disabled"
                or sql_server.get("minimalTlsVersion") != "1.2"):
            raise ValueError()
        return VerifiedManagement(instance, _hash(instance), APP, SERVER)
    except Exception:
        raise SnapshotHold("SELECT22_MANAGEMENT_SNAPSHOT_HOLD") from None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_MANAGEMENT_LIVE_ENTRYPOINT")
