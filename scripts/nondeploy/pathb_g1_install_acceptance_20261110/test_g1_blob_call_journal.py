import unittest
from unittest.mock import Mock
import g1_blob_call_journal_REVIEW_ONLY as m

OP='a'*32;CLAIM='b'*64;SQL='c'*64

class Tests(unittest.TestCase):
    def test_fixed_minimal_shape(self):
        r=m.construct(OP,CLAIM,0,'BEFORE',SQL)
        self.assertTrue(r.key.endswith('/00-before.json'))
        self.assertIn(b'"phase":"BEFORE"',r.payload)
        self.assertNotIn(b'exit_status',r.payload)
    def test_after(self):
        r=m.construct(OP,CLAIM,15,'AFTER',SQL,'UNKNOWN')
        self.assertTrue(r.key.endswith('/15-after.json'))
        self.assertIn(b'"exit_status":"UNKNOWN"',r.payload)
    def test_reject_bad_operation(self):
        with self.assertRaises(m.JournalHold): m.construct('../wrong',CLAIM,0,'BEFORE',SQL)
    def test_reject_bad_index(self):
        with self.assertRaises(m.JournalHold): m.construct(OP,CLAIM,16,'BEFORE',SQL)
    def test_reject_phase_status_mismatch(self):
        with self.assertRaises(m.JournalHold): m.construct(OP,CLAIM,0,'BEFORE',SQL,'SUCCESS')
        with self.assertRaises(m.JournalHold): m.construct(OP,CLAIM,0,'AFTER',SQL)
    def test_reject_extra_contents(self):
        with self.assertRaises(m.JournalHold): m.construct(OP,CLAIM,0,'AFTER',SQL,'ERROR: password=abc')
    def test_success_with_confirmed_readback(self):
        r=m.construct(OP,CLAIM,1,'BEFORE',SQL)
        q=Mock(side_effect=[(201,'"abc"',b''),(200,'"abc"',r.payload)])
        self.assertEqual(m.persist_exact(r,q),r.digest)
        self.assertEqual(q.call_count,2)
        self.assertEqual(q.call_args_list[0].args[3]['If-None-Match'],'*')
    def test_put_unknown_must_not_read_retry(self):
        r=m.construct(OP,CLAIM,1,'BEFORE',SQL)
        q=Mock(side_effect=OSError('timeout'))
        with self.assertRaises(m.JournalHold):m.persist_exact(r,q)
        self.assertEqual(q.call_count,1)
    def test_blob_present_blocks_replay(self):
        r=m.construct(OP,CLAIM,1,'BEFORE',SQL)
        q=Mock(return_value=(412,'',b''))
        with self.assertRaises(m.JournalHold):m.persist_exact(r,q)
        self.assertEqual(q.call_count,1)
    def test_get_wrong_must_hold(self):
        r=m.construct(OP,CLAIM,1,'BEFORE',SQL)
        q=Mock(side_effect=[(201,'"a"',b''),(200,'"b"',r.payload)])
        with self.assertRaises(m.JournalHold):m.persist_exact(r,q)
    def test_recovery_missing_unknown(self):
        r=m.construct(OP,CLAIM,1,'BEFORE',SQL)
        with self.assertRaises(m.JournalHold):m.recover_exact(r,lambda *args:(404,'',b''))
    def test_recovery_readonly(self):
        r=m.construct(OP,CLAIM,1,'BEFORE',SQL)
        q=Mock(return_value=(200,'"a"',r.payload))
        self.assertEqual(m.recover_exact(r,q),r.digest)
        self.assertEqual(q.call_args.args[0],'GET')
    def test_no_live_entrypoint(self):
        self.assertEqual(m.main(),1)

if __name__=='__main__':unittest.main()
