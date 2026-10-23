#!/usr/bin/env python3
"""NONDEPLOY provenance guard for the reviewed UAT010 necessary-reference inputs.

This proves only source/hash and frozen-scope integrity. It never treats the
candidate migration contract as an observed UAT010 schema baseline.
"""
import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

EXPECTED_REFERENCE_SHA256 = "f86a42ad88230411b2351a59cd9c1bb40ad2586076631ab249949fce13e91eb7"
EXPECTED_RC = "f29affb3299828f45b1f131321779e8bea2f86a8"
EXPECTED_SCOPE = "CANDIDATE_NECESSARY_CONTRACT_NOT_OBSERVED_UAT"
EXPECTED_COUNTS = {
    "all": {"columns": 86, "checks": 34, "foreignKeys": 19, "indexes": 15},
    "without_019": {"columns": 74, "checks": 29, "foreignKeys": 18, "indexes": 15},
}
EXPECTED_CASES = (
    [f"VIS-{i:02d}" for i in range(1, 7)]
    + [f"LEAD-{i:02d}" for i in range(1, 7)]
    + [f"ADMIN-{i:02d}" for i in range(1, 5)]
    + [f"XLS-{i:02d}" for i in range(1, 6)]
    + [f"TRIP-{i:02d}" for i in range(1, 3)]
    + [f"SEC-{i:02d}" for i in range(1, 3)]
)
CASE_PATH = "docs/release/SCHEME_A_BUSINESS_UAT_ROLE_CASES_NONDEPLOY.md"
APP_MODEL_PATH = "backend/src/FieldVisit.Infrastructure/AppDbContext.cs"


class Stop(Exception):
    def __init__(self, code):
        self.code = code


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def load_reference(root):
    path = root / "docs/release/g1-minimal-preflight/necessary-reference.json"
    raw = path.read_bytes()
    if sha256(raw) != EXPECTED_REFERENCE_SHA256:
        raise Stop("REFERENCE_PIN")
    try:
        reference = json.loads(raw)
    except Exception:
        raise Stop("REFERENCE_SHAPE")
    if reference.get("rc") != EXPECTED_RC or reference.get("scope") != EXPECTED_SCOPE:
        raise Stop("REFERENCE_SCOPE")
    inputs = reference.get("inputs")
    if not isinstance(inputs, dict) or len(inputs) != 22:
        raise Stop("SOURCE_MANIFEST")
    required = {
        CASE_PATH,
        APP_MODEL_PATH,
        "database/015_schema_v1_5_verify.sql",
        *[p for p in inputs if re.fullmatch(r"database/migrations/1800_\d{3}_[^/]+/Up\.sql", p)
          and p.split("/")[2].startswith(tuple(f"1800_{n:03d}_" for n in range(1, 11)))]
    }
    if not {CASE_PATH, APP_MODEL_PATH, "database/015_schema_v1_5_verify.sql"}.issubset(inputs):
        raise Stop("SOURCE_SCOPE")
    expected_migrations = {
        f"database/migrations/{path.name}/Up.sql"
        for path in (root / "database/migrations").glob("1800_0??_*/Up.sql")
        if path.name == "Up.sql" and path.parent.name[:9] in
           {f"1800_{n:03d}" for n in range(1, 11)}
    }
    pinned_migrations = {
        p for p in inputs
        if re.fullmatch(r"database/migrations/1800_0(?:0[1-9]|10)_[^/]+/Up\.sql", p)
    }
    if len(pinned_migrations) != 10:
        raise Stop("MIGRATION_SCOPE")
    for rel, expected_hash in inputs.items():
        if rel.startswith("/") or ".." in Path(rel).parts or not re.fullmatch(r"[0-9a-f]{64}", str(expected_hash)):
            raise Stop("SOURCE_MANIFEST")
        try:
            actual = sha256((root / rel).read_bytes())
        except Exception:
            raise Stop("SOURCE_MISSING")
        if actual != expected_hash:
            raise Stop("SOURCE_HASH")
    return reference


def check_scope(root, reference):
    for key, expected in EXPECTED_COUNTS["all"].items():
        values = reference.get(key)
        if not isinstance(values, list) or len(values) != expected:
            raise Stop("CANDIDATE_COUNTS")
    without_019 = {
        key: sum(1 for item in reference[key] if item.get("migration") != "019")
        for key in EXPECTED_COUNTS["all"]
    }
    if without_019 != EXPECTED_COUNTS["without_019"]:
        raise Stop("NECESSARY_COUNTS")
    case_text = (root / CASE_PATH).read_text(encoding="utf-8")
    case_table = case_text.split("## Prerequisites", 1)[0]
    case_ids = re.findall(r"^\|\s*((?:VIS|LEAD|ADMIN|XLS|TRIP|SEC)-\d{2})\s*\|", case_table, re.M)
    if case_ids != EXPECTED_CASES:
        raise Stop("FROZEN_CASE_SCOPE")
    merge_path = next((p for p in reference["inputs"] if "/1800_019/" in p), None)
    if not merge_path:
        raise Stop("MIGRATION_019_SOURCE")
    merge_sql = (root / merge_path).read_text(encoding="utf-8")
    if not re.search(r"CREATE\s+TABLE\s+dbo\.LocationMergeDetails\b", merge_sql, re.I):
        raise Stop("MIGRATION_019_SCOPE")
    if re.search(r"^\|\s*(?:VIS|LEAD|ADMIN|XLS|TRIP|SEC)-\d{2}\s*\|[^\n]*\bmerge\b", case_table, re.I | re.M):
        raise Stop("FROZEN_CASE_SCOPE")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", required=True, type=Path)
    args = parser.parse_args()
    try:
        root = args.candidate_root.resolve(strict=True)
        reference = load_reference(root)
        check_scope(root, reference)
    except Stop as exc:
        print(f"SourceContract=STOP; Class={exc.code}; Details=SUPPRESSED;")
        return 1
    except Exception:
        print("SourceContract=STOP; Class=INVALID; Details=SUPPRESSED;")
        return 1
    print("SourcePins=PASS; NecessaryCounts=PASS; FrozenCases=25; Migration019=NOT_REQUIRED_FOR_FROZEN_SCOPE;")
    print("Reference=CANDIDATE_ONLY; ObservedUAT010=UNKNOWN; TriggerParity=UNKNOWN; ReleaseGate=HOLD;")
    return 0


if __name__ == "__main__":
    sys.exit(main())
