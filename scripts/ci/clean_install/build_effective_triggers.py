#!/usr/bin/env python3
"""Derive 21 effective 1.8.0-010 triggers from pinned 001–010 migrations.

NONDEPLOY disposable SQL Server candidate. A+C final DDL remains HOLD.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

RC = "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a"
EXPECTED = {"001": 1, "002": 5, "003": 7, "004": 2, "005": 1,
            "006": 0, "007": 5, "008": 1, "009": 0, "010": 0}
DYNAMIC = re.compile(r"\bEXEC\s+sys\.sp_executesql\s+N'", re.I)
HEADER = re.compile(
    r"^\s*CREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+(?:dbo|\[dbo\])\s*\.\s*"
    r"(?:\[)?(?P<name>[A-Za-z_]\w*)(?:\])?\s+ON\s+"
    r"(?:dbo|\[dbo\])\s*\.\s*(?:\[)?(?P<table>[A-Za-z_]\w*)(?:\])?"
    r"\s+AFTER\s+(?:INSERT|UPDATE|DELETE)\b", re.I
)
NAME = re.compile(r"\bCREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+", re.I)


class TriggerSourceError(ValueError):
    pass


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def dynamic_statements(sql: str):
    for match in DYNAMIC.finditer(sql):
        pos = match.end()
        buf = []
        while pos < len(sql):
            char = sql[pos]
            if char == "'":
                if pos + 1 < len(sql) and sql[pos + 1] == "'":
                    buf.append("'")
                    pos += 2
                    continue
                break
            buf.append(char)
            pos += 1
        else:
            raise TriggerSourceError("DYNAMIC_SQL_UNTERMINATED")
        yield "".join(buf)


def build(root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise TriggerSourceError("OUTPUT_DIRECTORY_MISSING")
    migration_root = root / "database" / "migrations"
    effective = {}
    sources = []
    for mid, expected_count in EXPECTED.items():
        paths = list(migration_root.glob(f"1800_{mid}_*/Up.sql"))
        if len(paths) != 1:
            raise TriggerSourceError("SOURCE_MIGRATION_SCOPE:" + mid)
        path = paths[0]
        raw = path.read_bytes()
        source = raw.decode("utf-8-sig")
        trigger_statements = []
        for sql in dynamic_statements(source):
            if not NAME.search(sql):
                continue
            match = HEADER.match(sql)
            if not match or len(NAME.findall(sql)) != 1:
                raise TriggerSourceError("TRIGGER_HEADER_SCOPE:" + mid)
            trigger_statements.append((match.group("name"), match.group("table"), sql))
        if len(trigger_statements) != expected_count:
            raise TriggerSourceError("TRIGGER_COUNT_SCOPE:" + mid)
        if len({name for name, _, _ in trigger_statements}) != len(trigger_statements):
            raise TriggerSourceError("TRIGGER_DUPLICATE_IN_MIGRATION:" + mid)
        relative = path.relative_to(root).as_posix()
        sources.append({"migration": mid, "path": relative, "sha256": digest(raw)})
        for name, table, statement in trigger_statements:
            if name in effective and not (
                    name == "TR_MileageRateRules_ProtectSeries" and mid == "008"):
                raise TriggerSourceError("TRIGGER_UNEXPECTED_SUPERSESSION:" + name)
            effective[name] = {
                "name": name, "table": table, "sourceMigration": mid,
                "sourcePath": relative,
                "definitionSha256Utf16Le": digest(statement.encode("utf-16le")),
                "ddl": statement.strip()
            }
    if len(effective) != 21 or effective.get(
            "TR_MileageRateRules_ProtectSeries", {}).get("sourceMigration") != "008":
        raise TriggerSourceError("TRIGGER_EFFECTIVE_SCOPE")
    entries = [effective[name] for name in sorted(effective)]
    chunks = [
        "-- 21 effective source-derived v1.8.0-010 triggers. DISPOSABLE NONDEPLOY.\n"
        "-- A+C trigger/schema or complete 011–020 target contract still HOLD.\n"
    ]
    for entry in entries:
        # SQL Server requires CREATE TRIGGER as the first DDL in a batch.
        chunks.append("\n-- " + entry["sourcePath"] + "\n"
                      + entry["ddl"] + "\nGO\n")
    ddl = "".join(chunks).encode("utf-8")
    (output / "effective-source-triggers.sql").write_bytes(ddl)
    manifest = {
        "format": "field-visit-v180-effective-source-triggers-v1",
        "rcSourceSha": RC, "sourceMigrations": sources,
        "effectiveTriggerCount": len(entries),
        "triggers": [{k: v for k, v in e.items() if k != "ddl"} for e in entries],
        "sqlSha256": digest(ddl),
        "fullCleanInstall": "HOLD_OPTIONC_MIGRATION_AND_RUNTIME_COMPATIBILITY",
    }
    (output / "effective-source-triggers-manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        sha = subprocess.check_output(["git", "-C", str(args.rc_root), "rev-parse", "HEAD"],
                                      text=True, stderr=subprocess.DEVNULL).strip()
        if sha != RC:
            raise TriggerSourceError("SOURCE_RC_MISMATCH")
        result = build(args.rc_root, args.output)
        if digest((args.output / "effective-source-triggers.sql").read_bytes()) != result["sqlSha256"]:
            raise TriggerSourceError("TRIGGER_OUTPUT_DRIFT")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("EFFECTIVE_TRIGGER_SOURCE=FAIL_CLOSED")
        return 1
    print("EFFECTIVE_TRIGGER_SOURCE=PASS")
    print("EFFECTIVE_TRIGGERS=21")
    print("FULL_CLEAN_INSTALL=HOLD_OPTIONC_AND_RUNTIME_COMPATIBILITY")
    return 0


if __name__ == "__main__":
    sys.exit(main())
