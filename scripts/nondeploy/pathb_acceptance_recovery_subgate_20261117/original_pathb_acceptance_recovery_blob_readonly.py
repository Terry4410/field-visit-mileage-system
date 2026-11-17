#!/usr/bin/env python3
from __future__ import annotations

import email.utils
import hashlib
import http.client
import json
import os
import re
import ssl
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from zipfile import ZipFile

SOURCE_SHA = "3fe8008d10aacdaec5528ab99b5aee01cf1f6bf5a0bd1fec0c1f369faedb1a28"
BINDING_SHA = "4665b013d1ec4517b59359cc0d6ee49cc6f868c4c0c0df1a508a2758d5f83927"
SOURCE_NAME = "PathB_G1_ShellReady_NONDEPLOY_Source_d4c16bec.zip"
RG = "rg-fieldvisit-uat"
APP = "api-fieldvisit-pathb-uat"
ACCOUNT = "stfvsc261026a"
CONTAINER = "schema-evidence"
CLAIM_KEY = "operator-once/pathb-g1-original-installer-kudu-interactive-v2-20261116.json"


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def az_json(*args: str):
    run = subprocess.run(
        ["az", *args, "--only-show-errors", "--output", "json"],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, timeout=120,
    )
    return json.loads(run.stdout)


def main() -> int:
    result = {
        "ACCESS": "HOLD", "CLAIM": "UNKNOWN",
        "DURABLE_RECEIPT": "UNKNOWN", "EXTERNAL_LEDGER": "UNKNOWN",
        "BLOB_FINGERPRINT_SHA256": "NOT_AVAILABLE",
        "BLOB_GET_RETRY": "FORBIDDEN",
        "BLOB_WRITE": "NO", "SQL_QUERY": "NO",
    }
    token = None
    try:
        source_zip = Path.home() / SOURCE_NAME
        if (not source_zip.is_file() or source_zip.is_symlink()
                or digest(source_zip.read_bytes()) != SOURCE_SHA):
            raise RuntimeError("SOURCE")
        with ZipFile(source_zip) as archive:
            if digest(archive.read("binding-index.json")) != BINDING_SHA:
                raise RuntimeError("BINDING")
            with tempfile.TemporaryDirectory(prefix="fv-acceptance-recovery-") as raw:
                root = Path(raw)
                archive.extractall(root)
                sys.path.insert(0, str(root))
                import g1_blob_call_journal_REVIEW_ONLY as ledger
                import g1_readonly_recovery_REVIEW_ONLY as recovery
                from p3_install_gates import PAYLOAD_SHA256
                from p3_kudu_cloudshell_parent import ident
                from p3_kudu_transport import select_single_instance
                from p3_ssh_transport_contract import OWNER_SHA, check_owner_account, validate_owner_token

                check_owner_account(az_json("account", "show"))
                owner = az_json("ad", "signed-in-user", "show")
                if ident(owner.get("id", "")) != OWNER_SHA:
                    raise RuntimeError("OWNER")
                instance = select_single_instance(
                    az_json("webapp", "list-instances", "-g", RG, "-n", APP)
                )
                current_instance_sha = ident(instance)
                response = az_json(
                    "account", "get-access-token", "--resource",
                    "https://storage.azure.com/"
                )
                token = response.get("accessToken")
                if not isinstance(token, str):
                    raise RuntimeError("TOKEN")
                validate_owner_token(token, "storage", time.time())

                def get_key(key: str, limit: int = 4096):
                    if not isinstance(key, str):
                        raise RuntimeError("TARGET")
                    allowed_claim = key == CLAIM_KEY
                    allowed_ledger = re.fullmatch(
                        re.escape(ledger.PREFIX)
                        + r"[0-9a-f]{32}/(?:(?:0[0-9]|1[0-5])-(?:before|after)|postcheck)\.json",
                        key,
                    ) is not None
                    if not (allowed_claim or allowed_ledger):
                        raise RuntimeError("TARGET")
                    headers = {
                        "Authorization": "Bearer " + token,
                        "x-ms-version": "2023-11-03",
                        "x-ms-date": email.utils.formatdate(usegmt=True),
                    }
                    conn = http.client.HTTPSConnection(
                        ACCOUNT + ".blob.core.windows.net",
                        timeout=20, context=ssl.create_default_context(),
                    )
                    try:
                        conn.request("GET", "/" + CONTAINER + "/" + key, headers=headers)
                        resp = conn.getresponse()
                        body = resp.read(limit + 1)
                        if len(body) > limit:
                            raise RuntimeError("SIZE")
                        return resp.status, resp.getheader("ETag", ""), body
                    finally:
                        conn.close()

                status, _etag, claim_raw = get_key(CLAIM_KEY)
                if status != 200 or not claim_raw:
                    result["CLAIM"] = "ABSENT" if status == 404 else "UNKNOWN"
                    raise RuntimeError("CLAIM_GET")
                claim = json.loads(claim_raw)
                operation_id = claim.get("operation_id")
                instance_sha = claim.get("instance_sha256")
                artifact_sha = claim.get("artifact_sha256")
                if (artifact_sha != SOURCE_SHA or instance_sha != current_instance_sha
                        or not re.fullmatch(r"[0-9a-f]{32}", str(operation_id or ""))):
                    result["CLAIM"] = "HOLD"
                    raise RuntimeError("CLAIM_SCOPE")
                claim_sha = recovery.verify_original_claim(
                    claim_raw, operation_id, SOURCE_SHA, current_instance_sha
                )
                result["CLAIM"] = "PASS"
                result["ACCESS"] = "PASS"

                def request(method, key, body, headers):
                    if method != "GET" or body != b"" or headers:
                        raise ledger.JournalHold("HOLD_G1_JOURNAL_RECOVERY_UNKNOWN")
                    return get_key(key, 2048)

                post_key = ledger.PREFIX + operation_id + "/postcheck.json"
                post_status, _post_etag, post_raw = request("GET", post_key, b"", {})
                if post_status == 404:
                    result["DURABLE_RECEIPT"] = "ABSENT"
                elif post_status != 200:
                    result["DURABLE_RECEIPT"] = "UNKNOWN"
                else:
                    try:
                        record = json.loads(post_raw)
                        fp = record.get("schema_fingerprint_sha256")
                        expected = ledger.construct_postcheck(operation_id, claim_sha, fp)
                        if post_raw != expected.payload:
                            raise RuntimeError("POSTCHECK")
                        result["DURABLE_RECEIPT"] = "PASS"
                        result["BLOB_FINGERPRINT_SHA256"] = fp
                    except Exception:
                        result["DURABLE_RECEIPT"] = "INVALID"

                if result["DURABLE_RECEIPT"] != "PASS":
                    successes = 0
                    for index, want in enumerate(PAYLOAD_SHA256):
                        before = ledger.construct(operation_id, claim_sha, index, "BEFORE", want)
                        after = ledger.construct(operation_id, claim_sha, index, "AFTER", want, "SUCCESS")
                        if ledger.recover_exact(before, request) != before.digest:
                            break
                        if ledger.recover_exact(after, request) != after.digest:
                            break
                        successes += 1
                    result["EXTERNAL_LEDGER"] = "PASS" if successes == 16 else "HOLD"
                    result["EXTERNAL_CONFIRMED_CALL_PAIRS"] = str(successes)
                else:
                    result["EXTERNAL_LEDGER"] = "NOT_CHECKED"
    except Exception:
        pass
    finally:
        token = None
    for key, value in result.items():
        print(f"{key}={value}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
