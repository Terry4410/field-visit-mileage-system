"""Targeted NONDEPLOY regression for SSH tunnel start failure cleanup."""
import unittest
from unittest.mock import patch
import p3_g1_pinned_ssh as ssh

class TestTunnelFailureCleanup(unittest.TestCase):
    def test_popen_failure_closes_tunnel(self):
        t=ssh.AppSshTunnel('workerinstance0001',99999999)
        with patch.object(ssh,'owner_subscription_id',return_value='12345678-1234-1234-1234-123456789012'), \
             patch.object(ssh.subprocess,'Popen',side_effect=OSError('synthetic failed spawn')), \
             patch.object(t,'close') as cleanup:
            with self.assertRaises(ssh.TransportHold):t.start()
        cleanup.assert_called_once_with()

    def test_tunnel_early_close_cannot_leave_az_process(self):
        class FakeProcess:
            pid=888
            stdout=None
            stderr=None
            def poll(self): return None
            def wait(self,timeout=None): return 0
        t=ssh.AppSshTunnel('workerinstance0001',99999999)
        fake=FakeProcess()
        with patch.object(ssh,'owner_subscription_id',return_value='12345678-1234-1234-1234-123456789012'), \
             patch.object(ssh.subprocess,'Popen',return_value=fake), \
             patch.object(ssh.os,'set_blocking',side_effect=OSError('synthetic pipe failure')), \
             patch.object(ssh.os,'killpg') as kill, \
             patch.object(fake,'poll',return_value=None), \
             patch.object(fake,'wait',return_value=0) as wait:
            # The fake must expose file descriptors before set_blocking fails.
            from io import BytesIO
            fake.stdout=BytesIO(b'x')
            fake.stderr=BytesIO(b'x')
            with self.assertRaises(ssh.TransportHold):t.start()
        kill.assert_called_once_with(888,ssh.signal.SIGTERM)
        wait.assert_called_once()

if __name__ == '__main__': unittest.main()
