#!/usr/bin/env python3
"""Compare normalized UAT metadata with a protected 001-010 reference inventory.

The CLI requires exact reviewed source and evidence-file bindings. Inputs contain
sensitive schema and aggregate evidence; CLI output is deliberately bounded. A
comparison match is not historical provenance or UAT_SCHEMA_FIDELITY_PASS.
The in-process compare/load helpers remain low-level facet utilities, not release gates.
"""

from __future__ import annotations

import argparse
import hashlib
from datetime import datetime
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
        return value  # SQL literal whitespace and identifier whitespace are significant.
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


SQL_SHA256 = "fcd35b6dc7df5f18e5cea09c9110e3d4efdeb82dee7dbb0291a57ffdedacf8df"
FIELDS = {
    "Columns": "SchemaName TableName Ordinal ColumnName SqlType MaxLengthBytesOrCharacters NumericPrecision NumericScale IsNullable IsIdentity IsComputed IsRowVersion DefaultDefinition",
    "Indexes": "SchemaName TableName IndexName IndexType IsPrimaryKey IsUniqueConstraint IsUnique IsDisabled HasFilter FilterDefinition KeyOrdinal IsIncluded ColumnName IsDescending",
    "ForeignKeys": "SchemaName TableName ForeignKeyName ParentColumn ReferencedSchema ReferencedTable ReferencedColumn ColumnOrdinal DeleteAction UpdateAction IsDisabled IsNotTrusted",
    "Checks": "SchemaName TableName CheckConstraintName Definition IsDisabled IsNotTrusted",
}
NUMBERS = {"Ordinal", "MaxLengthBytesOrCharacters", "NumericPrecision", "NumericScale", "KeyOrdinal", "ColumnOrdinal"}
NULLABLE_TEXT = {"DefaultDefinition", "FilterDefinition"}
HISTORICAL = {"VisitTrips", "VisitTripStops", "VisitTripSnapshots", "VisitTripSnapshotStops"}


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate field")
        result[key] = value
    return result


def strict_inventory(path: Path, source_sha: str, content_sha256: str) -> dict[str, Any]:
    """Validate reviewed evidence bytes before comparing relational facets.

    Caller pins assert approved provenance; this function cannot create it.
    """
    if not re.fullmatch(r"[0-9a-f]{40}", source_sha) or not re.fullmatch(r"[0-9a-f]{64}", content_sha256):
        raise ValueError("malformed binding")
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != content_sha256:
        raise ValueError("content binding")
    d = json.loads(raw, object_pairs_hook=unique_object)
    top = {"ContractVersion", "SourceSha", "SqlSha256", "Target", "SchemaVersion", "CapturedUtc", "HistoricalAggregates", *SECTIONS}
    if not isinstance(d, dict) or set(d) != top:
        raise ValueError("shape")
    if d["ContractVersion"] != CONTRACT or d["SourceSha"] != source_sha or d["SqlSha256"] != SQL_SHA256:
        raise ValueError("source binding")
    target = d["Target"]
    if not isinstance(target, dict) or set(target) != {"ServerName", "DatabaseName", "EvidenceMode"}:
        raise ValueError("target shape")
    if target["ServerName"] not in {"sql-fieldvisit-jpe-uat", "sql-fieldvisit-jpe-uat.database.windows.net"} or target["DatabaseName"] != "db-fieldvisit-uat" or target["EvidenceMode"] != "METADATA_ONLY_NO_ROW_VALUES":
        raise ValueError("target")
    version = d["SchemaVersion"]
    if not isinstance(version, dict) or set(version) != {"Count", "Latest"} or type(version["Count"]) is not int or version["Count"] < 1 or version["Latest"] != "1.8.0-010":
        raise ValueError("version")
    if not isinstance(d["CapturedUtc"], str) or datetime.fromisoformat(d["CapturedUtc"].replace("Z", "+00:00")).utcoffset() is None:
        raise ValueError("capture time")
    for section, fields in FIELDS.items():
        rows = d[section]
        if not isinstance(rows, list) or not rows:
            raise ValueError("empty section")
        for row in rows:
            if not isinstance(row, dict) or set(row) != set(fields.split()):
                raise ValueError("row shape")
            for key, value in row.items():
                if key.startswith("Is") or key == "HasFilter":
                    valid = type(value) is bool or (type(value) is int and value in (0, 1))
                elif key in NUMBERS:
                    valid = type(value) is int and (value >= 0 or (key == "MaxLengthBytesOrCharacters" and value == -1))
                elif key in NULLABLE_TEXT:
                    valid = value is None or isinstance(value, str)
                else:
                    valid = isinstance(value, str) and bool(value)
                if not valid:
                    raise ValueError("facet type")
        keyed_rows(d, section)
    aggregates = d["HistoricalAggregates"]
    if not isinstance(aggregates, list) or len(aggregates) != 4:
        raise ValueError("historical shape")
    seen = set()
    for row in aggregates:
        if not isinstance(row, dict) or set(row) != {"HistoricalTable", "RowCount", "HighWatermark"} or not isinstance(row["HistoricalTable"], str) or row["HistoricalTable"] not in HISTORICAL or row["HistoricalTable"] in seen:
            raise ValueError("historical table")
        seen.add(row["HistoricalTable"])
        if type(row["RowCount"]) is not int or row["RowCount"] < 0 or (row["HighWatermark"] is not None and type(row["HighWatermark"]) is not int):
            raise ValueError("historical type")
    return d


class PrivateParser(argparse.ArgumentParser):
    def error(self, message):
        # argparse's default error includes caller-supplied paths/arguments.
        raise ValueError("arguments")


def main() -> int:
    try:
        parser = PrivateParser()
        parser.add_argument("--expected", type=Path, required=True)
        parser.add_argument("--actual", type=Path, required=True)
        for name in ("expected-source-sha", "actual-source-sha", "expected-sha256", "actual-sha256"):
            parser.add_argument("--" + name, required=True)
        args = parser.parse_args()
        expected = strict_inventory(args.expected, args.expected_source_sha, args.expected_sha256)
        actual = strict_inventory(args.actual, args.actual_source_sha, args.actual_sha256)
        differences = assert_010_contract(expected) + assert_010_contract(actual) + compare(expected, actual)
        if differences:
            print("SCHEMA_FIDELITY_COMPARISON_HOLD")
            return 1
    except (OSError, ValueError, TypeError, KeyError, OverflowError, RecursionError):
        print("SCHEMA_FIDELITY_COMPARISON_HOLD")
        return 2
    print("SCHEMA_FIDELITY_COMPARISON_MATCH")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
