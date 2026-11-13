"""Offline two-sided integration: sender -> frame -> App receiver -> receipt.

No Azure, SSH, SQL, secrets, deployment or live entrypoint. Both ends of
the exact same frame are exercised with a synthetic SQL row.
"""
import os
import tempfile
import unittest
from pathb_readonly_flow_20261113 import execute_once, ReadonlyFlowHold
from pathb_readonly_ssh_receiver_20261113 import receive_once, ReceiverHold
from pathb_readonly_stdin_frame_20261113 import encode_frame

TOKEN="a"*60+"."+"b"*60+"."+"c"*60
NONCE="a"*32

def sql_row(server="sql-fieldvisit-jpe-pathb-uat"):
    return ("|".join(["FV_SQL",server,"db-fieldvisit-pathb-uat","5",
        "AABBCC","1","1","0","TRUE","FEDAUTH","TCP"]+["0"]*11)+"\n").encode()

class TwoSidedTests(unittest.TestCase):
    def test_sender_and_receiver_share_same_protocol(self):
        with tempfile.TemporaryDirectory() as d:
            os.chmod(d,0o700)
            counts={"transport":0,"sql":0}
            def sql_runner(token):
                self.assertEqual(token,TOKEN)
                counts["sql"]+=1
                return sql_row()
            def channel(frame):
                counts["transport"]+=1
                receipt=receive_once(frame,NONCE,sql_runner)
                self.assertEqual(receipt.sql_target,"PASS")
                # A real reviewed remote parent must serialize only these
                # sanitized fields; never serialize the raw SQL row.
                return b"SANITIZED_RECEIPT"
            def parser(raw):
                if raw!=b"SANITIZED_RECEIPT":
                    raise ValueError("untrusted response")
                return "PASS"
            self.assertEqual(execute_once(d,TOKEN,NONCE,channel,receipt_parser=parser),"PASS")
            with self.assertRaises(ReadonlyFlowHold):
                execute_once(d,TOKEN,NONCE,channel,receipt_parser=parser)
            self.assertEqual(counts,{"transport":1,"sql":1})
    def test_remote_rejection_spends_attempt(self):
        with tempfile.TemporaryDirectory() as d:
            os.chmod(d,0o700)
            calls=[]
            def channel(frame):
                calls.append(1)
                receive_once(frame,"b"*32,lambda t:sql_row())
                return b"UNREACHABLE"
            for _ in range(2):
                with self.assertRaises(ReadonlyFlowHold):
                    execute_once(d,TOKEN,NONCE,channel)
            self.assertEqual(calls,[1])

if __name__=="__main__":
    unittest.main()
