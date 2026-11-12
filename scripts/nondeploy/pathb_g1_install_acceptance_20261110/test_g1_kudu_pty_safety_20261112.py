"""G1 Kudu PTY safety: local offline tests. NO Azure/SSH/SQL/Blob/Token calls."""
import os
import select
import termios
import time
import unittest
from unittest.mock import Mock, patch

import p3_kudu_transport as transport


class TestKuduPTY(unittest.TestCase):
    def new_session(self, *, popen=None):
        return transport.KuduExecSession(
            "worker-instance-0123456789", deadline=time.monotonic() + 10,
            **({"popen": popen} if popen is not None else {})
        )

    def test_local_pty_preserves_long_synthetic_input(self):
        holders = []
        class FakeProcess:
            def __init__(self, fd):
                self.reader = os.dup(fd)
            def poll(self):
                return 0  # The fake child is not running.
        def fake_popen(argv, *, stdin, stdout, stderr, start_new_session):
            self.assertEqual(argv[:3], ["az", "webapp", "exec"])
            self.assertEqual(stdin, stdout)
            self.assertEqual(stdin, stderr)
            self.assertTrue(start_new_session)
            proc = FakeProcess(stdin)
            holders.append(proc)
            return proc

        session = self.new_session(popen=fake_popen)
        try:
            session.start()   # Only opens local PTY; fake_popen prevents Azure.
            fd = holders[0].reader
            flags = termios.tcgetattr(fd)[3]
            self.assertFalse(flags & termios.ECHO)
            self.assertFalse(flags & termios.ICANON)
            payload = b"A" * 7000 + b"\n"  # NOT a credential.
            for pos in range(0, len(payload), 350):
                os.write(session.fd, payload[pos:pos + 350])
            observed = b""
            end = time.monotonic() + 3
            while len(observed) < len(payload) and time.monotonic() < end:
                if select.select([fd], [], [], 0.05)[0]:
                    observed += os.read(fd, 16000)
            self.assertEqual(observed, payload)
        finally:
            session.close()
            for proc in holders:
                os.close(proc.reader)

    def test_remote_disables_echo_and_canonical_mode_before_handoff(self):
        session = self.new_session()
        session.state.begin()
        sends = []
        session.send_public = lambda content, code: sends.append((content, code))
        session.expect_fixed = Mock()
        session.establish_safe_channel()
        self.assertEqual(sends[0], (
            b"stty -echo -icanon min 1 time 0\n", "ECHO_DISABLE_SENT"))
        self.assertEqual(sends[1][1], "ECHO_PROBE_SENT")
        session.expect_fixed.assert_called_once()

    def test_termios_setup_error_closes_both_pty_descriptors(self):
        session = self.new_session(popen=Mock(side_effect=AssertionError("SHOULD_NOT_SPAWN")))
        with patch.object(transport.pty, "openpty", return_value=(111, 222)), \
             patch.object(transport.termios, "tcgetattr", side_effect=OSError("SENSITIVE")), \
             patch.object(transport.os, "close") as close:
            with self.assertRaises(transport.TransportHold) as err:
                session.start()
            self.assertEqual(str(err.exception), "KUDU_PROCESS_HOLD")
            self.assertEqual([x.args[0] for x in close.call_args_list], [111, 222])

    def test_spawn_error_is_sanitized_and_closes_local_pty(self):
        session = self.new_session(popen=Mock(side_effect=OSError("SENSITIVE")))
        with self.assertRaises(transport.TransportHold) as err:
            session.start()
        self.assertEqual(str(err.exception), "KUDU_PROCESS_HOLD")
        self.assertIsNone(session.fd)
        self.assertNotIn("SENSITIVE", str(err.exception))

    def test_close_clears_secret_memory(self):
        session = self.new_session()
        secret = bytearray(b"synthetic-NEVER-A-REAL-TOKEN")
        session._secrets.append(secret)
        session.close()
        self.assertEqual(secret, bytearray(len(secret)))
        self.assertEqual(session._secrets, [])


if __name__ == "__main__":
    unittest.main()
