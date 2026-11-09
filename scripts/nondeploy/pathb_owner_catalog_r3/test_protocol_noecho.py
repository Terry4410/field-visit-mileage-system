import importlib.util
import io
import os
import pty
import termios
import unittest
from pathlib import Path

SOURCE=Path(__file__).with_name('owner_sql_catalog_child_REVIEW_ONLY.py')
spec=importlib.util.spec_from_file_location('cat_child',SOURCE)
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class ProtocolTests(unittest.TestCase):
    def test_real_noecho_pty_accepts_and_emits_single_ack(self):
        master, slave=pty.openpty()
        try:
            attr=termios.tcgetattr(slave)
            attr[3] &= ~termios.ECHO
            termios.tcsetattr(slave,termios.TCSANOW,attr)
            output=io.TextIOWrapper(os.fdopen(os.dup(slave),'wb',buffering=0),encoding='ascii',write_through=True)
            input_=os.fdopen(os.dup(slave),'rb',buffering=0)
            try:
                self.assertTrue(module.verify_noecho_tty_and_signal(input_,output))
                raw=os.read(master,1024)
                self.assertEqual(raw.replace(b'\r\n',b'\n'),b'FV_OWNER_ECHO_OFF=PASS\n')
            finally:
                input_.close();output.close()
        finally:
            os.close(master);os.close(slave)
    def test_echo_enabled_pty_fails_without_ack(self):
        master, slave=pty.openpty()
        try:
            a=termios.tcgetattr(slave);a[3]|=termios.ECHO;termios.tcsetattr(slave,termios.TCSANOW,a)
            stdout=os.fdopen(os.dup(slave),'w')
            stdin=os.fdopen(os.dup(slave),'r')
            try:self.assertFalse(module.verify_noecho_tty_and_signal(stdin,stdout))
            finally:stdin.close();stdout.close()
        finally:os.close(master);os.close(slave)
    def test_non_tty_fails(self):
        with open('/dev/null') as f:
            self.assertFalse(module.verify_noecho_tty_and_signal(f,f))
    def test_reviewonly_main_remains_locked(self):
        self.assertIn("print('OWNER_SQL_CATALOG=HOLD_REVIEW_ONLY_NOT_EXECUTABLE')", SOURCE.read_text())

if __name__=='__main__':unittest.main()
