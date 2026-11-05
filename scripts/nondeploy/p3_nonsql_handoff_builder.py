#!/usr/bin/env python3
"""NONDEPLOY: build one integrity-bound, non-SQL P3 handoff archive.

This tool never opens SSH, calls Azure, reads credentials, executes packages,
changes SQL or modifies the original P3 installer. The output is NOT a
live-transport authorization or an executable Azure deployment.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
from p3_nonsql_abi_probe import CFG_SHA, BINDING_SHA, RELEASE, EXPECTED_NAMES
import stat
import zipfile

HANDLER_BLOB = "9a8227d79e320e46233452247417f657414787a6"
PROBE_BLOB = "df52dde16ae13bec46682437e6cdf56e286af6bf"
MAX_STAGE_BYTES = 128 * 1024 * 1024
MAX_HANDOFF_BYTES = 130 * 1024 * 1024
SCRIPTS = {
    "p3_nonsql_stage_handler.py": HANDLER_BLOB,
    "p3_nonsql_abi_probe.py": PROBE_BLOB,
}
DATA_MEMBERS = frozenset({
    "executor_config.json", "binding-index.json",
    "p3_cloudshell_parent.py", "p3_app_installer_runner.py",
    "p3_blob_guard.py", "p3_install_gates.py",
    "p3_ssh_stage.py", "p3_ssh_transport_contract.py",
    "seal_verifier.py", "token_adapter.py", "frozen_install.py.snapshot",
    "preflight.sql", "postcheck.sql", "schema-fingerprint.sql",
    "debs/msodbcsql18.deb", "debs/mssql-tools18.deb",
    "debs/libltdl7.deb", "debs/libodbc2.deb",
    "debs/libodbcinst2.deb",
})

class Hold(Exception):
    pass

def demand(ok):
    if not ok:
        raise Hold()

def regular_file(path):
    demand(path.is_file() and not path.is_symlink() and path.stat().st_nlink == 1)

def git_blob_sha1(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + bytes([0]) + raw).hexdigest()

def inspect_stage(raw):
    demand(len(raw) <= MAX_STAGE_BYTES)
    with zipfile.ZipFile(io.BytesIO(raw), "r") as z:
        members = z.infolist()
        demand(len(members) == 19 and {x.filename for x in members} == DATA_MEMBERS)
        demand(len({x.filename for x in members}) == len(members))
        total = 0
        for entry in members:
            demand(entry.file_size <= 40 * 1024 * 1024)
            demand(entry.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED))
            demand(not (entry.flag_bits & 1))
            mode = entry.external_attr >> 16
            demand(not stat.S_ISLNK(mode) and not stat.S_ISDIR(mode))
            total += entry.file_size
        demand(total <= 160 * 1024 * 1024)
        # Revalidate the sealed source and the exact five package digests,
        # rather than trusting only the untrusted stage ZIP's layout.
        records = {x.filename: z.read(x) for x in members}
        demand(hashlib.sha256(records["executor_config.json"]).hexdigest() == CFG_SHA)
        demand(hashlib.sha256(records["binding-index.json"]).hexdigest() == BINDING_SHA)
        cfg = json.loads(records["executor_config.json"])
        binding = json.loads(records["binding-index.json"])
        demand(cfg["approved_release"] == RELEASE and cfg["max_sql_calls"] == 19)
        demand(binding["contract"] == "FIELDVISIT_PATHB_P3_FINAL_BINDING_V1")
        demand(binding["approved_release"] == RELEASE)
        demand(len(binding["files"]) == 13)
        demand(set(binding["files"]) == DATA_MEMBERS - {"binding-index.json"} - {
            "debs/" + name + ".deb" for name in EXPECTED_NAMES})
        for name, expected in binding["files"].items():
            demand(hashlib.sha256(records[name]).hexdigest() == expected)
        demand(tuple(x["name"] for x in cfg["tools"]) == EXPECTED_NAMES)
        for tool in cfg["tools"]:
            demand(hashlib.sha256(records["debs/" + tool["name"] + ".deb"]).hexdigest()
                   == tool["sha256"])

def build(source_dir, stage):
    regular_file(stage)
    stage_bytes = stage.read_bytes()
    inspect_stage(stage_bytes)
    files = {}
    for filename, pinned_blob in SCRIPTS.items():
        f = source_dir / filename
        regular_file(f)
        raw = f.read_bytes()
        demand(len(raw) < 32 * 1024)
        demand(git_blob_sha1(raw) == pinned_blob)
        files[filename] = raw
    files["stage.zip"] = stage_bytes
    manifest = {
        "contract": "FIELDVISIT_P3_NONSQL_HANDOFF_V1",
        "live_execution_authorized": False,
        "stage_sha256": hashlib.sha256(stage_bytes).hexdigest(),
        "scripts": {
            name: {"git_blob_sha1": SCRIPTS[name],
                   "sha256": hashlib.sha256(files[name]).hexdigest()}
            for name in sorted(SCRIPTS)
        },
        "remote_entrypoint": "p3_nonsql_stage_handler.py",
        "remote_sql_token_ddl": "FORBIDDEN"
    }
    files["manifest.json"] = (json.dumps(
        manifest, sort_keys=True, separators=(",", ":")
    ) + "\n").encode()
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_STORED) as z:
        for filename in sorted(files):
            info = zipfile.ZipInfo(filename)
            info.external_attr = (0o100600 << 16)
            z.writestr(info, files[filename])
    raw_out = out.getvalue()
    demand(len(raw_out) <= MAX_HANDOFF_BYTES)
    return raw_out

def main(argv=None):
    p = argparse.ArgumentParser(description="NONDEPLOY source-pinned handoff builder")
    p.add_argument("--source-dir", required=True, type=Path)
    p.add_argument("--stage", required=True, type=Path)
    p.add_argument("--output", required=True, type=Path)
    a = p.parse_args(argv)
    try:
        demand(a.source_dir.is_dir() and not a.source_dir.is_symlink())
        demand(a.output.parent.is_dir())
        demand(not a.output.exists() and not a.output.is_symlink())
        data = build(a.source_dir, a.stage)
        fd = os.open(a.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, "wb") as f:
            f.write(data)
        print("P3_NONSQL_HANDOFF=PASS_OFFLINE_ONLY")
        print("HANDOFF_SHA256=" + hashlib.sha256(data).hexdigest())
        print("AZURE_SSH_TOKEN_SQL_MARKER_DDL=NOT_RUN")
        return 0
    except Exception:
        print("P3_NONSQL_HANDOFF=HOLD")
        print("AZURE_SSH_TOKEN_SQL_MARKER_DDL=NOT_RUN")
        return 1

if __name__ == "__main__":
    raise SystemExit(main())
