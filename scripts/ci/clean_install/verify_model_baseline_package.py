#!/usr/bin/env python3
"""Verify the generated EF-only SQL baseline and inventory source-only DDL obligations."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

EXPECTED_RC = "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a"
TABLE_RE = re.compile(
    r"\bCREATE\s+TABLE\s+(?:\[?dbo\]?\s*\.\s*)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?",
    re.IGNORECASE,
)
TRIGGER_RE = re.compile(
    r"\bCREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+(?:\[?dbo\]?\s*\.\s*)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?",
    re.IGNORECASE,
)
MIGRATION_RE = re.compile(r"^(\d{4})_(\d{3})_.*$")


class ContractError(ValueError):
    pass


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory_sql_only_objects(rc_root: Path, mapped_tables: set[str]) -> dict:
    migrations = rc_root / "database" / "migrations"
    if not migrations.is_dir():
        raise ContractError("MIGRATION_SOURCE_MISSING")

    table_occurrences: dict[str, list[dict[str, str]]] = {}
    trigger_occurrences: dict[str, list[dict[str, str]]] = {}
    files = sorted(migrations.glob("[0-9][0-9][0-9][0-9]_[0-9][0-9][0-9]_*/Up.sql"))
    if not files:
        raise ContractError("MIGRATION_SOURCE_EMPTY")

    for path in files:
        rel = path.relative_to(rc_root).as_posix()
        source = path.read_text(encoding="utf-8-sig")
        migration_dir = path.parent.name
        if not MIGRATION_RE.match(migration_dir):
            continue
        for name in TABLE_RE.findall(source):
            table_occurrences.setdefault(name, []).append({
                "source": rel, "migration": migration_dir, "sourceSha256": sha256(path)
            })
        for name in TRIGGER_RE.findall(source):
            trigger_occurrences.setdefault(name, []).append({
                "source": rel, "migration": migration_dir, "sourceSha256": sha256(path)
            })

    sql_only_tables = []
    for name, occurrences in sorted(table_occurrences.items()):
        if name not in mapped_tables:
            sql_only_tables.append({"name": name, "definedAt": occurrences})

    # Multiple migrations can replace a trigger definition. Keep the migration
    # lineage so IT review can select the final definition instead of guessing.
    sql_trigger_lineage = [
        {"name": name, "definitionSources": occurrences}
        for name, occurrences in sorted(trigger_occurrences.items())
    ]
    return {
        "migrationSourceFileCount": len(files),
        "modelMappedTableCount": len(mapped_tables),
        "sqlOnlyTableCount": len(sql_only_tables),
        "sqlOnlyTables": sql_only_tables,
        "triggerDefinitionCount": len(sql_trigger_lineage),
        "triggerLineage": sql_trigger_lineage,
        "coverage": "INCOMPLETE_EF_MODEL_DOES_NOT_INCLUDE_MIGRATION_ONLY_OBJECTS",
        "fullCleanInstallStatus": "HOLD_SQL_ONLY_DDL_AND_TRIGGER_REVIEW_REQUIRED",
    }


def verify(rc_sha: str, rc_root: Path, output: Path) -> dict:
    if rc_sha != EXPECTED_RC:
        raise ContractError("RC_SOURCE_PIN_MISMATCH")
    rc_root = rc_root.resolve()
    output = output.resolve()
    context = rc_root / "backend/src/FieldVisit.Infrastructure/AppDbContext.cs"
    sql = output / "model-baseline.sql"
    manifest_path = output / "model-baseline-manifest.json"
    if not context.is_file() or not sql.is_file() or not manifest_path.is_file():
        raise ContractError("PACKAGE_FILE_MISSING")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("format") != "field-visit-clean-install-model-baseline-v1":
        raise ContractError("MANIFEST_FORMAT_MISMATCH")
    if manifest.get("sourceRcSha") != rc_sha:
        raise ContractError("MANIFEST_SOURCE_MISMATCH")
    if manifest.get("appDbContextSha256") != sha256(context):
        raise ContractError("MODEL_SOURCE_HASH_MISMATCH")
    if manifest.get("generatedSqlSha256") != sha256(sql):
        raise ContractError("GENERATED_SQL_HASH_MISMATCH")
    if manifest.get("sqlServerMajorVersion") != 16:
        raise ContractError("SQL_SERVER_VERSION_MISMATCH")

    table_names = manifest.get("mappedTables")
    if not isinstance(table_names, list) or not table_names or any(not isinstance(x, str) for x in table_names):
        raise ContractError("MAPPED_TABLE_CONTRACT_INVALID")
    if len(set(table_names)) != len(table_names) or len(table_names) != manifest.get("mappedTableCount"):
        raise ContractError("MAPPED_TABLE_COUNT_MISMATCH")
    inventory = inventory_sql_only_objects(rc_root, set(table_names))
    inventory["format"] = "field-visit-clean-install-sql-object-inventory-v1"
    inventory["sourceRcSha"] = rc_sha
    inventory["appDbContextSha256"] = sha256(context)
    inventory["generatedSqlSha256"] = sha256(sql)
    destination = output / "sql-only-object-inventory.json"
    destination.write_text(json.dumps(inventory, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return inventory


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-sha", required=True)
    parser.add_argument("--rc-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        actual_rc = subprocess.check_output(
            ["git", "-C", str(args.rc_root), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL
        ).strip()
        if actual_rc != args.rc_sha:
            raise ContractError("RC_CHECKOUT_SHA_MISMATCH")
        result = verify(args.rc_sha, args.rc_root, args.output)
    except (OSError, subprocess.CalledProcessError, json.JSONDecodeError, ContractError):
        print("CLEAN_INSTALL_PACKAGE=FAIL_CLOSED")
        return 1
    print("MODEL_BASELINE_PACKAGE=PASS")
    print(f"SQL_ONLY_TABLES={result['sqlOnlyTableCount']}")
    print(f"SQL_TRIGGER_DEFINITIONS={result['triggerDefinitionCount']}")
    print("FULL_CLEAN_INSTALL=HOLD_SQL_ONLY_DDL_AND_TRIGGER_REVIEW_REQUIRED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
