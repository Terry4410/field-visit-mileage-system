#!/usr/bin/env python3
"""Authorization-locked Path B G1 installer parent using pinned App SSH tunnel.

Importing this module has no side effects.  Live mode remains unavailable
without a separately signed-off exact distribution SHA, binding SHA, unique
instance hash and short-lived Owner authorization.  There is no retry path.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import select
import stat
import subprocess
import tempfile
import time
import zipfile
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent

from p3_g1_owner_management import az, blob_download, download_once, private_endpoint_ip
from p3_historical_blob_evidence import verify_readonly as verify_historical_blobs
from p3_install_gates import digest, validate_local_source, validate_original_package
from p3_kudu_transport import (REMOTE_STAGE, TransportHold,
                               runner_command, safe_code, select_single_instance,
                               validate_cli_version)
from p3_g1_pinned_ssh import SshExecSession
from p3_ssh_stage import strict_seal_crossbinding
from p3_ssh_transport_contract import (APP, OWNER_SHA, RG, check_owner_account,
                                       validate_owner_token)

SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DATABASE = "db-fieldvisit-pathb-uat"
CONTRACT = "FIELDVISIT_PATHB_G1_KUDU_INSTALL_V1"
DECISION = "OWNER_APPROVED_PATHB_G1_KUDU_NEW_EXACT_ONCE"
TRANSPORT = "OFFICIAL_AZ_WEBAPP_SSH_PINNED_V1"
REMOTE_WORK = "/tmp/fv-pathb-g1-original-installer-20261110-work"
REMOTE_RAM = "/dev/shm/fv-pathb-g1-original-installer-20261110"
MAX_RECEIPT_BYTES = 16384


class Hold(Exception):
    pass


def require(value, code):
    if not value:
        raise Hold(code)


def ident(value):
    return hashlib.sha256(str(value).lower().encode()).hexdigest()


def load(path):
    return json.loads(Path(path).read_text("utf-8"))


def validate_binding(root):
    raw = (root / "binding-index.json").read_bytes()
    binding = json.loads(raw)
    require(binding.get("contract") == "FIELDVISIT_PATHB_G1_ORIGINAL_BINDING_V1",
            "BINDING_CONTRACT_HOLD")
    require(binding.get("approved_release") ==
            "d6c220877b9976b6478d85b5171d4e06293595ae",
            "BINDING_RELEASE_HOLD")
    files = binding.get("files")
    require(isinstance(files, dict) and files, "BINDING_FILES_HOLD")
    for name, expected in files.items():
        path = root / name
        require(re.fullmatch(r"[A-Za-z0-9_.-]{1,120}", name) is not None,
                "BINDING_PATH_HOLD")
        require(path.is_file() and not path.is_symlink() and
                re.fullmatch(r"[a-f0-9]{64}", str(expected)) is not None and
                digest(path.read_bytes()) == expected, "BINDING_FILE_HOLD")
    return binding, digest(raw)


def validate_distribution_archive(distribution, binding):
    """Fail closed on ZIP content, not only the Owner-pinned outer digest."""
    expected = dict(binding["files"])
    expected["binding-index.json"] = digest((HERE / "binding-index.json").read_bytes())
    try:
        with zipfile.ZipFile(distribution) as archive:
            infos = archive.infolist()
            names = [item.filename for item in infos]
            require(len(names) == len(expected) and set(names) == set(expected),
                    "DISTRIBUTION_MEMBERS_HOLD")
            for item in infos:
                require(not item.is_dir() and not (item.external_attr >> 16 & 0o170000) == 0o120000,
                        "DISTRIBUTION_ENTRY_HOLD")
                require(item.file_size <= 4 * 1024 * 1024,
                        "DISTRIBUTION_SIZE_HOLD")
                require(digest(archive.read(item)) == expected[item.filename],
                        "DISTRIBUTION_MEMBER_HASH_HOLD")
    except (OSError, zipfile.BadZipFile, RuntimeError, ValueError, KeyError) as exc:
        raise Hold("DISTRIBUTION_FORMAT_HOLD") from exc


def validate_authorization(path, artifact_sha, binding_sha, instance, now=None):
    auth = load(path)
    required = {"operation_id","contract", "decision", "artifact_sha256",
                "binding_index_sha256", "server", "database",
                "installation_attempts", "instance_sha256", "not_before",
                "expires_at"}
    require(isinstance(auth, dict) and set(auth) == required,
            "AUTHORIZATION_SHAPE_HOLD")
    require(auth["contract"] == CONTRACT and auth["decision"] == DECISION,
            "AUTHORIZATION_DECISION_HOLD")
    require(auth["artifact_sha256"] == artifact_sha and
            auth["binding_index_sha256"] == binding_sha,
            "AUTHORIZATION_ARTIFACT_HOLD")
    require(auth["server"] == SERVER and auth["database"] == DATABASE and
            auth["installation_attempts"] == 1,
            "AUTHORIZATION_SCOPE_HOLD")
    require(re.fullmatch(r'[0-9a-f]{32}',str(auth.get('operation_id',''))) is not None,
            'AUTHORIZATION_OPERATION_HOLD')
    require(re.fullmatch(r"[a-f0-9]{64}", str(auth["instance_sha256"])) is not None,
            "AUTHORIZATION_INSTANCE_HOLD")
    if instance is not None:
        require(auth["instance_sha256"] == ident(instance),
                "AUTHORIZATION_INSTANCE_HOLD")
    try:
        start = datetime.fromisoformat(auth["not_before"].replace("Z", "+00:00")).timestamp()
        end = datetime.fromisoformat(auth["expires_at"].replace("Z", "+00:00")).timestamp()
    except Exception as exc:
        raise Hold("AUTHORIZATION_TIME_HOLD") from exc
    clock = time.time() if now is None else float(now)
    require(auth["not_before"].endswith("Z") and auth["expires_at"].endswith("Z") and
            start <= clock <= end and 0 < end - start <= 1800,
            "AUTHORIZATION_TIME_HOLD")
    return auth


def management(cfg):
    account = az(["account", "show"])
    check_owner_account(account)
    owner = az(["ad", "signed-in-user", "show"])
    require(ident(owner.get("id", "")) == OWNER_SHA, "OWNER_IDENTITY_HOLD")
    validate_cli_version(az(["version"]))
    short = cfg["server"].split(".")[0]
    server = az(["sql", "server", "show", "-g", RG, "-n", short])
    rules = az(["sql", "server", "firewall-rule", "list", "-g", RG, "-s", short])
    admins = az(["sql", "server", "ad-admin", "list", "-g", RG, "-s", short])
    app = az(["webapp", "show", "-g", RG, "-n", APP])
    instances = az(["webapp", "list-instances", "-g", RG, "-n", APP])
    instance = select_single_instance(instances)
    require(server.get("fullyQualifiedDomainName") == cfg["server"] and
            server.get("publicNetworkAccess") == "Disabled" and len(rules) == 0,
            "SQL_PRIVATE_ONLY_HOLD")
    require(server.get("minimalTlsVersion") == "1.2" and
            app.get("name") == APP and app.get("state") == "Running" and
            bool(app.get("virtualNetworkSubnetId")), "APP_TARGET_HOLD")
    require(any(ident(item.get("sid", "")) == OWNER_SHA for item in admins),
            "SQL_ENTRA_ADMIN_HOLD")
    return instance, {
        "server": SERVER, "database": DATABASE, "app": APP,
        "pna": "Disabled", "firewall_count": 0,
        "owner_admin_match": True,
        "private_sql_ip": private_endpoint_ip(server["id"]),
        "exec_transport": TRANSPORT,
        "instance_sha256": ident(instance), "observed_at": time.time(),
    }


def build_stage(work, binding, authorization, facts):
    files = set(binding["files"]) | {"binding-index.json", "executor_config.json"}
    stage = work / "p3-kudu-stage.zip"
    with zipfile.ZipFile(stage, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(files):
            archive.write(HERE / name, name)
        archive.write(work / "original.zip", "original.zip")
        archive.write(work / "original.seal.json", "original.seal.json")
        archive.writestr("live-facts.json", json.dumps(facts, separators=(",", ":")))
        archive.write(authorization, "owner-authorization.json")
        for item in load(HERE / "executor_config.json")["tools"]:
            archive.write(work / "debs" / (item["name"] + ".deb"),
                          "debs/" + item["name"] + ".deb")
    members = {}
    with zipfile.ZipFile(stage) as archive:
        for info in archive.infolist():
            require(not info.is_dir(), "STAGE_MEMBER_HOLD")
            members[info.filename] = digest(archive.read(info.filename))
    return stage, members


def read_final_receipt(session, seconds=2700):
    end = min(session.deadline, session._clock() + seconds)
    prefix = b"FV_P3_FINAL_RESULT="
    while session._clock() < end:
        session._check_secret_echo()
        cursor = 0
        for raw in session.buffer.splitlines(keepends=True):
            if not raw.endswith(b"\n"):
                break
            line = raw[:-1].removesuffix(b"\r")
            cursor += len(raw)
            if line.startswith(prefix):
                require(len(line) <= MAX_RECEIPT_BYTES, "FINAL_RECEIPT_SIZE_HOLD")
                try:
                    obj = json.loads(line[len(prefix):].decode("ascii"))
                except Exception as exc:
                    raise Hold("FINAL_RECEIPT_JSON_HOLD") from exc
                session.buffer = session.buffer[cursor:]
                return validate_final_receipt(obj)
        readable = select.select([session.fd], [], [], 0.2)[0]
        if readable:
            chunk = os.read(session.fd, 65536)
            require(bool(chunk), "FINAL_CHANNEL_HOLD")
            session.buffer += chunk
            require(len(session.buffer) <= 2 * 1024 * 1024,
                    "FINAL_OUTPUT_HOLD")
        elif session.proc is None or session.proc.poll() is not None:
            raise Hold("FINAL_CHANNEL_HOLD")
    raise Hold("FINAL_RECEIPT_TIMEOUT_HOLD")


def validate_final_receipt(obj):
    require(isinstance(obj, dict), "FINAL_RECEIPT_SHAPE_HOLD")
    allowed = {"status", "sql_call_count", "layer_progress", "marker",
               "journal", "user_tables", "markers",
               "schema_fingerprint_sha256", "failure", "cleanup",
               "elapsed_seconds"}
    require(set(obj).issubset(allowed), "FINAL_RECEIPT_SHAPE_HOLD")
    require(obj.get("status") in {"PASS", "HOLD"} and
            isinstance(obj.get("sql_call_count"), int) and
            0 <= obj["sql_call_count"] <= 19 and
            isinstance(obj.get("layer_progress"), int) and
            0 <= obj["layer_progress"] <= 14 and
            obj.get("marker") in {"NOT_CONSUMED", "CONSUMED"} and
            obj.get("cleanup") in {"PASS", "HOLD"},
            "FINAL_RECEIPT_SHAPE_HOLD")
    if obj["status"] == "PASS":
        require(obj["sql_call_count"] == 19 and obj["layer_progress"] == 14 and
                obj.get("marker") == "CONSUMED" and
                obj.get("user_tables") == 69 and obj.get("markers") == 3 and
                re.fullmatch(r"[a-f0-9]{64}",
                             str(obj.get("schema_fingerprint_sha256", ""))) is not None and
                obj.get("cleanup") == "PASS" and "failure" not in obj,
                "FINAL_PASS_CONTRACT_HOLD")
    return obj


def cleanup_command(member_hashes, verify_only=False):
    expected = json.dumps(member_hashes, sort_keys=True, separators=(",", ":"))
    source = f'''import hashlib,json,os,shutil,stat\nfrom pathlib import Path\nr=Path({REMOTE_STAGE!r});w=Path({REMOTE_WORK!r});m=Path({REMOTE_RAM!r});e=json.loads({expected!r})\ndef refs():\n n=0;needles=[str(r).encode(),str(w).encode(),str(m).encode()];me=os.getpid();parent=os.getppid()\n for x in os.listdir('/proc'):\n  if not x.isdigit() or int(x) in (me,parent):continue\n  try:\n   b=open('/proc/'+x+'/cmdline','rb').read();n+=int(any(v in b for v in needles))\n  except (FileNotFoundError,PermissionError,ProcessLookupError):pass\n return n\nif {verify_only!r}:\n ok=not os.path.lexists(r) and not os.path.lexists(w) and not os.path.lexists(m) and refs()==0\n print('\\nFV_INDEPENDENT_CLEANUP='+('PASS' if ok else 'HOLD'),flush=True)\nelse:\n ok=r.is_dir() and not r.is_symlink() and r.stat().st_uid==os.geteuid() and stat.S_IMODE(r.stat().st_mode)==0o700 and not os.path.lexists(w) and not os.path.lexists(m) and refs()==0\n if ok:\n  children=set(os.listdir(r));expected_dirs={{str(Path(name).parent) for name in e if '/' in name}};expected_dirs.discard('.')\n expected_root={{name.split('/')[0] for name in e}}\n ok=children==expected_root or children==expected_root|{{'__pycache__'}}\n if ok:\n  actual_dirs=set();actual_files=set()\n  for base,dirs,files in os.walk(r,followlinks=False):\n   rel=Path(base).relative_to(r)\n   for name in dirs:\n    p=Path(base)/name;sub=str((rel/name).as_posix());ok=ok and not p.is_symlink() and p.stat().st_uid==os.geteuid();actual_dirs.add(sub)\n   for name in files:\n    p=Path(base)/name;sub=str((rel/name).as_posix());ok=ok and not p.is_symlink() and p.stat().st_uid==os.geteuid();actual_files.add(sub)\n  ok=ok and actual_dirs==expected_dirs|({{'__pycache__'}} if '__pycache__' in children else set())\n  ok=ok and actual_files==set(e)|({{'__pycache__/'+name for name in os.listdir(r/'__pycache__')}} if '__pycache__' in children else set())\n if ok:\n  for name,want in e.items():\n   p=r/name;ok=ok and p.is_file() and not p.is_symlink() and p.stat().st_uid==os.geteuid() and hashlib.sha256(p.read_bytes()).hexdigest()==want\n if ok and '__pycache__' in children:\n  c=r/'__pycache__';ok=c.is_dir() and not c.is_symlink() and c.stat().st_uid==os.geteuid()\n  if ok:\n   for name in os.listdir(c):\n    p=c/name;ok=ok and name.endswith('.pyc') and p.is_file() and not p.is_symlink() and p.stat().st_uid==os.geteuid()\n if ok:shutil.rmtree(r)\n print('\\nFV_KUDU_OWNED_CLEANUP='+('PASS' if ok else 'HOLD'),flush=True)\n'''
    import base64
    encoded = base64.b64encode(source.encode()).decode()
    return ("python3 -c \"import base64;exec(compile(base64.b64decode('" +
            encoded + "'),'p3-cleanup','exec'))\"\n").encode()


def execute(args):
    result = "HOLD"
    stop = "LOCAL_BINDING"
    receipt = None
    session = verify_session = None
    staged = cleaned = cleanup_verified = False
    sql_requested = storage_requested = False
    sql_token = storage_token = None
    member_hashes = None
    instance = None
    sessions_opened = 0
    deadline = None
    def new_session():
        nonlocal sessions_opened
        require(instance is not None and deadline is not None and sessions_opened < 2,
                "KUDU_SESSION_CAP_HOLD")
        sessions_opened += 1
        return SshExecSession(instance, deadline=deadline)
    try:
        require(re.fullmatch(r"[a-f0-9]{64}", args.artifact_sha256 or "") is not None,
                "ARTIFACT_SHA_HOLD")
        distribution = Path(args.distribution)
        require(distribution.is_file() and not distribution.is_symlink() and
                digest(distribution.read_bytes()) == args.artifact_sha256,
                "DISTRIBUTION_HOLD")
        validate_local_source(HERE)
        binding, binding_sha = validate_binding(HERE)
        validate_distribution_archive(distribution, binding)
        cfg = load(HERE / "executor_config.json")
        # Reject missing, expired, or mismatched Owner scope before ANY Azure call.
        stop = "PRE_AUTHORIZATION"
        validate_authorization(args.authorization, args.artifact_sha256,
                               binding_sha, None)
        stop = "MANAGEMENT"
        instance, facts = management(cfg)
        # Fresh instance identity is only available after authorized management GETs.
        validate_authorization(args.authorization, args.artifact_sha256,
                               binding_sha, instance)
        deadline = time.monotonic() + cfg["total_deadline_seconds"]
        with tempfile.TemporaryDirectory(prefix="fv-p3-kudu-parent-") as raw:
            work = Path(raw)
            (work / "debs").mkdir()
            stop = "ORIGINAL_PACKAGE"
            blob_download(cfg, cfg["package_blob"], work / "original.zip")
            blob_download(cfg, cfg["seal_blob"], work / "original.seal.json")
            package = (work / "original.zip").read_bytes()
            seal = (work / "original.seal.json").read_bytes()
            require(digest(package) == cfg["package_sha256"] and
                    digest(seal) == cfg["seal_sha256"], "ORIGINAL_BLOB_HASH_HOLD")
            validate_original_package(package)
            strict_seal_crossbinding(package, seal)
            stop = "TOOLS"
            for item in cfg["tools"]:
                download_once(item["url"], work / "debs" / (item["name"] + ".deb"),
                              item["sha256"])
            stop = "STORAGE_EVIDENCE"
            storage_requested = True
            storage_token = az(["account", "get-access-token", "--resource",
                                "https://storage.azure.com/"])["accessToken"]
            validate_owner_token(storage_token, "storage", time.time())
            old = verify_historical_blobs(cfg, az, storage_token)
            require(old["current_head_get_contract"] == "PASS" and
                    old["historical_immutability_proven"] is False and
                    old["historical_parent_execution_proven"] is False,
                    "HISTORICAL_BLOB_HOLD")
            stop = "STAGE"
            stage_file, member_hashes = build_stage(work, binding,
                                                     Path(args.authorization), facts)
            stage_bytes = stage_file.read_bytes()
            session = new_session()
            session.start()
            session.await_cli_shell_banner()
            session.establish_safe_channel()
            session.stage_archive(stage_bytes, digest(stage_bytes))
            staged = True
            session.send_public(runner_command(args.artifact_sha256),
                                "P3_RUNNER_SENT")
            session.expect_fixed(b"FV_P3_TOKENS_READY", 300, "TOKENS_READY")
            stop = "TOKEN_HANDOFF"
            sql_requested = True
            sql_token = az(["account", "get-access-token", "--resource",
                            "https://database.windows.net/"])["accessToken"]
            validate_owner_token(sql_token, "sql", time.time())
            validate_owner_token(storage_token, "storage", time.time())
            session.send_secret_line(sql_token, validator=lambda token:
                                     validate_owner_token(token, "sql", time.time()),
                                     audit_code="SQL_TOKEN_SENT")
            sql_token = None
            session.send_secret_line(storage_token, validator=lambda token:
                                     validate_owner_token(token, "storage", time.time()),
                                     audit_code="STORAGE_TOKEN_SENT")
            storage_token = None
            session.mark_sql_started()
            stop = "FINAL_RECEIPT"
            receipt = read_final_receipt(session)
            session.mark_complete()
            stop = "CLEANUP"
            session.send_public(cleanup_command(member_hashes), "OWNED_CLEANUP_SENT")
            session.expect_fixed(b"FV_KUDU_OWNED_CLEANUP=PASS", 60,
                                 "OWNED_CLEANUP")
            cleaned = True
            session.close()
            session = None
            verify_session = new_session()
            verify_session.start()
            verify_session.await_cli_shell_banner()
            verify_session.establish_safe_channel()
            verify_session.send_public(cleanup_command(member_hashes, True),
                                       "CLEANUP_VERIFY_SENT")
            verify_session.expect_fixed(b"FV_INDEPENDENT_CLEANUP=PASS", 60,
                                        "CLEANUP_VERIFIED")
            cleanup_verified = True
            result = receipt["status"]
            stop = "COMPLETE" if result == "PASS" else "RUNNER_HOLD"
    except Exception as exc:
        if isinstance(exc, TransportHold):
            stop = "TRANSPORT_" + safe_code(exc)
        result = "HOLD"
    finally:
        sql_token = storage_token = None
        if session is not None and staged and member_hashes is not None and receipt is not None:
            try:
                session.send_public(cleanup_command(member_hashes),
                                    "OWNED_CLEANUP_AFTER_HOLD")
                session.expect_fixed(b"FV_KUDU_OWNED_CLEANUP=PASS", 60,
                                     "OWNED_CLEANUP")
                cleaned = True
            except Exception:
                pass
        if session is not None:
            session.close()
        # If the first session ended without a trusted final receipt, the one
        # remaining independent session may clean only when the bounded remote
        # command proves there is no related process and both work/RAM paths
        # are already absent.  Otherwise it emits HOLD and nothing is removed.
        if staged and not cleaned and verify_session is None and instance is not None and sessions_opened < 2:
            try:
                verify_session = new_session()
                verify_session.start()
                verify_session.await_cli_shell_banner()
                verify_session.establish_safe_channel()
                verify_session.send_public(cleanup_command(member_hashes),
                                           "OWNED_CLEANUP_INDEPENDENT")
                verify_session.expect_fixed(b"FV_KUDU_OWNED_CLEANUP=PASS", 60,
                                            "OWNED_CLEANUP")
                cleaned = True
                verify_session.send_public(cleanup_command(member_hashes, True),
                                           "CLEANUP_VERIFY_SENT")
                verify_session.expect_fixed(b"FV_INDEPENDENT_CLEANUP=PASS", 60,
                                            "CLEANUP_VERIFIED")
                cleanup_verified = True
            except Exception:
                cleanup_verified = False
        elif cleaned and not cleanup_verified and verify_session is None and instance is not None and sessions_opened < 2:
            try:
                verify_session = new_session()
                verify_session.start()
                verify_session.await_cli_shell_banner()
                verify_session.establish_safe_channel()
                verify_session.send_public(cleanup_command(member_hashes, True),
                                           "CLEANUP_VERIFY_SENT")
                verify_session.expect_fixed(b"FV_INDEPENDENT_CLEANUP=PASS", 60,
                                            "CLEANUP_VERIFIED")
                cleanup_verified = True
            except Exception:
                cleanup_verified = False
        if verify_session is not None:
            verify_session.close()
        # Bounded non-secret phase only: distinguishes CLI/echo/stage/runner timeout.
        latest = (session.audit[-1] if session is not None and session.audit else "NONE")
        if not re.fullmatch(r"[A-Z0-9_]{3,80}", latest):
            latest = "UNKNOWN"
        print("LAST_PUBLIC_CHANNEL_CHECKPOINT=" + latest)
        print("P3_KUDU_LIVE=" + result)
        print("STOP_STAGE=" + re.sub(r"[^A-Z0-9_]", "_", stop)[:100])
        print("SQL_TOKEN_REQUEST=" + ("YES" if sql_requested else "NO"))
        print("STORAGE_TOKEN_REQUEST=" + ("YES" if storage_requested else "NO"))
        print("SQL_CALL_COUNT=" + (str(receipt["sql_call_count"])
                                   if receipt is not None else "0_OR_1_UNKNOWN"))
        print("LAYER_PROGRESS=" + (str(receipt["layer_progress"])
                                  if receipt is not None else "UNKNOWN"))
        print("BLOB_MARKER=" + (receipt.get("marker", "UNKNOWN")
                               if receipt is not None else "UNKNOWN"))
        print("REMOTE_CLEANUP=" + ("PASS" if cleaned and cleanup_verified
                                   else "HOLD_OR_UNKNOWN"))
        print("AUTO_RETRY=FORBIDDEN")
    return 0 if result == "PASS" and cleanup_verified else 1


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--live", action="store_true")
    parser.add_argument("--authorization")
    parser.add_argument("--distribution")
    parser.add_argument("--artifact-sha256")
    args = parser.parse_args()
    if not args.live:
        print("AUTHORIZATION_LOCKED_NONDEPLOY")
        return 0
    require(args.authorization and args.distribution and args.artifact_sha256,
            "OWNER_AUTHORIZATION_REQUIRED")
    return execute(args)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception:
        print("P3_KUDU_LIVE=HOLD")
        print("STOP_STAGE=PRELAUNCH_HOLD")
        print("AUTO_RETRY=FORBIDDEN")
        raise SystemExit(1)
