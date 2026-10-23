#!/usr/bin/env python3
"""Build final effective 011-020 CHECK predicates on disposable SQL Server 2022.

Only explicitly source-pinned A+C rules are in scope. Earlier superseded
definitions (011, 012, 015, 016, 017, 018) are NEVER replayed as migrations.
All selected SQL is emitted for clean disposable DB only; no markers/privileges.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from build_007_checks import extract_balanced_check
from build_sql_only_tables import RC_SHA, SourceContractError, sha256

ROOT = "docs/release/optionc-canonical-sql-draft/candidate"
# target table -> final effective CHECK name -> one canonical immutable source.
CONTRACT = {
    "ChangeRequests": {
        "CK_B3_ChangeRequests_KnownCodes": "017",
        "CK_B3_ChangeRequests_KnownStatus": "012",
        "CK_B3_ChangeRequests_ExpectedLocationVersion": "011",
        "CK_B3_ChangeRequests_ProposedJson": "011",
        "CK_B3_ChangeRequests_ReviewState": "018",
        "CK_OptionC_CreateLowRisk_BeforeImage": "017",
    },
    "ChangeRequestEvents": {
        "CK_B3_ChangeRequestEvents_EventType": "018",
        "CK_B3_ChangeRequestEvents_DetailsJson": "011",
        "CK_B3_ChangeRequestEvents_DecisionState": "018",
    },
    "ChangeRequestLeaderReviews": {
        "CK_OptionC_LeaderReview_Stage": "016",
        "CK_OptionC_LeaderReview_Status": "016",
        "CK_OptionC_LeaderReview_DecisionState": "016",
    },
    "LocationPublishedVersions": {
        "CK_OptionC_Published_Origin": "017",
    },
    "ImportBatches": {"CK_ImportBatches_Status": "020"},
    "ImportBatchItems": {"CK_ImportBatchItems_Status": "020"},
}
COUNT = 15
CANDIDATE_SOURCE_IDS = {"011", "012", "016", "017", "018", "020"}
OUT_SQL = "optionc-final-target-checks.sql"
OUT_JSON = "optionc-final-target-checks-manifest.json"


def build(rc_root: Path, output: Path) -> dict:
    if not output.is_dir():
        raise SourceContractError("OUTPUT_NOT_DIRECTORY")
    source_files = {}
    for mid in sorted(CANDIDATE_SOURCE_IDS):
        path = f"{ROOT}/1800_{mid}/Up.sql"
        raw = (rc_root / path).read_bytes()
        source_files[mid] = (path, raw, raw.decode("utf-8-sig"))

    entries = []
    chunks = [
        "-- A+C source-derived final target CHECKs; DISPOSABLE SQL2022 ONLY.\n"
        "-- Historical migrations 011..020 are NOT run. No SchemaVersion backfill.\n"
        "-- Sources include NONEXECUTABLE DESIGN contracts; this is not live DDL.\n"
    ]
    for table, named in CONTRACT.items():
        for name, mid in named.items():
            rel, raw, source = source_files[mid]
            definition = extract_balanced_check(source, name)
            # Do not install any source definition that references an earlier
            # constraint name; the selected DDL must be a single CHECK clause.
            if not re.match(r"(?is)^CONSTRAINT\s+" + re.escape(name) + r"\s+CHECK\s*\(", definition):
                raise SourceContractError("CHECK_SOURCE_SHAPE:" + name)
            # 014's source-pinned candidate table carries its older Origin
            # CHECK inline; 017's explicitly reviewed definition supersedes it.
            # No other constraint may be silently replaced.
            if name == "CK_OptionC_Published_Origin":
                chunks.append(
                    "IF OBJECT_ID(N'dbo.CK_OptionC_Published_Origin', N'C') IS NULL\n"
                    "    THROW 56091, N'CLEAN_OPTIONC_014_PREDECESSOR_ORIGIN_MISSING', 1;\n"
                    "ALTER TABLE dbo.LocationPublishedVersions "
                    "DROP CONSTRAINT CK_OptionC_Published_Origin;\nGO\n"
                )
            chunks.append(
                f"\n-- FINAL dbo.{table}.{name} from {rel}\n"
                f"IF OBJECT_ID(N'dbo.{name}', N'C') IS NOT NULL\n"
                f"    THROW 56090, N'CLEAN_OPTIONC_FINAL_CHECK_ALREADY_EXISTS', 1;\n"
                f"ALTER TABLE dbo.{table} WITH CHECK ADD {definition};\n"
                f"ALTER TABLE dbo.{table} CHECK CONSTRAINT {name};\nGO\n"
            )
            entries.append({
                "name": name, "table": f"dbo.{table}", "sourceVersion": mid,
                "sourcePath": rel, "sourceSha256": sha256(raw),
                "definitionSha256": sha256(definition.encode("utf-8")),
            })

    if (len(entries) != COUNT or
        len({entry["name"] for entry in entries}) != COUNT):
        raise SourceContractError("OPTIONC_CHECK_SET_INVALID")
    body = "".join(chunks).encode("utf-8")
    (output / OUT_SQL).write_bytes(body)
    manifest = {
        "format": "field-visit-v180-optionc-effective-checks-v1",
        "rcSourceSha": RC_SHA,
        "checkCount": COUNT,
        "checks": entries,
        "sqlSha256": sha256(body),
        "scope": "DISPOSABLE_SOURCE_DERIVED_NOT_APPROVED_IT_FINAL_INSTALL",
        "fullCleanInstall": "HOLD_OTHER_SOURCE_FACETS_AND_API_RBAC",
    }
    (output / OUT_JSON).write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        actual_sha = subprocess.check_output(
            ["git", "-C", str(args.rc_root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL
        ).strip()
        if actual_sha != RC_SHA:
            raise SourceContractError("RC_PIN_MISMATCH")
        m = build(args.rc_root, args.output)
        if sha256((args.output / OUT_SQL).read_bytes()) != m["sqlSha256"]:
            raise SourceContractError("SQL_OUTPUT_HASH_DRIFT")
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        print("OPTIONC_FINAL_CHECK_SOURCE=FAIL_CLOSED")
        return 1
    print("OPTIONC_FINAL_CHECK_SOURCE=PASS; Count=15; Supersession=FINAL_ONLY")
    print("FULL_CLEAN_INSTALL=HOLD_OTHER_SOURCE_FACETS_AND_API_RBAC")
    return 0


if __name__ == "__main__":
    sys.exit(main())
