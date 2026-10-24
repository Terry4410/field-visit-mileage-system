"""Focused adversarial contracts. Synthetic fixtures only; no Azure or SQL."""
import copy
import io
import json
import mmap
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from unittest import mock
import main_channel as channel
import main_probe as probe
import arm
import d0
import guard
from frontend import FrontendReader


class MainProbeContracts(unittest.TestCase):
    def test_selected_environment_does_not_export_unselected_secrets(self):
        data=b'OTHER_PASSWORD=must-not-leave\0ConnectionStrings__DefaultConnection=synthetic-only\0optionc__candidate__enabled=false\0'
        selected=probe.selected_environment(io.BytesIO(data))
        self.assertEqual(selected,{'ConnectionStrings__DefaultConnection':'synthetic-only','OptionC__Candidate__Enabled':'false'})
        self.assertNotIn('must-not-leave',str(selected))

    def test_duplicate_host_override_and_unterminated_environment_rejected(self):
        for data in [b'OptionC__Candidate__Enabled=false\0OPTIONC__CANDIDATE__ENABLED=true\0',
                     b'ASPNETCORE_ConnectionStrings__DefaultConnection=other\0',b'KEY=unterminated']:
            with self.assertRaises(probe.Stop):probe.selected_environment(io.BytesIO(data))

    def test_actual_linux_mapped_bytes_then_replaced_inode_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);maps=[];files=[]
            try:
                for name in probe.DLLS:
                    path=root/name;path.write_bytes(b'mapped-synthetic-'+name.encode())
                    f=path.open('rb');files.append(f);maps.append(mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ))
                measured=probe.runtime_snapshot('self',root)
                self.assertEqual(set(measured['assemblies']),set(probe.DLLS))
                replacement=root/'new';replacement.write_bytes(b'new-unmapped')
                replacement.replace(root/probe.DLLS[0])
                with self.assertRaises(probe.Stop):probe.runtime_snapshot('self',root)
            finally:
                for m in maps:m.close()
                for f in files:f.close()

    def test_configuration_precedence_duplicate_case_and_newer_file_rejected(self):
        with tempfile.TemporaryDirectory() as folder,mock.patch.object(probe,'ROOT',Path(folder)):
            file=Path(folder)/'appsettings.json'
            file.write_text(json.dumps({'ConnectionStrings':{'DefaultConnection':'not-a-credential'},'OptionC':{'Candidate':{'Enabled':False}}}))
            env={'WEBSITE_SITE_NAME':'api-fieldvisit-uat','WEBSITE_RESOURCE_GROUP':'rg-fieldvisit-uat'}
            config,match,_=probe.effective_configuration(1,env,time.time()+1)
            self.assertTrue(match);self.assertEqual(config['connectionString'],'not-a-credential')
            _,match,_=probe.effective_configuration(1,dict(env,OptionC__Candidate__Enabled='true'),time.time()+1)
            self.assertFalse(match)
            with self.assertRaises(probe.Stop):probe.effective_configuration(1,env,0)
            file.write_text('{"OptionC":{},"optionc":{}}')
            with self.assertRaises(probe.Stop):probe.effective_configuration(1,env,time.time()+1)

    def test_partial_private_pipe_respects_deadline(self):
        read,write=os.pipe()
        try:
            os.write(write,b'partial')
            with os.fdopen(read,'rb',buffering=0) as pipe:
                start=time.monotonic()
                with self.assertRaises(probe.Stop):probe.read_line(pipe,.03)
                self.assertLess(time.monotonic()-start,1)
        finally:os.close(write)


class MainChannelContracts(unittest.TestCase):
    def test_d0_exact_approval_is_distinct_from_full_g1(self):
        c=dict(user={'login':'Terry4410','id':170161890},author_association='OWNER',
            issue_url='https://api.github.com/repos/'+arm.REPO+'/issues/41',
            body='G1_D0_OBSERVATION_APPROVED '+'a'*40+' '+guard.D0_WORKFLOW+' ONE_WSS ZERO_SQL')
        self.assertTrue(guard.valid_approval(c,'a'*40,True))
        self.assertFalse(guard.valid_approval(c,'a'*40,False))
        self.assertFalse(guard.valid_approval(c,'b'*40,True))

    def test_d0_runner_rejects_missing_guard_before_auth(self):
        with mock.patch.dict(d0.os.environ,{},clear=True),mock.patch.object(d0,'exchange_token',side_effect=AssertionError('no auth')),mock.patch('sys.stdout',new_callable=io.StringIO) as output:
            self.assertEqual(d0.run(),1)
            result=json.loads(output.getvalue())
            self.assertEqual(result['sqlConnections'],0)

    def test_d0_guard_has_its_own_one_shot_and_needs_no_sql_cost(self):
        e=dict(GITHUB_ACTIONS='true',GITHUB_REPOSITORY=arm.REPO,GITHUB_REF=arm.REF,
            GITHUB_EVENT_NAME='workflow_dispatch',GITHUB_ACTOR='Terry4410',GITHUB_TRIGGERING_ACTOR='Terry4410',
            GITHUB_RUN_ATTEMPT='1',GITHUB_SHA='a'*40,APPROVED_SHA='a'*40,G1_MODE='D0_VERSION_ONLY',
            ACCEPT_COST='false',APPROVAL_COMMENT='7000000000',GITHUB_RUN_ID='123',GH_TOKEN='synthetic')
        c=dict(user={'login':'Terry4410','id':170161890},author_association='OWNER',
            issue_url='https://api.github.com/repos/'+arm.REPO+'/issues/41',
            body='G1_D0_OBSERVATION_APPROVED '+'a'*40+' '+guard.D0_WORKFLOW+' ONE_WSS ZERO_SQL')
        runs=dict(total_count=1,workflow_runs=[dict(id=123,head_sha='a'*40,run_attempt=1)])
        delta=''.join('A\t'+p+'\n' for p in sorted(guard.ALLOWED))
        with mock.patch.dict(guard.os.environ,e,clear=True),mock.patch.object(guard.subprocess,'check_output',return_value=delta),mock.patch.object(guard,'get',side_effect=[c,runs]) as get,mock.patch.object(guard,'coverage_ready',side_effect=AssertionError('D0 not full G1')),mock.patch('sys.stdout',new_callable=io.StringIO):
            self.assertEqual(guard.main(),0)
            self.assertIn(guard.D0_WORKFLOW,get.call_args.args[0])
            self.assertNotIn(guard.WORKFLOW,get.call_args.args[0])

    def test_arm_collection_single_fixed_instance_and_failed_path_no_retry(self):
        path='/subscriptions/fixed/resourceGroups/rg-fieldvisit-uat/providers/Microsoft.Web/sites/api-fieldvisit-uat/instances?api-version=2024-04-01'
        class Response:
            status=200
            def __enter__(self):return self
            def __exit__(self,*args):pass
            def geturl(self):return 'https://management.azure.com'+path
            def read(self,n):return json.dumps({'value':[{'name':'a'*32,'id':path.split('?')[0]+'/'+'a'*32}]}).encode()
        opener=mock.Mock();opener.open.return_value=Response()
        read=arm.Reader({'instances':path},'synthetic',opener=opener,collections=(path,))
        self.assertEqual(read.get(path)['value'][0]['name'],'a'*32)
        with self.assertRaises(arm.Stop):read.get(path)
        self.assertEqual(opener.open.call_count,1)

    def test_discovery_transport_has_no_probe_token_configuration_or_go(self):
        plan,obs,recovery,files=self.fixture();nonce=plan['nonce']
        result=dict(kind='G1_DISCOVERY',nonce=nonce,runtime=obs['runtime'],
            fixedFiles={'Microsoft.Data.SqlClient.dll':'c'*64,'FieldVisit.Api.runtimeconfig.json':'d'*64},
            processStable=True,sqlConnections=0,configuration='NOT_READ')
        class Socket:
            def __init__(self):self.sent=[];self.frames=[b'private-banner\n',('G1_READY_'+nonce+'\n').encode(),(json.dumps(result)+'\n').encode()]
            def send(self,value,opcode):self.sent.append((value,opcode))
            def settimeout(self,value):pass
            def recv_data(self):return 2,self.frames.pop(0)
            def close(self,timeout):pass
        socket=Socket();connect=mock.Mock(return_value=socket)
        site={'name':'api-fieldvisit-uat','properties':{'state':'Running','hostNameSslStates':[{'hostType':'Repository','name':'api-fieldvisit-uat.scm.azurewebsites.net'}]}}
        session=channel.DiscoverySession(site,{'value':[{'name':'b'*32}]},'synthetic-private-token',b'# sealed source placeholder',nonce,connect)
        try:
            self.assertEqual(session.observe(),result)
            with self.assertRaises(probe.Stop):session.sql_once()
            sent=b''.join(v for v,k in socket.sent)
            self.assertNotIn(b'G1_SQL_GO',sent);self.assertNotIn(b'probeBase64',sent)
            self.assertNotIn(b'synthetic-private-token',sent)
            self.assertIn(b'discover_main',sent)
            self.assertTrue(all(k==2 for v,k in socket.sent))
            self.assertEqual(connect.call_count,1)
            self.assertEqual(connect.call_args.kwargs['redirect_limit'],0)
            self.assertTrue(connect.call_args.kwargs['sslopt']['check_hostname'])
        finally:session.close()

    def test_bad_discovery_shape_cannot_emit_private_fields(self):
        session=channel.DiscoverySession.__new__(channel.DiscoverySession)
        session.nonce='a'*64;session.clock=lambda:0;session.end=100
        session.line=mock.Mock(return_value=json.dumps(dict(kind='G1_DISCOVERY',nonce='a'*64,private='not-exportable')).encode())
        with self.assertRaises(probe.Stop):session.record('G1_DISCOVERY',30)

    def fixture(self):
        nonce='a'*64;files={'/index.html':b'known-off-index','/config.js':b'known-off-config'}
        assemblies={name:probe.digest(name.encode()) for name in probe.DLLS}
        composite=probe.digest(''.join(n+':'+assemblies[n]+'\n' for n in probe.DLLS).encode())
        plan=dict(nonce=nonce,apiAssemblies=assemblies,apiCompositeSha256=composite,
            sourcePolicySha256='b'*64,frontendFiles={p:probe.digest(b) for p,b in files.items()},
            frontendOrigin='https://reviewed.example',rollbackBundleSha256='c'*64,
            restoreUtc=5000,schemaSha256='d'*64,rollbackAvailableUntil=200000,frontendOffFile='/config.js')
        obs=dict(kind='G1_OBSERVATION',nonce=nonce,runtime={'assemblies':assemblies,'apiCompositeSha256':composite},
            flags='MATCH',authentication='API_SQL_LOGIN',sqlConnections=0,configuration='STARTUP_BOUND')
        recovery=dict(earliestUtc=1000,observedUtc=10000,retentionDays=7,continuous=True)
        return plan,obs,recovery,files

    def test_pre_sql_real_hashes_positive_and_drift_denied(self):
        plan,obs,recovery,files=self.fixture()
        self.assertEqual(channel.pre_sql(obs,plan,files.__getitem__,recovery,10000)['recovery'],'MATCH')
        for change in [dict(flags='HOLD'),dict(sqlConnections=1),dict(nonce='e'*64),dict(runtime={})]:
            with self.assertRaises(probe.Stop):channel.pre_sql(dict(obs,**change),plan,files.__getitem__,recovery,10000)
        for p in [dict(plan,restoreUtc=9999),dict(plan,rollbackAvailableUntil=10001),dict(plan,frontendOrigin='http://insecure')]:
            with self.assertRaises(probe.Stop):channel.pre_sql(obs,p,files.__getitem__,recovery,10000)
        with self.assertRaises(probe.Stop):channel.pre_sql(obs,plan,lambda p:b'changed-live-bytes',recovery,10000)
        with self.assertRaises(probe.Stop):channel.pre_sql(obs,plan,files.__getitem__,dict(recovery,continuous=False),10000)

    def test_fixed_main_container_url_and_one_instance(self):
        site={'name':'api-fieldvisit-uat','properties':{'state':'Running','hostNameSslStates':[{'hostType':'Repository','name':'api-fieldvisit-uat.scm.azurewebsites.net'}]}}
        instances={'value':[{'name':'a'*32}]}
        url,_=channel.scm_url(site,instances)
        self.assertEqual(url,'wss://api-fieldvisit-uat.scm.azurewebsites.net/exec/shell?shell=/bin/sh')
        self.assertNotIn('kudu',url)
        for bad in [{'value':[]},{'value':instances['value']*2},dict(instances,nextLink='other')]:
            with self.assertRaises(probe.Stop):channel.scm_url(site,bad)
        site['properties']['hostNameSslStates'][0]['name']='api-fieldvisit-uat.scm.azurewebsites.net.evil.example'
        with self.assertRaises(probe.Stop):channel.scm_url(site,instances)

    def test_sql_once_requires_all_preflight_and_never_retries(self):
        plan,obs,recovery,files=self.fixture()
        session=channel.MainSession.__new__(channel.MainSession)
        session.nonce=plan['nonce'];session.go_sent=False;session.finished=False
        session.send=mock.Mock()
        session.record=mock.Mock(return_value=dict(processStable=True,sqlConnections=1,apiPrincipal='MATCH',schema='MATCH',actualSchemaSha256='d'*64,principalSidSha256='e'*64))
        with self.assertRaises(probe.Stop):session.sql_once(dict(obs,flags='HOLD'),plan,files.__getitem__,recovery,10000)
        session.send.assert_not_called()
        result=session.sql_once(obs,plan,files.__getitem__,recovery,10000)
        self.assertEqual(result['liveG1'],'PASS');session.send.assert_called_once_with('G1_SQL_GO '+plan['nonce']+'\n')
        with self.assertRaises(probe.Stop):session.sql_once(obs,plan,files.__getitem__,recovery,10000)
        self.assertEqual(session.send.call_count,1)

    def test_static_reader_path_limit_and_failed_get_consumes_slot(self):
        class Fails:
            def __init__(self):self.calls=0
            def open(self,*args,**kwargs):self.calls+=1;raise OSError('private-error')
        opener=Fails();manifest={'/index.html':'a'*64}
        read=FrontendReader('https://reviewed.example',manifest,opener)
        with self.assertRaises(probe.Stop):read('/not-allowed')
        self.assertEqual(opener.calls,0)
        with self.assertRaises(OSError):read('/index.html')
        with self.assertRaises(probe.Stop):read('/index.html')
        self.assertEqual(opener.calls,1)
        for path in ['/../secret','//host/file','/path?token=x','/x%2fsecret']:
            with self.assertRaises(probe.Stop):channel.file_manifest({'/index.html':'a'*64,path:'b'*64})


if __name__=='__main__':unittest.main()
