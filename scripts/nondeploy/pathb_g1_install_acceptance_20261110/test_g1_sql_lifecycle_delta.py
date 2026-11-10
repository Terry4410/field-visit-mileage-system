import os, pathlib, sys, tempfile, time, unittest
import p3_app_installer_runner as runner

class SqlClientLifecycleTests(unittest.TestCase):
    def executable(self, folder, body):
        p=pathlib.Path(folder)/'fake-sqlcmd'
        p.write_text('#!'+sys.executable+'\n'+body+'\n')
        p.chmod(0o700)
        return p

    def invoke(self, executable, budget):
        return runner.sql_call(executable, os.environ.copy(),'/tmp/test-only-nonexistent-token',b'SELECT TEST',time.monotonic()+budget,19)

    def test_preflight_or_postcheck_success_cleans_group(self):
        with tempfile.TemporaryDirectory() as d:
            p=self.executable(d,"import sys;sys.stdin.buffer.read();sys.stdout.buffer.write(b'FV_TEST_OK')")
            self.assertEqual(self.invoke(p,3),b'FV_TEST_OK')
            self.assertTrue(runner.INSTALLER_GROUP_CLEARED)

    def test_preflight_or_postcheck_timeout_stops_group(self):
        with tempfile.TemporaryDirectory() as d:
            pid=pathlib.Path(d)/'pid'
            p=self.executable(d,"import os,subprocess,sys,time; c=subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)']);open("+repr(str(pid)) +",'w').write(str(c.pid));time.sleep(30)")
            with self.assertRaises(runner.SqlFailure) as e:self.invoke(p,1.1)
            self.assertEqual(e.exception.record['category'],'PROCESS_TIMEOUT_UNKNOWN_SQL_OUTCOME')
            self.assertTrue(runner.INSTALLER_GROUP_CLEARED)
            if pid.exists():
                stat=pathlib.Path('/proc')/pid.read_text().strip()/'stat'
                if stat.exists():self.assertEqual(stat.read_text().split()[2],'Z')

    def test_successful_parent_with_child_must_hold(self):
        with tempfile.TemporaryDirectory() as d:
            p=self.executable(d,"import subprocess,sys; subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)'],stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)")
            with self.assertRaises(runner.Hold) as e:self.invoke(p,3)
            self.assertEqual(str(e.exception),'SQL_CLIENT_DESCENDANTS_AFTER_EXIT')
            self.assertTrue(runner.INSTALLER_GROUP_CLEARED)

if __name__=='__main__':unittest.main()
