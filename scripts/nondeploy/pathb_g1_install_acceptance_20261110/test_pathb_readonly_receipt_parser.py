"""Synthetic-only receipt tests: no Azure, SQL, or token access."""
import ast
import unittest
from pathlib import Path
from dataclasses import asdict
from pathb_readonly_receipt_parser import parse_preflight, ReceiptHold

def row(**changes):
    fields=['FV_SQL','synthetic-server','db-fieldvisit-pathb-uat','5',
            'AABBCC','0','1','0','TRUE','FEDAUTH','TCP']+['0']*11
    for key,value in changes.items(): fields[int(key)]=value
    return ('|'.join(fields)+'\n').encode()

class ReceiptTests(unittest.TestCase):
    def test_empty_candidate_is_not_install_authorization(self):
        rec=parse_preflight(row())
        self.assertEqual(rec.schema,'EMPTY_CANDIDATE')
        self.assertEqual(rec.next_action,'SEPARATE_INSTALL_AUTH_REQUIRED')
        self.assertNotIn('AABBCC',str(rec))
    def test_nonempty_never_declares_complete(self):
        rec=parse_preflight(row(**{'7':'69'}))
        self.assertEqual(rec.schema,'NONEMPTY_OR_PARTIAL')
        self.assertEqual(rec.next_action,'HOLD_MANUAL_RECONCILIATION')
    def test_ambiguous_results_hold(self):
        bad=[b'',b'garbage',row()+row(),row(**{'2':'other-db'}),
             row(**{'8':'FALSE'}),row(**{'10':'Shared memory'}),
             row(**{'7':'-1'}),row(**{'4':'secret-value'}),
             row()+b'\xff',b'x'*8193]
        for value in bad:
            with self.subTest(length=len(value)),self.assertRaises(ReceiptHold):
                parse_preflight(value)
    def test_no_runtime_or_secret_output(self):
        tree=ast.parse(Path(__import__('pathb_readonly_receipt_parser').__file__).read_text())
        imports=set()
        for node in ast.walk(tree):
            if isinstance(node,ast.Import):imports.update(a.name for a in node.names)
            if isinstance(node,ast.ImportFrom):imports.add(node.module)
        self.assertEqual(imports,{'dataclasses'})
        self.assertFalse(any(isinstance(node,ast.Call) and isinstance(node.func,ast.Name)
                             and node.func.id=='print' for node in ast.walk(tree)))

if __name__=='__main__':unittest.main()
