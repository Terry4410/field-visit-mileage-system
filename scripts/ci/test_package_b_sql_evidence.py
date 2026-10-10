import tempfile
import unittest
from pathlib import Path

from package_b_sql_evidence import validate

class SqlEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.path=Path(self.folder.name)/"sql.trx"
        self.put(8,8)

    def put(self,total,passed,skipped=0,cases=None):
        if cases is None:
            cases='<UnitTestResult outcome="Passed"/>'*total
        self.path.write_text(
            '<TestRun><Results>'+cases+'</Results>'
            '<ResultSummary><Counters total="'+str(total)+'" passed="'+
            str(passed)+'" failed="0" error="0" notExecuted="'+str(skipped)+
            '"/></ResultSummary></TestRun>')

    def test_valid_sql_evidence_keeps_go_on_hold(self):
        data=validate(self.path,"a"*40)
        self.assertEqual("PASS",data["status"])
        self.assertEqual(8,data["tests_passed"])
        self.assertEqual("HARD_HOLD",data["production"])
        self.assertEqual("NOT_EXECUTED",data["formal_schema_migration"])

    def test_missing_evidence_is_rejected(self):
        self.path.unlink()
        with self.assertRaisesRegex(ValueError,"missing"):
            validate(self.path)

    def test_skipped_test_is_rejected(self):
        self.put(8,7,skipped=1)
        with self.assertRaises(ValueError):
            validate(self.path)

    def test_forged_test_count_is_rejected(self):
        self.put(100,100,cases='<UnitTestResult outcome="Passed"/>')
        with self.assertRaisesRegex(ValueError,"count mismatch"):
            validate(self.path)

    def test_failed_case_hidden_by_counters_is_rejected(self):
        self.put(8,8,cases=
            '<UnitTestResult outcome="Failed"/>'+
            '<UnitTestResult outcome="Passed"/>'*7)
        with self.assertRaisesRegex(ValueError,"non-passing"):
            validate(self.path)

    def test_unknown_source_sha_is_rejected(self):
        with self.assertRaisesRegex(ValueError,"SHA"):
            validate(self.path,"unverified")

if __name__=="__main__":
    unittest.main()
