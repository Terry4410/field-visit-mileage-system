"""Synthetic offline end-to-end tests for the one-shot transport boundary."""
import unittest
from pathb_readonly_oneshot_transport_20261113 import OneShotTransport, TransportHold
from pathb_readonly_stdin_frame_20261113 import decode_frame

NONCE="a"*32
TOKEN="a"*60+"."+"b"*60+"."+"c"*60

class OneShotTests(unittest.TestCase):
    def test_single_success_and_no_replay(self):
        frames=[]
        def fake(frame):
            frames.append(frame)
            self.assertEqual(decode_frame(frame,NONCE),TOKEN)
            return b"REDACTED"
        runner=OneShotTransport(fake)
        self.assertEqual(runner.dispatch(TOKEN,NONCE),b"REDACTED")
        with self.assertRaises(TransportHold):
            runner.dispatch(TOKEN,NONCE)
        self.assertEqual(len(frames),1)
    def test_timeout_is_terminal_and_sanitized(self):
        calls=[]
        def timeout(frame):
            calls.append(1)
            raise TimeoutError("SECRET="+TOKEN)
        runner=OneShotTransport(timeout)
        try:
            runner.dispatch(TOKEN,NONCE)
        except TransportHold as exc:
            self.assertEqual(str(exc),"TRANSPORT_HOLD")
            self.assertNotIn(TOKEN,str(exc))
        else:
            self.fail("expected HOLD")
        with self.assertRaises(TransportHold):
            runner.dispatch(TOKEN,NONCE)
        self.assertEqual(len(calls),1)
    def test_invalid_token_consumes_attempt(self):
        calls=[]
        runner=OneShotTransport(lambda frame:calls.append(frame))
        with self.assertRaises(TransportHold):
            runner.dispatch("bad",NONCE)
        with self.assertRaises(TransportHold):
            runner.dispatch(TOKEN,NONCE)
        self.assertEqual(calls,[])
    def test_oversize_response_is_terminal(self):
        runner=OneShotTransport(lambda frame:b"x"*8193)
        with self.assertRaises(TransportHold):
            runner.dispatch(TOKEN,NONCE)
        with self.assertRaises(TransportHold):
            runner.dispatch(TOKEN,NONCE)

if __name__=="__main__":
    unittest.main()
