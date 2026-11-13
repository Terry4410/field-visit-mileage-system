"""Synthetic end-to-end App-side SELECT22 candidate tests; STRICTLY offline."""
import ast
import hashlib
import os
from pathlib import Path
import shutil
import stat
import tempfile
import unittest
from unittest import mock

import pathb_select22_app_once_candidate as candidate
from pathb_connection_only_tool_stage_nondeploy import load_bound_config

HERE = Path(__file__).resolve().parent


def sql_row():
    return ("|".join([
        "FV_SQL", "sql-fieldvisit-jpe-pathb-uat",
        "db-fieldvisit-pathb-uat", "5", "AABBCC",
        "1", "1", "0", "TRUE", "FEDAUTH", "TCP",
    ] + ["0"] * 11) + "\n").encode()


class CandidateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.debs = self.root / "debs"
        self.debs.mkdir()
        self.fake_binary_hashes = {}
        self.cfg = load_bound_config()
        self.cfg["tools"] = [dict(tool) for tool in self.cfg["tools"]]
        for item in self.cfg["tools"]:
            data = ("SYNTHETIC_DEB_" + item["name"]).encode()
            (self.debs / (item["name"] + ".deb")).write_bytes(data)
            item["sha256"] = hashlib.sha256(data).hexdigest()
        self.calls = []
        self.token = "not-a-real-token"
        self.claim_dir = Path(tempfile.mkdtemp(prefix="fv-pathb-select22-", dir="/dev/shm"))
        os.chmod(self.claim_dir, 0o700)
        self.addCleanup(lambda: shutil.rmtree(self.claim_dir))
        self.patcher_config = mock.patch.object(candidate, "load_bound_config", return_value=self.cfg)
        self.patcher_auth = mock.patch.object(candidate, "validate_owner_token")
        self.patcher_tools = mock.patch.object(candidate, "_stage_verified_tools",
                                               side_effect=self.fake_stage)
        # Synthetic /dev/shm unit-test seam only. Production requires a
        # verified persistent /home mount and rejects this test path.
        self.patcher_claim = mock.patch.object(candidate, "_check_private_claim_dir",
                                               side_effect=self.fake_claim_gate)
        for patcher in (self.patcher_config, self.patcher_auth,
                        self.patcher_tools, self.patcher_claim):
            patcher.start()
            self.addCleanup(patcher.stop)

    def fake_claim_gate(self, folder):
        path = Path(folder)
        if stat.S_IMODE(path.stat().st_mode) != 0o700:
            raise candidate.Select22Hold("PRIVATE_DIR_HOLD")
        return path

    def fake_stage(self, cfg, debs, sandbox):
        binary = sandbox / "sqlcmd"
        binary.write_bytes(b"synthetic client never executed")
        return binary, {"PATH": "/usr/bin:/bin"}

    def invoke(self, binary, env, token_path, sql, deadline, index):
        self.calls.append((binary, env, token_path, sql, deadline, index))
        self.assertEqual(sql, (HERE / "preflight.sql").read_bytes())
        self.assertEqual(index, 1)
        self.assertEqual(token_path.read_bytes(), self.token.encode("utf-16le"))
        self.assertEqual(stat.S_IMODE(token_path.stat().st_mode), 0o600)
        self.assertTrue(binary.is_file())
        self.assertNotIn(self.token, repr(env))
        return sql_row()

    def test_one_good_dispatch_returns_only_sanitized_receipt(self):
        receipt = candidate.run_once(
            debs_dir=self.debs, claim_dir=self.claim_dir,
            sql_token=self.token, invoke_sql=self.invoke)
        self.assertEqual((receipt.sql_target, receipt.connection, receipt.schema),
                         ("PASS", "PASS", "EMPTY_CANDIDATE"))
        self.assertFalse(hasattr(receipt, "raw"))
        self.assertEqual(len(self.calls), 1)
        self.assertEqual((self.claim_dir / "readonly-sql-attempt-spent").read_bytes(),
                         b"SPENT\n")
        self.assertFalse((self.claim_dir / "sql-token").exists())

    def test_reentry_is_rejected_even_after_success(self):
        candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                           sql_token=self.token, invoke_sql=self.invoke)
        with self.assertRaisesRegex(candidate.Select22Hold, "SELECT22_ONCE_HOLD"):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=self.invoke)
        self.assertEqual(len(self.calls), 1)

    def test_transport_failure_consumes_one_attempt(self):
        def fail(*args):
            self.calls.append(1)
            raise TimeoutError("sensitive token should never print")
        with self.assertRaisesRegex(candidate.Select22Hold, "^SELECT22_ONCE_HOLD$"):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=fail)
        self.assertEqual(len(self.calls), 1)
        self.assertTrue((self.claim_dir / "readonly-sql-attempt-spent").exists())
        self.assertFalse((self.claim_dir / "sql-token").exists())

    def test_unexpected_sql_output_consumes_attempt(self):
        with self.assertRaisesRegex(candidate.Select22Hold, "^SELECT22_ONCE_HOLD$"):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=lambda *args: b"secret output")
        self.assertTrue((self.claim_dir / "readonly-sql-attempt-spent").exists())

    def test_missing_package_fails_before_claim(self):
        (self.debs / "libodbc2.deb").unlink()
        with self.assertRaises(Exception):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=self.invoke)
        self.assertFalse((self.claim_dir / "readonly-sql-attempt-spent").exists())
        self.assertEqual(self.calls, [])

    def test_tampered_package_fails_before_claim(self):
        (self.debs / "libodbc2.deb").write_bytes(b"BAD")
        with self.assertRaises(Exception):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=self.invoke)
        self.assertFalse((self.claim_dir / "readonly-sql-attempt-spent").exists())

    def test_existing_token_file_fails_before_claim(self):
        (self.claim_dir / "sql-token").write_bytes(b"unknown prior value")
        with self.assertRaisesRegex(candidate.Select22Hold, "^SELECT22_ONCE_HOLD$"):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=self.invoke)
        self.assertFalse((self.claim_dir / "readonly-sql-attempt-spent").exists())
        self.assertEqual(self.calls, [])

    def test_bad_private_dir_rejected_before_claim(self):
        os.chmod(self.claim_dir, 0o755)
        with self.assertRaisesRegex(candidate.Select22Hold, "PRIVATE_DIR_HOLD"):
            candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                               sql_token=self.token, invoke_sql=self.invoke)
        self.assertEqual(self.calls, [])

    def test_bad_token_is_rejected_before_claim(self):
        with mock.patch.object(candidate, "validate_owner_token",
                               side_effect=ValueError("reject auth")):
            with self.assertRaises(ValueError):
                candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                                   sql_token=self.token, invoke_sql=self.invoke)
        self.assertFalse((self.claim_dir / "readonly-sql-attempt-spent").exists())

    def test_corrupt_query_fails_before_claim(self):
        with mock.patch.object(candidate, "verify_exact_query",
                               side_effect=ValueError("bad sha")):
            with self.assertRaises(ValueError):
                candidate.run_once(debs_dir=self.debs, claim_dir=self.claim_dir,
                                   sql_token=self.token, invoke_sql=self.invoke)
        self.assertEqual(self.calls, [])
        self.assertFalse((self.claim_dir / "readonly-sql-attempt-spent").exists())

    def test_offline_contract_no_cli_entrypoint(self):
        tree = ast.parse(Path(candidate.__file__).read_text(encoding="utf-8"))
        self.assertFalse(any(isinstance(x, ast.Call) and
                             isinstance(x.func, ast.Name) and x.func.id == "run_once"
                             for x in tree.body))
        self.assertIn("NONDEPLOY_NO_LIVE_ENTRYPOINT", Path(candidate.__file__).read_text())


if __name__ == "__main__":
    unittest.main()
