import os, pathlib, subprocess, sys, tempfile, time, unittest
from unittest import mock
import p3_app_installer_runner as runner

class ProcessLifecycleTests(unittest.TestCase):
    def test_normal_exits_cleanly(self):
        result=runner.run_installer_managed([sys.executable,'-c','print("OK")'],os.environ.copy(),3)
        self.assertEqual(result.returncode,0)
        self.assertEqual(result.stdout.strip(),b'OK')

    def test_timeout_kills_descendant_process_group(self):
        with tempfile.TemporaryDirectory() as d:
            pidfile=pathlib.Path(d)/'child.pid'
            code='''import os,subprocess,sys,time
p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)'])
with open(sys.argv[1],'w') as f:f.write(str(p.pid));f.flush();os.fsync(f.fileno())
time.sleep(30)
'''
            with self.assertRaises(runner.Hold):
                runner.run_installer_managed([sys.executable,'-c',code,str(pidfile)],os.environ.copy(),1.0)
            self.assertTrue(pidfile.exists())
            pid=int(pidfile.read_text())
            # A reparented zombie may linger, but it must not be running.
            statpath=pathlib.Path(f'/proc/{pid}/stat')
            if statpath.exists():
                self.assertEqual(statpath.read_text().split()[2],'Z')

    def test_orphan_after_success_must_hold(self):
        with tempfile.TemporaryDirectory() as d:
            code='''import subprocess,sys
subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,stdin=subprocess.DEVNULL)
'''
            with self.assertRaises(runner.Hold):
                runner.run_installer_managed([sys.executable,'-c',code],os.environ.copy(),2)

    def test_adapter_sqlcmd_must_inherit_session(self):
        txt=pathlib.Path('token_adapter_g1_REVIEW_ONLY.py').read_text()
        self.assertIn('start_new_session=False',txt)
        self.assertIn('SQL_PROCESS_GROUP_MISMATCH',txt)
        self.assertNotIn('os.killpg(p.pid,signal.SIGKILL)',txt)

if __name__=='__main__':unittest.main()
