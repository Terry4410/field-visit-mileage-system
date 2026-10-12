#!/usr/bin/env python3
"""Read-only B4/SQL traceability. SQL fixture PASS never implies B4 GO."""
import argparse
import json
from pathlib import Path
import re
import sys
from package_b_sql_evidence import REQUIRED_SQL_TESTS, SQL_TEST_PREFIX

# Reviewed 22-scenario bridge: SQL links are partial disposable-fixture proof,
# never live HTTP/HR authorization or full scenario acceptance.
LINKS = {
  1: ("011 catalog and trusted schema", ("Candidate_011_catalog_probe_executes_and_accepts_intact_schema", "Untrusted_foreign_key_fails_catalog_until_it_is_retrusted"), "Missing/partial 011 HTTP readiness"),
  2: ("Schema version drift", ("Live_latest_version_is_denied_if_an_older_schema_row_is_newer", "Future_schema_version_is_rejected_until_latest_version_is_restored"), "API fail-closed readiness 503"),
  3: ("Disabled unique indexes", ("Catalog_denies_disabled_unique_index_and_enabled_table_trigger", "Catalog_denies_disabled_decision_unique_index_until_rebuilt"), "API/index mutation after-state"),
  4: ("Column type and CHECK drift", ("Catalog_denies_column_width_drift_until_exact_type_is_restored", "Catalog_rejects_untrusted_check_until_constraint_is_retrusted"), "API schema fail-closed responses"),
  5: ("Concurrent Pending submissions", ("Two_concurrent_pending_inserts_yield_one_winner_and_unique_conflict", "Concurrent_submission_transactions_commit_exactly_one_submitted_event", "Serializable_request_lock_does_not_block_unrelated_pending_request"), "Real authenticated submitter concurrency"),
  6: ("DecisionKey replay", ("Same_decision_key_across_concurrent_reviews_never_creates_two_events", "Replayed_decision_key_rolls_back_second_request_and_keeps_first_audit"), "Authenticated rejection replay HTTP 409"),
  7: ("Stale request and entity RowVersion", ("Stale_rowversion_cannot_update_request", "Ef_SQL_stale_tracked_request_update_fails_concurrency_without_audit"), "HTTP entity/request 409 and after-state"),
  8: ("Peer team location ownership", ("Ef_SQL_Mine_query_hides_peer_and_cross_organization_proposals", "Ef_SQL_peer_and_cross_org_sensitive_before_evidence_json_not_disclosed"), "Peer write/submit denied; SQL links are read only"),
  9: ("Cross-organization request IDOR", ("Ef_SQL_Admin_pending_query_excludes_foreign_org_and_rejected", "Ef_SQL_switching_actor_org_between_queries_never_reuses_previous_tenant_scope"), "Cross-tenant HTTP GUID reads and writes"),
 10: ("Leave/terminated/prehire/expired role", (), "Live persisted HR and API token revocation"),
 11: ("Inactive team and revoked membership", (), "Live effective-dated team grant denial"),
 12: ("External identity forged Admin role", (), "Live identity and role origin API denial"),
 13: ("Requester self-review", ("Real_sql_constraints_block_self_review_null_reason_and_premature_review_fields",), "Dual-role requester live API rejection"),
 14: ("Unattested historical Leader", (), "Independently certified manager grant, then denied API write"),
 15: ("Geocoding pending race", (), "Geocoding callback/API after-state no publish"),
 16: ("Safe delete referenced historical data", ("Trusted_audit_FKs_prevent_deleting_request_or_user_with_history", "Rejected_history_blocks_delete_after_new_pending_is_created"), "Snapshot/note/geocoding/deployment delete paths"),
 17: ("Spoofed teamId shared history", (), "Shared history endpoint tenant/team IDOR"),
 18: ("Unknown operation / JSON tampering", ("Invalid_request_operation_or_risk_codes_are_rejected_by_real_checks", "Candidate_CHECK_rejects_unapproved_states_and_malformed_JSON"), "Tampered HTTP request, no apply"),
 19: ("Known duplicate versus SQL faults", ("Two_concurrent_pending_inserts_yield_one_winner_and_unique_conflict", "Same_decision_key_across_concurrent_reviews_never_creates_two_events"), "409 named conflicts; unknown SQL errors fail closed"),
 20: ("Revoked Admin bulk operations", (), "Live bulk API rights after HR/role revocation"),
 21: ("Enabled SQL triggers", ("Catalog_denies_enabled_event_trigger_until_removed", "Catalog_denies_disabled_unique_index_and_enabled_table_trigger"), "Enabled-feature HTTP 503 without trigger mutation"),
 22: ("Malformed review and audit CHECK", ("Pending_request_cannot_carry_reviewer_or_applied_fields", "Submitted_audit_cannot_have_decision_key_and_rejected_audit_requires_one", "Serializable_rejection_transaction_rolls_back_request_and_audit_on_injected_failure", "Serializable_rejection_audit_insert_error_rolls_back_request_update"), "Invalid review HTTP + audit after-state"),
}
EXTRA_GATE = {1:"1800_011_NOT_AUTHORIZED",2:"1800_011_NOT_AUTHORIZED",4:"1800_011_NOT_AUTHORIZED",
              11:"VAL_B1_002_BLOCKED",14:"VAL_B1_002_BLOCKED",
              18:"APPROVE_APPLY_DENY_ALL",21:"1800_011_NOT_AUTHORIZED"}

def validate_links():
    if set(LINKS) != set(range(1,23)):
        raise ValueError("B4 must enumerate exactly 22 scenarios")
    for ident,(name,links,gap) in LINKS.items():
        if len(name)<8 or len(gap)<8 or len(set(links))!=len(links):
            raise ValueError(f"B4-SQL-{ident:02d} invalid metadata/duplicate link")
        if not set(links).issubset(REQUIRED_SQL_TESTS):
            raise ValueError(f"B4-SQL-{ident:02d} unknown SQL fixture test link")

def map_sql_evidence(source):
    validate_links()
    if not isinstance(source,dict) or source.get("kind")!="B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE":
        raise ValueError("unrecognized SQL evidence source")
    sha=source.get("source_sha")
    digest=source.get("trx_sha256")
    if not isinstance(sha,str) or not re.fullmatch("[0-9a-f]{40}",sha):
        raise ValueError("missing/invalid source SHA")
    if not isinstance(digest,str) or not re.fullmatch("[0-9a-f]{64}",digest):
        raise ValueError("missing/invalid TRX SHA-256")
    n=len(REQUIRED_SQL_TESTS)
    if (source.get("status")!="PASS"
        or source.get("scope")!="DISPOSABLE_LOCALHOST_SQL_SERVER_2022"
        or source.get("tests_passed")!=n or source.get("tests_total")!=n
        or source.get("verified_case_names")!=sorted(REQUIRED_SQL_TESTS)
        or source.get("formal_schema_migration")!="NOT_EXECUTED"
        or source.get("existing_uat_or_production_database")!="NOT_TOUCHED"
        or source.get("full_22_case_b4_sql_runtime")!="NOT_COMPLETE"
        or source.get("b3_approve_apply")!="DENY_ALL"
        or source.get("business_uat")!="HOLD" or source.get("production")!="HARD_HOLD"):
        raise ValueError("SQL evidence or mandatory release HOLD contract invalid")
    actual=source.get("case_results")
    if not isinstance(actual,list) or len(actual)!=n:
        raise ValueError("SQL TRX per-case results missing")
    indexed={}
    seen_ids=set()
    for row in actual:
        if not isinstance(row,dict):
            raise ValueError("malformed SQL result row")
        name=row.get("name")
        testid=row.get("test_id")
        execid=row.get("execution_id")
        if (name in indexed or name not in {SQL_TEST_PREFIX+x for x in REQUIRED_SQL_TESTS}
            or row.get("outcome")!="Passed" or not testid or not execid
            or testid in seen_ids or execid in seen_ids):
            raise ValueError("SQL TRX case failed, duplicated, unknown or missing ID")
        indexed[name]=row
        seen_ids.update((testid,execid))
    if len(indexed)!=n:
        raise ValueError("SQL evidence incomplete")
    rows=[]
    for number,(title,links,gap) in sorted(LINKS.items()):
        cases=[{"name":name,"test_id":indexed[SQL_TEST_PREFIX+name]["test_id"],
                "execution_id":indexed[SQL_TEST_PREFIX+name]["execution_id"]}
               for name in links]
        rows.append({"id":f"B4-SQL-{number:02d}","scenario":title,
                     "sql_fixture_status":"PARTIAL_ISOLATED_ONLY" if cases else "NO_DIRECT_SQL_FIXTURE",
                     "sql_test_links":cases,
                     "live_api_hr_business_proof":"PENDING",
                     "remaining_proof":gap,
                     "independent_gate":EXTRA_GATE.get(number,"B3_FEATURE_OFF"),
                     "formal_acceptance":"PENDING"})
    partial=sum(bool(row["sql_test_links"]) for row in rows)
    return {"kind":"B4_22_CASE_PARTIAL_EVIDENCE_NONRELEASE",
            "status":"TRACEABILITY_ONLY_RELEASE_HOLD",
            "source_sha":sha,"source_sql_trx_sha256":digest,
            "sql_tests_passed":n,"b4_cases":22,
            "b4_partial_sql_cases":partial,"b4_without_direct_sql":22-partial,
            "formal_b4_accepted":0,"full_b4_api_hr_uat":"NOT_EXECUTED",
            "manager_grant":"BLOCKED","migration_1800_011":"NOT_AUTHORIZED",
            "b3_flag":"OFF","approve_apply":"DENY_ALL",
            "business_uat":"HOLD","production":"HARD_HOLD",
            "cases":rows}

def summarize(report):
    lines=["### B4 SQL fixture-to-scenario traceability (NOT ACCEPTANCE)",
           "SQL source SHA: "+report["source_sha"],
           "SQL TRX SHA-256: "+report["source_sql_trx_sha256"],
           "Partial SQL evidence: "+str(report["b4_partial_sql_cases"])+"/22 B4 cases",
           "No directly linked SQL fixture: "+str(report["b4_without_direct_sql"])+"/22",
           "**Formally accepted B4: 0/22. Full API/HR/Business E2E: NOT EXECUTED.**",
           "**Manager Grant BLOCKED; 011 NOT AUTHORIZED; B3 OFF; Approve/Apply DENY ALL; Business UAT HOLD; Production HARD HOLD.**",
           "","| Case | Isolated SQL links (partial only) | Missing live proof / gate |",
           "| --- | --- | --- |"]
    for row in report["cases"]:
        count=len(row["sql_test_links"])
        lines.append("| "+row["id"]+" | "+
                     (str(count)+" actual SQL TRX case(s)" if count else "NONE")+" | "+
                     row["remaining_proof"]+" / "+row["independent_gate"]+" |")
    return "\n".join(lines)+"\n"

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--sql",required=True)
    p.add_argument("--report",required=True)
    p.add_argument("--summary",required=True)
    args=p.parse_args()
    try:
        report=map_sql_evidence(json.loads(Path(args.sql).read_text(encoding="utf-8")))
        rc=0
    except (OSError,ValueError,TypeError,KeyError) as exc:
        report={"kind":"B4_22_CASE_PARTIAL_EVIDENCE_NONRELEASE",
                "status":"FAIL_CLOSED","reason":str(exc),
                "formal_b4_accepted":0,"business_uat":"HOLD","production":"HARD_HOLD"}
        rc=1
    path=Path(args.report);path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    target=Path(args.summary);target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(summarize(report) if rc==0 else
                      "### B4 mapping FAIL CLOSED\n"+report["reason"]+
                      "\nFormal B4 0/22. UAT HOLD. Production HARD HOLD.\n",encoding="utf-8")
    print(report["status"],"B4 accepted 0/22")
    return rc

if __name__=="__main__":
    sys.exit(main())
