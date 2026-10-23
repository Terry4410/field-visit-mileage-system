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
        # Verify the new SQL-only layer without changing the accepted EF-only
        # 57-table verification contract. This is still NOT full Clean Install.
        layer_sql = args.output / "sql-only-core-tables.sql"
        layer_path = args.output / "sql-only-core-tables-manifest.json"
        if not layer_sql.is_file() or not layer_path.is_file():
            raise ContractError("SQL_ONLY_CORE_LAYER_MISSING")
        layer = json.loads(layer_path.read_text(encoding="utf-8"))
        if (layer.get("format") != "field-visit-v180-sql-only-core-tables-v1"
                or layer.get("rcSourceSha") != EXPECTED_RC
                or layer.get("requiredSqlOnlyTableCount") != 7
                or layer.get("sqlSha256") != sha256(layer_sql)):
            raise ContractError("SQL_ONLY_CORE_LAYER_INVALID")
        objects = layer.get("objects")
        if not isinstance(objects, list) or len(objects) != 7:
            raise ContractError("SQL_ONLY_CORE_OBJECT_LIST_INVALID")
        source_paths = {
            "database/migrations/1800_001_organization_center_team_lifecycle/Up.sql",
            "database/migrations/1800_006_notification_framework/Up.sql",
        }
        expected_names = {
            "dbo.SchemaMigrationDataBaselines", "dbo.NotificationEnvironmentPolicies",
            "dbo.NotificationEmailAllowlist", "dbo.NotificationSettings",
            "dbo.NotificationSettingRecipients", "dbo.MailOutbox", "dbo.MailDeliveryLogs"
        }
        if {o.get("name") for o in objects} != expected_names:
            raise ContractError("SQL_ONLY_CORE_OBJECT_NAMES_MISMATCH")
        for obj in objects:
            if obj.get("sourcePath") not in source_paths:
                raise ContractError("SQL_ONLY_SOURCE_PATH_MISMATCH")
            if sha256(args.rc_root / obj["sourcePath"]) != obj.get("sourceSha256"):
                raise ContractError("SQL_ONLY_SOURCE_HASH_MISMATCH")

        trigger_sql = args.output / "effective-source-triggers.sql"
        trigger_manifest = args.output / "effective-source-triggers-manifest.json"
        if not trigger_sql.is_file() or not trigger_manifest.is_file():
            raise ContractError("EFFECTIVE_TRIGGER_LAYER_MISSING")
        trigger = json.loads(trigger_manifest.read_text(encoding="utf-8"))
        if (trigger.get("format") != "field-visit-v180-effective-source-triggers-v1"
                or trigger.get("rcSourceSha") != EXPECTED_RC
                or trigger.get("effectiveTriggerCount") != 21
                or trigger.get("sqlSha256") != sha256(trigger_sql)):
            raise ContractError("EFFECTIVE_TRIGGER_LAYER_INVALID")
        t_entries = trigger.get("triggers")
        t_sources = trigger.get("sourceMigrations")
        if not isinstance(t_entries, list) or len(t_entries) != 21:
            raise ContractError("EFFECTIVE_TRIGGER_NAMES_INVALID")
        if len({e.get("name") for e in t_entries}) != 21:
            raise ContractError("EFFECTIVE_TRIGGER_NAMES_DUPLICATE")
        if not isinstance(t_sources, list) or len(t_sources) != 10:
            raise ContractError("EFFECTIVE_TRIGGER_SOURCE_SCOPE")
        if {s.get("migration") for s in t_sources} != {f"{i:03d}" for i in range(1, 11)}:
            raise ContractError("EFFECTIVE_TRIGGER_MIGRATION_SET")
        for source in t_sources:
            path = source.get("path", "")
            if (not isinstance(path, str)
                    or not re.fullmatch(r"database/migrations/1800_\d{3}_[^/]+/Up\.sql", path)):
                raise ContractError("EFFECTIVE_TRIGGER_SOURCE_PATH")
            if sha256(args.rc_root / path) != source.get("sha256"):
                raise ContractError("EFFECTIVE_TRIGGER_SOURCE_HASH")
        candidate_sql = args.output / "optionc-sql-only-candidates.sql"
        candidate_path = args.output / "optionc-sql-only-candidates-manifest.json"
        if not candidate_sql.is_file() or not candidate_path.is_file():
            raise ContractError("OPTIONC_CANDIDATE_LAYER_MISSING")
        candidate = json.loads(candidate_path.read_text(encoding="utf-8"))
        if (candidate.get("format") != "field-visit-v180-optionc-sql-only-table-probe-v1"
                or candidate.get("rcSourceSha") != EXPECTED_RC
                or candidate.get("disposableCandidateTableCount") != 4
                or candidate.get("sqlSha256") != sha256(candidate_sql)):
            raise ContractError("OPTIONC_CANDIDATE_MANIFEST_INVALID")
        expected_candidates = {
            "dbo.LocationPublicationStages", "dbo.LocationPublishedVersions",
            "dbo.LocationCreateReservations", "dbo.LocationMergeDetails"
        }
        cand_objects = candidate.get("objects")
        if not isinstance(cand_objects, list) or len(cand_objects) != 4:
            raise ContractError("OPTIONC_CANDIDATE_NAMES")
        if {x.get("name") for x in cand_objects} != expected_candidates:
            raise ContractError("OPTIONC_CANDIDATE_OBJECT_SET")
        for item in cand_objects:
            path = item.get("sourcePath", "")
            if (not isinstance(path, str) or
                    not re.fullmatch(
                        r"docs/release/optionc-canonical-sql-draft/candidate/1800_0(?:14|17|19)/Up\.sql", path)):
                raise ContractError("OPTIONC_CANDIDATE_SOURCE_PATH")
            if sha256(args.rc_root / path) != item.get("sourceSha256"):
                raise ContractError("OPTIONC_CANDIDATE_SOURCE_HASH")
            if item.get("scope") != "DISPOSABLE_CANDIDATE_NOT_APPROVED_FINAL_SCHEMA":
                raise ContractError("OPTIONC_CANDIDATE_UNSAFE_SCOPE")
    except (OSError, subprocess.CalledProcessError, json.JSONDecodeError, ContractError):
        print("CLEAN_INSTALL_PACKAGE=FAIL_CLOSED")
        return 1
    print("MODEL_BASELINE_PACKAGE=PASS")
    print(f"SQL_ONLY_TABLES={result['sqlOnlyTableCount']}")
    print(f"SQL_TRIGGER_DEFINITIONS={result['triggerDefinitionCount']}")
    print("SQL_ONLY_CORE_TABLE_PACKAGE=PASS")
    print("EFFECTIVE_TRIGGER_PACKAGE=PASS")
    print("OPTIONC_CANDIDATE_TABLE_PACKAGE=PASS")
    print("FULL_CLEAN_INSTALL=HOLD_OPTIONC_FINAL_OBJECTS_AND_APP_COMPATIBILITY")
    return 0


if __name__ == "__main__":
    sys.exit(main())
