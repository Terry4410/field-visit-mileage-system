#!/usr/bin/env python3
"""Authorization-locked, nondeploy Kudu channel diagnostic.

The only remote actions are one deterministic synthetic archive stage and an
independent, ownership-bounded cleanup.  Importing this module does nothing.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import re
import subprocess
import time
import zipfile
from datetime import datetime, timezone
from pathlib import Path

from p3_kudu_transport import (
    APP,
    DIAGNOSTIC_REMOTE_STAGE,
    RESOURCE_GROUP,
    KuduExecSession,
    TransportHold,
    select_single_instance,
    sha256,
    validate_cli_version,
)

SUBSCRIPTION_HASH = "7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47"
TENANT_HASH = "976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c"
OWNER_HASH = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"
CONTRACT = "FIELDVISIT_PATHB_KUDU_CHANNEL_DIAGNOSTIC_V1"
PAYLOAD_NAME = "fv-kudu-channel-diagnostic.txt"
PAYLOAD = b"FIELDVISIT_PATHB_KUDU_CHANNEL_DIAGNOSTIC_V1\n"
MAX_SECONDS = 600


def hold(condition: bool) -> None:
    if not condition:
        raise TransportHold("PROTOCOL_STATE_HOLD")


def identity_hash(value: object) -> str:
    return sha256(str(value).lower().encode("utf-8"))


def az_json(args: list[str], timeout: int = 30) -> object:
    result = subprocess.run(
        ["az", *args, "--only-show-errors", "-o", "json"],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        timeout=timeout,
        check=False,
    )
    hold(result.returncode == 0)
    try:
        return json.loads(result.stdout)
    except Exception as exc:
        raise TransportHold("PROTOCOL_STATE_HOLD") from exc


def validate_account(account: object, owner: object, app: object) -> None:
    hold(isinstance(account, dict) and isinstance(owner, dict) and isinstance(app, dict))
    hold(identity_hash(account.get("id", "")) == SUBSCRIPTION_HASH)
    hold(identity_hash(account.get("tenantId", "")) == TENANT_HASH)
    hold(str(account.get("user", {}).get("type", "")).lower() == "user")
    hold(identity_hash(owner.get("id", "")) == OWNER_HASH)
    suffix = "/resourceGroups/rg-fieldvisit-uat/providers/Microsoft.Web/sites/api-fieldvisit-pathb-uat"
    hold(str(app.get("id", "")).lower().endswith(suffix.lower()))
    hold(app.get("name") == APP)
    hold(str(app.get("resourceGroup", "")).lower() == RESOURCE_GROUP)


def deterministic_stage() -> bytes:
    stream = io.BytesIO()
    info = zipfile.ZipInfo(PAYLOAD_NAME, date_time=(2026, 11, 5, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100600 << 16
    with zipfile.ZipFile(stream, "w") as archive:
        archive.writestr(info, PAYLOAD)
    return stream.getvalue()


def parse_time(value: object) -> float:
    hold(isinstance(value, str) and value.endswith("Z"))
    try:
        return datetime.fromisoformat(value[:-1] + "+00:00").timestamp()
    except Exception as exc:
        raise TransportHold("PROTOCOL_STATE_HOLD") from exc


def load_authorization(path: Path, distribution_sha256: str, instance: str,
                       *, now: float) -> None:
    try:
        record = json.loads(path.read_text("utf-8"))
    except Exception as exc:
        raise TransportHold("PROTOCOL_STATE_HOLD") from exc
    hold(isinstance(record, dict))
    hold(record.get("contract") == CONTRACT and record.get("approved") is True)
    hold(record.get("distribution_sha256") == distribution_sha256)
    hold(record.get("resource_group") == RESOURCE_GROUP and record.get("app") == APP)
    hold(record.get("instance_sha256") == sha256(instance.encode("utf-8")))
    hold(record.get("max_seconds") == MAX_SECONDS and record.get("max_sessions") == 2)
    hold(record.get("payload_count") == 1)
    not_before = parse_time(record.get("not_before"))
    expires_at = parse_time(record.get("expires_at"))
    hold(not_before <= now <= expires_at and 0 < expires_at - not_before <= 1800)


def cleanup_command() -> bytes:
    expected = sha256(PAYLOAD)
    source = f'''import hashlib,os,shutil,stat\nfrom pathlib import Path\nr=Path({DIAGNOSTIC_REMOTE_STAGE!r})\np=r/{PAYLOAD_NAME!r}\nok=False\nif not r.exists():\n ok=True\nelif r.is_dir() and not r.is_symlink() and r.stat().st_uid==os.geteuid() and stat.S_IMODE(r.stat().st_mode)==0o700:\n ok=p.is_file() and not p.is_symlink() and p.stat().st_uid==os.geteuid() and stat.S_IMODE(p.stat().st_mode)==0o600 and hashlib.sha256(p.read_bytes()).hexdigest()=={expected!r} and len(list(r.iterdir()))==1\n if ok:shutil.rmtree(r)\nok=ok and not r.exists()\nprint("FV_KUDU_OWNED_CLEANUP_VERIFIED="+("PASS" if ok else "HOLD"),flush=True)\n'''
    encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
    return (
        "python3 -c \"import base64;exec(compile(base64.b64decode('"
        + encoded + "'),'fv-kudu-cleanup','exec'))\"\n"
    ).encode("ascii")


def emit(status: dict[str, str]) -> None:
    for name in ("PROCESS_START", "ECHO_READY", "STAGE_READY", "OWNED_CLEANUP"):
        value = status[name]
        print(f"{name}={value if value in {'PASS', 'HOLD', 'UNKNOWN'} else 'UNKNOWN'}",
              flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--live", action="store_true")
    parser.add_argument("--authorization")
    parser.add_argument("--distribution")
    parser.add_argument("--distribution-sha256")
    args = parser.parse_args()
    status = {"PROCESS_START": "HOLD", "ECHO_READY": "HOLD",
              "STAGE_READY": "HOLD", "OWNED_CLEANUP": "UNKNOWN"}
    first = None
    second = None
    try:
        hold(args.live and args.authorization and args.distribution and
             re.fullmatch(r"[a-f0-9]{64}", args.distribution_sha256 or "") is not None)
        distribution = Path(args.distribution)
        hold(distribution.is_file() and not distribution.is_symlink())
        hold(sha256(distribution.read_bytes()) == args.distribution_sha256)
        account = az_json(["account", "show"])
        owner = az_json(["ad", "signed-in-user", "show"])
        app = az_json(["webapp", "show", "-g", RESOURCE_GROUP, "-n", APP])
        validate_account(account, owner, app)
        validate_cli_version(az_json(["version"]))
        instance = select_single_instance(
            az_json(["webapp", "list-instances", "-g", RESOURCE_GROUP, "-n", APP]))
        load_authorization(Path(args.authorization), args.distribution_sha256,
                           instance, now=time.time())
        deadline = time.monotonic() + MAX_SECONDS
        stage = deterministic_stage()
        first = KuduExecSession(instance, deadline=deadline)
        first.start()
        status["PROCESS_START"] = "PASS"
        first.establish_safe_channel()
        status["ECHO_READY"] = "PASS"
        first.stage_archive(stage, sha256(stage), remote_stage=DIAGNOSTIC_REMOTE_STAGE)
        status["STAGE_READY"] = "PASS"
        first.close()
        first = None
        second = KuduExecSession(instance, deadline=deadline)
        second.start()
        second.establish_safe_channel()
        boundary = len(second.buffer)
        second.send_public(cleanup_command(), "OWNED_CLEANUP_SENT")
        second.expect_fixed(b"FV_KUDU_OWNED_CLEANUP_VERIFIED=PASS", 60,
                            "OWNED_CLEANUP", after=boundary)
        status["OWNED_CLEANUP"] = "PASS"
    except Exception:
        if status["PROCESS_START"] == "PASS" and status["STAGE_READY"] != "PASS":
            status["OWNED_CLEANUP"] = "UNKNOWN"
    finally:
        if first is not None:
            first.close()
        if second is not None:
            second.close()
        emit(status)
    return 0 if all(value == "PASS" for value in status.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
