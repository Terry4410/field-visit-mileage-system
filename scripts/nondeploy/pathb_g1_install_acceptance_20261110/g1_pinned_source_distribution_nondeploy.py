#!/usr/bin/env python3
"""Build and validate the EXISTING pinned G1 source-only distribution, OFFLINE.

No Azure operations, SQL connections, tokens, credentials, DDL, or deployment.
Never bundles the original Azure Blob SQL archive or seal: those are unavailable
under this NONDEPLOY authorization and must NOT be substituted with test data.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from zipfile import ZIP_STORED, ZipFile, ZipInfo

APPROVED_RELEASE = "d6c220877b9976b6478d85b5171d4e06293595ae"
EXPECTED_CONTRACT = "FIELDVISIT_PATHB_G1_ORIGINAL_BINDING_V1"
INSTALLER_SHA = "b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f"
PACKAGE_SHA = "5a0c956d4ef5c88e0ff49d2d606ee9c1cb34e81e7f0c0f85b5aea8c5743e5828"
SEAL_SHA = "a37dc3c7fbf1f805639fb72a76cc788bd9bec1f0f9933e079e2eacae5c4a5f"
EXPECTED_TARGET = ("api-fieldvisit-pathb-uat",
                   "sql-fieldvisit-jpe-pathb-uat.database.windows.net",
                   "db-fieldvisit-pathb-uat")
BOUND_FILE_LIMIT = 4 * 1024 * 1024
NAME_PATTERN = re.compile(r"[A-Za-z0-9_.-]{1,120}\Z")
SHA_PATTERN = re.compile(r"[a-f0-9]{64}\Z")


class OfflineHold(ValueError):
    """Sanitized fail-closed source verification error."""


def _require(ok: bool, code: str) -> None:
    if not ok:
        raise OfflineHold(code)


def _digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _read(root: Path, name: str) -> bytes:
    _require(bool(NAME_PATTERN.fullmatch(name)), "MEMBER_NAME_HOLD")
    path = root / name
    _require(path.is_file() and not path.is_symlink(), "MEMBER_TYPE_HOLD")
    _require(path.stat().st_size <= BOUND_FILE_LIMIT, "MEMBER_SIZE_HOLD")
    return path.read_bytes()


def verified_source_members(root: Path) -> tuple[dict[str, bytes], str]:
    """Verify each pinned byte; do not read Azure SQL package or credentials."""
    _require(root.is_dir() and not root.is_symlink(), "SOURCE_ROOT_HOLD")
    binding_bytes = _read(root, "binding-index.json")
    try:
        binding = json.loads(binding_bytes)
        files = binding["files"]
        _require(binding["contract"] == EXPECTED_CONTRACT, "CONTRACT_HOLD")
        _require(binding["approved_release"] == APPROVED_RELEASE, "RELEASE_HOLD")
        _require(isinstance(files, dict) and len(files) >= 20, "MEMBER_SET_HOLD")
        _require(set(files).isdisjoint({"original.zip", "original.seal.json"}),
                 "ORIGINAL_SQL_BUNDLE_FORBIDDEN")
        _require(files["frozen_install.py.snapshot"] == INSTALLER_SHA,
                 "INSTALLER_PIN_HOLD")
        cfg_bytes = _read(root, "executor_config.json")
        cfg = json.loads(cfg_bytes)
        _require((cfg["app"], cfg["server"], cfg["database"]) == EXPECTED_TARGET,
                 "TARGET_HOLD")
        _require(cfg["approved_release"] == APPROVED_RELEASE,
                 "CONFIG_RELEASE_HOLD")
        _require(cfg["installer_sha256"] == INSTALLER_SHA,
                 "CONFIG_INSTALLER_HOLD")
        _require(cfg["package_sha256"] == PACKAGE_SHA and
                 cfg["seal_sha256"] == SEAL_SHA, "ORIGINAL_BLOB_PIN_HOLD")
        _require(cfg["max_sql_calls"] == 19, "SQL_CEILING_HOLD")
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise OfflineHold("PINNED_METADATA_HOLD") from exc

    result = {"binding-index.json": binding_bytes}
    for name, expected in sorted(files.items()):
        _require(isinstance(name, str) and bool(NAME_PATTERN.fullmatch(name)),
                 "MEMBER_NAME_HOLD")
        _require(isinstance(expected, str) and
                 bool(SHA_PATTERN.fullmatch(expected)), "MEMBER_SHA_HOLD")
        data = _read(root, name)
        _require(_digest(data) == expected, "MEMBER_HASH_HOLD")
        result[name] = data
    _require("original.zip" not in result and "original.seal.json" not in result,
             "ORIGINAL_SQL_BUNDLE_FORBIDDEN")
    return result, _digest(binding_bytes)


def build_source_only(root: Path, output: Path) -> tuple[str, str, int]:
    """Create exactly one deterministic ZIP; fail rather than overwrite."""
    members, binding_sha = verified_source_members(root)
    _require(not output.exists() and not output.is_symlink(),
             "OUTPUT_ALREADY_EXISTS")
    _require(output.parent.is_dir(), "OUTPUT_PARENT_HOLD")
    created_by_this_call = False
    try:
        with ZipFile(output, mode="x", compression=ZIP_STORED) as archive:
            created_by_this_call = True
            for name, data in sorted(members.items()):
                info = ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = ZIP_STORED
                info.create_system = 3
                info.external_attr = 0o100644 << 16
                archive.writestr(info, data)
        with ZipFile(output) as archive:
            infos = archive.infolist()
            _require(len(infos) == len(members) and
                     [i.filename for i in infos] == sorted(members),
                     "OUTPUT_MEMBER_SET_HOLD")
            for info in infos:
                _require(archive.read(info) == members[info.filename],
                         "OUTPUT_CONTENT_HOLD")
        return _digest(output.read_bytes()), binding_sha, len(members)
    except Exception:
        # Only remove the file this invocation exclusively created.
        if created_by_this_call and output.is_file() and not output.is_symlink():
            output.unlink()
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        root = Path(__file__).resolve().parent
        sha, binding, count = build_source_only(root, args.output)
    except (OfflineHold, OSError, ValueError):
        print("G1_SOURCE_DELIVERY=HOLD")
        return 1
    print("G1_SOURCE_DELIVERY=OFFLINE_PASS")
    print("G1_SOURCE_ZIP_SHA256=" + sha)
    print("G1_BINDING_INDEX_SHA256=" + binding)
    print("G1_SOURCE_MEMBER_COUNT=" + str(count))
    print("ORIGINAL_14_LAYER_SQL_BLOB=NOT_INCLUDED")
    print("LIVE_SQL_DDL_TOKEN_BLOB_AZURE_DEPLOY=NOT_RUN")
    print("LIVE_G1_AUTHORIZATION=REQUIRED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
