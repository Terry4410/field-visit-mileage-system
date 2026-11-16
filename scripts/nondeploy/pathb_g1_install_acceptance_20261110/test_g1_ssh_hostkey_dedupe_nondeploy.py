"""No network: strict host-key parsing regression for G1 pinned SSH transport."""
import base64
import hashlib
import struct
import unittest

from p3_g1_pinned_ssh import validate_host_key_scan, validate_tunnel_host_fingerprint
from p3_kudu_transport import TransportHold
from p3_ssh_transport_contract import HOST_FP

PORT = 45678


def ssh_key(byte=7, kind=b'ssh-ed25519'):
    wire = struct.pack('>I', len(kind)) + kind + struct.pack('>I', 32) + bytes([byte]) * 32
    return base64.b64encode(wire)


def record(*, key=None, host=None, kind=b'ssh-ed25519'):
    return ((host or f'[127.0.0.1]:{PORT}'.encode()) + b' ' + kind + b' ' +
            (key if key is not None else ssh_key()))


class StrictHostKeyTests(unittest.TestCase):
    def assert_hold(self, raw, port=PORT):
        with self.assertRaises(TransportHold) as observed:
            validate_host_key_scan(raw, port)
        self.assertEqual(observed.exception.code, 'SSH_HOST_KEY_AMBIGUOUS_HOLD')

    def test_one_valid_record_returns_canonical_known_hosts(self):
        self.assertEqual(validate_host_key_scan(record() + b'\n', PORT), record() + b'\n')

    def test_exact_duplicate_two_lines_becomes_exactly_one(self):
        self.assertEqual(validate_host_key_scan((record() + b'\n') * 2, PORT), record() + b'\n')

    def test_exact_duplicate_up_to_eight_lines(self):
        self.assertEqual(validate_host_key_scan((record() + b'\n') * 8, PORT), record() + b'\n')

    def test_conflicting_ed25519_keys_fail_closed(self):
        self.assert_hold(record() + b'\n' + record(key=ssh_key(9)) + b'\n')

    def test_wrong_port_fails_closed(self):
        self.assert_hold(record(host=b'[127.0.0.1]:45679') + b'\n')

    def test_wrong_host_fails_closed(self):
        self.assert_hold(record(host=b'[localhost]:45678') + b'\n')

    def test_wrong_key_algorithm_fails_closed(self):
        self.assert_hold(record(kind=b'ssh-rsa') + b'\n')

    def test_wrong_ssh_wire_algorithm_fails_closed(self):
        self.assert_hold(record(key=ssh_key(kind=b'ssh-rsa')) + b'\n')

    def test_invalid_base64_fails_closed(self):
        self.assert_hold(record(key=b'A==') + b'\n')

    def test_random_base64_fails_closed(self):
        self.assert_hold(record(key=base64.b64encode(bytes([8]) * 51)) + b'\n')

    def test_extra_field_fails_closed(self):
        self.assert_hold(record() + b' additional\n')

    def test_comments_or_blank_lines_fail_closed(self):
        self.assert_hold(b'# banner\n' + record() + b'\n')
        self.assert_hold(record() + b'\n\n')

    def test_more_than_eight_lines_fail_closed(self):
        self.assert_hold((record() + b'\n') * 9)

    def test_output_above_size_limit_fails_closed(self):
        self.assert_hold((record() + b'\n') * 30)

    def test_no_key_fails_closed(self):
        with self.assertRaises(TransportHold) as caught:
            validate_host_key_scan(b'', PORT)
        self.assertEqual(caught.exception.code, 'SSH_HOST_KEY_NOT_OBSERVED_HOLD')

    def test_invalid_port_fails_closed(self):
        self.assert_hold(record() + b'\n', port=100)

    def test_mismatched_fingerprint_still_rejected(self):
        with self.assertRaises(TransportHold) as caught:
            validate_tunnel_host_fingerprint(b'256 SHA256:another-key sample (ED25519)\n')
        self.assertEqual(caught.exception.code, 'SSH_HOST_KEY_MISMATCH_HOLD')

    def test_multi_line_fingerprint_remains_rejected(self):
        with self.assertRaises(TransportHold) as caught:
            validate_tunnel_host_fingerprint((b'256 ' + HOST_FP.encode() + b' sample (ED25519)\n') * 2)
        self.assertEqual(caught.exception.code, 'SSH_HOST_KEY_AMBIGUOUS_HOLD')

    def test_fixed_fingerprint_not_rotated(self):
        self.assertEqual(HOST_FP, 'SHA256:FKM/np9YzcuZaOsPqy3TL4gcae0szuZ3Z0dEG06KyZk')


if __name__ == '__main__':
    unittest.main()
