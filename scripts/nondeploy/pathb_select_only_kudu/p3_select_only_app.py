#!/usr/bin/env python3
"""Authorization-locked App-side runner for one fixed Path B SELECT.

This program has no Installer, Blob, marker, DDL, DML, or retry path.  It is
intended to run only inside the pinned App Service instance after a separately
approved Kudu parent stages this exact artifact.  Output is fixed and redacted.
"""
from __future__ import annotations

import argparse, base64, hashlib, json, os, re, shutil, socket, subprocess
import tempfile, termios, time, urllib.request
from pathlib import Path

from p3_select_only_boundary import select_only

ROOT = Path(__file__).resolve().parent
CONFIG = ROOT / "select-only-config.json"
QUERY = ROOT / "preflight.sql"
CONFIG_SHA256 = "29ca3d74e60c198e211beb2e94cf87958f97031605651389b7c2b46240d8c372"
TARGET_SERVER = "sql-fieldvisit-jpe-pathb-uat.database.windows.net"
TARGET_DATABASE = "db-fieldvisit-pathb-uat"
RAM_ROOT = Path("/dev/shm/fv-pathb-select-only-live-v1-20261105")
RAM_RECEIPT = b"FIELDVISIT_PATHB_SELECT_ONLY_RAM_V1\n"


class Hold(Exception):
    pass


def need(value, code):
    if not value:
        raise Hold(code)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def load_config():
    raw = CONFIG.read_bytes()
    need(sha(raw) == CONFIG_SHA256, "CONFIG_HASH")
    cfg = json.loads(raw)
    need(cfg["server"] == TARGET_SERVER and cfg["database"] == TARGET_DATABASE, "TARGET")
    need(sha(QUERY.read_bytes()) == cfg["preflight_sha256"], "SQL_HASH")
    text = QUERY.read_text("utf-8")
    scrubbed = re.sub(r"--.*?$|/\*.*?\*/|'(?:''|[^'])*'", "", text, flags=re.M | re.S).upper()
    need(not re.search(r"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|GRANT|DENY|REVOKE|EXEC(?:UTE)?)\b", scrubbed), "NOT_SELECT_ONLY")
    return cfg


def validate_dns(cfg):
    ips = {row[4][0] for row in socket.getaddrinfo(cfg["server"], 1433, type=socket.SOCK_STREAM)}
    need(ips == {cfg["private_sql_ip"]}, "PRIVATE_DNS")


def token_claims(token):
    need(isinstance(token, str) and token.isascii() and token.count(".") == 2 and len(token) <= 16384, "TOKEN_SHAPE")
    try:
        part = token.split(".")[1]
        return json.loads(base64.urlsafe_b64decode(part + "=" * (-len(part) % 4)))
    except Exception as exc:
        raise Hold("TOKEN_SHAPE") from exc


def validate_token(token, cfg, now=None):
    claims = token_claims(token)
    now = time.time() if now is None else float(now)
    need(sha(str(claims.get("oid", "")).lower().encode()) == cfg["owner_oid_sha256"], "TOKEN_OWNER")
    need(sha(str(claims.get("tid", "")).lower().encode()) == cfg["tenant_sha256"], "TOKEN_TENANT")
    need(str(claims.get("aud", "")).rstrip("/") == "https://database.windows.net", "TOKEN_AUDIENCE")
    try:
        nbf, exp = float(claims.get("nbf", 0)), float(claims["exp"])
    except Exception as exc:
        raise Hold("TOKEN_TIME") from exc
    need(nbf <= now + 30 and exp - now >= cfg["minimum_token_ttl_seconds"], "TOKEN_TTL")


def fetch_packages(cfg, package_dir):
    package_dir.mkdir(mode=0o700)
    context = __import__("ssl").create_default_context()
    for item in cfg["tool_packages"]:
        need(item["url"].startswith("https://"), "TOOL_URL")
        with urllib.request.urlopen(item["url"], context=context, timeout=45) as response:
            raw = response.read(25_000_001)
        need(0 < len(raw) <= 25_000_000 and sha(raw) == item["sha256"], "TOOL_PACKAGE")
        path = package_dir / (item["name"] + ".deb")
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "wb") as stream:
            stream.write(raw)


def stage_tools(cfg, package_dir, stage):
    tools = stage / "tools"
    for item in cfg["tool_packages"]:
        package = package_dir / (item["name"] + ".deb")
        need(package.is_file() and not package.is_symlink() and sha(package.read_bytes()) == item["sha256"], "TOOL_PACKAGE")
        run = subprocess.run(["dpkg-deb", "-x", str(package), str(tools)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
        need(run.returncode == 0, "TOOL_EXTRACT")
    for rel, digest in cfg["tool_files"].items():
        path = tools / rel
        need(path.is_file() and not path.is_symlink() and sha(path.read_bytes()) == digest, "TOOL_FILE")
    drivers = list((tools / "opt/microsoft/msodbcsql18/lib64").glob("libmsodbcsql-*.so.*"))
    need(len(drivers) == 1, "ODBC_DRIVER")
    conf = stage / "odbc"; conf.mkdir(mode=0o700)
    (conf / "odbcinst.ini").write_text("[ODBC Driver 18 for SQL Server]\nDriver=" + str(drivers[0]) + "\nUsageCount=1\n", encoding="utf-8")
    (conf / "odbc.ini").write_text("", encoding="utf-8")
    env = {"PATH":"/usr/bin:/bin", "HOME":str(stage), "LANG":"C.UTF-8", "LC_ALL":"C.UTF-8", "LD_LIBRARY_PATH":str(tools / "usr/lib/x86_64-linux-gnu"), "ODBCSYSINI":str(conf), "ODBCINSTINI":"odbcinst.ini", "ODBCINI":str(conf / "odbc.ini"), "ACCEPT_EULA":"Y"}
    binary = tools / "opt/mssql-tools18/bin/sqlcmd"
    for executable in (binary, drivers[0]):
        check = subprocess.run(["ldd", str(executable)], capture_output=True, env=env, timeout=15)
        need(check.returncode == 0 and b"not found" not in check.stdout + check.stderr, "TOOL_DEPENDENCY")
    version = subprocess.run([str(binary), "-?"], capture_output=True, env=env, timeout=15)
    need(version.returncode == 0 and b"18.6" in version.stdout + version.stderr, "TOOL_VERSION")
    return binary, env


def parse_row(output, cfg):
    rows = [line.strip().split("|") for line in output.decode("utf-8", "strict").splitlines() if line.strip().startswith("FV_SQL|")]
    need(len(rows) == 1 and len(rows[0]) == 22, "ROW_SHAPE")
    v = [x.strip() for x in rows[0]]
    need(v[1] in {TARGET_SERVER, "sql-fieldvisit-jpe-pathb-uat"} and v[2] == TARGET_DATABASE and v[3] == "5", "EXACT_TARGET")
    try:
        sid = bytes.fromhex(v[4]); import uuid
        candidates = (str(uuid.UUID(bytes=sid)), str(uuid.UUID(bytes_le=sid)))
    except Exception as exc:
        raise Hold("SQL_IDENTITY") from exc
    need(len(sid) == 16 and any(sha(x.encode()) == cfg["owner_oid_sha256"] for x in candidates), "SQL_IDENTITY")
    need(v[5:8] == ["1", "1", "0"], "METADATA")
    need(v[8].upper() == "TRUE" and v[10].upper() == "TCP", "TLS")
    need(all(re.fullmatch(r"0|[1-9][0-9]*", x) for x in v[11:]) and all(x == "0" for x in v[11:]), "NOT_EMPTY")
    return "DATABASE_EMPTY_PASS"


def read_token_from_non_echo_pty():
    need(os.isatty(0), "PTY_REQUIRED")
    need(not (termios.tcgetattr(0)[3] & termios.ECHO), "ECHO_ON")
    chunks=[]; total=0
    while True:
        chunk=os.read(0, min(4096, 16386-total))
        need(bool(chunk), "TOKEN_FRAME")
        chunks.append(chunk); total += len(chunk)
        need(total <= 16385, "TOKEN_FRAME")
        if b"\n" in chunk: break
    raw=b"".join(chunks)
    need(1 < len(raw) <= 16385 and raw.endswith(b"\n") and raw.count(b"\n") == 1, "TOKEN_FRAME")
    try:
        token = raw[:-1].decode("ascii")
    except UnicodeDecodeError as exc:
        raise Hold("TOKEN_FRAME") from exc
    need(re.fullmatch(r"[A-Za-z0-9._-]+", token) is not None, "TOKEN_FRAME")
    return token


def main():
    parser = argparse.ArgumentParser(); parser.add_argument("--kudu-pty", action="store_true")
    args = parser.parse_args()
    if not args.kudu_pty:
        print("FV_SELECT_ONLY=HOLD_AUTH_LOCK", flush=True); return 2
    stage = packages = ram = None; sql_started = False
    try:
        cfg = load_config(); validate_dns(cfg)
        stage = Path(tempfile.mkdtemp(prefix="work-tools.", dir=ROOT)); os.chmod(stage, 0o700)
        packages = Path(tempfile.mkdtemp(prefix="work-debs.", dir=ROOT)); os.chmod(packages, 0o700)
        fetch_packages(cfg, packages); binary, env = stage_tools(cfg, packages, stage)
        need(not RAM_ROOT.exists() and not RAM_ROOT.is_symlink(), "RAM_COLLISION")
        RAM_ROOT.mkdir(mode=0o700)
        fd=os.open(RAM_ROOT/"receipt", os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
        with os.fdopen(fd,"wb") as stream: stream.write(RAM_RECEIPT)
        print("FV_P3_TOKENS_READY", flush=True)
        token = read_token_from_non_echo_pty(); validate_token(token, cfg)
        ram = RAM_ROOT
        token_path = ram / "token"; fd = os.open(token_path, os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
        with os.fdopen(fd, "wb") as stream: stream.write(token.encode("utf-16le"))
        token = ""
        def one_select(sql):
            nonlocal sql_started
            need(not sql_started, "REPLAY"); sql_started = True
            cmd = [str(binary), "-S", TARGET_SERVER, "-d", TARGET_DATABASE, "-G", "-P", str(token_path), "-I", "-b", "-l", str(cfg["login_timeout_seconds"]), "-Nm", "-h", "-1", "-W", "-w", "65535", "-s", "|", "-Q", sql.decode("utf-8")]
            result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, timeout=cfg["query_timeout_seconds"], start_new_session=True)
            need(result.returncode == 0, "SQL_CALL_HOLD")
            return result.stdout
        outcome = select_only(QUERY.read_bytes(), verified_readonly_context=True, one_select=one_select, verify_row=lambda raw: parse_row(raw, cfg))
        need(outcome == "PASS_SELECT_ONLY", "SELECT_HOLD")
        print("FV_P3_FINAL_RESULT=PASS_SELECT_ONLY", flush=True); return 0
    except Exception:
        print("FV_P3_FINAL_RESULT=HOLD_SELECT_ONLY", flush=True); return 1
    finally:
        for path in (ram, stage, packages):
            if path is not None: shutil.rmtree(path, ignore_errors=True)
        print("FV_SELECT_ONLY_SQL_CALL=" + ("1_OR_UNKNOWN" if sql_started else "0"), flush=True)
        print("FV_SELECT_ONLY_CLEANUP=" + ("PASS" if all(p is None or not p.exists() for p in (ram, stage, packages)) else "HOLD"), flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
