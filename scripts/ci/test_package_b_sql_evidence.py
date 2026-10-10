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
        self.assertEqual(26,result["tests_passed"])
        self.assertEqual(self.names,result["verified_case_names"])
        self.assertEqual("HARD_HOLD",result["production"])
        self.assertEqual("NOT_EXECUTED",result["formal_schema_migration"])

    def test_case_results_are_individually_exported_and_sha_bound(self):
        result=validate(self.path,"a"*40)
        self.assertEqual("a"*40,result["source_sha"])
        self.assertEqual(26,len(result["case_results"]))
        self.assertEqual(26,len({x["test_id"] for x in result["case_results"]}))
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

    def test_missing_trx_fails_closed(self):
        self.path.unlink()
        with self.assertRaisesRegex(ValueError,"missing"):
            validate(self.path)

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
