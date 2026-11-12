"""Offline-only classification and cleanup tests for the Path B G1 SSH delta.

This test deliberately stubs out Azure CLI, SSH, Kudu and filesystem processes.
It does not grant authorization or exercise any live transport.
"""
import importlib.util
import pathlib
import sys
import types
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parent
HOST_FP = 'SHA256:qlMlSfV+nfx3Y5BdqQC+4WY1VFChCdRZMjqE+T/NgOI'
CODES = {
    'SSH_OWNER_CONTEXT_HOLD', 'SSH_TUNNEL_EXIT_HOLD',
    'SSH_TUNNEL_TIMEOUT_HOLD', 'SSH_TUNNEL_SETUP_HOLD',
    'SSH_HOST_KEY_NOT_OBSERVED_HOLD', 'SSH_HOST_KEY_MISMATCH_HOLD',
    'SSH_SESSION_SPAWN_HOLD', 'INSTANCE_ID_HOLD',
    'SSH_HOST_KEY_AMBIGUOUS_HOLD',
}

class TransportHold(Exception):
    def __init__(self, code):
        self.code = code if code in CODES else 'UNCLASSIFIED_HOLD'
        super().__init__(self.code)

class FakeState:
    def __init__(self): self.state = 'NEW'
    def begin(self): self.state = 'CHANNEL'

class FakeKuduExecSession:
    def __init__(self, instance, *, deadline):
        self.instance, self.deadline = instance, deadline
        self.state, self.audit = FakeState(), []
        self.fd, self.proc = None, None
    def close(self):
        if self.fd is not None:
            import os
            os.close(self.fd)
            self.fd = None

class HostHold(Exception): pass

def pinned_host_fingerprint(raw):
    if HOST_FP not in raw.decode('ascii', 'replace').split():
        raise HostHold('synthetic sensitive detail: NEVER-PUBLISH')
    return True

stub_kudu = types.ModuleType('p3_kudu_transport')
stub_kudu.KuduExecSession = FakeKuduExecSession
stub_kudu.TransportHold = TransportHold
stub_kudu.MAX_OUTPUT_BYTES = 2048
stub_contract = types.ModuleType('p3_ssh_transport_contract')
stub_contract.APP = 'api-fieldvisit-pathb-uat'
stub_contract.RG = 'rg-fieldvisit-uat'
stub_contract.ssh_interactive_args = lambda p, h: ['ssh', '-tt', '-o', 'StrictHostKeyChecking=yes']
stub_contract.pinned_host_fingerprint = pinned_host_fingerprint
stub_contract.check_owner_account = lambda _r: True
with patch.dict(sys.modules, {'p3_kudu_transport': stub_kudu,
                              'p3_ssh_transport_contract': stub_contract}):
    spec = importlib.util.spec_from_file_location('p3_g1_pinned_ssh', ROOT/'p3_g1_pinned_ssh.py')
    ssh = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ssh)

class TestG1SshFailureClassification(unittest.TestCase):
    def assert_hold(self, code, callback):
        with self.assertRaises(TransportHold) as caught:
            callback()
        self.assertEqual(caught.exception.code, code)
        self.assertNotIn('NEVER-PUBLISH', str(caught.exception))
        return caught.exception

    def test_owner_context_failure_not_labeled_process(self):
        with patch.object(ssh.subprocess, 'run', side_effect=OSError('NEVER-PUBLISH')):
            self.assert_hold('SSH_OWNER_CONTEXT_HOLD', ssh.owner_subscription_id)

    def test_missing_tunnel_key_fails_closed(self):
        self.assert_hold('SSH_HOST_KEY_NOT_OBSERVED_HOLD',
                         lambda: ssh.validate_tunnel_host_fingerprint(b''))

    def test_wrong_tunnel_key_fails_closed(self):
        self.assert_hold('SSH_HOST_KEY_MISMATCH_HOLD',
                         lambda: ssh.validate_tunnel_host_fingerprint(b'256 SHA256:WRONG host'))

    def test_only_exact_pinned_tunnel_key_is_accepted(self):
        self.assertIsNone(ssh.validate_tunnel_host_fingerprint(
            ('256 '+HOST_FP+' test-host').encode('ascii')))

    def test_single_exact_scan_host_and_key_shape(self):
        raw = b'[127.0.0.1]:50000 ssh-ed25519 QUJDRA==\n'
        self.assertIsNone(ssh.validate_host_key_scan(raw, 50000))

    def test_multiple_scan_keys_rejected_before_trust(self):
        row = b'[127.0.0.1]:50000 ssh-ed25519 QUJDRA==\n'
        self.assert_hold('SSH_HOST_KEY_AMBIGUOUS_HOLD',
                         lambda: ssh.validate_host_key_scan(row + row, 50000))

    def test_wrong_scan_host_or_port_rejected(self):
        self.assert_hold('SSH_HOST_KEY_AMBIGUOUS_HOLD',
                         lambda: ssh.validate_host_key_scan(
                             b'[127.0.0.1]:50001 ssh-ed25519 QUJDRA==\n', 50000))
        self.assert_hold('SSH_HOST_KEY_AMBIGUOUS_HOLD',
                         lambda: ssh.validate_host_key_scan(
                             b'[evil.example]:50000 ssh-ed25519 QUJDRA==\n', 50000))

    def test_unexpected_key_algorithm_rejected(self):
        self.assert_hold('SSH_HOST_KEY_AMBIGUOUS_HOLD',
                         lambda: ssh.validate_host_key_scan(
                             b'[127.0.0.1]:50000 ssh-rsa QUJDRA==\n', 50000))

    def test_multiple_fingerprint_lines_cannot_whitelist_other_keys(self):
        self.assert_hold('SSH_HOST_KEY_AMBIGUOUS_HOLD',
                         lambda: ssh.validate_tunnel_host_fingerprint(
                             ('256 ' + HOST_FP + ' trusted (ED25519)\n' +
                              '256 SHA256:WRONG other (ED25519)\n').encode('ascii')))

    def test_inner_transport_hold_survives_session_start(self):
        session = ssh.SshExecSession('fixed-instance', deadline=999999999)
        with patch.object(ssh, 'AppSshTunnel') as klass:
            klass.return_value.start.side_effect = TransportHold('SSH_TUNNEL_EXIT_HOLD')
            self.assert_hold('SSH_TUNNEL_EXIT_HOLD', session.start)
            self.assertEqual(session.audit, [])
            self.assertEqual(session.state.state, 'CHANNEL')

    def test_ssh_spawn_failure_closes_both_pty_descriptors(self):
        session = ssh.SshExecSession('fixed-instance', deadline=999999999)
        attrs=[0,0,0,0,0,0,[0]*32]
        with patch.object(ssh,'AppSshTunnel') as klass, \
             patch.object(ssh.pty,'openpty',return_value=(451,452)), \
             patch.object(ssh.termios,'tcgetattr',return_value=attrs), \
             patch.object(ssh.termios,'tcsetattr'), \
             patch.object(ssh.subprocess,'Popen',side_effect=OSError('NEVER-PUBLISH')), \
             patch.object(ssh.os,'close') as close:
            klass.return_value.start.return_value.port = 40000
            klass.return_value.start.return_value.known_hosts = '/fake/known_hosts'
            self.assert_hold('SSH_SESSION_SPAWN_HOLD', session.start)
            self.assertEqual([x.args[0] for x in close.call_args_list], [452,451])
            klass.return_value.start.return_value.close.assert_called_once_with()

    def test_failed_tunnel_setup_is_sanitized_and_closed(self):
        tunnel=ssh.AppSshTunnel('fixed-instance',deadline=999999999)
        with patch.object(ssh.socket,'socket',side_effect=OSError('NEVER-PUBLISH')), \
             patch.object(tunnel,'close') as cleaned:
            self.assert_hold('SSH_TUNNEL_SETUP_HOLD', tunnel.start)
            cleaned.assert_called_once_with()

    def test_invalid_instance_does_not_spawn_tunnel(self):
        self.assert_hold('INSTANCE_ID_HOLD', lambda: ssh.ssh_tunnel_args('bad',3000,'x'))

if __name__=='__main__': unittest.main(verbosity=2)
