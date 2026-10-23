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
        index_sql = args.output / "sql-only-extra-indexes.sql"
        index_manifest = args.output / "sql-only-extra-indexes-manifest.json"
        if not index_sql.is_file() or not index_manifest.is_file():
            raise ContractError("SQL_ONLY_INDEX_LAYER_MISSING")
        index = json.loads(index_manifest.read_text(encoding="utf-8"))
        if (index.get("format") != "field-visit-v180-sql-only-index-probe-v1"
                or index.get("rcSourceSha") != EXPECTED_RC
                or index.get("indexCount") != 13
                or index.get("sqlSha256") != sha256(index_sql)):
            raise ContractError("SQL_ONLY_INDEX_MANIFEST_INVALID")
        indexes = index.get("indexes")
        if not isinstance(indexes, list) or len(indexes) != 13:
            raise ContractError("SQL_ONLY_INDEX_OBJECT_LIST")
        if len({i.get("name") for i in indexes}) != 13:
            raise ContractError("SQL_ONLY_INDEX_DUPLICATE")
        for item in indexes:
            path = item.get("sourcePath", "")
            if path not in (
                    "database/migrations/1800_006_notification_framework/Up.sql",
                    "docs/release/optionc-canonical-sql-draft/candidate/1800_014/Up.sql",
                    "docs/release/optionc-canonical-sql-draft/candidate/1800_017/Up.sql"):
                raise ContractError("SQL_ONLY_INDEX_PROVENANCE")
            if sha256(args.rc_root / path) != item.get("sourceSha256"):
                raise ContractError("SQL_ONLY_INDEX_SOURCE_HASH")
        source_check_sql = args.output / "007_source_checks.sql"
        source_check_manifest = args.output / "007_source_checks-manifest.json"
        if not source_check_sql.is_file() or not source_check_manifest.is_file():
            raise ContractError("SOURCE_007_CHECK_PACKAGE_MISSING")
        m = json.loads(source_check_manifest.read_text(encoding="utf-8"))
        if (m.get("format") != "field-visit-v180-source-checks-007-v1"
                or m.get("rcSourceSha") != EXPECTED_RC
                or m.get("count") != 9
                or m.get("sqlSha256") != sha256(source_check_sql)):
            raise ContractError("SOURCE_007_CHECK_MANIFEST_INVALID")
        expected_007_names = {
            "CK_GeocodingAttempts_Status", "CK_GeocodingAttempts_Result",
            "CK_MileageGovernanceEvents_Type",
            "CK_MileageCalculations_ApprovedDistanceSource",
            "CK_MileageCalculations_ApprovalEvidence",
            "CK_VisitTripSnapshots_RouteMode",
            "CK_VisitTripSnapshots_RouteStatus",
            "CK_VisitTripSnapshots_ApprovedDistanceSource",
            "CK_VisitTripSnapshots_ApprovalBasis"
        }
        entries=m.get("checks")
        if not isinstance(entries,list) or len(entries)!=9 or {e.get("name") for e in entries} != expected_007_names:
            raise ContractError("SOURCE_007_CHECK_SCOPE")
        path="database/migrations/1800_007_mileage_google_governance/Up.sql"
        if m.get("sourcePath")!=path or m.get("sourceSha256")!=sha256(args.rc_root / path):
            raise ContractError("SOURCE_007_CHECK_PROVENANCE")
        checks001_sql = args.output / "001_004_source_checks.sql"
        checks001_manifest = args.output / "001_004_source_checks-manifest.json"
        if not checks001_sql.is_file() or not checks001_manifest.is_file():
            raise ContractError("CHECK_001_004_PACKAGE_MISSING")
        obj001 = json.loads(checks001_manifest.read_text(encoding="utf-8"))
        if (obj001.get("format") != "field-visit-v180-source-checks-001-004-v1"
                or obj001.get("rcSourceSha") != EXPECTED_RC
                or obj001.get("count") != 17
                or obj001.get("sqlSha256") != sha256(checks001_sql)):
            raise ContractError("CHECK_001_004_PACKAGE_INVALID")
        check_list=obj001.get("checks")
        if (not isinstance(check_list,list) or len(check_list)!=17
                or len({e.get("name") for e in check_list})!=17):
            raise ContractError("CHECK_001_004_SCOPE")
        approved={
            f"database/migrations/1800_00{i}_{title}/Up.sql"
            for i,title in [
                (1,"organization_center_team_lifecycle"),
                (2,"person_employment_role_membership"),
                (3,"deployment_sites"),
                (4,"location_governance")
            ]
        }
        if {e.get("sourcePath") for e in check_list}!=approved:
            raise ContractError("CHECK_001_004_SOURCES")
        for entry in check_list:
            if sha256(args.rc_root / entry["sourcePath"])!=entry.get("sourceSha256"):
                raise ContractError("CHECK_001_004_SOURCE_HASH")
        facets005 = args.output / "005_indexable_facets.sql"
        if not facets005.is_file():
            raise ContractError("SOURCE_005_INDEXABLE_FACETS_MISSING")
        exact_facets = (
            "ALTER TABLE dbo.VisitTypes ALTER COLUMN VisitTypeCode NVARCHAR(50) NOT NULL;"
            "\nALTER TABLE dbo.VisitTypes ALTER COLUMN VisitTypeName NVARCHAR(200) NOT NULL;"
            "\nALTER TABLE dbo.MileageRateRules ALTER COLUMN VehicleType NVARCHAR(50) NOT NULL;"
        )
        if facets005.read_text(encoding="utf-8").strip() != exact_facets:
            raise ContractError("SOURCE_005_INDEXABLE_FACETS_NOT_REVIEWED")
        control005_sql = args.output / "005_source_controls.sql"
        control005_manifest = args.output / "005_source_controls-manifest.json"
        if not control005_sql.is_file() or not control005_manifest.is_file():
            raise ContractError("SOURCE_005_CONTROLS_MISSING")
        c005 = json.loads(control005_manifest.read_text(encoding="utf-8"))
        src005 = rc_root / "database/migrations/1800_005_project_visit_rate_lifecycle/Up.sql"
        if (c005.get("format") != "field-visit-v180-source-005-controls-v1"
                or c005.get("rcSourceSha") != EXPECTED_RC
                or c005.get("sourceSha256") != sha256(src005)
                or c005.get("sqlSha256") != sha256(control005_sql)
                or set(c005.get("checks", [])) != {
                    "CK_Projects_Dates", "CK_MileageRateRules_VehicleType",
                    "CK_MileageRateRules_Dates", "CK_MileageRateRules_NonNegativeRate"}
                or set(c005.get("indexes", [])) != {
                    "IX_Projects_Search", "UX_VisitTypes_VisitTypeCode",
                    "IX_VisitTypes_Active_Sort",
                    "UX_MileageRateRules_Scope_Vehicle_Start",
                    "IX_MileageRateRules_AsOf"}):
            raise ContractError("SOURCE_005_CONTROLS_INVALID")
        optionc_final_sql = args.output / "optionc-final-target-checks.sql"
        optionc_final_manifest = args.output / "optionc-final-target-checks-manifest.json"
        if not optionc_final_sql.is_file() or not optionc_final_manifest.is_file():
            raise ContractError("OPTIONC_FINAL_CHECK_SOURCE_MISSING")
        final_checks = json.loads(optionc_final_manifest.read_text(encoding="utf-8"))
        if (final_checks.get("format") != "field-visit-v180-optionc-effective-checks-v1"
                or final_checks.get("rcSourceSha") != EXPECTED_RC
                or final_checks.get("checkCount") != 15
                or final_checks.get("sqlSha256") != sha256(optionc_final_sql)):
            raise ContractError("OPTIONC_FINAL_CHECK_SOURCE_CONTRACT")
        checks = final_checks.get("checks")
        if not isinstance(checks, list) or len(checks) != 15 or len({e.get("name") for e in checks}) != 15:
            raise ContractError("OPTIONC_FINAL_CHECK_SET")
        expected_replacements = {
            "CK_B3_ChangeRequests_KnownCodes","CK_B3_ChangeRequests_KnownStatus",
            "CK_B3_ChangeRequests_ExpectedLocationVersion","CK_B3_ChangeRequests_ProposedJson",
            "CK_B3_ChangeRequests_ReviewState","CK_B3_ChangeRequestEvents_EventType",
            "CK_B3_ChangeRequestEvents_DetailsJson","CK_B3_ChangeRequestEvents_DecisionState",
            "CK_OptionC_Published_Origin"
        }
        if {e.get("name") for e in checks if e.get("replacesReviewedPredecessor")} != expected_replacements:
            raise ContractError("OPTIONC_FINAL_REPLACEMENT_SCOPE")
        for e in checks:
            p=e.get("sourcePath","")
            if not re.fullmatch(r"docs/release/optionc-canonical-sql-draft/candidate/1800_0(?:11|12|16|17|18|20)/Up\.sql",p):
                raise ContractError("OPTIONC_FINAL_SOURCE_PATH")
            if e.get("sourceSha256") != sha256(args.rc_root / p):
                raise ContractError("OPTIONC_FINAL_SOURCE_DIGEST")
    except (OSError, subprocess.CalledProcessError, json.JSONDecodeError, ContractError):
        print("CLEAN_INSTALL_PACKAGE=FAIL_CLOSED")
        return 1
    print("MODEL_BASELINE_PACKAGE=PASS")
    print(f"SQL_ONLY_TABLES={result['sqlOnlyTableCount']}")
    print(f"SQL_TRIGGER_DEFINITIONS={result['triggerDefinitionCount']}")
    print("SQL_ONLY_CORE_TABLE_PACKAGE=PASS")
    print("EFFECTIVE_TRIGGER_PACKAGE=PASS")
    print("OPTIONC_CANDIDATE_TABLE_PACKAGE=PASS")
    print("SQL_ONLY_INDEX_PACKAGE=PASS")
    print("SOURCE_007_OTHER_CHECKS_PACKAGE=PASS")
    print("SOURCE_001_004_CHECKS_PACKAGE=PASS")
    print("OPTIONC_FINAL_CHECKS_PACKAGE=PASS")
    print("FULL_CLEAN_INSTALL=HOLD_OTHER_OBJECTS_AND_APP_COMPATIBILITY")
    return 0


if __name__ == "__main__":
    sys.exit(main())
