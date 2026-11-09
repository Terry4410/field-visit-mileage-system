import importlib.util
from pathlib import Path
import unittest
p=Path(__file__).with_name('owner_sql_catalog_child_REVIEW_ONLY.py')
spec=importlib.util.spec_from_file_location('owner_child',p)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class Tests(unittest.TestCase):
 def test_no_raw_success(self):
  self.assertEqual(m.sanitize_sql_result(b'other text\n'), 'HOLD_RESULT_SHAPE')
 def test_no_multiline(self):
  self.assertEqual(m.sanitize_sql_result(b'FV_CAT|PASS\nSecret'), 'HOLD_RESULT_SHAPE')
 def test_shape(self):
  row='FV_CAT|PASS|PASS|PASS|PASS|69|69|1|1|3|3|0|0|0'
  self.assertEqual(m.sanitize_sql_result(row.encode()),'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED')
 def test_target_hold(self):
  row='FV_CAT|PASS|HOLD|PASS|PASS|69|69|1|1|3|3|0|0|0'
  self.assertEqual(m.sanitize_sql_result(row.encode()),'HOLD_SQL_TARGET_OR_METADATA')
 def test_empty(self):
  row='FV_CAT|PASS|PASS|PASS|PASS|0|0|0|0|NA|NA|0|0|0'
  self.assertEqual(m.sanitize_sql_result(row.encode()),'EMPTY_CATALOG_CANDIDATE')
 def test_69_but_missing_versions_is_hold(self):
  self.assertEqual(m.sanitize_sql_result(b'FV_CAT|PASS|PASS|PASS|PASS|69|69|1|1|3|2|0|0|0'),'HOLD_PARTIAL_OR_INCONSISTENT')
 def test_69_but_bad_fk_is_hold(self):
  self.assertEqual(m.sanitize_sql_result(b'FV_CAT|PASS|PASS|PASS|PASS|69|69|1|1|3|3|0|1|0'),'HOLD_PARTIAL_OR_INCONSISTENT')
 def test_68_tables_is_hold(self):
  self.assertEqual(m.sanitize_sql_result(b'FV_CAT|PASS|PASS|PASS|PASS|68|68|1|1|3|3|0|0|0'),'HOLD_PARTIAL_OR_INCONSISTENT')
 def test_zero_tables_with_ledger_is_hold(self):
  self.assertEqual(m.sanitize_sql_result(b'FV_CAT|PASS|PASS|PASS|PASS|0|0|0|1|NA|NA|0|0|0'),'HOLD_PARTIAL_OR_INCONSISTENT')
 def test_invalid_flag_is_hold(self):
  self.assertEqual(m.sanitize_sql_result(b'FV_CAT|PASS|PASS|PASS|PASS|69|69|2|1|3|3|0|0|0'),'HOLD_RESULT_SHAPE')
 def test_review_lock(self):
  self.assertIn('HOLD_REVIEW_ONLY_NOT_EXECUTABLE',p.read_text())
  self.assertIn('return 1',p.read_text())
 def test_all_five_tool_files_are_pinned(self):
  self.assertEqual(len(m.EXPECTED),5)
  self.assertTrue(all(len(value)==64 for value in m.EXPECTED.values()))
if __name__=='__main__':unittest.main()
