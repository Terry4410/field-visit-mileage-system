"""Offline access-boundary tests; no Azure, tokens or SQL connections."""
import importlib.util
import pathlib
import unittest
from unittest import mock
import contextlib
import io
import hashlib
import sys
sys.path.insert(0,str(pathlib.Path(__file__).parent))

ROOT = pathlib.Path(__file__).parent


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


arm, guard = load('arm'), load('guard')


class Boundary(unittest.TestCase):
    def environment(self):
        return dict(GITHUB_ACTIONS='true', GITHUB_REPOSITORY=arm.REPO, GITHUB_REF=arm.REF,
                    GITHUB_ACTOR='Terry4410', GITHUB_TRIGGERING_ACTOR='Terry4410',
                    GITHUB_EVENT_NAME='workflow_dispatch', GITHUB_RUN_ATTEMPT='1',
                    GITHUB_SHA='a'*40, APPROVED_SHA='a'*40)

    def test_positive_prelogin_does_not_call_azure(self):
        e = self.environment()
        e.update(AZURE_TENANT_ID='synthetic-tenant', AZURE_SUBSCRIPTION_ID='synthetic-sub', AZURE_CLIENT_ID='synthetic-client')
        digest = hashlib.sha256(bytes([10]).join(x.encode() for x in ('synthetic-tenant','synthetic-sub','synthetic-client'))).hexdigest()
        with mock.patch.dict(arm.os.environ,e,clear=True), mock.patch.object(arm,'CONTEXT_SHA256',digest), mock.patch.object(arm.sys,'argv',['arm.py','--prelogin']), mock.patch.object(arm,'exchange_token',side_effect=AssertionError('no Azure')), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(arm.main(),0)

    def test_exact_source_guard_positive_and_modified_file_denial(self):
        e=self.environment()
        e.update(ACCEPT_COST='true',APPROVAL_COMMENT='7000000000',GITHUB_RUN_ID='123',GH_TOKEN='synthetic')
        comment=dict(user={'login':'Terry4410','id':170161890},author_association='OWNER',
                     issue_url='https://api.github.com/repos/'+arm.REPO+'/issues/41',
                     body='G1_V2_DISPATCH_APPROVED '+'a'*40+' '+guard.WORKFLOW+' ONE_CONNECTION')
        runs=dict(total_count=1,workflow_runs=[dict(id=123,head_sha='a'*40,run_attempt=1)])
        delta=''.join('A'+chr(9)+p+chr(10) for p in sorted(guard.ALLOWED))
        with mock.patch.dict(guard.os.environ,e,clear=True), mock.patch.object(guard.subprocess,'check_output',return_value=delta), mock.patch.object(guard,'get',side_effect=[comment,runs]), mock.patch.object(guard,'coverage_ready',return_value=True), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(guard.main(),0)
        with mock.patch.dict(guard.os.environ,e,clear=True), mock.patch.object(guard.subprocess,'check_output',return_value=delta), mock.patch.object(guard,'get',side_effect=[comment,runs]), mock.patch.object(guard,'coverage_ready',return_value=False), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(guard.main(),1)
        for bad in [delta.replace('A'+chr(9),'M'+chr(9),1),delta+'A'+chr(9)+'backend/changed.cs'+chr(10)]:
            with mock.patch.dict(guard.os.environ,e,clear=True), mock.patch.object(guard.subprocess,'check_output',return_value=bad), mock.patch.object(guard,'get',side_effect=AssertionError('no network')), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(guard.main(),1)

    def test_ref_actor_and_rerun_denial(self):
        e = dict(GITHUB_ACTIONS='true', GITHUB_REPOSITORY=arm.REPO, GITHUB_REF=arm.REF,
                 GITHUB_ACTOR='Terry4410', GITHUB_TRIGGERING_ACTOR='Terry4410',
                 GITHUB_EVENT_NAME='workflow_dispatch', GITHUB_RUN_ATTEMPT='1',
                 GITHUB_SHA='a'*40, APPROVED_SHA='a'*40)
        self.assertTrue(arm.context_valid(e))
        for key, value in [('GITHUB_REF', 'refs/heads/main'), ('GITHUB_REF', 'refs/heads/work/other'),
                           ('GITHUB_ACTOR', 'other'), ('GITHUB_TRIGGERING_ACTOR', 'other'),
                           ('GITHUB_RUN_ATTEMPT', '2'), ('RUNNER_DEBUG', '1'),
                           ('GITHUB_EVENT_NAME', 'push'), ('APPROVED_SHA', 'b'*40)]:
            with self.subTest(key=key, value=value):
                self.assertFalse(arm.context_valid(dict(e, **{key: value})))

    def test_request_rejected_before_transport(self):
        class Never:
            def open(self, *a, **k):
                raise AssertionError('transport must not run')
        plan = arm.paths('fixed')
        r = arm.Reader(plan, 'synthetic', opener=Never(), clock=lambda: 0)
        for path in ['/anything', 'https://evil.example']:
            with self.assertRaises(arm.Stop):
                r.get(path)
        r.calls = 5
        with self.assertRaises(arm.Stop):
            r.get(next(iter(plan.values())))
        r.calls = 0
        r.clock = lambda: 121
        with self.assertRaises(arm.Stop):
            r.get(next(iter(plan.values())))

    def test_failed_request_is_consumed_without_retry(self):
        class Fails:
            calls = 0
            def open(self, *a, **k):
                self.calls += 1
                raise OSError('synthetic-private-provider-message')
        transport = Fails()
        plan = arm.paths('fixed')
        r = arm.Reader(plan, 'synthetic', opener=transport, clock=lambda: 0)
        with self.assertRaises(OSError):
            r.get(next(iter(plan.values())))
        self.assertEqual((r.calls, transport.calls), (1, 1))

    def test_no_redirect(self):
        with self.assertRaises(arm.Stop):
            arm.NoRedirect().redirect_request(None)

    def test_new_exact_owner_consent(self):
        c = dict(user={'login': 'Terry4410', 'id': 170161890}, author_association='OWNER',
                 issue_url='https://api.github.com/repos/'+arm.REPO+'/issues/41',
                 body='G1_V2_DISPATCH_APPROVED '+'a'*40+' '+guard.WORKFLOW+' ONE_CONNECTION')
        self.assertTrue(guard.valid_approval(c, 'a'*40))
        self.assertFalse(guard.valid_approval(c, 'b'*40))
        for change in [dict(user={'login':'Terry4410','id':1}),dict(author_association='NONE'),
                       dict(issue_url='other'),dict(body='Owner generally approved G1')]:
            self.assertFalse(guard.valid_approval(dict(c, **change), 'a'*40))

    def test_full_gate_positive_and_each_missing_mismatched_gate(self):
        keys={'target','runtime','schema','apiPrincipal','flags','recovery'}
        expected=dict(sourceSha='a'*40,producers={k:'1'*64 for k in keys})
        receipts={k:dict(measurementSha256='2'*64,sourceSha='a'*40,
            targetBinding=arm.CONTEXT_SHA256,observedAt=1000,outcome='MATCH',producer='1'*64) for k in keys}
        self.assertEqual(arm.final_gate(receipts,1001,expected),'PASS')
        for key in keys:
            for change in [dict(outcome='UNKNOWN'),dict(observedAt=399),dict(observedAt=1002),
                           dict(producer='3'*64),dict(measurementSha256=True),dict(sourceSha='b'*40),dict(targetBinding='wrong')]:
                with self.subTest(gate=key,change=change):
                    bad={**receipts,key:{**receipts[key],**change}}
                    self.assertEqual(arm.final_gate(bad,1001,expected),'HOLD')
            self.assertEqual(arm.final_gate({k:v for k,v in receipts.items() if k!=key},1001,expected),'HOLD')
        self.assertEqual(arm.final_gate({'all':'PASS'},1001,expected),'HOLD')

    def test_oidc_scope_and_host_denied_before_transport(self):
        class Never:
            def open(self,*a,**k):raise AssertionError('no network')
        for url in ['http://pipelines.actions.githubusercontent.com/idtoken','https://evil.example/idtoken',
                    'https://pipelines.actions.githubusercontent.com.evil.example/idtoken',
                    'https://user@pipelines.actions.githubusercontent.com/idtoken',
                    'https://pipelines.actions.githubusercontent.com/idtoken?audience=wrong']:
            with mock.patch.dict(arm.os.environ,{'ACTIONS_ID_TOKEN_REQUEST_URL':url},clear=True):
                with self.assertRaises(arm.Stop):
                    arm.exchange_token('https://management.azure.com/.default',Never())
        with self.assertRaises(arm.Stop):
            arm.exchange_token('https://graph.microsoft.com/.default',Never())

    def test_oidc_flow_has_only_auth_requests_and_no_arm_discovery(self):
        import json
        class Response:
            status=200
            def __init__(self,url,payload):self.url,self.payload=url,payload
            def __enter__(self):return self
            def __exit__(self,*a):pass
            def geturl(self):return self.url
            def read(self,n):return json.dumps(self.payload).encode()
        class Opener:
            def __init__(self):self.calls=[]
            def open(self,req,timeout):
                self.calls.append((req.method,req.full_url,timeout))
                return Response(req.full_url,{'value':'synthetic-assertion'} if len(self.calls)==1 else {'access_token':'x'*200})
        env=dict(ACTIONS_ID_TOKEN_REQUEST_URL='https://pipelines.actions.githubusercontent.com/idtoken?api-version=2',
            ACTIONS_ID_TOKEN_REQUEST_TOKEN='synthetic',AZURE_TENANT_ID='synthetic',AZURE_CLIENT_ID='synthetic')
        op=Opener()
        with mock.patch.dict(arm.os.environ,env,clear=True):
            self.assertEqual(arm.exchange_token('https://management.azure.com/.default',op),'x'*200)
        self.assertEqual([x[0] for x in op.calls],['GET','POST'])
        self.assertTrue(all('management.azure.com' not in url for _,url,_ in op.calls))


if __name__ == '__main__':
    unittest.main()
