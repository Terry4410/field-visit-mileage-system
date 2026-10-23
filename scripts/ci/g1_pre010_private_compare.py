#!/usr/bin/env python3
"""Status-only private comparison of observed PRE-010 metadata to pinned source.

Not a full 1.5 baseline, not G1-S release GO. No network/SQL. Raw metadata
stays in runner temp and must never be uploaded to GitHub artifacts.
"""
from __future__ import annotations
import argparse
import importlib.util
import json
import re
import hashlib
import sys
from pathlib import Path

SOURCE = Path(__file__).with_name("g1_pre010_trigger_ddl.py")
spec = importlib.util.spec_from_file_location("trigger_source", SOURCE)
source_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(source_module)
COL_SOURCE=Path(__file__).with_name("g1_pre010_created_columns.py")
column_spec=importlib.util.spec_from_file_location("g1_columns",COL_SOURCE)
column_module=importlib.util.module_from_spec(column_spec)
column_spec.loader.exec_module(column_module)
COLUMN_FIELDS=("SchemaName","TableName","ColumnName","SqlType","MaxLengthBytes","Scale","IsNullable","IsIdentity")
HEX64 = re.compile(r"^[0-9a-fA-F]{64}$")
REQUIRED007 = "TR_Locations_SelectedGeocodingAttempt"
FIELDS = ("SchemaName", "TableName", "TriggerName", "Events",
          "IsDisabled", "IsInsteadOfTrigger", "DefinitionSha256")

class Stop(Exception):
    def __init__(self, code):
        self.code = code

def check_columns(actual, expected):
    if not isinstance(actual,list) or len(actual)>4000:raise Stop("COLUMN_SHAPE")
    index={}
    for row in actual:
        if not isinstance(row,dict) or any(x not in row for x in COLUMN_FIELDS):
            raise Stop("COLUMN_SHAPE")
        key=(row["SchemaName"],row["TableName"],row["ColumnName"])
        if any(not isinstance(x,str) or not x for x in key) or key in index:
            raise Stop("COLUMN_SHAPE")
        index[key]=row
    expected_map={(r["SchemaName"],r["TableName"],r["ColumnName"]):r for r in expected}
    observed_scoped={k:v for k,v in index.items() if k[0]=="dbo" and k[1] in column_module.SCOPE}
    if set(observed_scoped)!=set(expected_map):raise Stop("COLUMN_SCOPE")
    for key, row in expected_map.items():
        actual_row=observed_scoped[key]
        for facet in COLUMN_FIELDS[3:]:
            value=actual_row[facet]
            if facet in ("IsNullable","IsIdentity"):
                if type(value) is not bool and value not in (0,1):raise Stop("COLUMN_SHAPE")
                if bool(value)!=row[facet]:raise Stop("COLUMN_DRIFT")
            elif type(value)!=type(row[facet]) or value!=row[facet]:
                raise Stop("COLUMN_DRIFT")

def compare(actual: dict, sources: dict, mode: str, protected_sha: str, expected_columns=None):
    if not isinstance(actual, dict) or actual.get("ContractVersion") != "g1-pre010-private-observed-v1":
        raise Stop("SHAPE")
    if not re.fullmatch(r"[0-9a-f]{40}", protected_sha) or actual.get("SourceSha") != protected_sha:
        raise Stop("SOURCE")
    if mode == "live":
        if actual.get("Provenance") != "READONLY_UAT_CAPTURE" or actual.get("Target") != {
            "ServerName": "sql-fieldvisit-jpe-uat", "DatabaseName": "db-fieldvisit-uat"
        } or actual.get("SchemaVersion") != "1.8.0-010":
            raise Stop("TARGET_PREDECESSOR")
        selected = sources
        if actual.get("ReasonCheck") != "PASS":
            raise Stop("PRE010_REASON")
        if expected_columns is None:raise Stop("COLUMN_REFERENCE")
        check_columns(actual.get("Columns"), expected_columns)

    elif mode == "disposable":
        if actual.get("Provenance") != "DISPOSABLE_SQL" or actual.get("SchemaVersion") != "DISPOSABLE":
            raise Stop("DISPOSABLE_SCOPE")
        selected = {REQUIRED007: sources[REQUIRED007]}
    else:
        raise Stop("MODE")
    rows = actual.get("Triggers")
    if not isinstance(rows, list) or len(rows) > 2048:
        raise Stop("SHAPE")
    seen = {}
    for row in rows:
        if not isinstance(row, dict) or any(k not in row for k in FIELDS):
            raise Stop("SHAPE")
        name = row["TriggerName"]
        if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]*", name):
            raise Stop("SHAPE")
        if name in seen:
            raise Stop("DUPLICATE")
        seen[name] = row
    if not set(selected).issubset(seen):
        raise Stop("REQUIRED_TRIGGER_MISSING")
    if mode == "disposable" and set(seen) != set(selected):
        raise Stop("DISPOSABLE_SCOPE")
    for name, src in selected.items():
        row = seen[name]
        header = source_module.core.TRIGGER_HEADER.search(src["Ddl"])
        if not header:
            raise Stop("SOURCE_SHAPE")
        expected_events = ",".join(sorted(set(x.upper() for x in header.groups()[2:] if x)))
        if row["SchemaName"] != "dbo" or row["TableName"] != header.group(2):
            raise Stop("TRIGGER_PARENT")
        if row["Events"] != expected_events:
            raise Stop("TRIGGER_EVENTS")
        if row["IsDisabled"] not in (False, 0) or row["IsInsteadOfTrigger"] not in (False, 0):
            raise Stop("TRIGGER_STATE")
        hashed = row["DefinitionSha256"]
        if not isinstance(hashed, str) or not HEX64.fullmatch(hashed):
            raise Stop("DEFINITION_VISIBILITY")
        if hashed.lower() != src["Utf16LeSha256"].lower():
            # 007 has separately proven stored-definition equivalence in disposable SQL.
            # Remaining source hashes are exact NVARCHAR definitions, not separately
            # created one-by-one as SQL Server stored objects.
            raise Stop("007_DEFINITION_DRIFT" if name == REQUIRED007 else "TRIGGER_DEFINITION_DRIFT")
    return "PASS"  # source-scoped collector success; release gate is always HOLD

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", type=Path, required=True)
    parser.add_argument("--private-actual", type=Path, required=True)
    parser.add_argument("--protected-sha", required=True)
    parser.add_argument("--mode", choices=("live", "disposable"), required=True)
    args = parser.parse_args()
    try:
        src = source_module.extract(args.candidate_root)
        if not args.private_actual.is_file() or args.private_actual.stat().st_size > 250000:
            raise Stop("PRIVATE_CAPTURE")
        actual = json.loads(args.private_actual.read_text(encoding="utf-8-sig"))
        expected_columns=column_module.derive(args.candidate_root)
        compare(actual, src, args.mode, args.protected_sha, expected_columns)
        # Even perfect catalog equality cannot prove pre-1.5 facets, history, API principal or recovery.
        scope = "21_TRIGGERS" if args.mode == "live" else "007_ONLY"
        print(f"G1Pre010Private=PASS; Mode={args.mode.upper()}; StructuralScope={scope}; SourceDefinitionHashes=21; NewTableColumnFacets=47; PhysicalStoredObjectProof=007_ONLY; Base15=UNKNOWN; History=UNKNOWN; ReleaseGate=HOLD;")
        return 0
    except Stop as e:
        print(f"G1Pre010Private=STOP; Class={e.code}; Details=SUPPRESSED;")
    except Exception:
        print("G1Pre010Private=STOP; Class=INVALID; Details=SUPPRESSED;")
    return 1

if __name__ == "__main__":
    sys.exit(main())
