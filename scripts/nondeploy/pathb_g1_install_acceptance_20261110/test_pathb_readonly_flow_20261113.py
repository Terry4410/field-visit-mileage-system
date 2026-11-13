"""Offline orchestration tests: process-safe claim, no retry on failure."""
import os
import tempfile
import unittest
from pathb_readonly_flow_20261113 import execute_once, ReadonlyFlowHold
from pathb_readonly_stdin_frame_20261113 import decode_frame

TOKEN="a"*60+"."+"b"*60+"."+"c"*60
NONCE="a"*32

class FlowTests(unittest.TestCase):
    def test_success_once(self):
        with tempfile.TemporaryDirectory() as d:
            os.chmod(d,0o700)
            calls=[]
            def transport(frame):
                calls.append(decode_frame(frame,NONCE))
                return b"synthetic receipt"
            parser=lambda raw: "SANITIZED" if raw==b"synthetic receipt" else None
            self.assertEqual(execute_once(d,TOKEN,NONCE,transport,receipt_parser=parser),"SANITIZED")
            with self.assertRaises(ReadonlyFlowHold):
                execute_once(d,TOKEN,NONCE,transport,receipt_parser=parser)
            self.assertEqual(calls,[TOKEN])
    def test_ambiguous_transport_failure_cannot_replay(self):
        with tempfile.TemporaryDirectory() as d:
            os.chmod(d,0o700)
            calls=[]
            def transport(frame):
                calls.append(1)
                raise TimeoutError("SECRET "+TOKEN)
            for _ in range(2):
                with self.assertRaises(ReadonlyFlowHold) as ctx:
                    execute_once(d,TOKEN,NONCE,transport)
                self.assertNotIn(TOKEN,str(ctx.exception))
            self.assertEqual(calls,[1])
    def test_bad_receipt_consumes_claim(self):
        with tempfile.TemporaryDirectory() as d:
            os.chmod(d,0o700)
            calls=[]
            def transport(frame):
                calls.append(1)
                return b"BAD"
            def parser(raw):
                raise ValueError("RAW SECRET "+TOKEN)
            for _ in range(2):
                with self.assertRaises(ReadonlyFlowHold):
                    execute_once(d,TOKEN,NONCE,transport,receipt_parser=parser)
            self.assertEqual(calls,[1])

if __name__=="__main__":
    unittest.main()
