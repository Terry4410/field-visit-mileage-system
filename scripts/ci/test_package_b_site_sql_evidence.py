import tempfile
import unittest
from pathlib import Path

from package_b_site_sql_evidence import validate,REQUIRED,PREFIX

class SiteSqlEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path=Path(self.tmp.name)/"site.trx"
        self.names=sorted(REQUIRED)
        self.make()

    def make(self,names=None,*,failed=False,missing_id=False,skip=0):
        names=self.names if names is None else names
        rows=[]
        for i,name in enumerate(names):
            outcome="Failed" if failed and i==0 else "Passed"
            ids="" if missing_id and i==0 else f' testId="case-{i}" executionId="exec-{i}"'
            rows.append(
                f'<UnitTestResult testName="{PREFIX}{name}" outcome="{outcome}" '
                f'duration="00:00:01.001"{ids}/>')
        self.path.write_text(
            '<TestRun><Results>'+''.join(rows)+'</Results>'
            '<ResultSummary outcome="Completed"><Counters '
            f'total="{len(names)}" passed="{len(names)-skip}" failed="0" '
            f'error="0" notExecuted="{skip}"/></ResultSummary></TestRun>')

    def test_all_six_required_prove_only_disposable_partial_SQL(self):
        result=validate(self.path,"a"*40)
        self.assertEqual(6,result["tests_passed"])
        self.assertEqual(sorted(REQUIRED),result["verified_case_names"])
        self.assertEqual("DISPOSABLE_LOCALHOST_14335",result["sql_server"])
        self.assertEqual("NOT_TESTED",result["real_authenticated_sql_http"])
        self.assertEqual(0,result["b4_formally_accepted"])
        self.assertEqual("HARD_HOLD",result["production"])

    def test_missing_named_case_rejected(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"missing/skipped/failed"):
            validate(self.path,"a"*40)

    def test_duplicate_name_replacing_one_rejected(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"named cases"):
            validate(self.path,"a"*40)

    def test_failed_case_cannot_hide_behind_green_counters(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"not Passed"):
            validate(self.path,"a"*40)

    def test_skipped_case_rejected(self):
        self.make(skip=1)
        with self.assertRaisesRegex(ValueError,"missing/skipped/failed"):
            validate(self.path,"a"*40)

    def test_missing_result_id_fails(self):
        self.make(missing_id=True)
        with self.assertRaisesRegex(ValueError,"identity"):
            validate(self.path,"a"*40)

    def test_duplicate_test_id_fails(self):
        text=self.path.read_text().replace('testId="case-1"','testId="case-0"')
        self.path.write_text(text)
        with self.assertRaisesRegex(ValueError,"identity"):
            validate(self.path,"a"*40)

    def test_malformed_duration_rejected(self):
        self.path.write_text(self.path.read_text().replace(
            'duration="00:00:01.001"','duration="missing"',1))
        with self.assertRaisesRegex(ValueError,"duration"):
            validate(self.path,"a"*40)

    def test_missing_sha_rejected(self):
        with self.assertRaisesRegex(ValueError,"SHA"):
            validate(self.path,None)

    def test_forged_incomplete_summary_rejected(self):
        self.path.write_text(self.path.read_text().replace(
            'outcome="Completed"','outcome="Failed"'))
        with self.assertRaisesRegex(ValueError,"not completed"):
            validate(self.path,"a"*40)

    def test_timeout_counter_rejected(self):
        self.path.write_text(self.path.read_text().replace(
            'notExecuted="0"','notExecuted="0" timeout="1"'))
        with self.assertRaisesRegex(ValueError,"incomplete"):
            validate(self.path,"a"*40)

    def test_missing_trx_rejected(self):
        self.path.unlink()
        with self.assertRaisesRegex(ValueError,"missing"):
            validate(self.path,"a"*40)

if __name__=="__main__":
    unittest.main()
