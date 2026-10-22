#!/usr/bin/env python3
"""NONDEPLOY. Private source-bound UAT010 comparator: sanitized status codes only."""
import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

CONTRACT = "g1-uat010-source-parity-v2"
SECTIONS = {
    "Columns": ("SchemaName", "TableName", "Ordinal", "ColumnName"),
    "Indexes": ("SchemaName", "TableName", "IndexName", "IsIncluded", "KeyOrdinal", "ColumnName"),
    "ForeignKeys": ("SchemaName", "TableName", "ForeignKeyName", "ColumnOrdinal", "ParentColumn"),
    "Checks": ("SchemaName", "TableName", "CheckConstraintName"),
    "Triggers": ("SchemaName", "TableName", "TriggerName"),
}
TRIGGER_RE = re.compile(r"\bCREATE\s+(?:OR\s+ALTER\s+)?TRIGGER\s+(?:\[?dbo\]?\.)?\[?([A-Za-z_][A-Za-z_0-9]*)\]?", re.I)
HEX40 = re.compile("^[0-9a-f]{40}$")
HEX64 = re.compile("^[0-9a-f]{64}$")
TARGET = ("sql-fieldvisit-jpe-uat", "db-fieldvisit-uat")

class Hold(Exception):
    def __init__(self, code):
        self.code = code

def digest(b):
    return hashlib.sha256(b).hexdigest()

def check_shape(doc):
    if not isinstance(doc, dict) or doc.get("ContractVersion") != CONTRACT:
        raise Hold("SHAPE")
    sv = doc.get("SchemaVersion")
    if not isinstance(sv, dict) or sv.get("Latest") != "1.8.0-010":
        raise Hold("PREDECESSOR")
    for section, keys in SECTIONS.items():
        rows = doc.get(section)
        if not isinstance(rows, list) or not rows or len(rows) > 20000:
            raise Hold("SHAPE")
        seen = set()
        for r in rows:
            if not isinstance(r, dict) or any(k not in r for k in keys):
                raise Hold("SHAPE")
            identity = tuple(r[k] for k in keys)
            if any(type(k) not in (str, int) or k == "" for k in identity):
                raise Hold("SHAPE")
            if identity in seen:
                raise Hold("SHAPE")
            seen.add(identity)
            if section == "Triggers":
                h = r.get("DefinitionSha256")
                if not isinstance(h, str) or not HEX64.fullmatch(h.lower()):
                    raise Hold("TRIGGER_VISIBILITY")
                if r.get("IsDisabled") not in (False, 0) or r.get("IsInsteadOfTrigger") not in (False, 0):
                    raise Hold("TRIGGER_STATE")
                if not isinstance(r.get("Events"), str) or not r["Events"]:
                    raise Hold("TRIGGER_STATE")

def check_reference(root, expected):
    provenance = expected.get("Provenance")
    if not isinstance(provenance, dict) or provenance.get("Kind") != "INDEPENDENT_APPROVED_150_TO_010_REPLAY":
        raise Hold("REFERENCE")
    base = provenance.get("BaseArchive")
    if not isinstance(base, dict) or base.get("Approved") is not True or not HEX64.fullmatch(str(base.get("Sha256", ""))):
        raise Hold("REFERENCE")
    files = provenance.get("SourceFiles")
    if not isinstance(files, dict) or len(files) != 10:
        raise Hold("SOURCE")
    expected_triggers = set()
    seen = set()
    for n in range(1, 11):
        matches = list((root / "database/migrations").glob(f"1800_{n:03d}_*/Up.sql"))
        if len(matches) != 1:
            raise Hold("SOURCE")
        p = matches[0]
        rel = p.relative_to(root).as_posix()
        if rel not in files or not HEX64.fullmatch(str(files[rel])):
            raise Hold("SOURCE")
        raw = p.read_bytes()
        if digest(raw) != files[rel]:
            raise Hold("SOURCE")
        seen.add(rel)
        expected_triggers.update(m.group(1) for m in TRIGGER_RE.finditer(raw.decode("utf-8")))
    if seen != set(files) or not expected_triggers:
        raise Hold("SOURCE")
    if not expected_triggers.issubset({r["TriggerName"] for r in expected["Triggers"]}):
        raise Hold("REFERENCE")

def check(args):
    if not HEX40.fullmatch(args.protected_sha) or not HEX64.fullmatch(args.reference_sha256):
        raise Hold("PIN")
    original = args.reference.read_bytes()
    if digest(original) != args.reference_sha256:
        raise Hold("PIN")
    expected = json.loads(original)
    actual = json.loads(args.actual.read_bytes())
    check_shape(expected)
    check_shape(actual)
    if actual.get("SourceSha") != args.protected_sha:
        raise Hold("PIN")
    target = actual.get("Target")
    if not isinstance(target, dict) or (target.get("ServerName"), target.get("DatabaseName")) != TARGET:
        raise Hold("TARGET")
    provenance = actual.get("Provenance")
    if not isinstance(provenance, dict) or provenance.get("Kind") != "READONLY_UAT_CAPTURE":
        raise Hold("CAPTURE")
    check_reference(args.repo_root.resolve(), expected)
    for section, keys in SECTIONS.items():
        a = {tuple(x[k] for k in keys): x for x in actual[section]}
        e = {tuple(x[k] for k in keys): x for x in expected[section]}
        if a != e:
            raise Hold("METADATA_DRIFT")
    return "PASS"

def main():
    parser = argparse.ArgumentParser(description="Offline bounded UAT010 fidelity check")
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--actual", required=True, type=Path)
    parser.add_argument("--reference-sha256", required=True)
    parser.add_argument("--protected-sha", required=True)
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    args = parser.parse_args()
    try:
        result = check(args)
    except Hold as e:
        result = e.code
    except Exception:
        result = "INVALID"
    if result == "PASS":
        print("G1Evidence=PASS; Scope=SOURCE_REPLAY_METADATA; LiveAuthorization=NONE;")
        return 0
    print(f"G1Evidence=HOLD; Class={result}; Details=SUPPRESSED;")
    return 1

if __name__ == "__main__":
    sys.exit(main())
