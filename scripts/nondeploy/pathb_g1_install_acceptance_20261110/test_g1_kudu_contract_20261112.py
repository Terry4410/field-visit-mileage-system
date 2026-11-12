"""NONDEPLOY G1 Kudu contract regression tests; no Azure/SQL/Blob/Token calls."""
import datetime
import json
import tempfile
import unittest
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

    def test_parent_uses_kudu_not_the_consumed_ssh_parent(self):
        src = (Path(__file__).resolve().parent / "p3_kudu_cloudshell_parent.py").read_text()
        self.assertIn("return KuduExecSession(instance, deadline=deadline)", src)
        self.assertNotIn("return SshExecSession(instance, deadline=deadline)", src)


if __name__ == "__main__":
    unittest.main()
