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
import sys
from pathlib import Path

SOURCE = Path(__file__).with_name("g1_pre010_trigger_ddl.py")
spec = importlib.util.spec_from_file_location("trigger_source", SOURCE)
source_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(source_module)
HEX64 = re.compile(r"^[0-9a-fA-F]{64}$")
REQUIRED007 = "TR_Locations_SelectedGeocodingAttempt"
FIELDS = ("SchemaName", "TableName", "TriggerName", "Events",
          "IsDisabled", "IsInsteadOfTrigger", "DefinitionSha256")

class Stop(Exception):
    def __init__(self, code):
        self.code = code

def compare(actual: dict, sources: dict, mode: str, protected_sha: str):
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
        # Only 007 has independently proven SQL Server stored-definition equality.
        if name == REQUIRED007 and hashed.lower() != src["Utf16LeSha256"].lower():
            raise Stop("007_DEFINITION_DRIFT")
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
        compare(actual, src, args.mode, args.protected_sha)
        # Even perfect catalog equality cannot prove pre-1.5 facets, history, API principal or recovery.
        scope = "21_TRIGGERS" if args.mode == "live" else "007_ONLY"
        print(f"G1Pre010Private=PASS; Mode={args.mode.upper()}; StructuralScope={scope}; DefinitionProof=007_ONLY; BaseFacets=UNKNOWN; History=UNKNOWN; ReleaseGate=HOLD;")
        return 0
    except Stop as e:
        print(f"G1Pre010Private=STOP; Class={e.code}; Details=SUPPRESSED;")
    except Exception:
        print("G1Pre010Private=STOP; Class=INVALID; Details=SUPPRESSED;")
    return 1

if __name__ == "__main__":
    sys.exit(main())
