"""Offline parser security tests: never disclose SID, reject wrong target."""
import unittest
from pathb_readonly_receipt_parser import parse_preflight, ReceiptHold

def row(server='sql-fieldvisit-jpe-pathb-uat', db='db-fieldvisit-pathb-uat'):
    cells=['FV_SQL',server,db,'5','AABBCC','1','1','0','TRUE','FEDAUTH','TCP']
    cells += ['0'] * 11
    assert len(cells)==22
    return ('|'.join(cells)+'\n').encode()

class ReceiptSecurityTests(unittest.TestCase):
    def test_expected_empty_candidate(self):
        result=parse_preflight(row())
        self.assertEqual(result.sql_target,'PASS')
        self.assertEqual(result.schema,'EMPTY_CANDIDATE')
        self.assertNotIn('AABBCC',str(result))
    def test_wrong_server_holds(self):
        with self.assertRaises(ReceiptHold):
            parse_preflight(row(server='other-server'))
    def test_wrong_database_holds(self):
        with self.assertRaises(ReceiptHold):
            parse_preflight(row(db='other-database'))
    def test_extra_rows_holds(self):
        with self.assertRaises(ReceiptHold):
            parse_preflight(row()+row())
    def test_nonempty_holds_for_manual_reconciliation(self):
        data=row().decode().strip().split('|')
        data[7]='1'
        result=parse_preflight(('|'.join(data)+'\n').encode())
        self.assertEqual(result.schema,'NONEMPTY_OR_PARTIAL')
        self.assertEqual(result.next_action,'HOLD_MANUAL_RECONCILIATION')

if __name__=='__main__':
    unittest.main()
