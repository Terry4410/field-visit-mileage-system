"""NONDEPLOY exact SELECT22 durable Blob claim contract.

No live CLI, Azure, HTTP client, token, SQL, installer, Blob request or marker
creation. A future independently authorized and authenticated Owner parent
MAY inject one reviewed Azure Storage request function after matching exact
account, container, blob, SQL, App, stage, instance, grant and token identity.

The claim is a NEW operation requiring separate Owner authorization, not a
replay of legacy SELECT22 or G1 installer markers. Do not execute this module.
"""
from dataclasses import dataclass
import hashlib
import json
import re

ACCOUNT = "stfvsc261026a"
CONTAINER = "schema-evidence"
# One fixed *candidate* key; ONLY a future explicit Owner authorization can
# permit writing it. Never derive a new name from retries, timestamp, or nonce.
KEY = "operator-once/pathb-select22-schema-readonly-once-20261113.json"
CONTRACT = "FIELDVISIT_PATHB_SELECT22_READONLY_STORAGE_CLAIM_V1"
SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DATABASE = "db-fieldvisit-pathb-uat"
APP = "api-fieldvisit-pathb-uat"
RELEASE = "d6c220877b9976b6478d85b5171d4e06293595ae"
QUERY_SHA = "e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add"
OWNER_SHA = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"

MAX_PAYLOAD = 1024


class StorageOnceHold(Exception):
    """Static safe failure code only; no raw HTTP, identities or credentials."""


@dataclass(frozen=True)
class Response:
    status: int
    etag: str = ""
    body: bytes = b""


@dataclass(frozen=True)
class Claim:
    key: str
    payload: bytes
    sha256: str


def _check(value):
    if not value:
        raise StorageOnceHold("SELECT22_STORAGE_CLAIM_HOLD")


def prepare(*, stage_sha256, instance_sha256):
    """Immutable, nonsecret intent bound to exact preapproved source and instance.

    Stage hash and instance hash MUST originate from separately validated
    transport/management gates; this routine does not verify Azure or grant.
    """
    for sha in (stage_sha256, instance_sha256):
        _check(isinstance(sha, str) and re.fullmatch("[0-9a-f]{64}", sha))
    record = {
        "contract": CONTRACT,
        "purpose": "READONLY_SCHEMA_ONLY_NO_INSTALL",
        "account": ACCOUNT, "container": CONTAINER, "blob": KEY,
        "server": SERVER, "database": DATABASE, "app": APP,
        "release": RELEASE, "query_sha256": QUERY_SHA,
        "owner_oid_sha256": OWNER_SHA,
        "stage_sha256": stage_sha256,
        "instance_sha256": instance_sha256,
        "status": "CLAIMED_BEFORE_SQL_TOKEN_OR_SQL",
    }
    payload = (json.dumps(record, sort_keys=True, separators=(",", ":")) + "\n").encode("ascii")
    _check(len(payload) <= MAX_PAYLOAD)
    return Claim(KEY, payload, hashlib.sha256(payload).hexdigest())


class OnceGuard:
    """Injected request(method, account, container, key, headers, body)->Response.

    EXTERNAL prerequisite: a separate Owner grant specifically permitting the
    new Blob PUT and readback, trusted Storage audience token provenance,
    Azure RBAC, unique instance, SSH/private path and exact artifact binding.
    This class does NOT satisfy those prerequisites and has NO live transport.
    """

    def __init__(self, request):
        _check(callable(request))
        self._request = request
        self._attempted = False

    def consume(self, claim):
        """One conditional PUT then one exact readback GET; never retry/recreate.

        On any ambiguous outcome (including PUT accepted but readback lost),
        the attempt is terminal. There is no HEAD, DELETE or alternate key.
        """
        _check(isinstance(claim, Claim) and claim.key == KEY
               and isinstance(claim.payload, bytes)
               and hashlib.sha256(claim.payload).hexdigest() == claim.sha256)
        _check(not self._attempted)
        self._attempted = True   # BEFORE any possible network dispatch
        try:
            response = self._request(
                "PUT", ACCOUNT, CONTAINER, KEY,
                {"If-None-Match": "*", "x-ms-blob-type": "BlockBlob",
                 "Content-Type": "application/json",
                 "Content-Length": str(len(claim.payload))},
                claim.payload)
        except Exception:
            raise StorageOnceHold("SELECT22_STORAGE_CLAIM_OUTCOME_UNKNOWN") from None
        if not isinstance(response, Response):
            raise StorageOnceHold("SELECT22_STORAGE_CLAIM_OUTCOME_UNKNOWN")
        if response.status == 412:
            raise StorageOnceHold("SELECT22_STORAGE_REPLAY_BLOCKED")
        if (response.status != 201 or not isinstance(response.etag, str)
                or not response.etag or len(response.etag) > 128):
            raise StorageOnceHold("SELECT22_STORAGE_CLAIM_OUTCOME_UNKNOWN")
        try:
            read = self._request(
                "GET", ACCOUNT, CONTAINER, KEY,
                {"If-Match": response.etag}, b"")
        except Exception:
            raise StorageOnceHold("SELECT22_STORAGE_READBACK_UNKNOWN") from None
        if (not isinstance(read, Response) or read.status != 200 or
                read.etag != response.etag or read.body != claim.payload):
            raise StorageOnceHold("SELECT22_STORAGE_READBACK_UNKNOWN")
        return {"claim": "PASS", "readback": "PASS",
                "sha256": claim.sha256, "sql_token_request": "NO",
                "sql_calls": 0}


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_STORAGE_OR_SQL_ENTRYPOINT")
