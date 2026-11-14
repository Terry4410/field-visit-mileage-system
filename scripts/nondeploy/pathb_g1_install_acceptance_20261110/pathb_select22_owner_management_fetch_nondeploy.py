"""SELECT22 Owner management fetch boundary, NONDEPLOY candidate.

This module does not invoke Azure on import and has no runnable CLI.
An authenticated live Owner Parent must independently approve and invoke
this exact read-only evidence collector. NO Token, SSH, Blob, SQL connection,
SQL statement, DDL, deployment or App configuration changes are possible here.
"""
from pathb_select22_management_snapshot_nondeploy import (
    verify_snapshot, SnapshotHold, APP, RG, SQL_RESOURCE,
)


class OwnerFetchHold(Exception):
    pass


# Explicit read-only management queries. Do not add mutations, arbitrary
# args, Account token acquisition, or fallback/default subscription routing.
OWNER_ACCOUNT = ("account", "show")
OWNER_USER = ("ad", "signed-in-user", "show")
APP_GET = ("webapp", "show", "-g", RG, "-n", APP)
APP_INSTANCES = ("webapp", "list-instances", "-g", RG, "-n", APP)
SQL_GET = ("sql", "server", "show", "-g", RG, "-n", SQL_RESOURCE)
COMMANDS = (OWNER_ACCOUNT, OWNER_USER, APP_GET, APP_INSTANCES, SQL_GET)

# Separate *candidate*, not a fallback for the consumed signed-in-user attempt.
# Microsoft Graph /me resolves the current delegated sign-in principal at the
# Graph service. Its id is independently checked by verify_snapshot.
# Official Azure CLI az rest may implicitly acquire a Graph audience token:
# this requires FRESH, exact Owner authorization before any live invocation.
# A Graph error is terminal HOLD; never attempt the old CLI path afterward.
GRAPH_ME = ("rest", "--method", "get", "--url",
            "https://graph.microsoft.com/v1.0/me?$select=id")
GRAPH_COMMANDS = (OWNER_ACCOUNT, GRAPH_ME, APP_GET, APP_INSTANCES, SQL_GET)


def collect_exact_management(*, reader=None):
    """One ordered read per target; fail closed, no retries or raw exceptions.

    No default live Azure reader is allowed. 'reader' is an explicit,
    separately authorized test/adaptor seam. Supplying mocked or fabricated
    records NEVER proves authentic Azure ownership; the independent Live
    Parent must pin the actual official Azure CLI and Owner session, verify
    source hashes, and separately validate fresh grants and SSH.
    """
    if not callable(reader):
        raise OwnerFetchHold("SELECT22_MANAGEMENT_FETCH_HOLD")
    try:
        data = [reader(list(command)) for command in COMMANDS]
        return verify_snapshot(
            account=data[0], signed_in_owner=data[1], app=data[2],
            instances=data[3], sql_server=data[4])
    except Exception:
        raise OwnerFetchHold("SELECT22_MANAGEMENT_FETCH_HOLD") from None


def collect_graph_me_management_once(*, reader=None):
    """Separate 5-read Graph-/me Owner evidence candidate; NEVER auto-fallback.

    NONDEPLOY only. This is NOT a safe replacement for the consumed
    az ad signed-in-user Graph identity check, and must not be promoted based
    on mock PASS. No Azure reader is supplied by default. The Graph call, if
    separately executed, is a new Azure invocation and may acquire
    an implicit delegated Graph token; prior approvals DO NOT authorize it.
    Returned values are not cryptographically sealed against a fake reader.
    An audited future Parent must pin the Azure CLI provenance, fresh Owner
    approval and exact source, or fail closed. This function proves neither.
    """
    if not callable(reader):
        raise OwnerFetchHold("SELECT22_GRAPH_OWNER_HOLD")
    try:
        data = []
        for command in GRAPH_COMMANDS:
            data.append(reader(list(command)))
        return verify_snapshot(
            account=data[0], signed_in_owner=data[1], app=data[2],
            instances=data[3], sql_server=data[4])
    except Exception:
        raise OwnerFetchHold("SELECT22_GRAPH_OWNER_HOLD") from None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_AZURE_EXECUTION")
