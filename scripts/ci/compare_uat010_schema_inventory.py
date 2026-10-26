#!/usr/bin/env python3
"""Compare normalized UAT metadata with a protected 001-010 reference inventory.

The script intentionally accepts only the normalized JSON emitted by
uat_schema_fidelity_evidence.ps1. It never connects to UAT and never reads row
values. A PASS requires an independently generated protected 001-010 reference;
the current whole-catalog fingerprint is not an acceptable substitute.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


CONTRACT_V1 = "uat-schema-fidelity-v1"
CONTRACT_V2 = "uat-schema-fidelity-v2"
SECTIONS_V1: dict[str, tuple[str, ...]] = {
    "Columns": ("SchemaName", "TableName", "Ordinal", "ColumnName"),
    "Indexes": ("SchemaName", "TableName", "IndexName", "IsIncluded", "KeyOrdinal", "ColumnName"),
    "ForeignKeys": ("SchemaName", "TableName", "ForeignKeyName", "ColumnOrdinal", "ParentColumn"),
    "Checks": ("SchemaName", "TableName", "CheckConstraintName"),
}
SECTIONS_V2: dict[str, tuple[str, ...]] = {
    "Tables": ("SchemaName", "TableName"),
    **SECTIONS_V1,
    "Triggers": ("SchemaName", "TableName", "TriggerName", "EventType"),
}
SECTIONS_BY_CONTRACT = {CONTRACT_V1: SECTIONS_V1, CONTRACT_V2: SECTIONS_V2}
RAW_CAPTURE_FORMAT = "FieldVisitExisting010SchemaCapture/v1"
RAW_SQL_SHA256 = "ac8ee4d845e4154af8162ebd4679d3a9a1db50492d4cdfe779e82f8c3186ab91"
IGNORED_TOP_LEVEL_V1 = {"SourceSha", "CapturedUtc", "SqlSha256", "HistoricalAggregates", "Target"}
IGNORED_TOP_LEVEL_V2 = {"SourceSha", "CapturedUtc", "SqlSha256", "Target", "SchemaVersion"}
RAW_SET_FIELDS: tuple[tuple[str, ...], ...] = (
    ("ServerName", "DatabaseName", "EvidenceMode", "DboTableCount", "SchemaVersionCount", "LatestVersion"),
    ("SchemaName", "TableName", "TemporalType", "IsMemoryOptimized", "IsFileTable", "LockEscalation"),
    ("SchemaName", "TableName", "Ordinal", "ColumnName", "TypeSchema", "SqlType", "MaxLengthBytes",
     "NumericPrecision", "NumericScale", "IsNullable", "IsIdentity", "IdentitySeed", "IdentityIncrement",
     "IsComputed", "ComputedDefinition", "IsPersisted", "IsRowGuid", "IsSparse", "IsColumnSet",
     "GeneratedAlwaysType", "CollationName", "DefaultDefinition"),
    ("SchemaName", "TableName", "IndexName", "IndexType", "IsPrimaryKey", "IsUniqueConstraint", "IsUnique",
     "IsDisabled", "IsHypothetical", "IgnoreDuplicateKey", "HasFilter", "FilterDefinition", "KeyOrdinal",
     "IsIncluded", "ColumnName", "IsDescending"),
    ("SchemaName", "TableName", "ForeignKeyName", "ParentColumn", "ReferencedSchema", "ReferencedTable",
     "ReferencedColumn", "ColumnOrdinal", "DeleteAction", "UpdateAction", "IsDisabled", "IsNotTrusted",
     "IsNotForReplication"),
    ("SchemaName", "TableName", "CheckConstraintName", "Definition", "IsDisabled", "IsNotTrusted",
     "IsNotForReplication"),
    ("SchemaName", "TableName", "TriggerName", "IsDisabled", "IsInsteadOfTrigger", "IsNotForReplication",
     "IsMicrosoftShipped", "CreatedAt", "ModifiedAt", "EventType", "UsesAnsiNulls", "UsesQuotedIdentifier",
     "Definition"),
)


def load_inventory(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("format") == RAW_CAPTURE_FORMAT:
        document = normalize_raw_capture(document)
    contract = document.get("ContractVersion")
    if contract not in SECTIONS_BY_CONTRACT:
        raise ValueError(f"{path}: unsupported or missing ContractVersion")
    if document.get("SchemaVersion", {}).get("Latest") != "1.8.0-010":
        raise ValueError(f"{path}: Latest schema version is not 1.8.0-010")
    for section, keys in SECTIONS_BY_CONTRACT[contract].items():
        rows = document.get(section)
        if not isinstance(rows, list) or not rows:
            raise ValueError(f"{path}: {section} is missing or empty")
        for row in rows:
            if not isinstance(row, dict) or any(key not in row for key in keys):
                raise ValueError(f"{path}: {section} contains a malformed row")
    return document


def normalize_raw_capture(document: dict[str, Any]) -> dict[str, Any]:
    """Convert the exact seven SELECT result sets to a comparison-safe v2 inventory.

    Definitions are retained byte-for-character as decoded JSON strings. Volatile
    trigger create/modify timestamps are retained in the capture but excluded from
    equality because they are not migration contract facets.
    """
    if document.get("status") != "COMPLETE" or document.get("format") != RAW_CAPTURE_FORMAT:
        raise ValueError("raw capture is incomplete or has an unsupported format")
    source_sha = document.get("sourceCommit")
    query_sha = document.get("sourceQuerySha256")
    if not isinstance(source_sha, str) or not re.fullmatch(r"[0-9a-f]{40}", source_sha):
        raise ValueError("raw capture source commit is malformed")
    if not isinstance(query_sha, str) or query_sha.lower() != RAW_SQL_SHA256:
        raise ValueError("raw capture query SHA-256 is not the approved exact query")
    if document.get("queryExecutedOnce") is not True or document.get("queryErrorNumber") is not None:
        raise ValueError("raw capture does not prove one successful query execution")
    attempts = document.get("connectionAttempts")
    if not isinstance(attempts, int) or not 1 <= attempts <= 4:
        raise ValueError("raw capture connection attempt count is outside the authorized bound")
    sets = document.get("resultSets")
    if not isinstance(sets, list) or len(sets) != 7:
        raise ValueError("raw capture must contain exactly seven result sets")
    for index, (result_set, fields) in enumerate(zip(sets, RAW_SET_FIELDS), start=1):
        if not isinstance(result_set, dict) or not isinstance(result_set.get("rows"), list) or not result_set["rows"]:
            raise ValueError(f"raw capture result set {index} is missing or empty")
        for row in result_set["rows"]:
            if not isinstance(row, dict) or any(field not in row for field in fields):
                raise ValueError(f"raw capture result set {index} has an incomplete row")
    target = sets[0]["rows"]
    if len(target) != 1:
        raise ValueError("raw capture target/version set must contain exactly one row")
    target = target[0]
    if (target["ServerName"] not in ("sql-fieldvisit-jpe-uat", "sql-fieldvisit-jpe-uat.database.windows.net")
            or target["DatabaseName"] != "db-fieldvisit-uat"
            or target["EvidenceMode"] != "METADATA_ONLY_NO_BUSINESS_ROW_VALUES"
            or target["DboTableCount"] != 63
            or target["SchemaVersionCount"] != 26
            or target["LatestVersion"] != "1.8.0-010"):
        raise ValueError("raw capture target or Existing010 predecessor guard did not match")
    if len(sets[1]["rows"]) != 63:
        raise ValueError("raw capture dbo table inventory did not contain 63 tables")
    # The seven query result sets become the seven independently compared sections.
    return {
        "ContractVersion": CONTRACT_V2,
        "SourceSha": source_sha,
        "SqlSha256": query_sha.lower(),
        "CapturedUtc": document.get("capturedUtc"),
        "Target": {key: target[key] for key in ("ServerName", "DatabaseName", "EvidenceMode")},
        "SchemaVersion": {"Count": target["SchemaVersionCount"], "Latest": target["LatestVersion"]},
        "Tables": sets[1]["rows"],
        "Columns": sets[2]["rows"],
        "Indexes": sets[3]["rows"],
        "ForeignKeys": sets[4]["rows"],
        "Checks": sets[5]["rows"],
        "Triggers": sets[6]["rows"],
    }


def canonical(value: Any) -> Any:
    if isinstance(value, str):
        return re.sub(r"\s+", " ", value.strip())
    return value


def keyed_rows(document: dict[str, Any], section: str) -> dict[tuple[Any, ...], dict[str, Any]]:
    keys = SECTIONS_BY_CONTRACT[document["ContractVersion"]][section]
    result: dict[tuple[Any, ...], dict[str, Any]] = {}
    for row in document[section]:
        key = tuple(canonical(row[name]) for name in keys)
        if key in result:
            raise ValueError(f"{section}: duplicate comparison key {key!r}")
        preserve = {"Definition", "ComputedDefinition", "DefaultDefinition", "FilterDefinition"}
        result[key] = {
            name: value if document["ContractVersion"] == CONTRACT_V2 and name in preserve else canonical(value)
            for name, value in row.items()
        }
    return result


def compare(expected: dict[str, Any], actual: dict[str, Any]) -> list[str]:
    differences: list[str] = []
    expected_contract = expected.get("ContractVersion")
    actual_contract = actual.get("ContractVersion")
    if expected_contract != actual_contract:
        return ["inventory contract versions differ"]
    sections = SECTIONS_BY_CONTRACT.get(expected_contract)
    if sections is None:
        return ["unsupported inventory contract"]
    for section in sections:
        expected_rows = keyed_rows(expected, section)
        actual_rows = keyed_rows(actual, section)
        for key in sorted(expected_rows.keys() - actual_rows.keys(), key=repr):
            differences.append(f"{section}: missing {key!r}")
        for key in sorted(actual_rows.keys() - expected_rows.keys(), key=repr):
            differences.append(f"{section}: unexpected {key!r}")
        for key in sorted(expected_rows.keys() & actual_rows.keys(), key=repr):
            # Trigger timestamps describe when an object was created/altered, not
            # its source definition. Every other captured trigger facet is compared.
            left = dict(expected_rows[key])
            right = dict(actual_rows[key])
            if section == "Triggers":
                for volatile in ("CreatedAt", "ModifiedAt"):
                    left.pop(volatile, None)
                    right.pop(volatile, None)
            if left != right:
                changed = sorted(
                    name for name in left.keys() | right.keys()
                    if left.get(name) != right.get(name)
                )
                differences.append(f"{section}: facet drift {key!r}: {','.join(changed)}")
    return differences


def assert_010_contract(document: dict[str, Any]) -> list[str]:
    differences: list[str] = []
    candidates = [
        row for row in document["Checks"]
        if row.get("SchemaName") == "dbo"
        and row.get("TableName") == "RouteCalculationAttempts"
        and row.get("CheckConstraintName") == "CK_RouteCalculationAttempts_Reason"
    ]
    if len(candidates) != 1:
        return ["010: CK_RouteCalculationAttempts_Reason missing or duplicated"]
    row = candidates[0]
    definition = canonical(row.get("Definition") or "").lower()
    for token in ("visitorcalculate", "leaderretry", "correctionrecalculate", "backgroundmileagejob"):
        if token not in definition:
            differences.append(f"010: reason constraint lacks {token}")
    if row.get("IsDisabled") not in (False, 0):
        differences.append("010: reason constraint is disabled")
    if row.get("IsNotTrusted") not in (False, 0):
        differences.append("010: reason constraint is not trusted")
    return differences


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--expected", type=Path, required=True,
                        help="normalized inventory from a protected-source disposable 001-010 database")
    parser.add_argument("--actual", type=Path, required=True,
                        help="normalized metadata-only UAT inventory")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    try:
        expected = load_inventory(args.expected)
        actual = load_inventory(args.actual)
        differences = assert_010_contract(expected) + assert_010_contract(actual) + compare(expected, actual)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"SCHEMA_FIDELITY_HOLD: {exc}", file=sys.stderr)
        return 2

    contract = actual["ContractVersion"]
    report = {
        "result": "PASS" if not differences else "HOLD",
        "contractVersion": contract,
        "expectedSchemaVersion": expected["SchemaVersion"]["Latest"],
        "actualSchemaVersion": actual["SchemaVersion"]["Latest"],
        "differenceCount": len(differences),
        "differences": differences[:200],
        "comparisonScope": list(SECTIONS_BY_CONTRACT[contract]),
        "excludedFromEquality": sorted(IGNORED_TOP_LEVEL_V1 if contract == CONTRACT_V1 else IGNORED_TOP_LEVEL_V2),
    }
    output = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True)
    print(output)
    if args.report:
        args.report.write_text(output + "\n", encoding="utf-8")
    return 0 if not differences else 1


if __name__ == "__main__":
    raise SystemExit(main())
