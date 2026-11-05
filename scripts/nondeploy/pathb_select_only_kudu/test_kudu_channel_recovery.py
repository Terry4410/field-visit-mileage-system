import base64
import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import p3_kudu_channel_diagnostic as diagnostic
import p3_kudu_transport as transport


class MarkerLineTests(unittest.TestCase):
    MARKER = b"FV_KUDU_CHANNEL_READY"

    def test_lf_exact_line_passes(self):
        found, remaining = transport.consume_exact_marker_line(
            b"prompt\nFV_KUDU_CHANNEL_READY\nnext", self.MARKER)
        self.assertTrue(found)
        self.assertEqual(remaining, b"next")

    def test_crlf_exact_line_passes(self):
        found, remaining = transport.consume_exact_marker_line(
            b"FV_KUDU_CHANNEL_READY\r\nnext", self.MARKER)
        self.assertTrue(found)
        self.assertEqual(remaining, b"next")

    def test_echoed_command_does_not_pass(self):
        raw = b"printf '%s\\n' FV_KUDU_CHANNEL_READY\r\n"
        self.assertFalse(transport.consume_exact_marker_line(raw, self.MARKER)[0])

    def test_partial_line_does_not_pass(self):
        self.assertFalse(transport.consume_exact_marker_line(
            b"FV_KUDU_CHANNEL_READY", self.MARKER)[0])

    def test_larger_line_does_not_pass(self):
        for raw in (b"xFV_KUDU_CHANNEL_READY\n",
                    b"FV_KUDU_CHANNEL_READYx\n"):
            self.assertFalse(transport.consume_exact_marker_line(raw, self.MARKER)[0])

    def test_stale_line_before_boundary_does_not_pass(self):
        raw = b"FV_KUDU_CHANNEL_READY\nnew output\n"
        self.assertFalse(transport.consume_exact_marker_line(
            raw, self.MARKER, after=len(b"FV_KUDU_CHANNEL_READY\n"))[0])

    def test_boundary_inside_line_skips_suffix(self):
        raw = b"old-prefix-FV_KUDU_CHANNEL_READY\nnext\n"
        self.assertFalse(transport.consume_exact_marker_line(
            raw, self.MARKER, after=len(b"old-prefix-"))[0])


class ChannelDeltaTests(unittest.TestCase):
    def test_echo_disable_and_marker_are_separate_writes(self):
        session = transport.KuduExecSession("worker-12345678", deadline=100)
        session.state.begin()
        sent = []
        expected = []
        session.send_public = lambda raw, code: sent.append((raw, code))
        session.expect_fixed = lambda marker, seconds, code, **kw: expected.append(
            (marker, seconds, code, kw))
        session.establish_safe_channel()
        self.assertEqual(sent[0], (b"stty -echo\n", "ECHO_DISABLE_SENT"))
        self.assertNotIn(b"FV_KUDU_CHANNEL_READY", sent[0][0])
        self.assertEqual(sent[1][1], "ECHO_PROBE_SENT")
        self.assertEqual(expected[0][2], "ECHO_READY")
        self.assertIn("after", expected[0][3])

    def test_stage_ready_uses_independent_boundary(self):
        stage = diagnostic.deterministic_stage()
        session = transport.KuduExecSession("worker-12345678", deadline=100)
        session.state.begin()
        sent = []
        expected = []
        session.send_public = lambda raw, code: sent.append((raw, code))
        session._write_all = lambda raw: None
        session.expect_fixed = lambda marker, seconds, code, **kw: expected.append(
            (marker, seconds, code, kw))
        session.stage_archive(stage, transport.sha256(stage),
                              remote_stage=transport.DIAGNOSTIC_REMOTE_STAGE)
        self.assertEqual(expected[0][0], b"FV_KUDU_STAGE_READY")
        self.assertEqual(expected[0][2], "STAGE_READY")
        self.assertIn("after", expected[0][3])

    def test_remote_stage_is_allowlisted(self):
        stage = diagnostic.deterministic_stage()
        with self.assertRaises(transport.TransportHold):
            transport.remote_bootstrap_command(
                transport.sha256(stage), len(stage), remote_stage="/tmp/not-approved")

    def test_diagnostic_bootstrap_is_distinct(self):
        stage = diagnostic.deterministic_stage()
        command = transport.remote_bootstrap_command(
            transport.sha256(stage), len(stage),
            remote_stage=transport.DIAGNOSTIC_REMOTE_STAGE)
        encoded = command.split(b"'")[1]
        source = base64.b64decode(encoded)
        self.assertIn(transport.DIAGNOSTIC_REMOTE_STAGE.encode(), source)
        self.assertNotIn(transport.REMOTE_STAGE.encode(), source)

    def test_diagnostic_stage_is_deterministic_and_single_member(self):
        first = diagnostic.deterministic_stage()
        second = diagnostic.deterministic_stage()
        self.assertEqual(first, second)
        self.assertEqual(transport.validate_stage_archive(first),
                         [diagnostic.PAYLOAD_NAME])

    def test_cleanup_is_exact_owned_path(self):
        command = diagnostic.cleanup_command()
        source = base64.b64decode(command.split(b"'")[1])
        self.assertIn(transport.DIAGNOSTIC_REMOTE_STAGE.encode(), source)
        self.assertIn(diagnostic.PAYLOAD_NAME.encode(), source)
        self.assertNotIn(b"/tmp/*", source)

    def test_authorization_is_short_lived_and_instance_bound(self):
        record = {
            "contract": diagnostic.CONTRACT,
            "approved": True,
            "distribution_sha256": "a" * 64,
            "resource_group": transport.RESOURCE_GROUP,
            "app": transport.APP,
            "instance_sha256": transport.sha256(b"worker-12345678"),
            "max_seconds": 600,
            "max_sessions": 2,
            "payload_count": 1,
            "not_before": "2026-11-05T10:00:00Z",
            "expires_at": "2026-11-05T10:10:00Z",
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "auth.json"
            path.write_text(json.dumps(record), encoding="utf-8")
            diagnostic.load_authorization(path, "a" * 64, "worker-12345678",
                                          now=1793873100)
            with self.assertRaises(transport.TransportHold):
                diagnostic.load_authorization(path, "a" * 64, "other-12345678",
                                              now=1793873100)

    def test_no_live_default_and_fixed_receipt_only(self):
        output = io.StringIO()
        with mock.patch("sys.argv", ["diagnostic"]), contextlib.redirect_stdout(output):
            self.assertEqual(diagnostic.main(), 1)
        self.assertEqual(output.getvalue().splitlines(), [
            "PROCESS_START=HOLD",
            "ECHO_READY=HOLD",
            "STAGE_READY=HOLD",
            "OWNED_CLEANUP=UNKNOWN",
        ])

    def test_diagnostic_has_no_credential_or_database_path(self):
        source = Path(diagnostic.__file__).read_text("utf-8").lower()
        for forbidden in ("get-access-token", "database.windows.net",
                          "preflight.sql", "select-only-config.json",
                          "installer", "blob marker"):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
