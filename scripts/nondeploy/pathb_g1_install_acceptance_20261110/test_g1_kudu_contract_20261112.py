"""NONDEPLOY G1 Kudu contract regression tests; no Azure/SQL/Blob/Token calls."""
import datetime
import hashlib
import subprocess
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

import p3_kudu_cloudshell_parent as parent
import p3_app_installer_runner as child
import p3_blob_guard as guard
import p3_kudu_transport as kudu


class KuduContractTests(unittest.TestCase):
    def test_parent_and_runner_use_distinct_kudu_approval(self):
        self.assertEqual(parent.TRANSPORT, "OFFICIAL_AZ_WEBAPP_EXEC_APP_KUDU_V1")
        self.assertEqual(parent.CONTRACT, "FIELDVISIT_PATHB_G1_KUDU_INSTALL_V1")
        self.assertEqual(child.EXPECTED_APPROVAL, parent.DECISION)
        self.assertNotEqual(parent.DECISION, "OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE")

    def test_legacy_ssh_approval_rejected_by_parent_and_child(self):
        now = datetime.datetime.now(datetime.timezone.utc)
        auth = {
            "operation_id": "a" * 32,
            "contract": "FIELDVISIT_PATHB_G1_INSTALL_V1",
            "decision": "OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE",
            "artifact_sha256": "b" * 64,
            "binding_index_sha256": "c" * 64,
            "server": parent.SERVER,
            "database": parent.DATABASE,
            "installation_attempts": 1,
            "instance_sha256": parent.ident("worker-instance-0123456789"),
            "not_before": (now - datetime.timedelta(minutes=1)).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "expires_at": (now + datetime.timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
        with tempfile.TemporaryDirectory() as directory:
            f = Path(directory) / "synthetic-only.json"
            f.write_text(json.dumps(auth), encoding="utf-8")
            with self.assertRaises(parent.Hold):
                parent.validate_authorization(f, "b" * 64, "c" * 64,
                                              "worker-instance-0123456789")
        with self.assertRaises(child.Hold):
            child.verify_authorization(auth, "b" * 64, "c" * 64,
                                       parent.ident("worker-instance-0123456789"))

    def test_owner_pre_authorization_rejects_bad_scope_before_management(self):
        """No Azure calls on missing, expired or wrong Owner authorization."""
        now = datetime.datetime.now(datetime.timezone.utc)
        valid = {
            "operation_id": "a" * 32,
            "contract": parent.CONTRACT,
            "decision": parent.DECISION,
            "artifact_sha256": "b" * 64,
            "binding_index_sha256": "c" * 64,
            "server": parent.SERVER,
            "database": parent.DATABASE,
            "installation_attempts": 1,
            "instance_sha256": parent.ident("worker-instance-0123456789"),
            "not_before": (now - datetime.timedelta(minutes=1)).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "expires_at": (now + datetime.timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "auth.json"
            for change in ("missing", "expired", "wrong_scope", "wrong_instance_format"):
                with self.subTest(change=change):
                    auth = dict(valid)
                    if change == "expired":
                        auth["not_before"] = (now - datetime.timedelta(minutes=10)).strftime("%Y-%m-%dT%H:%M:%SZ")
                        auth["expires_at"] = (now - datetime.timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")
                    elif change == "wrong_scope":
                        auth["server"] = "unapproved.example"
                    elif change == "wrong_instance_format":
                        auth["instance_sha256"] = "not-a-hash"
                    if change != "missing":
                        path.write_text(json.dumps(auth), encoding="utf-8")
                    elif path.exists():
                        path.unlink()
                    with self.assertRaises((parent.Hold, FileNotFoundError)):
                        parent.validate_authorization(path, "b" * 64, "c" * 64, None)
            path.write_text(json.dumps(valid), encoding="utf-8")
            parent.validate_authorization(path, "b" * 64, "c" * 64, None)
            parent.validate_authorization(path, "b" * 64, "c" * 64, "worker-instance-0123456789")
            with self.assertRaises(parent.Hold):
                parent.validate_authorization(path, "b" * 64, "c" * 64, "wrong-instance")

    def test_parent_execute_pre_authorization_precedes_management(self):
        """Static source ordering prevents a regression to Azure-before-auth."""
        import inspect
        source = inspect.getsource(parent.execute)
        pre = source.index('stop = "PRE_AUTHORIZATION"')
        first = source.index('validate_authorization(args.authorization', pre)
        management = source.index('instance, facts = management(cfg)', first)
        second = source.index('validate_authorization(args.authorization', management)
        self.assertLess(pre, first)
        self.assertLess(first, management)
        self.assertLess(management, second)

    def test_blob_marker_is_new_and_guard_checks_old_g1(self):
        self.assertNotEqual(guard.NEW_BLOB, guard.PRIOR_G1_INSTALL_BLOB)
        self.assertEqual(guard.RUN_ID, "pathb-g1-original-installer-single-kudu-20261112")
        self.assertIn("PROTECTED_OWNER_KUDU", guard.REQUIRED_PRE_DDL_GATES)
        self.assertNotIn("PROTECTED_OWNER_SSH", guard.REQUIRED_PRE_DDL_GATES)

    def test_prior_g1_present_or_unknown_stops_before_new_marker(self):
        for prior_status, error_code in ((200, ""), (500, "")):
            observed = []
            def request(method, key, headers, body=b""):
                observed.append((method, key))
                if key == guard.PRIOR_G1_INSTALL_BLOB:
                    return guard.Response(prior_status, '"old-etag"', b"", error_code)
                return guard.Response(404, "", b"", "BlobNotFound")
            with self.subTest(prior_status=prior_status):
                with patch.object(guard, "validate_owner_storage_token", return_value=True):
                    g = guard.OneTimeInstallBlobGuard("synthetic-not-a-token",
                                                      request=request)
                    g.predecessors_verified = True
                    with self.assertRaises(guard.InstallGuardHold):
                        g.check_available()
                self.assertNotIn(("HEAD", guard.NEW_BLOB), observed)

    def test_kudu_cli_targets_one_main_app_container_interactively(self):
        args = kudu.interactive_exec_args("worker-instance-0123456789")
        self.assertEqual(args[:3], ["az", "webapp", "exec"])
        self.assertEqual(args[args.index("--mode") + 1], "shell")
        self.assertEqual(args[args.index("--target") + 1], "app")
        self.assertEqual(args[args.index("--instance") + 1],
                         "worker-instance-0123456789")
        self.assertNotIn("execute", args)
        self.assertNotIn("all", args)

    def test_full_remote_stage_shape_offline_with_synthetic_dependencies(self):
        """Prove the parent creates the exact nested 5-tool Stage, no live calls."""
        root = Path(parent.__file__).resolve().parent
        binding, _ = parent.validate_binding(root)
        cfg = json.loads((root / "executor_config.json").read_text("utf-8"))
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            (work / "debs").mkdir()
            (work / "original.zip").write_bytes(b"synthetic-only-not-real-sql")
            (work / "original.seal.json").write_bytes(b"{}")
            authorization = work / "synthetic-authorization.json"
            authorization.write_bytes(b"{}")
            for item in cfg["tools"]:
                (work / "debs" / (item["name"] + ".deb")).write_bytes(b"synthetic-deb")
            stage, hashes = parent.build_stage(work, binding, authorization,
                                                {"synthetic": True})
            names = kudu.validate_stage_archive(stage.read_bytes())
            expected = (set(binding["files"]) |
                        {"binding-index.json", "executor_config.json",
                         "original.zip", "original.seal.json",
                         "live-facts.json", "owner-authorization.json"} |
                        {"debs/" + item["name"] + ".deb" for item in cfg["tools"]})
            self.assertEqual(set(names), expected)
            self.assertEqual(len(names), len(expected))
            self.assertEqual(set(hashes), expected)
            with zipfile.ZipFile(stage) as archive:
                for name, value in hashes.items():
                    self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), value)

    def test_distribution_archive_rejects_unbound_and_tampered_members(self):
        """No Azure: ZIP must exactly match every binding member."""
        root = Path(parent.__file__).resolve().parent
        binding, _ = parent.validate_binding(root)
        expected = sorted(set(binding["files"]) | {"binding-index.json"})
        with tempfile.TemporaryDirectory() as directory:
            archive_path = Path(directory) / "source.zip"
            def make_archive(extra=False, tamper=False):
                with zipfile.ZipFile(archive_path, "w") as archive:
                    for name in expected:
                        data = (root / name).read_bytes()
                        if tamper and name == "binding-index.json":
                            data += b"tampered"
                        archive.writestr(name, data)
                    if extra:
                        archive.writestr("unexpected.txt", b"unbound")
            make_archive()
            parent.validate_distribution_archive(archive_path, binding)
            for extra, tamper in ((True, False), (False, True)):
                with self.subTest(extra=extra, tamper=tamper):
                    make_archive(extra=extra, tamper=tamper)
                    with self.assertRaises(parent.Hold):
                        parent.validate_distribution_archive(archive_path, binding)

    def test_nested_debs_cleanup_accepts_exact_tree_and_rejects_extras(self):
        """Local-only subprocess: verify exact nested debs, fail closed on extras."""
        for extra in (False, True):
            with self.subTest(extra=extra), tempfile.TemporaryDirectory() as directory:
                stage = Path(directory) / "stage"
                stage.mkdir(mode=0o700)
                (stage / "debs").mkdir()
                (stage / "debs" / "tool.deb").write_bytes(b"synthetic-deb")
                (stage / "runner.py").write_bytes(b"synthetic-runner")
                if extra:
                    (stage / "debs" / "unbound.deb").write_bytes(b"extra")
                hashes = {
                    "debs/tool.deb": hashlib.sha256(b"synthetic-deb").hexdigest(),
                    "runner.py": hashlib.sha256(b"synthetic-runner").hexdigest(),
                }
                with patch.object(parent, "REMOTE_STAGE", str(stage)), \
                     patch.object(parent, "REMOTE_WORK", str(Path(directory) / "work")), \
                     patch.object(parent, "REMOTE_RAM", str(Path(directory) / "ram")):
                    command = parent.cleanup_command(hashes).decode("ascii")
                result = subprocess.run(command, shell=True, capture_output=True,
                                        text=True, timeout=10)
                expected = "HOLD" if extra else "PASS"
                self.assertIn("FV_KUDU_OWNED_CLEANUP=" + expected, result.stdout)
                self.assertEqual(stage.exists(), extra)

    def test_parent_uses_kudu_not_the_consumed_ssh_parent(self):
        src = (Path(__file__).resolve().parent / "p3_kudu_cloudshell_parent.py").read_text()
        self.assertIn("return KuduExecSession(instance, deadline=deadline)", src)
        self.assertNotIn("return SshExecSession(instance, deadline=deadline)", src)


if __name__ == "__main__":
    unittest.main()
