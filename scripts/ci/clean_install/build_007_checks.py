#!/usr/bin/env python3
"""Build nine complementary source-exact 1800_007 CHECK predicates on an empty SQL2022 DB.

All nine RouteCalculationAttempts checks are supplied by 20_route_reason_check.sql
(including final 009 StopCount>=1 and 010 BackgroundMileageJob).
This separate layer installs the nine other 007 guards exactly once.
This is NONDEPLOY new-baseline DDL, not execution of historical migrations.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

from build_effective_triggers import dynamic_statements
from build_sql_only_tables import RC_SHA, SourceContractError, sha256

RC_SHA = RC_SHA

SOURCE = "database/migrations/1800_007_mileage_google_governance/Up.sql"
TABLES = {
    "GeocodingAttempts": (
        "CK_GeocodingAttempts_Status", "CK_GeocodingAttempts_Result",
    ),
    "MileageGovernanceEvents": ("CK_MileageGovernanceEvents_Type",),
    "MileageCalculations": (
        "CK_MileageCalculations_ApprovedDistanceSource",
        "CK_MileageCalculations_ApprovalEvidence",
    ),
    "VisitTripSnapshots": (
        "CK_VisitTripSnapshots_RouteMode",
        "CK_VisitTripSnapshots_RouteStatus",
        "CK_VisitTripSnapshots_ApprovedDistanceSource",
        "CK_VisitTripSnapshots_ApprovalBasis",
    ),
}
EXPECTED_COUNT = 9
CHECK_HEADER = re.compile(r"\bCONSTRAINT\s+(?P<name>CK_[A-Za-z_0-9]+)\s+CHECK\s*(?=\()", re.I)


def extract_balanced_check(source: str, name: str) -> str:
    """Extract one CHECK(predicate) while preserving nested SQL and literals."""
    hits = [m for m in CHECK_HEADER.finditer(source)
            if m.group("name").lower() == name.lower()]
    if len(hits) != 1:
        raise SourceContractError("CHECK_DEFINITION_AMBIGUOUS:" + name)
    hit = hits[0]
    start = source.find("(", hit.end())
    if start < 0 or source[hit.end():start].strip():
        raise SourceContractError("CHECK_PAREN_MISSING:" + name)
    depth = 0
    quote = False
    i = start
    while i < len(source):
        c = source[i]
        if quote:
            if c == "'":
                if i + 1 < len(source) and source[i + 1] == "'":
                    i += 1
                else:
                    quote = False
        elif c == "'":
            quote = True
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return source[hit.start():i + 1].strip()
            if depth < 0:
                raise SourceContractError("CHECK_PAREN_UNBALANCED:" + name)
        i += 1
    raise SourceContractError("CHECK_PAREN_UNTERMINATED:" + name)


def build(root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise SourceContractError("OUTPUT_MISSING")
    source_path = root / SOURCE
    if not source_path.is_file():
        raise SourceContractError("PINNED_SOURCE_MISSING")
    raw = source_path.read_bytes()
    script = raw.decode("utf-8-sig")
    decoded = list(dynamic_statements(script))
    manifest = []
    chunks = [
        "-- FINAL 007 target check predicates, excluding 009/010 superseded names.\n"
        "-- SQL2022 disposable fresh DB only; no historical version replay.\n"
    ]
    for table, names in TABLES.items():
        for name in names:
            # Migration 007 contains static CREATE TABLE checks plus CHECK
            # constraints inside escaped sp_executesql statements. The latter
            # must be unescaped before constructing new target-state SQL.
            dynamic_hits = [part for part in decoded
                            if re.search(r"\bCONSTRAINT\s+" + re.escape(name) + r"\s+CHECK\b",
                                         part, re.I)]
            if len(dynamic_hits) > 1:
                raise SourceContractError("CHECK_DYNAMIC_DUPLICATE:" + name)
            candidate = dynamic_hits[0] if dynamic_hits else script
            ddl = extract_balanced_check(candidate, name)
            chunks.append(
                f"\n-- {SOURCE} -> dbo.{table}.{name}\n"
                f"IF OBJECT_ID(N'dbo.{name}', N'C') IS NOT NULL\n"
                f"    THROW 56050, N'CLEAN_CHECK_ALREADY_EXISTS', 1;\n"
                f"ALTER TABLE dbo.{table} WITH CHECK ADD {ddl};\n"
                f"ALTER TABLE dbo.{table} CHECK CONSTRAINT {name};\nGO\n"
            )
            manifest.append({
                "name": name, "table": "dbo." + table, "sourcePath": SOURCE,
                "sourceSha256": sha256(raw),
                "definitionSha256": hashlib.sha256(ddl.encode("utf-8")).hexdigest(),
            })
    if len(manifest) != EXPECTED_COUNT or len({c["name"] for c in manifest}) != EXPECTED_COUNT:
        raise SourceContractError("CHECK_EXPECTED_SET")
    if any(c["name"] in ("CK_RouteCalculationAttempts_StopCount",
                          "CK_RouteCalculationAttempts_Reason") for c in manifest):
        raise SourceContractError("CHECK_SUPERSESSION_VIOLATION")
    data = "".join(chunks).encode("utf-8")
    (output / "007_source_checks.sql").write_bytes(data)
    result = {
        "format": "field-visit-v180-source-checks-007-v1",
        "rcSourceSha": RC_SHA,
        "sourcePath": SOURCE,
        "sourceSha256": sha256(raw),
        "checks": manifest,
        "count": len(manifest),
        "supersededBy": {
            "CK_RouteCalculationAttempts_StopCount": "1800_009",
            "CK_RouteCalculationAttempts_Reason": "1800_010",
        },
        "sqlSha256": sha256(data),
        "fullCleanInstall": "HOLD_OTHER_MIGRATION_ONLY_FACETS_AND_APP_RBAC",
    }
    (output / "007_source_checks-manifest.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return result


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--rc-root", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    try:
        sha = subprocess.check_output(
            ["git", "-C", str(a.rc_root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL).strip()
        if sha != RC_SHA:
            raise SourceContractError("SOURCE_RC_MISMATCH")
        m = build(a.rc_root, a.output)
        if m["sqlSha256"] != sha256((a.output / "007_source_checks.sql").read_bytes()):
            raise SourceContractError("GENERATED_SQL_DRIFT")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("SOURCE_007_CHECKS=FAIL_CLOSED")
        return 1
    print("SOURCE_007_CHECKS=PASS; Count=9; RouteChecks=SEPARATE_LAYER")
    print("FULL_CLEAN_INSTALL=HOLD_OTHER_OBJECTS_APP_RBAC")
    return 0


if __name__ == "__main__":
    sys.exit(main())
