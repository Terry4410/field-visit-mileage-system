#!/usr/bin/env python3
"""Validate real SQL Server disposable-fixture TRX. Never authorize UAT GO."""
from __future__ import annotations
import argparse
import hashlib
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
    "Ef_composable_request_update_lock_executes_on_real_sql_server",
    "Serializable_update_lock_blocks_competing_request_reader_until_release",
    "Serializable_request_lock_does_not_block_unrelated_pending_request",
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
    "Failed_submitted_event_fk_rolls_back_new_pending_without_orphan_history",
    "Rejected_history_blocks_delete_after_new_pending_is_created",
    "Catalog_denies_reordered_pending_unique_index_keys_until_restored",
    "Catalog_denies_missing_event_table_without_attempted_schema_repair",
    "Catalog_denies_disabled_decision_unique_index_until_rebuilt",
    "Catalog_denies_enabled_event_trigger_until_removed",
    "Catalog_denies_untrusted_event_decision_check_until_retrusted",
    "Rolled_back_submission_can_retry_with_one_committed_audit",
    "Stale_reviewer_rowversion_cannot_create_decision_audit",
    "Catalog_denies_missing_request_public_unique_index_until_restored",
    "Cross_organization_pending_submission_preserves_separate_audit_histories",
    "Rejected_request_cannot_reopen_pending_when_newer_pending_exists",
    "Invalid_organization_or_team_fk_never_persists_change_request",
    "Invalid_request_operation_or_risk_codes_are_rejected_by_real_checks",
    "Pending_request_cannot_carry_reviewer_or_applied_fields",
    "Submitted_audit_cannot_have_decision_key_and_rejected_audit_requires_one",
    "Malformed_audit_event_cannot_replace_committed_decision_history",
    "Decision_audit_fk_preserves_reviewer_and_event_history",
    "Future_schema_version_is_rejected_until_latest_version_is_restored",
    "Request_with_invalid_expected_rowversion_bytes_is_denied_without_audit",
    "Ef_SQL_Mine_query_hides_peer_and_cross_organization_proposals",
    "Ef_SQL_Admin_pending_query_excludes_foreign_org_and_rejected",
    "Ef_SQL_Requester_refuses_unknown_identity_or_tenant_before_query",
    "Ef_SQL_Mine_query_does_not_mutate_rowversion_or_create_events",
    "Ef_SQL_stale_tracked_request_update_fails_concurrency_without_audit",
    "Ef_SQL_rejected_history_visible_only_to_owner_not_admin_pending",
    "Ef_SQL_peer_and_cross_org_sensitive_before_evidence_json_not_disclosed",
    "Ef_SQL_switching_actor_org_between_queries_never_reuses_previous_tenant_scope",
    "Ef_SQL_admin_org_queue_includes_peer_but_requester_mine_does_not",
    "Sql_transaction_rejected_event_fk_failure_rolls_back_request_decision",
    "Sql_transaction_duplicate_decision_key_rolls_back_second_request",
    "Sql_transaction_check_constraint_failure_rolls_back_decision_and_audit",
    "Sql_rolled_back_decision_key_is_reusable_without_phantom_event",
    "Serializable_rejection_transaction_rolls_back_request_and_audit_on_injected_failure",
    "Serializable_rejection_audit_insert_error_rolls_back_request_update",
    "Hypothetical_atomic_probe_commits_two_updates_and_correlated_events",
    "Hypothetical_atomic_probe_rolls_back_all_four_postwrite_fault_points",
    "Hypothetical_atomic_probe_duplicate_decision_key_rolls_back_second_apply",
    "Hypothetical_atomic_probe_stale_request_and_source_versions_fail_closed",
    "Hypothetical_atomic_probe_concurrent_reviewers_commit_at_most_once",
    "Hypothetical_atomic_probe_seven_fields_persist_and_scope_columns_remain_unchanged",
    "Hypothetical_atomic_probe_seven_fields_nullables_are_written_as_sql_null",
    "Hypothetical_atomic_probe_seven_field_rollback_restores_data_and_versions",
    "Hypothetical_atomic_probe_denies_foreign_peer_inactive_unapproved_and_noncustomer_scopes",
    "Hypothetical_012_probe_request_check_rejects_unknown_null_and_self_review_shapes",
    "Hypothetical_012_probe_request_check_accepts_valid_independent_rejection",
    "Hypothetical_012_probe_audit_check_rejects_null_actor_key_empty_guid_and_unknown_type",
    "Hypothetical_012_probe_audit_check_accepts_four_valid_per_row_event_shapes",
    "Hypothetical_012_probe_untrusted_review_check_is_detected_until_retrusted",
    "Hypothetical_012_probe_untrusted_audit_check_is_denied_until_retrusted",
    "Hypothetical_012_real_named_tables_catalog_denies_untrusted_and_missing_version",
    "Hypothetical_012_real_named_tables_atomic_state_and_audit_checks_rollback",
    "Hypothetical_012_real_named_tables_disabled_unique_decision_index_fails_shape",
    "Hypothetical_012_strict_fingerprint_denies_token_preserving_check_relaxation",
    "Hypothetical_012_strict_fingerprint_denies_same_name_narrowed_unique_filter",
    "Hypothetical_012_strict_fingerprint_denies_duplicate_011_predecessor",
    "Hypothetical_012_strict_fingerprint_denies_enabled_side_effect_trigger",
    "Hypothetical_012_real_named_locations_seven_fields_and_atomic_audit_pair",
    "Hypothetical_012_real_named_locations_four_fault_points_restore_savepoint",
    "Hypothetical_012_real_named_locations_replay_denies_second_write",
    "Hypothetical_012_real_named_locations_stale_versions_and_live_scope_denied",
    "Unknown_commit_probe_complete_persisted_apply_is_read_only_verified",
    "Unknown_commit_probe_rolled_back_apply_is_read_only_not_committed_evidence",
    "Unknown_commit_probe_approved_only_event_is_never_treated_as_success",
    "Unknown_commit_probe_changed_source_after_apply_is_indeterminate",
    "Unknown_commit_probe_key_on_foreign_request_is_indeterminate",
    "Unknown_commit_probe_missing_record_never_claims_no_commit",
    "Hypothetical_012_reduced_locations_stub_explicitly_fails_full_EF_parity",
    "Hypothetical_012_expanded_Locations_all_EF_columns_match_disposable_SQL",
    "Hypothetical_012_expanded_Locations_guarded_apply_preserves_noneditable_columns",
    "Hypothetical_012_expanded_Locations_rejects_Unicode_width_and_nullable_drift",
    "Hypothetical_012_expanded_Locations_detects_missing_duplicate_history_column",
    "Unknown_commit_simulated_server_commits_then_throws_ack_failure",
    "Unknown_commit_simulated_server_rolls_back_then_throws_identical_error",
    "Hypothetical_012_Locations_relations_trusted_no_cascade_and_exact_filtered_index",
    "Hypothetical_012_Locations_relations_enabled_but_untrusted_FK_denied",
    "Hypothetical_012_Locations_relations_same_named_wrong_FK_column_denied",
    "Hypothetical_012_Locations_relations_narrowed_selected_attempt_filter_denied",
    "Hypothetical_012_Locations_relations_existing_history_survives_apply_and_delete_denied",
    "Hypothetical_012_effective_HR_reviewer_and_team_grants_are_valid_on_inclusive_end_date",
    "Hypothetical_012_effective_HR_leave_or_terminated_submitter_denies_approval",
    "Hypothetical_012_effective_reviewer_admin_grant_revocation_and_employment_deny",
    "Hypothetical_012_effective_team_membership_scope_cannot_substitute_or_outlive_assignment",
    "Hypothetical_012_effective_organization_inactive_team_and_self_review_denied",
    "Hypothetical_012_SnapshotStop_published_values_survive_seven_field_location_apply",
    "Hypothetical_012_SnapshotStop_live_join_is_not_historical_source",
    "Hypothetical_012_SnapshotStop_tampered_history_is_detected_fail_closed",
    "Hypothetical_012_SnapshotStop_has_no_direct_Location_FK_and_rolls_back",
    "Hypothetical_012_real_V170_login_active_HR_passes_disabled_account_denied",
    "Hypothetical_012_real_V170_login_leave_terminated_prehire_denied",
    "Hypothetical_012_real_V170_login_expired_and_future_effective_HR_denied",
    "Hypothetical_012_real_V170_login_most_recent_effective_leave_overrides_active",
    "Hypothetical_012_real_V170_login_no_HR_facts_denies_legacy_actor",
    "Hypothetical_012_EF_SnapshotStop_query_reads_frozen_fields_after_location_apply",
    "Hypothetical_012_EF_Snapshot_parent_approved_distance_not_recomputed_on_location_apply",
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
    summary = root.find(".//{*}ResultSummary")
    if summary is None or summary.get("outcome") != "Completed":
        raise ValueError("SQL Server TRX summary is missing or not completed")
    if len(root.findall(".//{*}ResultSummary")) != 1:
        raise ValueError("SQL Server TRX duplicated run summaries")
    for key in ("aborted","timeout","inconclusive","notRunnable",
                "disconnected","passedButRunAborted","pending","inProgress"):
        if key in counters.attrib and int(counters.attrib[key]) != 0:
            raise ValueError("SQL Server TRX includes incomplete or aborted results: " + key)
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
    case_results = sorted(
        ({
            "name": case.get("testName"),
            "outcome": case.get("outcome"),
            "duration": case.get("duration"),
            "test_id": case.get("testId"),
            "execution_id": case.get("executionId"),
        } for case in cases),
        key=lambda item: item["name"],
    )
    for case in cases:
        duration=case.get("duration")
        if not duration or not re.fullmatch(r"\d{2,}:[0-5]\d:[0-5]\d(?:\.\d{1,7})?",duration):
            raise ValueError("SQL Server TRX missing or malformed per-test duration")
    if sha is None or not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("invalid or missing GitHub source SHA")
    return {
        "kind": "B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE",
        "status": "PASS",
        "source_sha": sha,
        "tests_passed": results["passed"],
        "tests_total": results["total"],
        "verified_case_names": sorted(REQUIRED_SQL_TESTS),
        "case_results": case_results,
        "trx_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
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
