#!/usr/bin/env python3
"""NONDEPLOY: inspect and temporarily stage pinned G1 SQL client packages OFFLINE.

No network fetching, Azure, Token, SQL client execution, SQL statement or installer
call path exists in this module. Extracted files are removed before it returns.
This is NOT a connection-only live executor and supplies no live authorization.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import stat
import subprocess
import tempfile


HERE = Path(__file__).resolve().parent
BOUND_CONFIG_SHA256 = "6dd056335f5e6d0fc86ef107f35f66731defe493c16d972b842a3217de04b0cc"
TOOL_NAMES = ("msodbcsql18", "mssql-tools18", "libltdl7", "libodbc2", "libodbcinst2")
TOOL_FILE_NAMES = (
    "opt/microsoft/msodbcsql18/lib64/libmsodbcsql-18.6.so.1.1",
    "opt/mssql-tools18/bin/sqlcmd",
    "usr/lib/x86_64-linux-gnu/libltdl.so.7.3.2",
    "usr/lib/x86_64-linux-gnu/libodbc.so.2.0.0",
    "usr/lib/x86_64-linux-gnu/libodbcinst.so.2.0.0",
)


class OfflineHold(Exception):
    """Fixed, sanitized fail-closed outcome."""


def require(condition, reason):
    if not condition:
        raise OfflineHold(reason)


def sha256_file(path):
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def is_regular_without_links(path):
    try:
        return stat.S_ISREG(path.lstat().st_mode)
    except (FileNotFoundError, OSError):
        return False


def load_bound_config():
    config = HERE / "executor_config.json"
    binding = HERE / "binding-index.json"
    require(is_regular_without_links(config) and is_regular_without_links(binding),
            "HOLD_BOUND_FILES")
    require(sha256_file(config) == BOUND_CONFIG_SHA256, "HOLD_CONFIG_DRIFT")
    try:
        b = json.loads(binding.read_text(encoding="utf-8"))
        cfg = json.loads(config.read_text(encoding="utf-8"))
    except (ValueError, UnicodeError):
        raise OfflineHold("HOLD_MANIFEST_JSON") from None
    require(b["files"].get("executor_config.json") == BOUND_CONFIG_SHA256,
            "HOLD_BINDING_DRIFT")
    validate_config(cfg)
    return cfg


def validate_config(cfg):
    try:
        tools = cfg["tools"]
        hashes = cfg["tool_files"]
        require([item["name"] for item in tools] == list(TOOL_NAMES),
                "HOLD_TOOL_NAMES")
        require(set(hashes) == set(TOOL_FILE_NAMES), "HOLD_BINARY_LIST")
        vals = [item["sha256"] for item in tools] + list(hashes.values())
        require(all(isinstance(v, str) and len(v) == 64
                    and all(ch in "0123456789abcdef" for ch in v)
                    for v in vals), "HOLD_HASH_FORMAT")
    except (KeyError, TypeError, AttributeError):
        raise OfflineHold("HOLD_CONFIG_SHAPE") from None


def verify_packages(debs_dir, cfg):
    """All five hashes verified BEFORE the first dpkg-deb process."""
    require(debs_dir.is_dir() and not debs_dir.is_symlink(), "HOLD_DEBS_ABSENT")
    files = []
    for item in cfg["tools"]:
        p = debs_dir / (item["name"] + ".deb")
        require(is_regular_without_links(p), "HOLD_DEBS_ABSENT")
        require(sha256_file(p) == item["sha256"], "HOLD_DEB_HASH")
        files.append(p)
    return files


def inspect_extracted_paths(root):
    """Allow in-tree relative Debian links; reject absolute or escaping links."""
    base = root.resolve(strict=True)
    for path in root.rglob("*"):
        if path.is_symlink():
            raw = os.readlink(path)
            require(not os.path.isabs(raw), "HOLD_EXTRACTED_LINK")
            target = (path.parent / raw).resolve(strict=False)
            require(target.is_relative_to(base), "HOLD_EXTRACTED_LINK")
        else:
            mode = path.lstat().st_mode
            require(stat.S_ISREG(mode) or stat.S_ISDIR(mode),
                    "HOLD_EXTRACTED_SPECIAL")


def stage_offline(cfg, debs_dir, *, extractor=None):
    """Check packages; extract to a fresh owned temporary directory; self-clean.

    extractor is a test seam only; production uses the fixed dpkg-deb argv.
    No SQL client or installer is executed.
    """
    validate_config(cfg)
    packages = verify_packages(Path(debs_dir), cfg)
    if extractor is None:
        def extractor(package, destination):
            subprocess.run(
                ["dpkg-deb", "-x", str(package), str(destination)],
                stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL, check=True, timeout=30,
            )
    try:
        with tempfile.TemporaryDirectory(prefix="fv-pathb-tools-offline-") as tmp:
            root = Path(tmp)
            os.chmod(root, 0o700)
            tools = root / "tools"
            tools.mkdir(mode=0o700)
            for package in packages:
                extractor(package, tools)
                inspect_extracted_paths(tools)
            for rel, expected in cfg["tool_files"].items():
                posix = PurePosixPath(rel)
                require(not posix.is_absolute() and ".." not in posix.parts,
                        "HOLD_BINARY_PATH")
                target = tools.joinpath(*posix.parts)
                require(is_regular_without_links(target), "HOLD_BINARY_MISSING")
                require(sha256_file(target) == expected, "HOLD_BINARY_HASH")
            driver = tools / TOOL_FILE_NAMES[0]
            odbc = root / "odbc"
            odbc.mkdir(mode=0o700)
            (odbc / "odbcinst.ini").write_text(
                "[ODBC Driver 18 for SQL Server]\nDriver=" + str(driver) +
                "\nUsageCount=1\n", encoding="ascii")
            (odbc / "odbc.ini").write_text("", encoding="ascii")
            require((odbc / "odbcinst.ini").is_file(), "HOLD_ODBC_STAGE")
    except (subprocess.SubprocessError, OSError):
        raise OfflineHold("HOLD_EXTRACTION_OR_IO") from None
    return "PASS"


def main(argv=None):
    parser = argparse.ArgumentParser(description="Offline-only G1 client-stage verifier")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check-config", action="store_true")
    mode.add_argument("--offline-stage", action="store_true")
    parser.add_argument("--debs-dir", type=Path)
    args = parser.parse_args(argv)
    try:
        cfg = load_bound_config()
        print("CONFIG_BINDING=PASS")
        if args.check_config:
            require(args.debs_dir is None, "HOLD_USAGE")
            print("OFFLINE_ONLY=YES")
            print("SQL_CONNECTION=NO")
            return 0
        require(args.debs_dir is not None, "HOLD_USAGE")
        result = stage_offline(cfg, args.debs_dir)
        print("TOOL_STAGE=" + result)
        print("TEMP_CLEANUP=PASS")
        print("OFFLINE_ONLY=YES")
        print("SQL_CONNECTION=NO")
        return 0
    except OfflineHold as error:
        print("TOOL_STAGE=" + str(error))
        print("SQL_CONNECTION=NO")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
