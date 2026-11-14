"""Shared-stage handoff tests with real session state/OnceGuard, fake I/O."""
import hashlib
import unittest
from unittest import mock

import pathb_select22_owner_shared_handoff_nondeploy as h
from pathb_select22_storage_once_nondeploy import OnceGuard, Response
from pathb_select22_ssh_session_nondeploy import Select22SshSession
from pathb_select22_receipt_bridge_nondeploy import EMPTY

NONCE = "a" * 32
TOKEN = "fake" + "." + "safe" + "." + "offline"


class SharedHandoffTests(unittest.TestCase):
    def setUp(self):
        self.session = Select22SshSession("instance01234567", deadline=10**12)
        self.session.state.begin()
        self.addCleanup(self.session.close)
        self.instance_hash = hashlib.sha256(
            self.session.instance.lower().encode()).hexdigest()
        self.events = []
        self.saved = None

    def storage_request(self, method, account, container, key, headers, body):
        self.events.append(method)
        if method == "PUT":
            self.assertEqual(headers["If-None-Match"], "*")
            self.saved = body
            return Response(201, '"e"')
        self.assertEqual(headers["If-Match"], '"e"')
        return Response(200, '"e"', self.saved)

    def dispatch(self, *, guard=None, grant=True, instance=None,
                 ready=True, provider=None):
        g = guard if guard is not None else OnceGuard(self.storage_request)
        token_fn = provider if provider is not None else (
            lambda: self.events.append("SQL_TOKEN") or TOKEN)
        def send(frame, nonce):
            self.events.append("FRAME")
            self.session.state.advance("STAGED", "TOKENS")
        def ready_fn(session):
            self.events.append("READY")
            if not ready:
                raise ValueError("synthetic pre-ready failure")
        def recv(session):
            self.events.append("RECEIPT")
            return EMPTY
        with mock.patch.object(self.session, "send_public",
                               side_effect=lambda *_: self.events.append("COMMAND")), \
             mock.patch.object(self.session, "send_select22_frame_once",
                               side_effect=send), \
             mock.patch.object(h, "expect_ready", side_effect=ready_fn), \
             mock.patch.object(h, "read_status", side_effect=recv):
            return h.handoff_shared_after_external_claim_once(
                self.session, nonce=NONCE,
                instance_sha256=self.instance_hash if instance is None else instance,
                storage_guard=g, sql_token_provider=token_fn,
                owner_new_grant_attested=grant)

    def test_existing_stage_no_reupload_blob_before_sql(self):
        result = self.dispatch()
        self.assertEqual(result, EMPTY)
        self.assertEqual(self.events,
                         ["COMMAND", "READY", "PUT", "GET", "SQL_TOKEN",
                          "FRAME", "RECEIPT"])
        self.assertEqual(self.session.state.state, "COMPLETE")

    def test_wrong_instance_blocks_before_command(self):
        with self.assertRaisesRegex(h.SharedHandoffHold, "PRECONDITION_HOLD"):
            self.dispatch(instance="0"*64)
        self.assertEqual(self.events, [])

    def test_missing_explicit_grant_blocks_everything(self):
        with self.assertRaisesRegex(h.SharedHandoffHold, "PRECONDITION_HOLD"):
            self.dispatch(grant=False)
        self.assertEqual(self.events, [])

    def test_remote_pre_ready_failure_cannot_create_claim(self):
        with self.assertRaises(h.SharedHandoffHold):
            self.dispatch(ready=False)
        self.assertEqual(self.events, ["COMMAND", "READY"])
        self.assertEqual(self.session.state.state, "UNKNOWN")

    def test_put_timeout_terminal_no_sql_token(self):
        requests = []
        def timeout(*args):
            requests.append("PUT")
            raise TimeoutError("synthetic")
        guard = OnceGuard(timeout)
        with self.assertRaises(h.SharedHandoffHold):
            self.dispatch(guard=guard)
        self.assertEqual(requests, ["PUT"])
        self.assertNotIn("SQL_TOKEN", self.events)
        self.assertEqual(self.session.state.state, "UNKNOWN")
        with self.assertRaises(h.SharedHandoffHold):
            self.dispatch(guard=guard)
        self.assertEqual(requests, ["PUT"])

    def test_readback_mismatch_holds_before_sql(self):
        def mismatch(method, account, container, key, headers, body):
            self.events.append(method)
            if method == "PUT":
                return Response(201, '"e"')
            return Response(200, '"e"', b"not the claim")
        with self.assertRaises(h.SharedHandoffHold):
            self.dispatch(guard=OnceGuard(mismatch))
        self.assertEqual(self.events, ["COMMAND", "READY", "PUT", "GET"])

    def test_live_release_is_not_exposed(self):
        with self.assertRaisesRegex(h.SharedHandoffHold, "OWNER_LIVE_PARENT_NOT_RELEASED"):
            h.release_live(owner=True, blob_grant=True, sql=True)


if __name__ == "__main__":
    unittest.main()
