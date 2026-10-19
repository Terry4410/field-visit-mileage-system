"""Synthetic Azure management responses only; no Azure credential or network access."""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('readiness',ROOT/'scripts/ci/r3_serverless_readiness.py')
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
SUB='00000001-2222-4333-8444-555555555555' # Synthetic only.

class Harness:
    def __init__(self,states,wake='Opened'):
        self.states=iter(states); self.reads=0; self.wakes=0; self.now=0; self.wake_result=wake; self.r3=0
    def read(self): self.reads+=1; return next(self.states)
    def wake(self): self.wakes+=1; return self.wake_result
    def sleep(self,n): self.now+=n
    def run(self,a=False,c=False):
        used=r.run_readiness(self.read,self.wake,a,c,lambda:self.now,self.sleep)
        self.r3+=1 # downstream launch permitted only after return, never on exception.
        return used

class ReadinessTests(unittest.TestCase):
    def test_online_never_wakes_even_if_opted_in(self):
        for a,c in ((False,False),(True,True)):
            h=Harness(['Online','Online']);self.assertFalse(h.run(a,c));self.assertEqual((h.wakes,h.reads,h.r3),(0,2,1))
    def test_paused_without_authorization_stops_before_r3(self):
        for a,c in ((False,False),(True,False),(False,True)):
            h=Harness(['Paused'])
            with self.assertRaisesRegex(r.Stop,'WakeNotAuthorized'):h.run(a,c)
            self.assertEqual((h.wakes,h.r3),(0,0))
    def test_single_wake_requires_independent_online_check(self):
        h=Harness(['Paused','Resuming','Online','Online'])
        self.assertTrue(h.run(True,True));self.assertEqual((h.wakes,h.reads,h.r3),(1,4,1))
    def test_expected_40613_resume_signal_is_not_r3_retry(self):
        h=Harness(['Paused','Online','Online'],'ResumeSignaled')
        self.assertTrue(h.run(True,True));self.assertEqual((h.wakes,h.r3),(1,1))
    def test_resuming_waits_without_wake(self):
        h=Harness(['Resuming','Resuming','Online','Online']);h.run()
        self.assertEqual((h.wakes,h.now),(0,20))
    def test_resuming_has_bounded_read_count(self):
        h=Harness(['Resuming']*r.MAX_READS)
        with self.assertRaisesRegex(r.Stop,'ReadinessTimeout'):h.run()
        self.assertEqual((h.reads,h.wakes,h.r3),(r.MAX_READS,0,0))
    def test_paused_after_wake_never_reconnects(self):
        h=Harness(['Paused','Paused'])
        with self.assertRaisesRegex(r.Stop,'PausedAfterWake'):h.run(True,True)
        self.assertEqual((h.wakes,h.r3),(1,0))
    def test_unexpected_state_stops(self):
        for state in ('Offline','Inaccessible','Pausing','',None,'Online\nPRIVATE'):
            h=Harness([state])
            with self.assertRaisesRegex(r.Stop,'UnavailableState'):h.run(True,True)
            self.assertEqual((h.wakes,h.r3),(0,0))
    def test_online_recheck_race_stops(self):
        h=Harness(['Online','Paused'])
        with self.assertRaisesRegex(r.Stop,'OnlineRecheckRejected'):h.run(True,True)
        self.assertEqual((h.wakes,h.r3),(0,0))
    def test_wall_clock_timeout_stops(self):
        h=Harness(['Resuming']*4)
        def slow():h.now+=301;return 'Online'
        with self.assertRaisesRegex(r.Stop,'ReadinessTimeout'):r.run_readiness(slow,h.wake,clock=lambda:h.now,sleep=h.sleep)
        self.assertEqual(h.wakes,0)
    def test_wake_failure_does_not_start_r3(self):
        h=Harness(['Paused'],'UnknownSqlFailure')
        with self.assertRaisesRegex(r.Stop,'WakeRejected'):h.run(True,True)
        self.assertEqual((h.wakes,h.r3),(1,0))
    def test_wrong_target_and_malformed_metadata_stop(self):
        good={'name':r.DATABASE,'id':f'/subscriptions/{SUB}/resourceGroups/{r.GROUP}/providers/Microsoft.Sql/servers/{r.SERVER}/databases/{r.DATABASE}','status':'Online'}
        self.assertEqual(r.state_from_metadata(good,SUB),'Online')
        for value in ({**good,'id':good['id'].replace(r.SERVER,'wrong')},{**good,'name':'wrong'},None,{}):
            with self.assertRaisesRegex(r.Stop,'TargetGuardRejected'):r.state_from_metadata(value,SUB)
    def test_source_debug_and_authorization_format_guards(self):
        good={'GITHUB_ACTIONS':'true','GITHUB_REPOSITORY':r.REPOSITORY,'GITHUB_REF':r.REF,'GITHUB_SHA':'a'*40,
              'R3_APPROVED_SHA':'a'*40,'R3_SUBSCRIPTION_ID':SUB,'R3_AUTHORIZE_WAKE':'false','R3_ACCEPT_CHARGES':'false'}
        r.validate_source(good)
        for key,value in (('RUNNER_DEBUG','1'),('GITHUB_REF','refs/heads/main'),('R3_APPROVED_SHA','b'*40),('R3_SUBSCRIPTION_ID','bad'),('R3_AUTHORIZE_WAKE','yes')):
            with self.assertRaisesRegex(r.Stop,'PrerequisiteRejected'):r.validate_source({**good,key:value})
    def test_permission_denial_and_raw_stderr_are_suppressed(self):
        with patch.object(r.subprocess,'run',return_value=SimpleNamespace(returncode=1,stdout='PRIVATE',stderr='RAW_SQL_IDENTITY')):
            with self.assertRaisesRegex(r.Stop,'ManagementAccessRejected'):r.azure_read(SUB)
    def test_management_cli_is_read_only_fixed_and_bounded(self):
        with patch.object(r.subprocess,'run',return_value=SimpleNamespace(returncode=0,stdout='{}')) as process:
            with self.assertRaises(r.Stop):r.azure_read(SUB)
            args,kwargs=process.call_args
            self.assertEqual(args[0][:4],['az','sql','db','show'])
            self.assertEqual(kwargs['timeout'],10);self.assertTrue(kwargs['capture_output'])
            self.assertNotIn('update',args[0]);self.assertNotIn('resume',args[0])
    def test_subprocess_timeout_has_no_provider_text(self):
        with patch.object(r.subprocess,'run',side_effect=r.subprocess.TimeoutExpired('SECRET_COMMAND',10)):
            with self.assertRaisesRegex(r.Stop,'ReadinessTimeout') as caught:r.azure_read(SUB)
            self.assertNotIn('SECRET',str(caught.exception))
    def test_gate_order_and_distinct_opt_in(self):
        parent=(ROOT/'.github/workflows/azure-sql-uat-schema-fidelity-readonly.yml').read_text()
        worker=(ROOT/'.github/workflows/r3-private-uat-execution.yml').read_text()
        self.assertIn('default: false',parent);self.assertIn('accept_compute_charges:',parent)
        self.assertIn('test "$APPROVED_SHA" = "$GITHUB_SHA"',parent)
        self.assertLess(worker.index('run: python3 scripts/ci/r3_serverless_readiness.py'),worker.index('Invoke-R3Diagnostic'))
        self.assertIn('timeout-minutes: 6',worker)
        self.assertNotIn('continue-on-error:',worker)
        self.assertNotIn('always()',worker.split('      - name: Execute four')[1].split('      - name: Sign out')[0])
    def test_wake_has_no_sql_commands_and_one_open(self):
        source=(ROOT/'scripts/ci/r3_connection_only_wake.ps1').read_text()
        self.assertEqual(source.count('$connection.Open()'),1)
        for forbidden in ('Invoke-Sqlcmd','CommandText','CreateCommand','ExecuteReader','ExecuteNonQuery','SELECT '):self.assertNotIn(forbidden,source)
        for required in ("['ConnectRetryCount']=0","['Connect Timeout']=120","['Pooling']=$false","['Trust Server Certificate']=$false",'R3_ACCEPT_CHARGES',"ProviderCode=40613;"):
            self.assertIn(required,source)
        self.assertNotIn('Start-Sleep',source)

if __name__=='__main__':unittest.main()
