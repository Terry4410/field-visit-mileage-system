#!/usr/bin/env python3
"""Source-pinned SQL-only *table* layer for the disposable v1.8.0 clean build.

This deliberately does NOT install migration triggers, A+C proposal objects,
security grants or seed/reference data. Full Clean Install remains HOLD.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

RC_SHA = "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a"
SOURCES = {
    "database/migrations/1800_001_organization_center_team_lifecycle/Up.sql": (
        "SchemaMigrationDataBaselines",
    ),
    "database/migrations/1800_006_notification_framework/Up.sql": (
        "NotificationEnvironmentPolicies",
        "NotificationEmailAllowlist",
        "NotificationSettings",
        "NotificationSettingRecipients",
        "MailOutbox",
        "MailDeliveryLogs",
    ),
}
OUTPUT_SQL = "sql-only-core-tables.sql"
OUTPUT_MANIFEST = "sql-only-core-tables-manifest.json"


class SourceContractError(ValueError):
    pass


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def extract_table(source: str, table: str) -> str:
    """Extract one DDL statement, balancing nested SQL parentheses and strings.

    Require a uniquely named dbo CREATE TABLE and a terminal semicolon. This
    only accepts reviewed source patterns, not an arbitrary T-SQL parser.
    """
    pattern = re.compile(
        r"\bCREATE\s+TABLE\s+(?:dbo|\[dbo\])\s*\.\s*"
        + r"(?:\[" + re.escape(table) + r"\]|" + re.escape(table) + r")"
        + r"\s*(?=\()", re.IGNORECASE,
    )
    matches = list(pattern.finditer(source))
    if len(matches) != 1:
        raise SourceContractError("TABLE_SOURCE_AMBIGUOUS:" + table)
    m = matches[0]
    start_paren = source.find("(", m.end())
    if start_paren < 0 or source[m.end():start_paren].strip():
        raise SourceContractError("TABLE_SOURCE_SHAPE:" + table)

    depth = 0
    state = "normal"
    i = start_paren
    end = -1
    while i < len(source):
        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ""
        if state == "line":
            if ch == "\n":
                state = "normal"
        elif state == "block":
            if ch == "*" and nxt == "/":
                state = "normal"
                i += 1
        elif state == "quote":
            if ch == "'" and nxt == "'":
                i += 1
            elif ch == "'":
                state = "normal"
        elif state == "bracket":
            if ch == "]" and nxt == "]":
                i += 1
            elif ch == "]":
                state = "normal"
        elif ch == "-" and nxt == "-":
            state = "line"
            i += 1
        elif ch == "/" and nxt == "*":
            state = "block"
            i += 1
        elif ch == "'":
            state = "quote"
        elif ch == "[":
            state = "bracket"
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                end = i + 1
                break
            if depth < 0:
                raise SourceContractError("TABLE_PARENS_INVALID:" + table)
        i += 1
    if end < 0 or state != "normal":
        raise SourceContractError("TABLE_PARENS_UNTERMINATED:" + table)
    tail = source[end:]
    term = re.match(r"\s*;", tail)
    if not term:
        raise SourceContractError("TABLE_MISSING_TERMINATOR:" + table)
    ddl = source[m.start():end + term.end()].strip()
    if re.search(r"(?im)^\s*GO(?:\s|$)", ddl):
        raise SourceContractError("TABLE_UNEXPECTED_BATCH:" + table)
    return ddl


def build(rc_root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise SourceContractError("OUTPUT_DIRECTORY_MISSING")
    entries = []
    statements = []
    for relative_path, names in SOURCES.items():
        path = rc_root / relative_path
        if not path.is_file():
            raise SourceContractError("PINNED_SOURCE_MISSING")
        raw = path.read_bytes()
        source = raw.decode("utf-8-sig")
        for name in names:
            ddl = extract_table(source, name)
            entries.append({
                "objectType": "TABLE",
                "name": "dbo." + name,
                "sourcePath": relative_path,
                "sourceSha256": sha256(raw),
                "ddlSha256": sha256((ddl + "\n").encode("utf-8")),
            })
            statements.append(
                "-- SOURCE: " + relative_path + "\n"
                + "-- SQL-ONLY TABLE: dbo." + name + "\n"
                + ddl + "\nGO\n"
            )
    if len(entries) != 7 or len({x["name"] for x in entries}) != 7:
        raise SourceContractError("TABLE_SCOPE_MISMATCH")
    body = (
        "-- NONDEPLOY SQL Server 2022 disposable clean-build extension.\n"
        "-- This is the seven-table layer ONLY; not the complete v1.8.0 installer.\n"
        "-- No application credentials, test accounts or historical version markers.\n"
        + "\n".join(statements)
    )
    data = body.encode("utf-8")
    sql_path = output / OUTPUT_SQL
    sql_path.write_bytes(data)
    manifest = {
        "format": "field-visit-v180-sql-only-core-tables-v1",
        "rcSourceSha": RC_SHA,
        "requiredSqlOnlyTableCount": len(entries),
        "objects": entries,
        "sqlSha256": sha256(data),
        "scope": "seven source-derived SQL-only tables; triggers/OptionC/permissions still HOLD",
        "fullCleanInstall": "HOLD_SQL_ONLY_TRIGGERS_OPTIONC_AND_APP_COMPATIBILITY",
    }
    (output / OUTPUT_MANIFEST).write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        actual = subprocess.check_output(
            ["git", "-C", str(args.rc_root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL
        ).strip()
        if actual != RC_SHA:
            raise SourceContractError("RC_SOURCE_PIN_MISMATCH")
        result = build(args.rc_root, args.output)
        if sha256((args.output / OUTPUT_SQL).read_bytes()) != result["sqlSha256"]:
            raise SourceContractError("OUTPUT_HASH_MISMATCH")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("SQL_ONLY_CORE_TABLE_SOURCE=FAIL_CLOSED")
        return 1
    print("SQL_ONLY_CORE_TABLE_SOURCE=PASS")
    print("SQL_ONLY_CORE_TABLES=7")
    print("FULL_CLEAN_INSTALL=HOLD_TRIGGERS_OPTIONC_AND_APP_COMPATIBILITY")
    return 0


if __name__ == "__main__":
    sys.exit(main())
