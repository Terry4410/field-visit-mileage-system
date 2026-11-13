#!/usr/bin/env python3
"""NONDEPLOY / offline-only: verify the immutable G1 read-only inputs.
No Azure, token, SSH, SQL, Blob, Claim, or installer access is implemented.
Fail closed: this file is NOT a live SQL operator.
"""
import hashlib
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
PREFLIGHT = HERE / "preflight.sql"
PREFLIGHT_SHA = "e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add"
TOOL_SHA = "4234f2b7db28e68b3f216712ab9fb016229823c111e008dc97749a8ae9190609"
OWNER_SHA = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"
TENANT_SHA = "976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c"
SUB_SHA = "7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47"
TARGET = ("sql-fieldvisit-jpe-pathb-uat.database.windows.net", "db-fieldvisit-pathb-uat")

def sha(data):
    return hashlib.sha256(data).hexdigest()

def validate_sql(data):
    # SHA is the binding. Do not reformat, parameterize, or reconstruct the query.
    return sha(data) == PREFLIGHT_SHA

def validate_identity(oid, tenant, subscription, account_type):
    return (account_type.lower() == "user"
            and sha(oid.lower().encode()) == OWNER_SHA
            and sha(tenant.lower().encode()) == TENANT_SHA
            and sha(subscription.lower().encode()) == SUB_SHA)

def validate_tool(data):
    return sha(data) == TOOL_SHA

def main():
    try:
        if len(sys.argv) != 1:
            print("OFFLINE_GATE=HOLD_ARGUMENTS")
            return 2
        raw = PREFLIGHT.read_bytes()
        if not validate_sql(raw):
            print("OFFLINE_GATE=HOLD_SQL_HASH")
            return 2
        print("PREFLIGHT_HASH=PASS")
        print("LIVE_TRANSPORT=NOT_IMPLEMENTED")
        print("TOKEN_REQUEST=NO")
        print("SQL_CALLS=0")
        print("OFFLINE_GATE=PASS")
        return 0
    except Exception:
        print("OFFLINE_GATE=HOLD")
        return 2

if __name__ == "__main__":
    raise SystemExit(main())
