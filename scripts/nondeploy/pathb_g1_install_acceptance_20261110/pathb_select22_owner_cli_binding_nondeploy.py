"""NONDEPLOY exact Azure CLI Owner context inspection contract.

This is a reusable bounded identity checker for a future reviewed Parent,
NOT a Live release and NOT a cryptographic JWT signature verifier.  Azure CLI
is the requested single credential context for both identity inspection and
subsequent SSH setup; a successful PowerShell Az context is never substituted.
No Azure or Token calls happen on import or in unit tests.
The only entry is an explicitly supplied trusted CLI runner, without default.
"""
import base64
import hashlib
import json
import re
import time

from p3_ssh_transport_contract import OWNER_SHA, TENANT_SHA, SUB_SHA

ARM_AUDIENCE = "https://management.azure.com"
MAX_TOKEN_SIZE = 16384
SUBSCRIPTION_COMMAND = ("account", "show", "-o", "json", "--only-show-errors")
TOKEN_COMMAND = ("account", "get-access-token", "--resource", ARM_AUDIENCE + "/",
                 "-o", "json", "--only-show-errors")


class OwnerCliBindingHold(Exception):
    pass


def _digest(value):
    return hashlib.sha256(value.lower().encode("ascii")).hexdigest()


def verify_cli_owner_once(*, runner=None, now=None):
    """Process two fixed CLI responses from ONE explicitly controlled runner.

    The runner MUST be supplied by a separately audited Parent which pins
    actual Azure CLI executable/process environment and new Owner permission.
    Returned token *claims* are structural evidence, NOT signature verification
    or proof that a different future CLI subprocess is the same user.
    Do not disclose or persist the returned access token.
    """
    if not callable(runner):
        raise OwnerCliBindingHold("CLI_OWNER_READER_NOT_RELEASED")
    if type(now) not in (int, float) or now < 0:
        raise OwnerCliBindingHold("CLI_OWNER_TIME_HOLD")
    # Never fall back to Graph, Az PowerShell or an older consumed operation.
    try:
        account = runner(SUBSCRIPTION_COMMAND)
        if type(account) is not dict or type(account.get("user")) is not dict:
            raise ValueError()
        if (account["user"].get("type", "").lower() != "user"
                or _digest(account["id"]) != SUB_SHA
                or _digest(account["tenantId"]) != TENANT_SHA):
            raise ValueError()
        # A new ARM token request here requires FRESH owner approval.
        record = runner(TOKEN_COMMAND)
        if type(record) is not dict:
            raise ValueError()
        token = record.get("accessToken")
        if (type(token) is not str or len(token) > MAX_TOKEN_SIZE
                or not token.isascii()):
            raise ValueError()
        parts = token.split(".")
        if len(parts) != 3 or not all(parts):
            raise ValueError()
        body = base64.urlsafe_b64decode(parts[1] + "=" * (-len(parts[1]) % 4))
        if len(body) > 8192:
            raise ValueError()
        claims = json.loads(body)
        if type(claims) is not dict:
            raise ValueError()
        oid, tid = claims["oid"], claims["tid"]
        aud, exp, nbf = claims["aud"], claims["exp"], claims["nbf"]
        if (type(oid) is not str or type(tid) is not str
                or type(aud) is not str or type(exp) not in (int, float)
                or type(nbf) not in (int, float)):
            raise ValueError()
        if (_digest(oid) != OWNER_SHA or _digest(tid) != TENANT_SHA
                or aud.rstrip("/") != ARM_AUDIENCE
                or nbf > now + 30 or exp - now < 300):
            raise ValueError()
        return {
            "cli_subscription": "PASS",
            "cli_token_claims": "PASS_UNVERIFIED_SIGNATURE",
            "owner_oid_claim": "MATCHES_PIN",
            "token_signature": "NOT_VERIFIED",
            "ssh_process_identity": "NOT_ATTESTED",
            "live_release": "HOLD",
        }
    except Exception:
        raise OwnerCliBindingHold("CLI_OWNER_BINDING_HOLD") from None


def release_live(*args, **kwargs):
    raise OwnerCliBindingHold("CLI_OWNER_LIVE_NOT_RELEASED")


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_AZURE_TOKEN_OR_SSH_ENTRYPOINT")
