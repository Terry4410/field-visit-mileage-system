#!/usr/bin/env python3
"""Cloud Shell parent for one separately-authorized SELECT-only Kudu run.

No execution is possible without an external exact-SHA authorization JSON.
The parent makes one SQL-audience token request only after the exact stage and
App-side tool/DNS gates report readiness.  It has no Storage, Blob, marker,
Installer, DDL, deployment, or retry code.
"""
from __future__ import annotations

import argparse, base64, hashlib, json, os, re, select, shutil, subprocess
import tempfile, time, zipfile
from pathlib import Path

from p3_kudu_transport import (KuduExecSession, REMOTE_STAGE, TransportHold,
                               safe_code, select_single_instance,
                               validate_cli_version)

RG = "rg-fieldvisit-uat"
APP = "api-fieldvisit-pathb-uat"
SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
DATABASE = "db-fieldvisit-pathb-uat"
SUBSCRIPTION_HASH = "7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47"
TENANT_HASH = "976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c"
OWNER_HASH = "2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"
SQL_AUDIENCE = "https://database.windows.net/"
MAX_SECONDS = 600
RAM_ROOT = "/dev/shm/fv-pathb-select-only-live-v1-20261105"
RAM_RECEIPT_SHA256 = "baeb9de23cffb4099b6dbe8b9ace59f83500e35dace443f234a638a62b9b755e"


class Hold(Exception): pass
def h(raw): return hashlib.sha256(raw).hexdigest()
def ident(value): return h(str(value).lower().encode())
def need(value, code):
    if not value: raise Hold(code)


def az_json(args, timeout=30):
    result = subprocess.run(["az", *args, "--only-show-errors", "-o", "json"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=timeout)
    need(result.returncode == 0, "AZURE_HOLD")
    try: return json.loads(result.stdout)
    except Exception as exc: raise Hold("AZURE_HOLD") from exc


def validate_account(record):
    need(ident(record.get("id", "")) == SUBSCRIPTION_HASH, "SUBSCRIPTION_HOLD")
    need(ident(record.get("tenantId", "")) == TENANT_HASH, "TENANT_HOLD")
    need(record.get("user", {}).get("type", "").lower() == "user", "OWNER_HOLD")


def validate_owner(record):
    need(ident(record.get("id", "")) == OWNER_HASH, "OWNER_HOLD")


def validate_app(record):
    expected_suffix = "/resourceGroups/rg-fieldvisit-uat/providers/Microsoft.Web/sites/api-fieldvisit-pathb-uat"
    need(str(record.get("id", "")).lower().endswith(expected_suffix.lower()), "APP_TARGET_HOLD")
    need(record.get("name") == APP and str(record.get("resourceGroup", "")).lower() == RG, "APP_TARGET_HOLD")


def validate_token(token, now=None):
    need(isinstance(token, str) and token.isascii() and token.count(".") == 2 and len(token) <= 16384, "TOKEN_HOLD")
    try:
        part = token.split(".")[1]; claims = json.loads(base64.urlsafe_b64decode(part + "=" * (-len(part) % 4)))
        clock = time.time() if now is None else float(now)
        need(ident(claims.get("oid", "")) == OWNER_HASH, "TOKEN_HOLD")
        need(ident(claims.get("tid", "")) == TENANT_HASH, "TOKEN_HOLD")
        need(str(claims.get("aud", "")).rstrip("/") == SQL_AUDIENCE.rstrip("/"), "TOKEN_HOLD")
        need(float(claims.get("nbf", 0)) <= clock + 30 and float(claims["exp"]) - clock >= 3000, "TOKEN_HOLD")
    except Hold: raise
    except Exception as exc: raise Hold("TOKEN_HOLD") from exc
    return True


def exact_stage(root):
    names = ["p3_select_only_app.py", "p3_select_only_boundary.py", "preflight.sql", "select-only-config.json"]
    out = Path(tempfile.mkstemp(prefix="fv-select-only-stage.", suffix=".zip", dir="/tmp")[1])
    try:
        with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name in names: archive.write(root / name, arcname=name)
        return out, out.read_bytes(), {name:h((root/name).read_bytes()) for name in names}
    except Exception:
        out.unlink(missing_ok=True); raise


def expect_exact_line(session, line, seconds):
    end = min(session.deadline, session._clock() + seconds)
    while session._clock() < end:
        session._check_secret_echo()
        if line + b"\r\n" in session.buffer or line + b"\n" in session.buffer:
            return
        readable = select.select([session.fd], [], [], 0.2)[0]
        if readable:
            chunk = os.read(session.fd, 65536)
            need(bool(chunk), "CHANNEL_HOLD"); session.buffer += chunk
            need(len(session.buffer) <= 2*1024*1024, "CHANNEL_HOLD")
        elif session.proc is None or session.proc.poll() is not None: raise Hold("CHANNEL_HOLD")
    raise Hold("CHANNEL_TIMEOUT")


def cleanup_command(file_hashes, *, verify_only=False):
    payload = json.dumps(file_hashes, sort_keys=True, separators=(",", ":"))
    body = f'''import hashlib,json,os,shutil,stat\nfrom pathlib import Path\nr=Path({REMOTE_STAGE!r});m=Path({RAM_ROOT!r});e=json.loads({payload!r})\nok=r.is_dir() and not r.is_symlink() and r.stat().st_uid==os.geteuid() and stat.S_IMODE(r.stat().st_mode)==0o700\nfor n,d in e.items():\n p=r/n;ok=ok and p.is_file() and not p.is_symlink() and hashlib.sha256(p.read_bytes()).hexdigest()==d\nif m.exists():\n ok=ok and m.is_dir() and not m.is_symlink() and m.stat().st_uid==os.geteuid() and stat.S_IMODE(m.stat().st_mode)==0o700\n q=m/'receipt';ok=ok and q.is_file() and not q.is_symlink() and hashlib.sha256(q.read_bytes()).hexdigest()=={RAM_RECEIPT_SHA256!r}\nif {str(verify_only)!r}=='True':\n print('FV_INDEPENDENT_CLEANUP='+('PASS' if not r.exists() and not m.exists() else 'HOLD'),flush=True)\nelif ok:\n shutil.rmtree(r);shutil.rmtree(m,ignore_errors=True);print('FV_SELECT_ONLY_CLEANUP=PASS',flush=True)\nelse:print('FV_SELECT_ONLY_CLEANUP=HOLD',flush=True)\n'''
    encoded = base64.b64encode(body.encode()).decode()
    return f"python3 -c \"import base64;exec(compile(base64.b64decode('{encoded}'),'cleanup','exec'))\"\n".encode()


def load_authorization(path, distribution_sha):
    data = json.loads(path.read_text("utf-8"))
    need(data == {"contract":"FIELDVISIT_PATHB_SELECT_ONLY_KUDU_V1", "approved":True,
                  "distribution_sha256":distribution_sha, "server":SERVER,
                  "database":DATABASE, "max_sql_calls":1}, "AUTHORIZATION_HOLD")


def main():
    parser=argparse.ArgumentParser(); parser.add_argument("--live", action="store_true"); parser.add_argument("--authorization"); parser.add_argument("--distribution"); parser.add_argument("--distribution-sha256")
    args=parser.parse_args(); root=Path(__file__).resolve().parent
    if not args.live or not args.authorization or not args.distribution or not args.distribution_sha256:
        print("SELECT_ONLY_LIVE=HOLD_AUTHORIZATION", flush=True); return 2
    stage_path=None; session=None; verify_session=None; token=""; result="HOLD"; staged=False; cleaned=False; cleanup_verified=False; instance=None; file_hashes=None; deadline=None
    try:
        need(re.fullmatch(r"[a-f0-9]{64}", args.distribution_sha256 or "") is not None, "AUTHORIZATION_HOLD")
        distribution=Path(args.distribution)
        need(distribution.is_file() and not distribution.is_symlink() and h(distribution.read_bytes())==args.distribution_sha256, "DISTRIBUTION_HOLD")
        load_authorization(Path(args.authorization), args.distribution_sha256)
        validate_account(az_json(["account","show"]))
        validate_owner(az_json(["ad","signed-in-user","show"]))
        validate_app(az_json(["webapp","show","-g",RG,"-n",APP]))
        validate_cli_version(az_json(["version"]))
        instances=az_json(["webapp","list-instances","-g",RG,"-n",APP])
        instance=select_single_instance(instances)
        stage_path, stage, file_hashes=exact_stage(root)
        deadline=time.monotonic()+MAX_SECONDS
        session=KuduExecSession(instance, deadline=deadline); session.start(); session.establish_safe_channel(); session.stage_archive(stage, h(stage)); staged=True
        session.send_public(f"python3 {REMOTE_STAGE}/p3_select_only_app.py --kudu-pty\n".encode(), "SELECT_RUNNER_SENT")
        session.expect_fixed(b"FV_P3_TOKENS_READY", 240, "SELECT_TOKEN_READY")
        token_record=az_json(["account","get-access-token","--resource",SQL_AUDIENCE], timeout=65)
        token=token_record.get("accessToken", ""); validate_token(token)
        session.send_secret_line(token, validator=lambda value: validate_token(value), audit_code="SQL_TOKEN_SENT"); token=""
        expect_exact_line(session, b"FV_P3_FINAL_RESULT=PASS_SELECT_ONLY", 150)
        session.send_public(cleanup_command(file_hashes), "CLEANUP_SENT"); expect_exact_line(session,b"FV_SELECT_ONLY_CLEANUP=PASS",30); cleaned=True
        session.close(); session=None
        verify_session=KuduExecSession(instance, deadline=deadline); verify_session.start(); verify_session.establish_safe_channel()
        verify_session.send_public(cleanup_command(file_hashes, verify_only=True), "CLEANUP_VERIFY_SENT")
        verify_session.expect_fixed(b"FV_INDEPENDENT_CLEANUP=PASS", 30, "CLEANUP_VERIFIED")
        cleanup_verified=True
        result="PASS"
    except Exception:
        result="HOLD"
    finally:
        token=""
        if session is not None and staged and not cleaned and file_hashes is not None:
            try:
                session.send_public(cleanup_command(file_hashes), "CLEANUP_SENT_AFTER_HOLD")
                expect_exact_line(session,b"FV_SELECT_ONLY_CLEANUP=PASS",30); cleaned=True
            except Exception:
                pass
        if session is not None: session.close()
        if cleaned and not cleanup_verified and instance is not None and deadline is not None and time.monotonic() < deadline:
            try:
                verify_session=KuduExecSession(instance, deadline=deadline); verify_session.start(); verify_session.establish_safe_channel()
                verify_session.send_public(cleanup_command(file_hashes, verify_only=True), "CLEANUP_VERIFY_SENT")
                verify_session.expect_fixed(b"FV_INDEPENDENT_CLEANUP=PASS", 30, "CLEANUP_VERIFIED"); cleanup_verified=True
            except Exception:
                cleanup_verified=False
        if verify_session is not None: verify_session.close()
        if stage_path is not None: stage_path.unlink(missing_ok=True)
        print("SELECT_ONLY_LIVE="+result, flush=True)
        print("SQL_CALL_COUNT="+("1" if result=="PASS" else "0_OR_1_UNKNOWN"), flush=True)
        print("REMOTE_CLEANUP="+("PASS" if cleaned and cleanup_verified else "HOLD_OR_UNKNOWN"), flush=True)
        print("AUTO_RETRY=FORBIDDEN", flush=True)
    return 0 if result=="PASS" else 1


if __name__ == "__main__": raise SystemExit(main())
