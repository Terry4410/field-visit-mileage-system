import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import token_adapter_g1_REVIEW_ONLY as adapter

RAW=b'--synthetic no SQL operations\n'

class Tests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();d=Path(self.temp.name)
        self.binary=d/'sqlcmd';self.binary.write_bytes(b'fake sqlcmd binary');self.binary.chmod(0o600)
        self.token=d/'token';self.token.write_text('fake token');self.token.chmod(0o600)
        self.state=d/'state';self.state.write_text(json.dumps({'next':0,'terminal':False,'records':[]}));self.state.chmod(0o600)
        self.config={'binary':str(self.binary), 'binary_sha256':adapter.sha(self.binary.read_bytes()),'token_file':str(self.token),
                     'state_file':str(self.state),'deadline':99999999999,'payload_sha256':[adapter.sha(RAW)],'call_timeout':1,
                     'g1_operation_id':'a'*32,'g1_claim_sha256':'b'*64}
    def tearDown(self):self.temp.cleanup()
    def test_without_durable_journal_never_dispatches(self):
        with mock.patch.object(adapter.subprocess,'Popen') as proc:
            with self.assertRaisesRegex(ValueError,'EXTERNAL_LEDGER_REQUIRED'):
                adapter.invoke(self.config,adapter.EXPECTED,RAW)
        proc.assert_not_called()
    def test_failed_before_never_dispatches(self):
        with mock.patch.object(adapter.subprocess,'Popen') as proc:
            with self.assertRaises(Exception):
                adapter.invoke(self.config,adapter.EXPECTED,RAW,journal_request=lambda *args: (500,'',b''))
        proc.assert_not_called()
        self.assertEqual(json.loads(self.state.read_text())['inflight'],0)
    def test_before_after_surround_process(self):
        order=[]
        class Proc:
            pid=12345
            returncode=0
            def communicate(self,raw,timeout):
                order.append('SQL');return (b'',b'')
        def request(method,key,body,headers):
            order.append(method+(':'+key.split('/')[-1] if method=='PUT' else ''))
            return (201,'etag',b'') if method=='PUT' else (200,'etag',cache[key])
        cache={}
        def store(method,key,body,headers):
            if method=='PUT':cache[key]=body
            return request(method,key,body,headers)
        with mock.patch.object(adapter.subprocess,'Popen',return_value=Proc()), \
             mock.patch.object(adapter.os,'getpgid',return_value=adapter.os.getpgrp()):
            rc=adapter.invoke(self.config,adapter.EXPECTED,RAW,journal_request=store)
        self.assertEqual(rc,0)
        self.assertEqual(order,['PUT:00-before.json','GET','SQL','PUT:00-after.json','GET'])
    def test_failed_after_is_unknown_and_never_replays(self):
        class Proc:
            returncode=0
            def communicate(self,raw,timeout):return(b'',b'')
        n={'put':0};cache={}
        def req(method,key,body,headers):
            if method=='PUT':
                n['put']+=1
                if n['put']==2:raise OSError('after uncertain')
                cache[key]=body;return (201,'etag',b'')
            return (200,'etag',cache[key])
        with mock.patch.object(adapter.subprocess,'Popen',return_value=Proc()):
            with self.assertRaises(Exception):adapter.invoke(self.config,adapter.EXPECTED,RAW,journal_request=req)
        self.assertEqual(json.loads(self.state.read_text())['inflight'],0)
        with mock.patch.object(adapter.subprocess,'Popen') as proc:
            with self.assertRaises(ValueError):adapter.invoke(self.config,adapter.EXPECTED,RAW,journal_request=req)
        proc.assert_not_called()

if __name__=='__main__':unittest.main()
