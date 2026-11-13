"""SELECT22 Owner management fetch boundary, NONDEPLOY candidate.

This module does not invoke Azure on import and has no runnable CLI.
An authenticated live Owner Parent must independently approve and invoke
this exact read-only evidence collector. NO Token, SSH, Blob, SQL connection,
SQL statement, DDL, deployment or App configuration changes are possible here.
"""
from p3_g1_owner_management import az
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


def collect_exact_management(*, reader=az):
    """One ordered read per target; fail closed, no retries or raw exceptions.

    'reader' is an offline test seam. Supplying a mocked reader or fabricated
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


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_AZURE_EXECUTION")
