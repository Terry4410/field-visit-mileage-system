"""Offline-only SELECT22 transport composition: one stage, one token, one receipt."""
import unittest
import hashlib
from unittest import mock

import pathb_select22_handoff_nondeploy as flow
from pathb_select22_receipt_bridge_nondeploy import EMPTY
from pathb_select22_storage_once_nondeploy import OnceGuard, Response
from pathb_select22_isolated_ssh_stage_nondeploy import Select22StageSession

TOKEN = "a" * 60 + "." + "b" * 60 + "." + "c" * 60
NONCE = "0123456789abcdef0123456789abcdef"
SHA = "b" * 64


class DirectHandoffProhibitedTests(unittest.TestCase):
    def test_obsolete_direct_token_path_fail_closed_without_any_action(self):
        session = Select22StageSession("instance01234567", deadline=10**12)
        session.state.begin()
        try:
            with mock.patch.object(session, "stage_select22_archive") as stage, \
                 mock.patch.object(session, "send_select22_frame_once") as send:
                with self.assertRaisesRegex(
                    flow.HandoffHold, "^EXTERNAL_STORAGE_CLAIM_REQUIRED$"):
                    flow.handoff_once(
                        session, stage_bytes=b"dummy", stage_sha=SHA,
                        nonce=NONCE, sql_token=TOKEN)
                stage.assert_not_called()
                send.assert_not_called()
                self.assertEqual(session.state.state, "CHANNEL")
        finally:
            session.close()


class ExternalBlobHandoffTests(unittest.TestCase):
    def setUp(self):
        self.session = Select22StageSession("instance01234567", deadline=10**12)
        self.session.state.begin()
        self.addCleanup(self.session.close)
        self.instance_sha = hashlib.sha256(self.session.instance.lower().encode()).hexdigest()
        self.events = []

    def _stage(self, data, digest):
        self.events.append("stage")
        self.session.state.advance("CHANNEL", "STAGED")

    def _token(self, token, nonce):
        self.events.append("token_sent")
        self.session.state.advance("STAGED", "TOKENS")

    def _ready(self, session):
        self.events.append("ready")

    def _receipt(self, session):
        self.events.append("receipt")
        return EMPTY

    def _claim(self, method, account, container, key, headers, payload):
        self.events.append(method.lower())
        if method == "PUT":
            self.assertEqual(headers["If-None-Match"], "*")
            self.payload = payload
            return Response(201, '"etag"')
        self.assertEqual(method, "GET")
        self.assertEqual(headers, {"If-Match": '"etag"'})
        return Response(200, '"etag"', self.payload)

    def _grant(self, *, claim=None, auth=True):
        guard = claim or OnceGuard(self._claim)
        provider = lambda: self.events.append("sql_token_requested") or TOKEN
        with mock.patch.object(self.session, "stage_select22_archive",
                               side_effect=self._stage), \
             mock.patch.object(self.session, "send_public",
                               side_effect=lambda *a: self.events.append("remote")), \
             mock.patch.object(self.session, "send_select22_frame_once",
                               side_effect=self._token), \
             mock.patch.object(flow, "expect_ready", side_effect=self._ready), \
             mock.patch.object(flow, "read_status", side_effect=self._receipt):
            return flow.handoff_after_storage_claim_once(
                self.session, stage_bytes=b"mock-stage", stage_sha=SHA,
                nonce=NONCE, instance_sha256=self.instance_sha,
                storage_guard=guard, sql_token_provider=provider,
                owner_storage_approved=auth)

    def test_blob_confirmed_before_first_sql_token_request(self):
        result = self._grant()
        self.assertEqual(result, EMPTY)
        self.assertEqual(self.events, [
            "stage", "remote", "ready", "put", "get",
            "sql_token_requested", "token_sent", "receipt",
        ])
        self.assertEqual(self.session.state.state, "COMPLETE")

    def test_blob_unknown_prevents_sql_token_and_replay(self):
        calls = []
        def timeout(method, *args):
            calls.append(method)
            raise TimeoutError("uncertain commit")
        guard = OnceGuard(timeout)
        with self.assertRaises(flow.HandoffHold):
            self._grant(claim=guard)
        self.assertEqual(calls, ["PUT"])
        self.assertEqual(self.session.state.state, "UNKNOWN")
        self.assertNotIn("sql_token_requested", self.events)
        with self.assertRaises(flow.HandoffHold):
            self._grant(claim=guard)
        self.assertEqual(calls, ["PUT"])

    def test_missing_new_storage_approval_blocks_all_actions(self):
        with self.assertRaisesRegex(flow.HandoffHold, "^EXTERNAL_CLAIM_PRECONDITION_HOLD$"):
            self._grant(auth=False)
        self.assertEqual(self.events, [])

    def test_mismatched_instance_provenance_blocks_before_claim(self):
        guard = OnceGuard(self._claim)
        with self.assertRaisesRegex(flow.HandoffHold, "^EXTERNAL_CLAIM_PRECONDITION_HOLD$"):
            flow.handoff_after_storage_claim_once(
                self.session, stage_bytes=b"mock", stage_sha=SHA, nonce=NONCE,
                instance_sha256="f" * 64, storage_guard=guard,
                sql_token_provider=lambda: TOKEN, owner_storage_approved=True)
        self.assertEqual(self.events, [])

    def test_staging_failure_no_blob_write_or_sql_token(self):
        guard = OnceGuard(self._claim)
        with mock.patch.object(self.session, "stage_select22_archive",
                               side_effect=TimeoutError("failed stage")):
            with self.assertRaises(flow.HandoffHold):
                flow.handoff_after_storage_claim_once(
                    self.session, stage_bytes=b"mock", stage_sha=SHA, nonce=NONCE,
                    instance_sha256=self.instance_sha, storage_guard=guard,
                    sql_token_provider=lambda: self.events.append("sql_token_requested"),
                    owner_storage_approved=True)
        self.assertEqual(self.events, [])
        self.assertEqual(self.session.state.state, "UNKNOWN")

if __name__ == "__main__":
    unittest.main()
