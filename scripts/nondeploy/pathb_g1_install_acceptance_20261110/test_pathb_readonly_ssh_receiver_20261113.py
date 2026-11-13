"""Synthetic App-side receiver tests, no SSH or SQL."""
import unittest
from pathb_readonly_ssh_receiver_20261113 import receive_once, ReceiverHold
from pathb_readonly_stdin_frame_20261113 import encode_frame

TOKEN="a"*60+"."+"b"*60+"."+"c"*60
NONCE="a"*32

def row():
    return ("|".join(['FV_SQL','sql-fieldvisit-jpe-pathb-uat','db-fieldvisit-pathb-uat',
        '5','AABBCC','1','1','0','TRUE','FEDAUTH','TCP']+['0']*11)+"\n").encode()

class ReceiverTests(unittest.TestCase):
    def test_sanitized_receipt_only(self):
        calls=[]
        def fake(token):
            calls.append(token)
            return row()
        receipt=receive_once(encode_frame(TOKEN,NONCE),NONCE,fake)
        self.assertEqual(receipt.sql_target,"PASS")
        self.assertNotIn("AABBCC",str(receipt))
        self.assertNotIn(TOKEN,str(receipt))
        self.assertEqual(calls,[TOKEN])
    def test_wrong_nonce_never_invokes_runner(self):
        calls=[]
        with self.assertRaises(ReceiverHold):
            receive_once(encode_frame(TOKEN,NONCE),"b"*32,lambda t:calls.append(t))
        self.assertEqual(calls,[])
    def test_sql_error_redacted(self):
        def fail(token):
            raise RuntimeError("secret="+token)
        with self.assertRaises(ReceiverHold) as ctx:
            receive_once(encode_frame(TOKEN,NONCE),NONCE,fail)
        self.assertNotIn(TOKEN,str(ctx.exception))
    def test_wrong_target_rejected(self):
        bad=row().replace(b"sql-fieldvisit-jpe-pathb-uat",b"other")
        with self.assertRaises(ReceiverHold):
            receive_once(encode_frame(TOKEN,NONCE),NONCE,lambda t:bad)

if __name__=="__main__":
    unittest.main()
