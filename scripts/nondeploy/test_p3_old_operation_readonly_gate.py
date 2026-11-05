import contextlib
import io
import os
import unittest
from unittest.mock import patch

import p3_old_operation_readonly_gate as g

OP = 'a' * 32


class GateTests(unittest.TestCase):
    def test_rejects_unbound_or_noncanonical_inputs(self):
        for value in ('', 'a'*31, 'A'*32, '../tmp', '0'*33):
            self.assertEqual(g.result(value), 'HOLD_INPUT')
        self.assertEqual(g.result(OP, root_base='/private/tmp'), 'HOLD_INPUT')

    def test_absent_exact_root(self):
        original = g.os.lstat
        def fake_lstat(path):
            if path == '/tmp':
                return original(path)
            raise FileNotFoundError()
        with patch.object(g.os, 'lstat', side_effect=fake_lstat):
            self.assertEqual(g.result(OP), 'ABSENT')

    def test_symlink_and_file_hold(self):
        original = g.os.lstat
        for mode in (0o120777, 0o100600):
            fake = type('Stat', (), {'st_mode': mode, 'st_uid': os.geteuid()})()
            with self.subTest(mode=mode), patch.object(g.os, 'lstat', side_effect=lambda path: original(path) if path == '/tmp' else fake):
                self.assertEqual(g.result(OP), 'HOLD_UNEXPECTED_TYPE')

    def test_owner_mode_restriction(self):
        original = g.os.lstat
        for owner, mode, expected in ((os.geteuid(),0o40700,'OWNED_ROOT_PRESENT'),
                                      (os.geteuid(),0o40755,'HOLD_UNEXPECTED_OWNER_MODE'),
                                      (os.geteuid()+1,0o40700,'HOLD_UNEXPECTED_OWNER_MODE')):
            fake = type('Stat', (), {'st_mode': mode, 'st_uid': owner})()
            with self.subTest(owner=owner, mode=mode), patch.object(g.os, 'lstat', side_effect=lambda path: original(path) if path == '/tmp' else fake):
                self.assertEqual(g.result(OP), expected)

    def test_unknown_on_os_error(self):
        with patch.object(g.os, 'lstat', side_effect=PermissionError()):
            self.assertEqual(g.result(OP), 'UNKNOWN')

    def test_output_is_fixed_status_only_and_never_write(self):
        with patch.object(g, 'result', return_value='OWNED_ROOT_PRESENT'), patch.object(g.os, 'unlink', side_effect=AssertionError('write attempted'), create=True), contextlib.redirect_stdout(io.StringIO()) as out:
            self.assertEqual(g.main(['--operation-id', OP]), 2)
        self.assertEqual(out.getvalue(), 'FV_P3_OLD_ROOT={"status":"OWNED_ROOT_PRESENT"}\n')


if __name__ == '__main__':
    unittest.main()
