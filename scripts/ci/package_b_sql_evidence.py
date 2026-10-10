#!/usr/bin/env python3
"""Validate real SQL Server disposable-fixture TRX. Never authorize UAT GO."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

# Reviewed immutable suite manifest; changes require explicit code review.
SQL_TEST_PREFIX="FieldVisit.B3.SqlServer.Tests.B3IsolatedSqlServerFixtureTests."
REQUIRED_SQL_TESTS=frozenset({
    "Candidate_011_catalog_probe_executes_and_accepts_intact_schema",
    "Two_concurrent_pending_inserts_yield_one_winner_and_unique_conflict",
    "Same_decision_key_across_concurrent_reviews_never_creates_two_events",
    "Stale_rowversion_cannot_update_request",
    "Trusted_audit_FKs_prevent_deleting_request_or_user_with_history",
    "Candidate_CHECK_rejects_unapproved_states_and_malformed_JSON",
    "Catalog_denies_disabled_unique_index_and_enabled_table_trigger",
    "Live_latest_version_is_denied_if_an_older_schema_row_is_newer",
    "Real_sql_constraints_block_self_review_null_reason_and_premature_review_fields",
    "Real_sql_rejects_invalid_audit_types_missing_actor_and_broken_json",
    "Serializable_transaction_rolls_back_request_if_audit_insert_is_invalid",
    "Untrusted_foreign_key_fails_catalog_until_it_is_retrusted",
    "Valid_rejection_reopens_pending_slot_but_preserves_decision_history",
    "Competing_serializable_reviewers_on_same_rowversion_commit_one_audit_event",
    "Request_public_identity_cannot_be_reused_for_a_different_location",
    "Pending_unique_key_partitions_identical_entity_ids_by_organization",
    "Catalog_rejects_untrusted_check_until_constraint_is_retrusted",
    "Failed_rejection_audit_rolls_back_status_rowversion_and_preserves_prior_history",
    "Replayed_decision_key_rolls_back_second_request_and_keeps_first_audit",
    "Concurrent_submission_transactions_commit_exactly_one_submitted_event",
    "Catalog_denies_altered_decision_filter_until_exact_index_is_restored",
    "Catalog_denies_column_width_drift_until_exact_type_is_restored",
})

def validate(path: Path, sha: str | None = None) -> dict:
    if not path.is_file():
        raise ValueError("SQL Server test TRX missing")
    root = ET.parse(path).getroot()
    counters = root.find(".//{*}Counters")
    if counters is None:
        raise ValueError("SQL Server test TRX counters missing")
    names = ("total", "passed", "failed", "error", "notExecuted")
    if any(name not in counters.attrib for name in names):
        raise ValueError("SQL Server test TRX counters incomplete")
    results = {k: int(counters.attrib[k]) for k in names}
    if (results["total"] != len(REQUIRED_SQL_TESTS) or results["passed"] != results["total"]
            or any(results[k] for k in ("failed","error","notExecuted"))):
        raise ValueError("SQL Server tests missing, skipped or failed: " + str(results))
    cases = root.findall(".//{*}UnitTestResult")
    if len(cases) != results["total"]:
        raise ValueError("SQL Server TRX per-test count mismatch")
    if any(c.get("outcome") != "Passed" for c in cases):
        raise ValueError("SQL Server TRX includes a non-passing test")
    actual_names=[case.get("testName") for case in cases]
    required_names={SQL_TEST_PREFIX+name for name in REQUIRED_SQL_TESTS}
    if set(actual_names)!=required_names or len(set(actual_names))!=len(actual_names):
        raise ValueError("SQL Server mandatory named test cases missing or duplicated")
    for attr in ("executionId","testId"):
        identifiers=[case.get(attr) for case in cases]
        if any(not value for value in identifiers) or len(set(identifiers))!=len(identifiers):
            raise ValueError("SQL Server missing or duplicated "+attr)
    if sha is not None and not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("invalid GitHub source SHA")
    return {
        "kind": "B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE",
        "status": "PASS",
        "source_sha": sha,
        "tests_passed": results["passed"],
        "tests_total": results["total"],
        "verified_case_names": sorted(REQUIRED_SQL_TESTS),
        "scope": "DISPOSABLE_LOCALHOST_SQL_SERVER_2022",
        "full_22_case_b4_sql_runtime": "NOT_COMPLETE",
        "formal_schema_migration": "NOT_EXECUTED",
        "existing_uat_or_production_database": "NOT_TOUCHED",
        "b3_approve_apply": "DENY_ALL",
        "business_uat": "HOLD",
        "production": "HARD_HOLD"
    }

def main() -> int:
    parser=argparse.ArgumentParser()
    parser.add_argument("--trx",required=True)
    parser.add_argument("--report",required=True)
    parser.add_argument("--summary",required=True)
    args=parser.parse_args()
    try:
        result=validate(Path(args.trx),os.getenv("GITHUB_SHA"))
        code=0
    except (OSError,ValueError,ET.ParseError) as exc:
        result={"kind":"B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE",
                "status":"FAIL_CLOSED","source_sha":os.getenv("GITHUB_SHA"),
                "error":str(exc),"business_uat":"HOLD","production":"HARD_HOLD"}
        code=1
    out=Path(args.report)
    md=Path(args.summary)
    out.parent.mkdir(parents=True,exist_ok=True)
    md.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    lines=["### B3/B4 actual disposable SQL Server tests",
           "Status: "+result["status"],
           "Source: "+str(result.get("source_sha")),
           "Cases: "+str(result.get("tests_passed",0))+
              "/"+str(result.get("tests_total",0)),
           "Scope: isolated localhost SQL Server 2022 only.",
           "Formal 011 migration: NOT EXECUTED. Full B4/API UAT: NOT COMPLETE.",
           "Business UAT: HOLD. Production: HARD HOLD."]
    if result.get("error"):
        lines.append("Error: "+result["error"])
    md.write_text("\n".join(lines)+"\n",encoding="utf-8")
    print("\n".join(lines))
    return code

if __name__=="__main__":
    sys.exit(main())
