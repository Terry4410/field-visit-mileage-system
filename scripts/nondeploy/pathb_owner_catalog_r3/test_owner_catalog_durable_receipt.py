import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

P = Path(__file__).with_name('owner_catalog_durable_receipt_REVIEW_ONLY.py')
spec = importlib.util.spec_from_file_location('durable_receipt', P)
r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)
OP = '0123456789abcdef0123456789abcdef'
CAND = 'a' * 64

class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
    def tearDown(self): self.temp.cleanup()
    def test_write_and_read(self):
        h = r.store(OP, CAND, 'EMPTY_CATALOG_CANDIDATE', True, self.root)
        got = r.read_once(OP, CAND, self.root)
        self.assertEqual(got, {'status':'EMPTY_CATALOG_CANDIDATE', 'sql_attempted':True, 'receipt_sha256':h})
    def test_no_overwrite(self):
        r.store(OP,CAND,'HOLD_OPERATION',True,self.root)
        with self.assertRaises(FileExistsError): r.store(OP,CAND,'EMPTY_CATALOG_CANDIDATE',True,self.root)
    def test_reject_wrong_operation(self):
        with self.assertRaises(r.ReceiptHold): r.store('../x',CAND,'HOLD_OPERATION',True,self.root)
    def test_reject_wrong_candidate(self):
        with self.assertRaises(r.ReceiptHold): r.store(OP,'invalid','HOLD_OPERATION',True,self.root)
    def test_reject_unrecognized_status(self):
        with self.assertRaises(r.ReceiptHold): r.store(OP,CAND,'UAT_GO',True,self.root)
    def test_absent_is_unknown(self):
        with self.assertRaises(r.ReceiptHold): r.read_once(OP,CAND,self.root)
    def test_reject_wrong_digest_on_read(self):
        r.store(OP,CAND,'HOLD_PRECHECK',False,self.root)
        with self.assertRaises(r.ReceiptHold): r.read_once(OP,'b'*64,self.root)
    def test_reject_tampered_content(self):
        r.store(OP,CAND,'HOLD_PRECHECK',False,self.root)
        target = next(self.root.iterdir())
        target.write_text(target.read_text()+'x')
        with self.assertRaises(r.ReceiptHold): r.read_once(OP,CAND,self.root)
    def test_reject_symlink_target(self):
        target = self.root/(r.PREFIX+OP+'.json')
        target.symlink_to('/etc/passwd')
        with self.assertRaises(r.ReceiptHold): r.read_once(OP,CAND,self.root)
        with self.assertRaises(FileExistsError): r.store(OP,CAND,'HOLD_PRECHECK',False,self.root)
    def test_reject_symlink_root(self):
        with tempfile.TemporaryDirectory() as tmp:
            lnk=Path(tmp)/'link';lnk.symlink_to(self.root)
            with self.assertRaises(r.ReceiptHold):r.store(OP,CAND,'HOLD_PRECHECK',False,lnk)
    def test_no_token_or_catalog_in_record(self):
        r.store(OP,CAND,'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED',True,self.root)
        raw=next(self.root.iterdir()).read_text()
        for word in ('token','access','server','connection','password','schema_name'):
            self.assertNotIn(word,raw)
    def test_non_executable(self):
        self.assertEqual(r.main(),1)
if __name__=='__main__':unittest.main()
