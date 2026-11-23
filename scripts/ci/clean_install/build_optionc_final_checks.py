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
import hashlib
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
# Immutable RC EF model creates eight predecessor checks; 014 candidate creates one.
# Only these specific, reviewable definitions can be replaced on the empty DB.
REPLACEMENT_NAMES = {
    "CK_B3_ChangeRequests_KnownCodes",
    "CK_B3_ChangeRequests_KnownStatus",
    "CK_B3_ChangeRequests_ExpectedLocationVersion",
    "CK_B3_ChangeRequests_ProposedJson",
    "CK_B3_ChangeRequests_ReviewState",
    "CK_B3_ChangeRequestEvents_EventType",
    "CK_B3_ChangeRequestEvents_DetailsJson",
    "CK_B3_ChangeRequestEvents_DecisionState",
    "CK_OptionC_Published_Origin",
}

CANDIDATE_SOURCE_IDS = {"011", "012", "016", "017", "018", "020"}

# The immutable clean-install RC predates the already-reviewed Confirming fix.
# Reuse EXACTLY the corrected 020 source already in the integrated PR #83 and
# selected UAT API source; never edit, replay, or re-mark its original migration.
LEGACY_020_BLOB = "7ccd3aadb8b1d2c1ffd0a7c88b0c0497df8ab6f2"
CORRECTED_020_BLOB = "ce7bbb6f204bf1ab69c106ae59a5b139077494bc"
STATUS_020_OLD = b"N'Previewed',N'Confirmed',N'PartiallyFailed',"
STATUS_020_NEW = b"N'Previewed',N'Confirming',N'Confirmed',N'PartiallyFailed',"


def _git_blob_sha1(raw: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\\0" + raw).hexdigest()


def load_pinned_corrected_020(rc_root: Path) -> tuple[str, bytes, str]:
    rel = f"{ROOT}/1800_020/Up.sql"
    accepted = (rc_root / rel).read_bytes()
    if _git_blob_sha1(accepted) != LEGACY_020_BLOB:
        raise SourceContractError("IMMUTABLE_020_RC_SOURCE_DRIFT")
    # File is checked into the nonprotected engineering branch, NOT the RC root.
    corrected = (Path(__file__).resolve().parents[3] / rel).read_bytes()
    if (_git_blob_sha1(corrected) != CORRECTED_020_BLOB
            or accepted.count(STATUS_020_OLD) != 1
            or corrected != accepted.replace(STATUS_020_OLD, STATUS_020_NEW)):
        raise SourceContractError("CORRECTED_020_SOURCE_NOT_EXACT_APPROVED_DELTA")
    return rel, corrected, corrected.decode("utf-8-sig")

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
    # Only the final target 020 CHECK is sourced from the already-corrected
    # pinned SQL; other clean-install sources remain immutable RC inputs.
    source_files["020"] = load_pinned_corrected_020(rc_root)

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
            if name in REPLACEMENT_NAMES:
                precheck = (
                    f"IF NOT EXISTS (SELECT 1 FROM sys.check_constraints "
                    f"WHERE name=N'{name}' "
                    f"AND parent_object_id=OBJECT_ID(N'dbo.{table}',N'U') "
                    f"AND is_disabled=0 AND is_not_trusted=0)\n"
                    f"    THROW 56090, N'CLEAN_OPTIONC_EXPECTED_PREDECESSOR_MISSING', 1;\n"
                    f"ALTER TABLE dbo.{table} DROP CONSTRAINT {name};\n"
                )
            else:
                precheck = (
                    f"IF OBJECT_ID(N'dbo.{name}', N'C') IS NOT NULL\n"
                    f"    THROW 56091, N'CLEAN_OPTIONC_UNEXPECTED_EXISTING_CHECK', 1;\n"
                )
            chunks.append(
                f"\n-- FINAL dbo.{table}.{name} from {rel}\n"
                + precheck
                + f"ALTER TABLE dbo.{table} WITH CHECK ADD {definition};\n"
                + f"ALTER TABLE dbo.{table} CHECK CONSTRAINT {name};\nGO\n"
            )
            entries.append({
                "name": name, "table": f"dbo.{table}", "sourceVersion": mid,
                "sourcePath": rel, "sourceSha256": sha256(raw),
                "definitionSha256": sha256(definition.encode("utf-8")),
                "replacesReviewedPredecessor": name in REPLACEMENT_NAMES,
            })

    if not REPLACEMENT_NAMES.issubset({e["name"] for e in entries}):
        raise SourceContractError("FINAL_SOURCE_REPLACEMENT_SCOPE")
    if (len(entries) != COUNT or
        len({entry["name"] for entry in entries}) != COUNT):
        raise SourceContractError("OPTIONC_CHECK_SET_INVALID")
    body = "".join(chunks).encode("utf-8")
    (output / OUT_SQL).write_bytes(body)
    manifest = {
        "format": "field-visit-v180-optionc-effective-checks-v1",
        "rcSourceSha": RC_SHA,
        "effective020ApprovedGitBlob": CORRECTED_020_BLOB,
        "effective020SourceMode": "PINNED_EXISTING_CORRECTED_SOURCE_NO_HISTORIC_MIGRATION",
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
