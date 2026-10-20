"""Synthetic processes only. No SQL connection or credential fixture."""
import contextlib
import hashlib
import io
from pathlib import Path
import signal
import sys
import tempfile
import time
import unittest
from supervisor import supervise

class Boundary(unittest.TestCase):
    def run_worker(self, text, *, expected=None, deadline=.5):
        with tempfile.TemporaryDirectory() as td:
            p=Path(td); worker=p/'worker.py'; result=p/'result'
            worker.write_text(text.replace('RESULT', repr(str(result))))
            digest=hashlib.sha256(worker.read_bytes()).hexdigest()
            stdout, stderr=io.StringIO(),io.StringIO()
            with contextlib.redirect_stdout(stdout),contextlib.redirect_stderr(stderr):
                code=supervise([sys.executable,str(worker)],reviewed_worker_sha256=expected or digest,
                               result_path=str(result),deadline=deadline)
            self.assertEqual(stdout.getvalue()+stderr.getvalue(),'')
            return code
    def test_status_only(self):
        self.assertEqual(self.run_worker("from pathlib import Path;Path(RESULT).write_text('ADAPTER_COLLECTED_HOLD')"),'ADAPTER_COLLECTED_HOLD')
    def test_wrong_hash(self):
        self.assertEqual(self.run_worker("raise SystemExit(0)",expected='0'*64),'ADAPTER_GUARD_STOP')
    def test_missing_result(self):
        self.assertEqual(self.run_worker('pass'),'ADAPTER_GUARD_STOP')
    def test_provider_secret_suppressed(self):
        self.assertEqual(self.run_worker("raise RuntimeError('SYNTHETIC_PASSWORD_NEVER_PUBLIC')"),'ADAPTER_GUARD_STOP')
    def test_forbidden_output_not_forwarded(self):
        self.assertEqual(self.run_worker("print('SYNTHETIC_CONNECTION_NEVER_PUBLIC')"),'ADAPTER_GUARD_STOP')
    def test_malformed_result(self):
        self.assertEqual(self.run_worker("from pathlib import Path;Path(RESULT).write_text('RAW_SQL_METADATA')"),'ADAPTER_GUARD_STOP')
    def test_oversized_result(self):
        self.assertEqual(self.run_worker("from pathlib import Path;Path(RESULT).write_text('X'*1000)"),'ADAPTER_GUARD_STOP')
    def test_deadline(self):
        start=time.monotonic()
        self.assertEqual(self.run_worker('import time;time.sleep(30)'),'ADAPTER_DEADLINE_STOP')
        self.assertLess(time.monotonic()-start,1.5)
    def test_forced_termination(self):
        self.assertEqual(self.run_worker('import signal,time;signal.signal(signal.SIGTERM,signal.SIG_IGN);time.sleep(30)'),'ADAPTER_DEADLINE_STOP')
    def test_cleanup_failure_status(self):
        self.assertEqual(self.run_worker("from pathlib import Path;Path(RESULT).write_text('ADAPTER_CLEANUP_STOP')"),'ADAPTER_CLEANUP_STOP')
    def test_no_repeat_worker(self):
        self.assertEqual(self.run_worker("from pathlib import Path;p=Path(RESULT);assert not p.exists();p.write_text('ADAPTER_GUARD_STOP')"),'ADAPTER_GUARD_STOP')
    def test_cannot_extend_ceiling(self):
        self.assertEqual(supervise([],reviewed_worker_sha256='',result_path='',deadline=241),'ADAPTER_GUARD_STOP')

if __name__=='__main__':
    suite=unittest.defaultTestLoader.loadTestsFromTestCase(Boundary)
    # Capture traceback/details even if a future failure contains a sensitive value.
    stream=io.StringIO();result=unittest.TextTestRunner(stream=stream).run(suite)
    print('OfflineSupervisor='+('PASS' if result.wasSuccessful() else 'STOP')+'; Cases='+str(result.testsRun))
    sys.exit(0 if result.wasSuccessful() else 1)
