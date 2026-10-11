"""Fail-closed tests for B4 case-to-real-SQL-fixture traceability."""
import unittest
from package_b_b4_coverage import LINKS, map_sql_evidence, summarize, validate_links
from package_b_sql_evidence import REQUIRED_SQL_TESTS, SQL_TEST_PREFIX

def proof():
    names=sorted(REQUIRED_SQL_TESTS)
    return {
        "kind":"B3_ISOLATED_REAL_SQL_SERVER_EVIDENCE",
        "source_sha":"a"*40,"trx_sha256":"b"*64,
        "status":"PASS","scope":"DISPOSABLE_LOCALHOST_SQL_SERVER_2022",
        "tests_passed":len(names),"tests_total":len(names),
        "verified_case_names":names,
        "case_results":[{"name":SQL_TEST_PREFIX+name,"outcome":"Passed",
                         "test_id":"test-"+str(i),"execution_id":"exec-"+str(i)}
                        for i,name in enumerate(names)],
        "formal_schema_migration":"NOT_EXECUTED",
        "existing_uat_or_production_database":"NOT_TOUCHED",
        "full_22_case_b4_sql_runtime":"NOT_COMPLETE",
        "b3_approve_apply":"DENY_ALL",
        "business_uat":"HOLD","production":"HARD_HOLD",
    }

class CoverageTests(unittest.TestCase):
    def test_exact_22_cases_and_only_known_sql_test_names(self):
        validate_links()
        self.assertEqual(set(range(1,23)),set(LINKS))
        report=map_sql_evidence(proof())
        self.assertEqual(len(REQUIRED_SQL_TESTS),report["sql_tests_passed"])
        self.assertEqual(22,report["b4_cases"])
        self.assertEqual(0,report["formal_b4_accepted"])
        self.assertEqual(22,report["b4_partial_sql_cases"]+report["b4_without_direct_sql"])
        self.assertTrue(all(x["formal_acceptance"]=="PENDING"
                            and x["live_api_hr_business_proof"]=="PENDING"
                            for x in report["cases"]))

    def test_peer_sql_is_partial_not_owner_write_or_full_api_uat(self):
        report=map_sql_evidence(proof())
        row=next(r for r in report["cases"] if r["id"]=="B4-SQL-08")
        self.assertEqual("PARTIAL_ISOLATED_ONLY",row["sql_fixture_status"])
        self.assertIn("read only",row["remaining_proof"])
        self.assertEqual("PENDING",row["formal_acceptance"])

    def test_concurrent_submission_includes_independent_request_lock_scope(self):
        report=map_sql_evidence(proof())
        row=report["cases"][4]
        self.assertEqual("B4-SQL-05",row["id"])
        names={item["name"] for item in row["sql_test_links"]}
        self.assertIn("Serializable_request_lock_does_not_block_unrelated_pending_request",names)
        self.assertIn("Two_concurrent_pending_inserts_yield_one_winner_and_unique_conflict",names)
        self.assertEqual("PARTIAL_ISOLATED_ONLY",row["sql_fixture_status"])
        self.assertEqual("PENDING",row["live_api_hr_business_proof"])
        self.assertEqual("PENDING",row["formal_acceptance"])

    def test_manager_hr_and_shared_history_have_no_direct_sql_fixture(self):
        report=map_sql_evidence(proof())
        for i in (10,11,12,14,15,17,20):
            row=report["cases"][i-1]
            self.assertEqual("NO_DIRECT_SQL_FIXTURE",row["sql_fixture_status"])
        self.assertEqual("VAL_B1_002_BLOCKED",report["cases"][13]["independent_gate"])

    def test_external_approval_gates_cannot_be_dropped_from_b4_matrix(self):
        report=map_sql_evidence(proof())
        by_id={row["id"]:row for row in report["cases"]}
        for number in (1,2,4,21):
            self.assertEqual("1800_011_NOT_AUTHORIZED",
                by_id[f"B4-SQL-{number:02d}"]["independent_gate"])
        for number in (11,14):
            self.assertEqual("VAL_B1_002_BLOCKED",
                by_id[f"B4-SQL-{number:02d}"]["independent_gate"])
        self.assertEqual("APPROVE_APPLY_DENY_ALL",
            by_id["B4-SQL-18"]["independent_gate"])
        self.assertEqual("NOT_AUTHORIZED",report["migration_1800_011"])
        self.assertEqual("BLOCKED",report["manager_grant"])
        self.assertEqual("OFF",report["b3_flag"])
        self.assertEqual("DENY_ALL",report["approve_apply"])
        self.assertEqual("NOT_EXECUTED",report["full_b4_api_hr_uat"])

    def test_summary_explicitly_preserves_hard_hold(self):
        report=map_sql_evidence(proof())
        md=summarize(report)
        self.assertIn("Formally accepted B4: 0/22",md)
        self.assertIn("Business UAT HOLD",md)
        self.assertIn("Production HARD HOLD",md)

    def test_missing_case_record_fails_even_if_all_passed_claimed(self):
        source=proof();source["case_results"].pop()
        with self.assertRaisesRegex(ValueError,"per-case"):
            map_sql_evidence(source)

    def test_replaced_case_record_fails_closed(self):
        source=proof();source["case_results"][0]["name"]="Invented_Case"
        with self.assertRaisesRegex(ValueError,"unknown"):
            map_sql_evidence(source)

    def test_failed_case_cannot_be_forged_as_pass(self):
        source=proof();source["case_results"][0]["outcome"]="Failed"
        with self.assertRaisesRegex(ValueError,"failed"):
            map_sql_evidence(source)

    def test_duplicate_result_id_is_not_independent_evidence(self):
        source=proof()
        source["case_results"][1]["execution_id"]=source["case_results"][0]["execution_id"]
        with self.assertRaisesRegex(ValueError,"duplicated"):
            map_sql_evidence(source)

    def test_wrong_sql_scope_fails_closed(self):
        source=proof();source["scope"]="UAT_SQL"
        with self.assertRaisesRegex(ValueError,"HOLD contract"):
            map_sql_evidence(source)

    def test_no_release_go_on_formal_migration_claim(self):
        source=proof();source["formal_schema_migration"]="EXECUTED"
        with self.assertRaisesRegex(ValueError,"HOLD contract"):
            map_sql_evidence(source)

    def test_no_release_go_on_approve_apply_or_business_go(self):
        for field,value in (("b3_approve_apply","ALLOWED"),("business_uat","GO"),
                            ("production","GO")):
            source=proof();source[field]=value
            with self.subTest(field=field):
                with self.assertRaisesRegex(ValueError,"HOLD contract"):
                    map_sql_evidence(source)

    def test_sha_and_trx_hash_mandatory(self):
        source=proof();source["source_sha"]="short"
        with self.assertRaisesRegex(ValueError,"SHA"):
            map_sql_evidence(source)
        source=proof();source["trx_sha256"]="no hash"
        with self.assertRaisesRegex(ValueError,"SHA-256"):
            map_sql_evidence(source)

    def test_unknown_link_fails_manifest_integrity(self):
        old=LINKS[22]
        try:
            LINKS[22]=(old[0],("Invalid_SQL_Test",),old[2])
            with self.assertRaisesRegex(ValueError,"unknown"):
                validate_links()
        finally:
            LINKS[22]=old

if __name__=="__main__":
    unittest.main()
