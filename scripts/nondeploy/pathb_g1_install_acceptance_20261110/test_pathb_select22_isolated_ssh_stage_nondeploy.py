"""Pure offline proof: SELECT22 bootstrap and pinned-SSH staged state."""
import hashlib
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

from p3_kudu_transport import iter_stage_frame, TransportHold
import pathb_select22_isolated_ssh_stage_nondeploy as stage


def sample_zip():
    sink = io.BytesIO()
    with zipfile.ZipFile(sink, "w") as z:
        for name in ("one.txt", "debs/example.deb"):
            info = zipfile.ZipInfo(name, (2026, 11, 13, 0, 0, 0))
            info.external_attr = 0o100600 << 16
            z.writestr(info, b"nonsecret mock bytes")
    raw = sink.getvalue()
    return raw, hashlib.sha256(raw).hexdigest()


class StageSshTests(unittest.TestCase):
    def test_separate_stage_path_cannot_be_installer(self):
        self.assertNotEqual(stage.SELECT22_STAGE,
                            "/tmp/fv-pathb-g1-original-installer-20261110-stage")
        self.assertIn("select22", stage.SELECT22_STAGE)
        self.assertNotIn("install", stage.SELECT22_STAGE)

    def test_bootstrap_consumes_exact_frame_in_local_isolated_python(self):
        raw, sha = sample_zip()
        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp) / "stage"
            code = stage._bootstrap_source(sha, len(raw), str(dest))
            run = subprocess.run(
                [sys.executable, "-c", code],
                input=b"".join(iter_stage_frame(raw, sha)),
                capture_output=True, timeout=5,
            )
            self.assertEqual(run.returncode, 0, run.stderr.decode("utf-8", "replace"))
            self.assertEqual(run.stdout.strip(), b"FV_KUDU_STAGE_READY")
            self.assertEqual((dest / "one.txt").read_bytes(), b"nonsecret mock bytes")
            self.assertEqual((dest / "debs" / "example.deb").read_bytes(),
                             b"nonsecret mock bytes")

    def test_bootstrap_fails_without_any_write_on_wrong_hash(self):
        raw, sha = sample_zip()
        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp) / "stage"
            wrong = stage._bootstrap_source("0" * 64, len(raw), str(dest))
            run = subprocess.run([sys.executable, "-c", wrong],
                                 input=b"".join(iter_stage_frame(raw, sha)),
                                 capture_output=True, timeout=5)
            self.assertNotEqual(run.returncode, 0)
            self.assertFalse(dest.exists())

    def test_stage_transport_over_existing_pinned_ssh_class(self):
        raw, sha = sample_zip()
        session = stage.Select22StageSession("instance01234567", deadline=10**12)
        self.assertTrue(isinstance(session, stage.Select22SshSession))
        session.state.begin()
        writes = []
        with mock.patch.object(stage, "verify_stage", return_value=True), \
             mock.patch.object(session, "send_public", side_effect=lambda b, c: writes.append(b)), \
             mock.patch.object(session, "_write_all", side_effect=writes.append), \
             mock.patch.object(session, "expect_fixed"):
            session.stage_select22_archive(raw, sha)
        self.assertEqual(session.state.state, "STAGED")
        self.assertEqual(session.audit[-1], "SELECT22_STAGE_FRAME_SENT")
        self.assertTrue(writes[0].startswith(b"python3 -c "))
        self.assertNotIn(b"p3_app_installer_runner", b"".join(writes))
        session.close()

    def test_original_installer_stage_api_disabled(self):
        session = stage.Select22StageSession("instance01234567", deadline=10**12)
        with self.assertRaises(TransportHold):
            session.stage_archive(b"bad", "0" * 64)
        session.close()

    def test_unverified_stage_refused_before_ssh_bytes(self):
        session = stage.Select22StageSession("instance01234567", deadline=10**12)
        session.state.begin()
        with mock.patch.object(stage, "verify_stage", side_effect=ValueError("bad")), \
             mock.patch.object(session, "_write_all") as sender:
            with self.assertRaisesRegex(TransportHold, "^STAGE_HASH_HOLD$"):
                session.stage_select22_archive(b"invalid", "0" * 64)
            sender.assert_not_called()
        self.assertEqual(session.state.state, "CHANNEL")
        session.close()

    def test_stage_channel_failure_terminal_no_retry(self):
        raw, sha = sample_zip()
        session = stage.Select22StageSession("instance01234567", deadline=10**12)
        session.state.begin()
        with mock.patch.object(stage, "verify_stage", return_value=True), \
             mock.patch.object(session, "send_public"), \
             mock.patch.object(session, "_write_all",
                               side_effect=TimeoutError("secret channel error")):
            with self.assertRaisesRegex(TransportHold, "^STAGE_CHANNEL_HOLD$"):
                session.stage_select22_archive(raw, sha)
            self.assertEqual(session.state.state, "UNKNOWN")
            with self.assertRaises(TransportHold):
                session.stage_select22_archive(raw, sha)
        session.close()


if __name__ == "__main__":
    unittest.main()
