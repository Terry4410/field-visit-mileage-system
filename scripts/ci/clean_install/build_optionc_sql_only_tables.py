#!/usr/bin/env python3
"""Isolate four SQL-only A+C proposal CREATE TABLE contracts for disposable SQL2022.

Input candidates are explicitly NONEXECUTABLE DESIGN; this extracts only
individually source-pinned table DDL. It is NOT approved UAT migration SQL.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

from build_sql_only_tables import RC_SHA, extract_table, sha256, SourceContractError

CANDIDATES = {
    "docs/release/optionc-canonical-sql-draft/candidate/1800_014/Up.sql": (
        "LocationPublicationStages", "LocationPublishedVersions"),
    "docs/release/optionc-canonical-sql-draft/candidate/1800_017/Up.sql": (
        "LocationCreateReservations",),
    "docs/release/optionc-canonical-sql-draft/candidate/1800_019/Up.sql": (
        "LocationMergeDetails",),
}
OUTPUT_SQL = "optionc-sql-only-candidates.sql"
OUTPUT_JSON = "optionc-sql-only-candidates-manifest.json"


def build(root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise SourceContractError("OUTPUT_DIRECTORY_MISSING")
    entries = []
    sql = ["-- NONDEPLOY ONLY: A+C candidate CREATE TABLE shapes, NOT approved promotion.\n"]
    for path, names in CANDIDATES.items():
        raw = (root / path).read_bytes()
        content = raw.decode("utf-8-sig")
        if "NONEXECUTABLE DESIGN" not in content.upper() and "DISPOSABLE" not in content.upper():
            # Candidate context must remain explicit; no silent promotion.
            raise SourceContractError("CANDIDATE_STATUS_MISSING")
        for name in names:
            ddl = extract_table(content, name)
            entries.append({
                "objectType": "TABLE",
                "name": "dbo." + name,
                "sourcePath": path,
                "sourceSha256": sha256(raw),
                "ddlSha256": sha256((ddl + "\n").encode("utf-8")),
                "scope": "DISPOSABLE_CANDIDATE_NOT_APPROVED_FINAL_SCHEMA",
            })
            sql.append("-- SOURCE: " + path + "\n" + ddl + "\nGO\n")
    if len(entries) != 4 or len({x["name"] for x in entries}) != 4:
        raise SourceContractError("CANDIDATE_TABLE_SCOPE")
    data = "\n".join(sql).encode("utf-8")
    (output / OUTPUT_SQL).write_bytes(data)
    manifest = {
        "format": "field-visit-v180-optionc-sql-only-table-probe-v1",
        "rcSourceSha": RC_SHA,
        "disposableCandidateTableCount": 4,
        "objects": entries,
        "sqlSha256": sha256(data),
        "fullCleanInstall": "HOLD_NON_TABLE_OBJECTS_REFERENCE_SEED_RBAC_APP_COMPAT",
    }
    (output / OUTPUT_JSON).write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    a = parser.parse_args()
    try:
        actual = subprocess.check_output(
            ["git", "-C", str(a.rc_root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL).strip()
        if actual != RC_SHA:
            raise SourceContractError("SOURCE_RC_MISMATCH")
        result = build(a.rc_root, a.output)
        if sha256((a.output / OUTPUT_SQL).read_bytes()) != result["sqlSha256"]:
            raise SourceContractError("CANDIDATE_OUTPUT_MISMATCH")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("OPTIONC_SQL_ONLY_TABLES=FAIL_CLOSED")
        return 1
    print("OPTIONC_SQL_ONLY_TABLE_SOURCE=PASS")
    print("OPTIONC_SQL_ONLY_TABLES=4")
    print("FULL_CLEAN_INSTALL=HOLD_OPTIONC_FINAL_OBJECTS_AND_APP_COMPATIBILITY")
    return 0


if __name__ == "__main__":
    sys.exit(main())
