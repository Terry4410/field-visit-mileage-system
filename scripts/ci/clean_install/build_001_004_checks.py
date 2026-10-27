#!/usr/bin/env python3
"""Source-derived v1.8.0 001–004 CHECK guards (NONDEPLOY disposable SQL2022).

These 17 CHECK predicates are absent from the EF-only CREATE model and protect
organization/team periods, employment/leader periods, deployments and Locations.
They are *not* a substitute for complete 001–020 target-schema fidelity.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import re
from pathlib import Path

from build_007_checks import extract_balanced_check
from build_effective_triggers import dynamic_statements
from build_sql_only_tables import RC_SHA, sha256, SourceContractError

SOURCES = {
    "database/migrations/1800_001_organization_center_team_lifecycle/Up.sql": {
        "Teams": ["CK_Teams_EffectiveDates"],
        "Centers": ["CK_Centers_EffectiveDates"],
        "TeamCenterAssignments": ["CK_TeamCenterAssignments_Dates"],
    },
    "database/migrations/1800_002_person_employment_role_membership/Up.sql": {
        "Employments": ["CK_Employments_Dates"],
        "EmploymentStatusPeriods": ["CK_EmploymentStatusPeriods_Status",
                                    "CK_EmploymentStatusPeriods_Dates"],
        "EmploymentRoleAssignments": ["CK_EmploymentRoleAssignments_Dates"],
        "TeamMemberships": ["CK_TeamMemberships_Dates"],
        "TeamLeaderAssignments": ["CK_TeamLeaderAssignments_Dates"],
        "TeamLeaderDelegations": ["CK_TeamLeaderDelegations_Dates"],
    },
    "database/migrations/1800_003_deployment_sites/Up.sql": {
        "DeploymentSites": ["CK_DeploymentSites_Dates"],
        "DeploymentSiteLocationAssignments": [
            "CK_DeploymentSiteLocationAssignments_Dates"],
        "TeamDeploymentSiteAssignments": [
            "CK_TeamDeploymentSiteAssignments_Dates"],
        "EmploymentDeploymentSiteAssignments": [
            "CK_EmploymentDeploymentSiteAssignments_Dates"],
    },
    "database/migrations/1800_004_location_governance/Up.sql": {
        "Locations": ["CK_Locations_DuplicateReference"],
        "TeamLocationNotes": ["CK_TeamLocationNotes_NotBlank"],
        "TeamLocationNoteHistory": ["CK_TeamLocationNoteHistory_Action"],
    },
}
COUNT = 17


def build(rc_root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise SourceContractError("OUTPUT_MISSING")
    entries = []
    chunks = [
        "-- Disposable source-derived 1800_001-004 target CHECKs.\n"
        "-- Historical migration markers are NEVER inserted.\n"
    ]
    for relative, tables in SOURCES.items():
        path = rc_root / relative
        if not path.is_file():
            raise SourceContractError("PINNED_SOURCE_MISSING")
        raw = path.read_bytes()
        script = raw.decode("utf-8-sig")
        dynamic = list(dynamic_statements(script))
        for table, checks in tables.items():
            for name in checks:
                decoded_matches = [
                    part for part in dynamic
                    if re.search(r"\bCONSTRAINT\s+" + re.escape(name)
                                 + r"\s+CHECK\b", part, re.I)
                ]
                if len(decoded_matches) > 1:
                    raise SourceContractError("CHECK_MULTIPLE_DYNAMIC_SOURCES")
                ddl = extract_balanced_check(
                    decoded_matches[0] if decoded_matches else script, name)
                chunks.append(
                    f"\n-- {relative} -> dbo.{table}.{name}\n"
                    f"IF OBJECT_ID(N'dbo.{name}', N'C') IS NOT NULL\n"
                    f"    THROW 56070, N'CLEAN_001_004_CHECK_ALREADY_EXISTS', 1;\n"
                    f"ALTER TABLE dbo.{table} WITH CHECK ADD {ddl};\n"
                    f"ALTER TABLE dbo.{table} CHECK CONSTRAINT {name};\nGO\n"
                )
                entries.append({
                    "name": name, "table": "dbo." + table,
                    "sourcePath": relative, "sourceSha256": sha256(raw),
                })
    if len(entries) != COUNT or len({x["name"] for x in entries}) != COUNT:
        raise SourceContractError("CHECK_SOURCE_SET_INCOMPLETE")
    sql = "".join(chunks).encode("utf-8")
    (output / "001_004_source_checks.sql").write_bytes(sql)
    result = {
        "format": "field-visit-v180-source-checks-001-004-v1",
        "rcSourceSha": RC_SHA,
        "count": COUNT,
        "checks": entries,
        "sqlSha256": sha256(sql),
        "fullCleanInstall": "HOLD_ADDITIONAL_SOURCE_DDL_AND_APP_RBAC",
    }
    (output / "001_004_source_checks-manifest.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        actual = subprocess.check_output(
            ["git", "-C", str(args.rc_root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL).strip()
        if actual != RC_SHA:
            raise SourceContractError("RC_PIN_MISMATCH")
        obj = build(args.rc_root, args.output)
        if obj["sqlSha256"] != sha256((args.output / "001_004_source_checks.sql").read_bytes()):
            raise SourceContractError("SQL_OUTPUT_DRIFT")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("SOURCE_001_004_CHECKS=FAIL_CLOSED")
        return 1
    print("SOURCE_001_004_CHECKS=PASS; Count=17")
    print("FULL_CLEAN_INSTALL=HOLD_OTHER_OBJECTS_APP_RBAC")
    return 0


if __name__ == "__main__":
    sys.exit(main())
