#!/usr/bin/env python3
"""Path B fixed-SELECT22 *App-side* one-shot candidate (NONDEPLOY).

No live CLI, Azure management, SSH, downloader, Blob, installer or SQL on import.
Only an independently reviewed, explicit caller may call run_once after it has
authenticated and pinned the private App channel, identity and target instance.

Reuses exact bound G1 tooling + SQL bytes, existing durable one-use claim,
existing SQL-client process-group guard and redacting receipt parser.
NOT approved for live execution merely because tests pass.
"""
from __future__ import annotations

import os
from pathlib import Path
import stat
import subprocess
import tempfile
import time

from pathb_connection_only_tool_stage_nondeploy import (
    load_bound_config, verify_packages, inspect_extracted_paths,
    TOOL_FILE_NAMES, sha256_file, require as require_offline, OfflineHold,
)
from pathb_readonly_durable_once_20261113 import consume_once
from pathb_readonly_isolated_gate import verify as verify_exact_query
from pathb_readonly_receipt_parser import parse_preflight, ReceiptHold
from pathb_readonly_sql_contract import sqlcmd_argv
from p3_ssh_transport_contract import validate_owner_token
# Importing the frozen G1 child has no live side effects. ONLY its bounded
# sql_call process-group guard is reused; never call its run/main/installer.
from p3_app_installer_runner import sql_call


class Select22Hold(Exception):
    pass


def _require(condition, label):
    if not condition:
        raise Select22Hold(label)


def _check_private_claim_dir(folder):
    path = Path(folder)
    try:
        info = path.lstat()
        _require(stat.S_ISDIR(info.st_mode), "PRIVATE_DIR_HOLD")
        _require(info.st_uid == os.geteuid()
                 and stat.S_IMODE(info.st_mode) == 0o700,
                 "PRIVATE_DIR_HOLD")
        # Prevent externally chosen or old work roots. Caller creates a fresh
        # 0700 owner-only directory under /dev/shm for this exact attempt.
        _require(path.resolve().parent == Path("/dev/shm"), "PRIVATE_DIR_HOLD")
        _require(path.name.startswith("fv-pathb-select22-"), "PRIVATE_DIR_HOLD")
    except OSError:
        raise Select22Hold("PRIVATE_DIR_HOLD") from None
    return path


def _stage_verified_tools(cfg, debs_dir, sandbox):
    """Verify all five hashes before unpacking and all outputs after unpacking."""
    packages = verify_packages(Path(debs_dir), cfg)
    tools = sandbox / "tools"
    tools.mkdir(mode=0o700)
    for package in packages:
        subprocess.run(
            ["dpkg-deb", "-x", str(package), str(tools)],
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL, check=True, timeout=30,
        )
        inspect_extracted_paths(tools)
    for rel, want in cfg["tool_files"].items():
        target = tools / rel
        _require(target.is_file() and not target.is_symlink(), "TOOL_BINARY_HOLD")
        _require(sha256_file(target) == want, "TOOL_BINARY_HASH_HOLD")
    odbc = sandbox / "odbc"
    odbc.mkdir(mode=0o700)
    driver = tools / TOOL_FILE_NAMES[0]
    (odbc / "odbcinst.ini").write_text(
        "[ODBC Driver 18 for SQL Server]\nDriver=" + str(driver) +
        "\nUsageCount=1\n", encoding="ascii",
    )
    (odbc / "odbc.ini").write_text("", encoding="ascii")
    env = {
        "PATH": "/usr/bin:/bin",
        "HOME": str(sandbox),
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
        "ODBCSYSINI": str(odbc),
        "ODBCINI": str(odbc / "odbc.ini"),
        "ODBCINSTINI": "odbcinst.ini",
        "LD_LIBRARY_PATH": ":".join((
            str(tools / "opt/microsoft/msodbcsql18/lib64"),
            str(tools / "usr/lib/x86_64-linux-gnu"),
        )),
    }
    return tools / TOOL_FILE_NAMES[1], env


def run_once(*, debs_dir, claim_dir, sql_token, invoke_sql=None):
    """At most one fixed SQL execution using one already-authorized token.

    invoke_sql is an offline test seam; production defaults to G1 sql_call.
    Does not acquire a token or initiate SSH/Azure. Claim is never deleted.
    Raw SQL result and secret never returned, printed or logged.
    """
    _require(isinstance(sql_token, str), "TOKEN_SHAPE_HOLD")
    cfg = load_bound_config()
    query_gates = verify_exact_query()
    _require(query_gates["PINNED_SQL"] == "PASS", "SQL_BINDING_HOLD")
    root = Path(__file__).resolve().parent
    sql = (root / "preflight.sql").read_bytes()
    _require(sha256_file(root / "preflight.sql") == cfg["preflight_sha256"],
             "SQL_HASH_HOLD")
    folder = _check_private_claim_dir(claim_dir)
    # Verify original Entra Owner, tenant, SQL audience and token TTL.
    # Never echo decoded token or its claims.
    validate_owner_token(sql_token, "sql", time.time())
    packages = verify_packages(Path(debs_dir), cfg)
    _require(len(packages) == 5, "TOOLS_NOT_READY_HOLD")
    # All preconditions above are fail-closed and do NOT consume the attempt.
    try:
        with tempfile.TemporaryDirectory(prefix="fv-pathb-select22-tools-") as tmp:
            sandbox = Path(tmp)
            os.chmod(sandbox, 0o700)
            binary, env = _stage_verified_tools(cfg, debs_dir, sandbox)
            token_path = folder / "sql-token"
            _require(not os.path.lexists(token_path), "TOKEN_PATH_COLLISION")
            # The pinned path/query validator is mandatory before claim.
            sqlcmd_argv(binary, token_path, sql)
            # Durable irrevocable attempt claim comes BEFORE token file/SQL.
            consume_once(folder)
            try:
                fd = os.open(
                    token_path,
                    os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                    getattr(os, "O_NOFOLLOW", 0),
                    0o600,
                )
                with os.fdopen(fd, "wb") as handle:
                    handle.write(sql_token.encode("utf-16le"))
                    handle.flush()
                    os.fsync(handle.fileno())
                sql_token = None
                # The original G1 sql_call uses a managed subprocess group,
                # bounded communication and *no* retry. It never runs Installer.
                caller = invoke_sql if invoke_sql is not None else sql_call
                raw = caller(binary, env, token_path, sql,
                             time.monotonic() + 110, 1)
                _require(isinstance(raw, bytes) and len(raw) <= 8192,
                         "SQL_RESULT_SHAPE_HOLD")
                return parse_preflight(raw)
            finally:
                # Do not delete claim. Remove only our own known token file.
                if os.path.lexists(token_path):
                    try:
                        info = token_path.lstat()
                        if (stat.S_ISREG(info.st_mode) and
                                info.st_uid == os.geteuid() and
                                stat.S_IMODE(info.st_mode) == 0o600):
                            token_path.unlink()
                    except OSError:
                        raise Select22Hold("TOKEN_CLEANUP_HOLD") from None
                sql_token = None
    except (OfflineHold, Select22Hold, ReceiptHold):
        raise Select22Hold("SELECT22_ONCE_HOLD") from None
    except Exception:
        # No exception detail, SQL row, JWT, identity or subprocess stderr.
        raise Select22Hold("SELECT22_ONCE_HOLD") from None


if __name__ == "__main__":
    raise SystemExit("NONDEPLOY_NO_LIVE_ENTRYPOINT")
