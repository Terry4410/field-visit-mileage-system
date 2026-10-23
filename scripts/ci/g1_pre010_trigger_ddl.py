#!/usr/bin/env python3
"""Private source-SQL extraction only. No network, Azure, SQL or raw stdout."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import os
import re
import sys
from pathlib import Path

CORE = Path(__file__).with_name("g1_pre010_source_contract.py")
spec = importlib.util.spec_from_file_location("g1_source", CORE)
core = importlib.util.module_from_spec(spec)
spec.loader.exec_module(core)
DYNAMIC = re.compile(r"\bEXEC\s+sys\.sp_executesql\s+N'", re.I)
TRIGGER_DDL = re.compile(r"^\s*CREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\b", re.I)

class Stop(Exception):
    def __init__(self, code):
        self.code = code

def dynamic_statements(sql: str):
    for match in DYNAMIC.finditer(sql):
        pos = match.end()
        buf = []
        closed = False
        while pos < len(sql):
            char = sql[pos]
            if char == "'":
                if pos + 1 < len(sql) and sql[pos+1] == "'":
                    buf.append("'")
                    pos += 2
                    continue
                closed = True
                break
            buf.append(char)
            pos += 1
        if not closed:
            raise Stop("SOURCE_SQL_QUOTING")
        yield "".join(buf)

def extract(root: Path):
    contract = core.derive(root)
    source = {}
    for mid, entry in contract["Migrations"].items():
        data = (root / entry["Path"]).read_bytes()
        if hashlib.sha256(data).hexdigest() != entry["Sha256"]:
            raise Stop("MIGRATION_PIN")
        for statement in dynamic_statements(data.decode("utf-8-sig")):
            if not TRIGGER_DDL.match(statement):
                continue
            match = core.TRIGGER_HEADER.search(statement)
            if not match or len(core.TRIGGER_NAME.findall(statement)) != 1:
                raise Stop("TRIGGER_SQL_SHAPE")
            name = match.group(1)
            if name in source and name != "TR_MileageRateRules_ProtectSeries":
                raise Stop("TRIGGER_DUPLICATE")
            source[name] = {
                "Name": name,
                "SourceMigration": mid,
                "Ddl": statement,
                "Utf16LeSha256": hashlib.sha256(statement.encode("utf-16le")).hexdigest(),
                "SqlServerProof": "UNKNOWN_UNTIL_DISPOSABLE_SQL",
            }
    expected = {t["Name"]: t for t in contract["Triggers"]}
    if set(source) != set(expected):
        raise Stop("TRIGGER_COMPLETENESS")
    for name, item in source.items():
        if item["SourceMigration"] != expected[name]["SourceMigration"]:
            raise Stop("TRIGGER_SUPERSESSION")
        if not core.TRIGGER_HEADER.search(item["Ddl"]):
            raise Stop("TRIGGER_SQL_SHAPE")
    return source

def save_private(path: Path, ddl: str):
    if not path.is_absolute() or path.exists():
        raise Stop("PRIVATE_OUTPUT")
    temp_root = os.environ.get("RUNNER_TEMP")
    if os.environ.get("GITHUB_ACTIONS") == "true":
        if not temp_root or path.resolve().parent != Path(temp_root).resolve():
            raise Stop("PRIVATE_BOUNDARY")
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    handle = os.open(str(path), flags, 0o600)
    with os.fdopen(handle, "w", encoding="utf-8", newline="") as out:
        out.write(ddl)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", type=Path, required=True)
    parser.add_argument("--selected-trigger", required=True)
    parser.add_argument("--private-output", type=Path, required=True)
    args = parser.parse_args()
    try:
        source = extract(args.candidate_root)
        if args.selected_trigger != "TR_Locations_SelectedGeocodingAttempt":
            raise Stop("TEST_TRIGGER_SCOPE")
        save_private(args.private_output, source[args.selected_trigger]["Ddl"])
        print("G1TriggerSource=PASS; EffectiveTriggers=21; PrivateDdl=CREATED; ActualUat=UNKNOWN;")
        return 0
    except Stop as e:
        print(f"G1TriggerSource=STOP; Class={e.code}; Details=SUPPRESSED;")
    except Exception:
        print("G1TriggerSource=STOP; Class=INVALID; Details=SUPPRESSED;")
    return 1

if __name__ == "__main__":
    sys.exit(main())
