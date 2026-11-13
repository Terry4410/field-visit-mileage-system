"""Offline tests for the existing isolated SELECT-only orchestrator.
No live SSH, token acquisition, Azure or SQL operations.
"""
import pathlib
import tempfile
import unittest
from unittest.mock import patch
import pathb_readonly_once as once

class OnceTests(unittest.TestCase):
    def test_missing_injector_fails_closed(self):
        with self.assertRaises(once.ExecutionHold):
            once.evaluate_once(None, pathlib.Path('/opt/sqlcmd'), pathlib.Path('/tmp/sql-token'))

    def test_fixed_sql_dispatched_once_and_receipt_redacted(self):
        calls = []
        raw = b'SECRET_SQL_SID|other sensitive data'
        class SafeReceipt:
            sql_target = 'PASS'
            connection = 'PASS'
            schema = 'EMPTY_CANDIDATE'
            next_action = 'SEPARATE_INSTALL_AUTH_REQUIRED'
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / 'preflight.sql').write_bytes(b'PINNED_SQL')
            def invoke(args, sql):
                calls.append((args, sql))
                return raw
            with patch.object(once, 'verify', return_value={'SOURCE_BINDING':'PASS'}), \
                 patch.object(once, 'sqlcmd_argv', return_value=['sqlcmd','-G']), \
                 patch.object(once, 'parse_preflight', return_value=SafeReceipt()):
                receipt = once.evaluate_once(invoke, pathlib.Path('/opt/sqlcmd'),
                                             pathlib.Path('/tmp/sql-token'), root)
            self.assertEqual(len(calls), 1)
            self.assertEqual(calls[0][1], b'PINNED_SQL')
            self.assertFalse(hasattr(receipt, 'raw'))
            self.assertEqual(receipt.schema, 'EMPTY_CANDIDATE')

    def test_invoke_error_never_retries(self):
        count = 0
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / 'preflight.sql').write_bytes(b'PINNED_SQL')
            def invoke(args, sql):
                nonlocal count
                count += 1
                raise TimeoutError('secret error detail')
            with patch.object(once, 'verify', return_value=True), \
                 patch.object(once, 'sqlcmd_argv', return_value=['sqlcmd','-G']):
                with self.assertRaisesRegex(once.ExecutionHold, '^READONLY_EXECUTION_HOLD$'):
                    once.evaluate_once(invoke, pathlib.Path('/opt/sqlcmd'),
                                       pathlib.Path('/tmp/sql-token'), root)
            self.assertEqual(count, 1)

if __name__ == '__main__':
    unittest.main()
