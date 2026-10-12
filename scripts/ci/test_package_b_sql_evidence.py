import tempfile
import unittest
from pathlib import Path
from package_b_sql_evidence import validate,REQUIRED_SQL_TESTS,SQL_TEST_PREFIX

class SqlEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path=Path(self.temp.name)/"sql.trx"
        self.names=sorted(REQUIRED_SQL_TESTS)
        self.make()

    def make(self,names=None,*,skip=0,outcomes=None,missing_ids=False):
        names=self.names if names is None else names
        cases=[]
        for i,name in enumerate(names):
            attrs=("" if missing_ids else
                   f' testId="test-{i}" executionId="exec-{i}"')
            outcome=(outcomes or {}).get(name,"Passed")
            cases.append(
                f'<UnitTestResult testName="{SQL_TEST_PREFIX}{name}"'
                f' outcome="{outcome}" duration="00:00:00.001"{attrs}/>')
        self.path.write_text(
            '<TestRun><Results>'+''.join(cases)+'</Results>'
            '<ResultSummary outcome="Completed"><Counters total="'+str(len(names))+
            '" passed="'+str(len(names)-skip)+'" failed="0" error="0"'
            ' notExecuted="'+str(skip)+'"/></ResultSummary></TestRun>')

    def test_complete_named_sql_suite_remains_nonrelease(self):
        result=validate(self.path,"a"*40)
        self.assertEqual(len(REQUIRED_SQL_TESTS),result["tests_passed"])
        self.assertEqual(self.names,result["verified_case_names"])
        self.assertEqual("HARD_HOLD",result["production"])
        self.assertEqual("NOT_EXECUTED",result["formal_schema_migration"])

    def test_case_results_are_individually_exported_and_sha_bound(self):
        result=validate(self.path,"a"*40)
        self.assertEqual("a"*40,result["source_sha"])
        self.assertEqual(len(REQUIRED_SQL_TESTS),len(result["case_results"]))
        self.assertEqual(len(REQUIRED_SQL_TESTS),len({x["test_id"] for x in result["case_results"]}))
        self.assertTrue(all(x["outcome"]=="Passed" for x in result["case_results"]))
        self.assertTrue(all(x["duration"]=="00:00:00.001" for x in result["case_results"]))

    def test_noncompleted_summary_fails_even_with_all_green_case_rows(self):
        self.path.write_text(self.path.read_text().replace(
            'outcome="Completed"','outcome="Failed"'))
        with self.assertRaisesRegex(ValueError,"not completed"):
            validate(self.path)

    def test_absent_result_summary_fails_closed(self):
        self.path.write_text(self.path.read_text().replace(
            '<ResultSummary outcome="Completed">','<ResultSummary>'))
        with self.assertRaisesRegex(ValueError,"not completed"):
            validate(self.path)

    def test_missing_sha_fails_even_with_all_green_results(self):
        with self.assertRaisesRegex(ValueError,"SHA"):
            validate(self.path)

    def test_trx_sha256_is_recorded_for_IT_reconciliation(self):
        import hashlib
        result=validate(self.path,"b"*40)
        self.assertEqual(hashlib.sha256(self.path.read_bytes()).hexdigest(),
                         result["trx_sha256"])

    def test_timeout_counter_cannot_be_hidden_by_forged_passes(self):
        content=self.path.read_text()
        self.path.write_text(content.replace('notExecuted="0"',
                                             'notExecuted="0" timeout="1"'))
        with self.assertRaisesRegex(ValueError,"aborted results"):
            validate(self.path,"c"*40)

    def test_nonrunnable_counter_fails_closed(self):
        content=self.path.read_text()
        self.path.write_text(content.replace('notExecuted="0"',
                                             'notExecuted="0" notRunnable="1"'))
        with self.assertRaisesRegex(ValueError,"aborted results"):
            validate(self.path,"c"*40)

    def test_duplicate_result_summary_fails_closed(self):
        content=self.path.read_text()
        summary=content[content.index("<ResultSummary"):content.index("</ResultSummary>")+len("</ResultSummary>")]
        self.path.write_text(content.replace("</TestRun>",summary+"</TestRun>"))
        with self.assertRaisesRegex(ValueError,"duplicated run summaries"):
            validate(self.path,"c"*40)

    def test_missing_per_test_duration_fails_closed(self):
        self.path.write_text(self.path.read_text().replace(
            ' duration="00:00:00.001"','',1))
        with self.assertRaisesRegex(ValueError,"duration"):
            validate(self.path,"c"*40)

    def test_malformed_per_test_duration_fails_closed(self):
        self.path.write_text(self.path.read_text().replace(
            'duration="00:00:00.001"','duration="negative"',1))
        with self.assertRaisesRegex(ValueError,"duration"):
            validate(self.path,"c"*40)

    def test_duplicate_test_id_is_denied(self):
        self.path.write_text(self.path.read_text().replace(
            'testId="test-1"','testId="test-0"'))
        with self.assertRaisesRegex(ValueError,"duplicated testId"):
            validate(self.path,"e"*40)

    def test_missing_test_name_is_denied(self):
        self.path.write_text(self.path.read_text().replace(
            f'testName="{SQL_TEST_PREFIX}{self.names[0]}"',''))
        with self.assertRaisesRegex(ValueError,"mandatory named test cases"):
            validate(self.path,"e"*40)

    def test_inconclusive_outcome_with_forged_counter_is_denied(self):
        self.make(outcomes={self.names[0]:"Inconclusive"})
        with self.assertRaisesRegex(ValueError,"non-passing"):
            validate(self.path,"e"*40)

    def test_negative_total_is_denied(self):
        self.path.write_text(self.path.read_text().replace(
            f'total="{len(REQUIRED_SQL_TESTS)}"',f'total="-{len(REQUIRED_SQL_TESTS)}"'))
        with self.assertRaisesRegex(ValueError,"missing, skipped or failed"):
            validate(self.path,"e"*40)

    def test_missing_trx_fails_closed(self):
        self.path.unlink()
        with self.assertRaisesRegex(ValueError,"missing"):
            validate(self.path)

    def test_manifest_exactly_matches_real_fixture_fact_methods(self):
        # Prevent CI-successful SQL tests being rejected at evidence export:
        # all actual [Fact] methods must be reviewed and explicitly registered.
        import re
        fixture=(Path(__file__).resolve().parents[2]/
            "backend/tests/FieldVisit.B3.SqlServer.Tests/B3IsolatedSqlServerFixtureTests.cs")
        source=fixture.read_text(encoding="utf-8")
        names=re.findall(
            r"\[Fact\]\s+public\s+async\s+Task\s+([A-Za-z_][A-Za-z_0-9]*)\s*\(",
            source)
        self.assertEqual(len(names),len(set(names)),"duplicate SQL fixture test methods")
        self.assertEqual(REQUIRED_SQL_TESTS,set(names),
            "SQL fixture tests and evidence manifest must match exactly")

    def test_new_sql_rollback_cases_are_named_and_required(self):
        new_cases={
            "Serializable_rejection_transaction_rolls_back_request_and_audit_on_injected_failure",
            "Serializable_rejection_audit_insert_error_rolls_back_request_update",
        }
        self.assertEqual(143,len(REQUIRED_SQL_TESTS))
        self.assertTrue(new_cases.issubset(REQUIRED_SQL_TESTS))
        for omitted in new_cases:
            with self.subTest(omitted=omitted):
                self.make([name for name in self.names if name!=omitted])
                with self.assertRaises(ValueError):
                    validate(self.path,"d"*40)

    def test_unreviewed_extra_case_cannot_silently_replace_manifest(self):
        self.make(self.names+["UNREVIEWED_rollback_extra"])
        with self.assertRaises(ValueError):
            validate(self.path,"d"*40)

    def test_missing_one_required_test_fails_even_if_others_pass(self):
        self.make(self.names[:-1])
        with self.assertRaises(ValueError):
            validate(self.path)

    def test_replacing_required_case_with_duplicate_fails(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"named test cases"):
            validate(self.path)

    def test_skipped_tests_fail(self):
        self.make(skip=1)
        with self.assertRaises(ValueError):
            validate(self.path)

    def test_failed_case_with_forged_pass_counter_fails(self):
        self.make(outcomes={self.names[0]:"Failed"})
        with self.assertRaisesRegex(ValueError,"non-passing"):
            validate(self.path)

    def test_missing_test_and_execution_ids_fail(self):
        self.make(missing_ids=True)
        with self.assertRaisesRegex(ValueError,"missing or duplicated"):
            validate(self.path)

    def test_duplicate_execution_id_fails(self):
        self.path.write_text(self.path.read_text().replace(
            'executionId="exec-1"','executionId="exec-0"'))
        with self.assertRaisesRegex(ValueError,"duplicated executionId"):
            validate(self.path)

    def test_invalid_sha_fails(self):
        with self.assertRaisesRegex(ValueError,"SHA"):
            validate(self.path,"bad")

if __name__=="__main__":
    unittest.main()
