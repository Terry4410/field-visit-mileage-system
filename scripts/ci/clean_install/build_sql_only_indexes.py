#!/usr/bin/env python3
"""Extract 13 migration-only indexes on newly installed SQL-only tables.

Only disposable SQL2022. This neither promotes design sources nor certifies
the other final indexes/constraints from all migrations.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from build_sql_only_tables import RC_SHA, sha256, SourceContractError

INDEX_SOURCES = {
    "database/migrations/1800_006_notification_framework/Up.sql": {
        "UX_NotificationEmailAllowlist_Environment_Email": "NotificationEmailAllowlist",
        "UX_MailOutbox_BusinessEvent_RecipientKey": "MailOutbox",
        "UX_MailOutbox_BusinessEvent_NormalizedRecipientEmail": "MailOutbox",
        "IX_MailOutbox_Dispatch": "MailOutbox",
        "IX_MailOutbox_Aggregate": "MailOutbox",
        "IX_MailOutbox_Correlation": "MailOutbox",
        "IX_MailDeliveryLogs_Status_Attempted": "MailDeliveryLogs",
    },
    "docs/release/optionc-canonical-sql-draft/candidate/1800_014/Up.sql": {
        "UX_OptionC_Stages_Request": "LocationPublicationStages",
        "UX_OptionC_Stages_OpenLocation": "LocationPublicationStages",
        "UX_OptionC_Published_Current": "LocationPublishedVersions",
        "UX_OptionC_Published_Request": "LocationPublishedVersions",
        "UX_OptionC_Published_Number": "LocationPublishedVersions",
    },
    "docs/release/optionc-canonical-sql-draft/candidate/1800_017/Up.sql": {
        "UX_OptionC_CreateReservation_Location": "LocationCreateReservations",
    },
}
NAME_PATTERN = re.compile(
    r"\bCREATE\s+(?:UNIQUE\s+)?(?:NONCLUSTERED\s+)?INDEX\s+"
    r"(?P<name>[A-Za-z_][A-Za-z_0-9]*)\s+ON\s+"
    r"(?:dbo|\[dbo\])\s*\.\s*(?P<table>[A-Za-z_][A-Za-z_0-9]*)\b", re.I
)


def extract_index(source: str, name: str, table: str) -> str:
    matches = [m for m in NAME_PATTERN.finditer(source)
               if m.group("name").lower() == name.lower()]
    if len(matches) != 1 or matches[0].group("table").lower() != table.lower():
        raise SourceContractError("INDEX_SOURCE_SHAPE:" + name)
    start = matches[0].start()
    # Reviewed 006/014/017 CREATE INDEX syntax uses statement terminators;
    # fail closed if a comment, batch or next statement crosses the boundary.
    end = source.find(";", matches[0].end())
    if end < 0:
        raise SourceContractError("INDEX_TERMINATOR_MISSING:" + name)
    ddl = source[start:end + 1].strip()
    if re.search(r"\b(?:CREATE\s+TABLE|INSERT\s+dbo|BEGIN\s+TRAN|COMMIT\s+TRAN)\b", ddl, re.I):
        raise SourceContractError("INDEX_SCOPE_CROSSED:" + name)
    if ddl.count("(") != ddl.count(")"):
        raise SourceContractError("INDEX_PARENS:" + name)
    if re.search(r"(?im)^\s*GO\s*$", ddl):
        raise SourceContractError("INDEX_UNEXPECTED_BATCH:" + name)
    return ddl


def build(root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise SourceContractError("OUTPUT_DIRECTORY_MISSING")
    entries = []
    chunks = ["-- Disposable-only source derived SQL-only table indexes.\n"]
    for path, named in INDEX_SOURCES.items():
        raw = (root / path).read_bytes()
        content = raw.decode("utf-8-sig")
        for name, table in named.items():
            ddl = extract_index(content, name, table)
            entries.append({
                "name": name, "table": "dbo." + table,
                "sourcePath": path, "sourceSha256": sha256(raw),
                "ddlSha256": sha256((ddl + "\n").encode("utf-8")),
            })
            chunks.append("-- SOURCE: " + path + "\n" + ddl + "\nGO\n")
    if len(entries) != 13 or len({x["name"] for x in entries}) != 13:
        raise SourceContractError("INDEX_OBJECT_SET")
    data = "\n".join(chunks).encode("utf-8")
    (output / "sql-only-extra-indexes.sql").write_bytes(data)
    manifest = {
        "format": "field-visit-v180-sql-only-index-probe-v1",
        "rcSourceSha": RC_SHA,
        "indexCount": len(entries),
        "indexes": entries,
        "sqlSha256": sha256(data),
        "fullCleanInstall": "HOLD_UNRECONCILED_OTHER_INDEXES_CONSTRAINTS_AND_RUNTIME_COMPAT",
    }
    (output / "sql-only-extra-indexes-manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")
    return manifest


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--rc-root", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    try:
        sha = subprocess.check_output(
            ["git", "-C", str(a.rc_root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL).strip()
        if sha != RC_SHA:
            raise SourceContractError("SOURCE_RC_MISMATCH")
        manifest = build(a.rc_root, a.output)
        if sha256((a.output / "sql-only-extra-indexes.sql").read_bytes()) != manifest["sqlSha256"]:
            raise SourceContractError("OUTPUT_INDEX_HASH")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("SQL_ONLY_INDEX_SOURCE=FAIL_CLOSED")
        return 1
    print("SQL_ONLY_INDEX_SOURCE=PASS")
    print("SQL_ONLY_INDEXES=13")
    print("FULL_CLEAN_INSTALL=HOLD_OTHER_OBJECTS_AND_RUNTIME_COMPAT")
    return 0


if __name__ == "__main__":
    sys.exit(main())
