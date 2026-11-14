"""Offline bounded SQL child tests: a local fake executable, no network/DB."""
from pathlib import Path
import os
import sys
import tempfile
import time
import unittest
from unittest import mock

import pathb_select22_bounded_sql_nondeploy as runner


HERE = Path(__file__).resolve().parent
QUERY = (HERE / "preflight.sql").read_bytes()


class BoundedSqlTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.dir = Path(self.tmp.name)
        self.binary = self.dir / "sqlcmd"
        self.tokenfile = self.dir / "sql-token"
        self.tokenfile.write_bytes(b"synthetic, not a credential")
        self.env = {"PATH": "/usr/bin:/bin", "HOME": str(self.dir), "LANG": "C.UTF-8"}

    def fake(self, body):
        self.binary.write_text("#!" + sys.executable + "\n" + body + "\n")
        self.binary.chmod(0o700)

    def call(self, timeout=2):
        return runner.sql_select22_once(
            self.binary, self.env, self.tokenfile, QUERY,
            time.monotonic() + timeout, 1)

    def test_synthetic_subprocess_returns_bytes_once(self):
        self.fake("import sys;sys.stdin.buffer.read();sys.stdout.buffer.write(b'FAKE_OK')")
        with mock.patch.object(runner.subprocess, "Popen",
                               wraps=runner.subprocess.Popen) as spawned:
            self.assertEqual(self.call(), b"FAKE_OK")
            self.assertEqual(spawned.call_count, 1)
            self.assertTrue(spawned.call_args.kwargs["start_new_session"])

    def test_nonzero_exit_is_terminal(self):
        self.fake("import sys;sys.stdin.buffer.read();sys.exit(7)")
        with self.assertRaisesRegex(runner.Select22SqlHold, "^SQL_CHILD_NONZERO_HOLD$"):
            self.call()

    def test_timeout_does_not_leave_live_child(self):
        self.fake("import time;time.sleep(10)")
        with self.assertRaisesRegex(runner.Select22SqlHold, "^SQL_TIMEOUT_OUTCOME_UNKNOWN$"):
            self.call(timeout=0.3)

    def test_bad_query_cannot_spawn_client(self):
        with mock.patch.object(runner.subprocess, "Popen") as spawned:
            with self.assertRaisesRegex(runner.Select22SqlHold, "^SQL_INPUT_HOLD$"):
                runner.sql_select22_once(
                    self.binary, self.env, self.tokenfile, b"SELECT 1;",
                    time.monotonic() + 5, 1)
            spawned.assert_not_called()

    def test_second_sql_call_index_is_blocked(self):
        with mock.patch.object(runner.subprocess, "Popen") as spawned:
            with self.assertRaisesRegex(runner.Select22SqlHold, "^SQL_PRECONDITION_HOLD$"):
                runner.sql_select22_once(
                    self.binary, self.env, self.tokenfile, QUERY,
                    time.monotonic() + 5, 2)
            spawned.assert_not_called()

    def test_ambient_auth_or_sql_config_is_forbidden(self):
        for bad in ("SQLCMDINI", "SQLCMDPASSWORD", "AZURE_CLIENT_SECRET", "IDENTITY_HEADER"):
            with self.subTest(bad=bad):
                env = {**self.env, bad: "not-real"}
                with mock.patch.object(runner.subprocess, "Popen") as spawned:
                    with self.assertRaisesRegex(runner.Select22SqlHold, "^SQL_PRECONDITION_HOLD$"):
                        runner.sql_select22_once(
                            self.binary, env, self.tokenfile, QUERY,
                            time.monotonic() + 5, 1)
                    spawned.assert_not_called()

    def test_no_imports_or_call_to_g1_installer(self):
        source = Path(runner.__file__).read_text()
        self.assertNotIn("p3_app_installer_runner", source)
        self.assertNotIn("runner_command(", source)
        self.assertNotIn("install.py", source)
        self.assertNotIn("get-access-token", source)
        self.assertNotIn("blob_download", source)


if __name__ == "__main__":
    unittest.main()
