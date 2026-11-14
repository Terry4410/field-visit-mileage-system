"""Offline-only SELECT22 Parent gate tests. No Azure or SQL operations."""
from dataclasses import replace
import hashlib
import unittest
from unittest import mock
import pathb_select22_owner_parent_nondeploy as parent
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession
from pathb_select22_storage_once_nondeploy import OnceGuard
from pathb_select22_management_snapshot_nondeploy import VerifiedManagement


class ParentTests(unittest.TestCase):
    def setUp(self):
        self.stage = b"synthetic"
        self.digest = hashlib.sha256(self.stage).hexdigest()
        self.session = Select22StageSession("instance01234567", deadline=10**12)
        self.session.state.begin()
        self.addCleanup(self.session.close)
        self.guard = OnceGuard(lambda *args: self.fail("no HTTP in offline test"))
        self.e = parent.Evidence(
            parent.TARGET, parent.OWNER_SHA, self.digest,
            hashlib.sha256(self.session.instance.lower().encode()).hexdigest(),
            VerifiedManagement(self.session.instance,
                               hashlib.sha256(self.session.instance.lower().encode()).hexdigest(),
                               parent.TARGET[1], parent.TARGET[2],\n                               read_only_verified=True),
            True, True, True, True, True, True, True, True)

    def test_all_synthetic_flags_still_cannot_release_live_handoff(self):
        """Even forged PASS flags never call Blob, stage or SQL providers."""
        with mock.patch.object(parent, "STAGE_SHA", self.digest):
            with mock.patch.object(self.session, "stage_select22_archive") as stage:
                with self.assertRaisesRegex(
                    parent.ParentHold, "^PARENT_LIVE_EXECUTION_NOT_RELEASED$"):
                    parent.handoff_candidate(
                        self.e, stage_bytes=self.stage, session=self.session,
                        nonce="0"*32, storage_guard=self.guard,
                        sql_token_provider=lambda: self.fail("no token"))
                stage.assert_not_called()
                self.assertEqual(self.session.state.state, "CHANNEL")

    def test_invalid_session_returns_sanitized_hold(self):
        with mock.patch.object(parent, "STAGE_SHA", self.digest):
            with self.assertRaises(parent.ParentHold):
                parent.preflight(
                    self.e, self.stage, None, self.guard,
                    lambda: self.fail("no token"))


    def test_unpinned_stage_rejected(self):
        with self.assertRaises(parent.ParentHold):
            parent.preflight(self.e, self.stage, self.session, self.guard,
                             lambda: None)

    def test_each_missing_authorization_fails_before_handoff(self):
        flags = ("tenant_verified", "subscription_verified",
                 "unique_instance_verified", "ssh_host_key_pinned",
                 "private_network_verified", "storage_https_rbac_verified",
                 "exact_new_blob_grant_verified", "exact_sql_select_grant_verified")
        with mock.patch.object(parent, "STAGE_SHA", self.digest):
            for flag in flags:
                with self.subTest(flag=flag):
                    with self.assertRaises(parent.ParentHold):
                        parent.preflight(replace(self.e, **{flag: False}),
                                         self.stage, self.session, self.guard,
                                         lambda: None)

    def test_missing_management_snapshot_fails_closed(self):
        with mock.patch.object(parent, "STAGE_SHA", self.digest):
            with self.assertRaises(parent.ParentHold):
                parent.preflight(replace(self.e, management_snapshot=None),
                                 self.stage, self.session, self.guard, lambda: None)

    def test_wrong_identity_instance_or_target_rejected(self):
        with mock.patch.object(parent, "STAGE_SHA", self.digest):
            for modified in (replace(self.e, owner_oid_sha256="f"*64),
                             replace(self.e, instance_sha256="f"*64),
                             replace(self.e, target=("wrong",))):
                with self.assertRaises(parent.ParentHold):
                    parent.preflight(modified, self.stage, self.session,
                                     self.guard, lambda: None)


if __name__ == "__main__":
    unittest.main()
