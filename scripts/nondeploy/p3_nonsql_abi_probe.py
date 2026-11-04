#!/usr/bin/env python3
"""Offline-only original P3 tool ABI gate. NO SSH/Token/network/SQL/DDL/marker.

This is a separate NONDEPLOY candidate; it is not a P3 installation entrypoint.
Inputs: an extracted original P3 executor and five SHA-pinned staged .deb files.
"""
import argparse
import hashlib
import json
import os
import platform
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

CFG_SHA = "c6e6e76906216e92e7573f3e4d00f37984ec7921d78389ee2268c37f22364472"
BINDING_SHA = "6d31c5699de177d09dd02c2f6fefb28937adf3a574bbcf2b4940979783eac233"
RELEASE = "d6c220877b9976b6478d85b5171d4e06293595ae"
EXPECTED_NAMES = ("msodbcsql18", "mssql-tools18", "libltdl7", "libodbc2", "libodbcinst2")
REQUIRED_COMMANDS = ("dpkg-deb", "readelf")

class Hold(Exception):
    pass

def sha_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            b = f.read(1024 * 1024)
            if not b:
                break
            h.update(b)
    return h.hexdigest()

def require(condition):
    if not condition:
        raise Hold()

def regular(path):
    require(path.is_file() and not path.is_symlink() and path.stat().st_nlink == 1)

def read_json(path):
    regular(path)
    return json.loads(path.read_text(encoding="utf-8"))

def fixed_bundle(bundle):
    require(bundle.is_dir() and not bundle.is_symlink())
    raw = bundle / "executor_config.json"
    regular(raw)
    require(sha_file(raw) == CFG_SHA)
    cfg = read_json(raw)
    require(cfg["approved_release"] == RELEASE and cfg["max_sql_calls"] == 19)
    binding_file = bundle / "binding-index.json"
    regular(binding_file)
    require(sha_file(binding_file) == BINDING_SHA)
    binding = read_json(binding_file)
    require(binding["contract"] == "FIELDVISIT_PATHB_P3_FINAL_BINDING_V1")
    require(binding["approved_release"] == RELEASE)
    require(len(binding["files"]) == 13)
    require(binding["files"]["executor_config.json"] == CFG_SHA)
    for name, expected in binding["files"].items():
        require(re.fullmatch(r"[A-Za-z0-9._-]+", name) is not None)
        item = bundle / name
        regular(item)
        require(sha_file(item) == expected)
    names = tuple(x["name"] for x in cfg["tools"])
    require(names == EXPECTED_NAMES and len(set(names)) == 5)
    require(len(cfg["tool_files"]) == 5)
    return cfg

def machine_gate():
    require(platform.machine().lower() in ("x86_64", "amd64"))
    raw = Path("/etc/os-release").read_text(encoding="utf-8")
    values = dict(re.findall(r'^([A-Z_]+)="?([^"\n]+)"?$', raw, re.M))
    require(values.get("ID") == "debian" and values.get("VERSION_ID") == "12")
    for name in REQUIRED_COMMANDS:
        require(shutil.which(name) is not None)

def safe_extract_toolset(cfg, debs, root):
    require(debs.is_dir() and not debs.is_symlink())
    for entry in cfg["tools"]:
        p = debs / (entry["name"] + ".deb")
        regular(p)
        require(sha_file(p) == entry["sha256"])
    # All five package hashes are checked before extracting any package.
    for entry in cfg["tools"]:
        p = debs / (entry["name"] + ".deb")
        cp = subprocess.run(
            ["dpkg-deb", "-x", str(p), str(root)],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            timeout=30, check=False)
        require(cp.returncode == 0)
    base = root.resolve()
    for item in root.rglob("*"):
        require(item.resolve().is_relative_to(base))
    for rel, expected in cfg["tool_files"].items():
        require(not rel.startswith("/") and ".." not in Path(rel).parts)
        p = root / rel
        require(p.is_file() and p.resolve().is_relative_to(base))
        require(sha_file(p) == expected)

def elf_gate(root, cfg):
    needed = []
    for rel in cfg["tool_files"]:
        p = root / rel
        h = subprocess.run(["readelf", "-h", str(p)],
                           capture_output=True, timeout=10, check=False)
        require(h.returncode == 0 and b"ELF64" in h.stdout and b"X86-64" in h.stdout)
        d = subprocess.run(["readelf", "-d", str(p)],
                           capture_output=True, timeout=10, check=False)
        require(d.returncode == 0)
        needed.extend(re.findall(rb"\(NEEDED\).*?\[([^\]]+)\]", d.stdout))
    require(bool(needed))
    # Static ELF compatibility only. No binary, dynamic loader or TLS execution.

def probe(bundle, debs):
    cfg = fixed_bundle(bundle)
    machine_gate()
    with tempfile.TemporaryDirectory(prefix="fv-p3-nonsql-abi-", dir="/tmp") as dirname:
        work = Path(dirname)
        require(work.is_dir() and not work.is_symlink())
        os.chmod(work, 0o700)
        root = work / "tools"
        root.mkdir(mode=0o700)
        safe_extract_toolset(cfg, debs, root)
        elf_gate(root, cfg)
    return True

def main(argv=None):
    p = argparse.ArgumentParser(description="NONDEPLOY OFFLINE P3 ABI PROBE; NO SQL")
    p.add_argument("--bundle", required=True, type=Path)
    p.add_argument("--debs", required=True, type=Path)
    a = p.parse_args(argv)
    try:
        probe(a.bundle, a.debs)
        print("P3_NONSQL_TOOL_ABI=PASS_STATIC_ONLY")
        print("REMOTE_SSH_SQL_AND_TOKENS=NOT_RUN")
        return 0
    except Exception:
        print("P3_NONSQL_TOOL_ABI=HOLD")
        print("REMOTE_SSH_SQL_AND_TOKENS=NOT_RUN")
        return 1

if __name__ == "__main__":
    raise SystemExit(main())
