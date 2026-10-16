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


CONTRACT = "uat-schema-fidelity-v1"
SECTIONS: dict[str, tuple[str, ...]] = {
    "Columns": ("SchemaName", "TableName", "Ordinal", "ColumnName"),
    "Indexes": ("SchemaName", "TableName", "IndexName", "IsIncluded", "KeyOrdinal", "ColumnName"),
    "ForeignKeys": ("SchemaName", "TableName", "ForeignKeyName", "ColumnOrdinal", "ParentColumn"),
    "Checks": ("SchemaName", "TableName", "CheckConstraintName"),
}
IGNORED_TOP_LEVEL = {"SourceSha", "CapturedUtc", "SqlSha256", "HistoricalAggregates", "Target"}


def load_inventory(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("ContractVersion") != CONTRACT:
        raise ValueError(f"{path}: unsupported or missing ContractVersion")
    if document.get("SchemaVersion", {}).get("Latest") != "1.8.0-010":
        raise ValueError(f"{path}: Latest schema version is not 1.8.0-010")
    for section, keys in SECTIONS.items():
        rows = document.get(section)
        if not isinstance(rows, list) or not rows:
            raise ValueError(f"{path}: {section} is missing or empty")
        for row in rows:
            if not isinstance(row, dict) or any(key not in row for key in keys):
                raise ValueError(f"{path}: {section} contains a malformed row")
    return document


def canonical(value: Any) -> Any:
    if isinstance(value, str):
        return re.sub(r"\s+", " ", value.strip())
    return value


def keyed_rows(document: dict[str, Any], section: str) -> dict[tuple[Any, ...], dict[str, Any]]:
    keys = SECTIONS[section]
    result: dict[tuple[Any, ...], dict[str, Any]] = {}
    for row in document[section]:
        key = tuple(canonical(row[name]) for name in keys)
        if key in result:
            raise ValueError(f"{section}: duplicate comparison key {key!r}")
        result[key] = {name: canonical(value) for name, value in row.items()}
    return result


def compare(expected: dict[str, Any], actual: dict[str, Any]) -> list[str]:
    differences: list[str] = []
    for section in SECTIONS:
        expected_rows = keyed_rows(expected, section)
        actual_rows = keyed_rows(actual, section)
        for key in sorted(expected_rows.keys() - actual_rows.keys(), key=repr):
            differences.append(f"{section}: missing {key!r}")
        for key in sorted(actual_rows.keys() - expected_rows.keys(), key=repr):
            differences.append(f"{section}: unexpected {key!r}")
        for key in sorted(expected_rows.keys() & actual_rows.keys(), key=repr):
            if expected_rows[key] != actual_rows[key]:
                changed = sorted(
                    name
                    for name in expected_rows[key].keys() | actual_rows[key].keys()
                    if expected_rows[key].get(name) != actual_rows[key].get(name)
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

    report = {
        "result": "PASS" if not differences else "HOLD",
        "contractVersion": CONTRACT,
        "expectedSchemaVersion": expected["SchemaVersion"]["Latest"],
        "actualSchemaVersion": actual["SchemaVersion"]["Latest"],
        "differenceCount": len(differences),
        "differences": differences[:200],
        "comparisonScope": list(SECTIONS),
        "excludedFromEquality": sorted(IGNORED_TOP_LEVEL),
    }
    output = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True)
    print(output)
    if args.report:
        args.report.write_text(output + "\n", encoding="utf-8")
    return 0 if not differences else 1


if __name__ == "__main__":
    raise SystemExit(main())
