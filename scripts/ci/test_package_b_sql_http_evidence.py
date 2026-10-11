import hashlib
import tempfile
import unittest
from pathlib import Path
from package_b_sql_http_evidence import PREFIX,REQUIRED,validate

class RealJwtSqlHttpEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.trx=Path(self.tmp.name)/"sql-http.trx"
        self.names=sorted(REQUIRED)
        self.make()

    def make(self,names=None,failed=False,missing_ids=False,skip=0):
        names=self.names if names is None else names
        cases=[]
        for i,name in enumerate(names):
            ident="" if i==0 and missing_ids else f' testId="case-{i}" executionId="execution-{i}"'
            outcome="Failed" if failed and i==0 else "Passed"
            cases.append(f'<UnitTestResult testName="{PREFIX}{name}" outcome="{outcome}"'
                         f' duration="00:00:01.1234567"{ident}/>')
        self.trx.write_text("<TestRun><Results>"+"".join(cases)+"</Results>"
            '<ResultSummary outcome="Completed"><Counters '
            f'total="{len(names)}" passed="{len(names)-skip}" failed="0" error="0"'
            f' notExecuted="{skip}"/></ResultSummary></TestRun>')

    def test_exact_sixteen_hold_contract_and_sha(self):
        proof=validate(self.trx,"a"*40)
        self.assertEqual(16,proof["passed"])
        self.assertEqual("a"*40,proof["source_sha"])
        self.assertEqual("HARD_HOLD",proof["production"])
        self.assertEqual(0,proof["formal_b4_accepted"])
        self.assertEqual("NOT_AUTHORIZED",proof["011_migration"])
        self.assertEqual("OFF",proof["b3_flag"])
        self.assertEqual("DENY_ALL",proof["approve_apply"])
        self.assertEqual(16,len(proof["case_results"]))
        self.assertEqual(hashlib.sha256(self.trx.read_bytes()).hexdigest(),
                         proof["trx_sha256"])

    def test_all_six_draft_paths_independently_required(self):
        draft=[x for x in self.names if "SQL_HTTP_DRAFT_" in x]
        self.assertEqual(6,len(draft))
        for case in draft:
            with self.subTest(missing_case=case):
                self.make([n for n in self.names if n!=case])
                with self.assertRaisesRegex(ValueError,"missing/failed/skipped"):
                    validate(self.trx,"b"*40)

    def test_all_six_session_and_site_paths_independently_required(self):
        initial=[x for x in self.names if "SQL_HTTP_DRAFT_" not in x and "SQL_HTTP_SUBMIT_" not in x]
        self.assertEqual(6,len(initial))
        for case in initial:
            with self.subTest(missing_case=case):
                self.make([n for n in self.names if n!=case])
                with self.assertRaisesRegex(ValueError,"missing/failed/skipped"):
                    validate(self.trx,"b"*40)

    def test_all_four_submit_snapshot_paths_mandatory(self):
        cases=[x for x in self.names if x.startswith("SQL_HTTP_SUBMIT_")]
        self.assertEqual(4,len(cases))
        for removed in cases:
            with self.subTest(removed=removed):
                self.make([n for n in self.names if n!=removed])
                with self.assertRaisesRegex(ValueError,"missing/failed/skipped"):
                    validate(self.trx,"b"*40)

    def test_missing_named_result_fails(self):
        self.make(self.names[:-1])
        with self.assertRaisesRegex(ValueError,"missing/failed/skipped"):
            validate(self.trx,"b"*40)

    def test_duplicate_fake_result_cannot_substitute_missing(self):
        self.make(self.names[:-1]+self.names[:1])
        with self.assertRaisesRegex(ValueError,"mandatory named"):
            validate(self.trx,"b"*40)

    def test_failed_case_denied_even_if_summary_is_green(self):
        self.make(failed=True)
        with self.assertRaisesRegex(ValueError,"not Passed"):
            validate(self.trx,"b"*40)

    def test_skipped_case_fail_closed(self):
        self.make(skip=1)
        with self.assertRaisesRegex(ValueError,"missing/failed/skipped"):
            validate(self.trx,"b"*40)

    def test_missing_individual_ids_denied(self):
        self.make(missing_ids=True)
        with self.assertRaisesRegex(ValueError,"IDs"):
            validate(self.trx,"b"*40)

    def test_duplicate_execution_id_denied(self):
        self.trx.write_text(self.trx.read_text().replace(
            'executionId="execution-1"','executionId="execution-0"'))
        with self.assertRaisesRegex(ValueError,"IDs"):
            validate(self.trx,"b"*40)

    def test_unknown_or_missing_sha_denied(self):
        for candidate in (None,"bad","f"*39):
            with self.subTest(candidate=candidate):
                with self.assertRaisesRegex(ValueError,"SHA"):
                    validate(self.trx,candidate)

    def test_malformed_elapsed_time_denied(self):
        self.trx.write_text(self.trx.read_text().replace(
            'duration="00:00:01.1234567"','duration="no"',1))
        with self.assertRaisesRegex(ValueError,"duration"):
            validate(self.trx,"b"*40)

    def test_aborted_run_denied(self):
        self.trx.write_text(self.trx.read_text().replace(
            'outcome="Completed"','outcome="Failed"'))
        with self.assertRaisesRegex(ValueError,"summary"):
            validate(self.trx,"b"*40)

    def test_timeout_counter_denied(self):
        self.trx.write_text(self.trx.read_text().replace(
            'notExecuted="0"','notExecuted="0" timeout="1"'))
        with self.assertRaisesRegex(ValueError,"timeout"):
            validate(self.trx,"b"*40)

    def test_missing_trx_denied(self):
        self.trx.unlink()
        with self.assertRaisesRegex(ValueError,"missing"):
            validate(self.trx,"b"*40)

if __name__=="__main__":
    unittest.main()
