"""Offline-only SELECT22 Parent gate tests. No Azure or SQL operations."""
from dataclasses import replace
import hashlib
import unittest
from unittest import mock
import pathb_select22_owner_parent_nondeploy as parent
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession
from pathb_select22_storage_once_nondeploy import OnceGuard


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
            True, True, True, True, True, True, True, True)

    def test_composition_propagates_only_after_exact_gates(self):
        with mock.patch.object(parent, "STAGE_SHA", self.digest):
            with mock.patch.object(parent, "handoff_after_storage_claim_once",
                                   return_value="OFFLINE") as flow:
                result = parent.handoff_candidate(
                    self.e, stage_bytes=self.stage, session=self.session,
                    nonce="0"*32, storage_guard=self.guard,
                    sql_token_provider=lambda: self.fail("no token"))
                self.assertEqual(result, "OFFLINE")
                flow.assert_called_once()

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
