import copy
import hashlib
import json
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch
import hub
import publish
import baseline
import schema

STAMP = '2026-10-16T09:00:00Z'

def record(**changes):
    value = {'Version': 1, 'Lane': 'WORK_A', 'SHA': 'a' * 40, 'CI': [100], 'Status': 'PASS',
             'Completed': ['NONDEPLOY_VALIDATION'], 'Blockers': [], 'NextDelta': ['OWNER_REVIEW'],
             'OwnerAuthorization': 'NONDEPLOY_ONLY', 'Evidence': ['e' * 64], 'Sequence': 1}
    value.update(changes)
    return value

def comment(value, identity='Terry4410', number=1):
    return {'id': number, 'user': {'login': identity}, 'body': publish.body_for(value),
            'created_at': STAMP, 'updated_at': STAMP,
            'issue_url': 'https://api.github.com/repos/' + hub.REPO + '/issues/41'}

class Fake:
    def __init__(self, comments=None):
        self.comments = comments or []
        self.paths = []
        self.moving = False
        self.ci_status = 'success'
        self.evidence_hash = 'e'*64
        self.expired = False
        self.artifact_sha = 'a'*40
        self.stream_moves = False
        self.pages_calls = 0
    def get(self, path):
        self.paths.append(path)
        if path.startswith('issues/comments/'):
            return copy.deepcopy(next(c for c in self.comments if c['id'] == int(path.rsplit('/', 1)[1])))
        if path.startswith('pulls/'):
            sha = 'b'*40 if self.moving and self.paths.count(path) > 1 else 'a'*40
            return {'head': {'sha': sha, 'repo': {'full_name': hub.REPO}}, 'state': 'open', 'merged': path == 'pulls/38',
                    'updated_at': STAMP, 'merged_at': STAMP if path == 'pulls/38' else None}
        if path.startswith('actions/runs?'):
            sha = path.split('head_sha=')[1].split('&')[0]
            return {'total_count': 1, 'workflow_runs': [{'id': 100, 'head_sha': sha, 'conclusion': self.ci_status,
                     'status': 'completed', 'repository': {'full_name': hub.REPO}, 'updated_at': STAMP, 'event': 'push'}]}
        if path.startswith('git/ref/'):
            return {'object': {'sha': 'f'*40}}
        if path.startswith('commits/'):
            return {'commit': {'committer': {'date': STAMP}}}
        raise ValueError('UNEXPECTED_ENDPOINT')
    def pages(self, path, key=None):
        if path.endswith('/artifacts'):
            return [{'id': 500, 'expired': self.expired, 'digest': 'sha256:' + self.evidence_hash,
                     'workflow_run': {'head_sha': self.artifact_sha, 'id': 100}, 'created_at': STAMP}]
        self.pages_calls += 1
        values = copy.deepcopy(self.comments)
        if self.stream_moves and self.pages_calls > 1:
            values.append(comment(record(Sequence=2), number=999))
        return values

class ContractTests(unittest.TestCase):
    def test_valid_record(self):
        self.assertEqual(hub.validate(record())['Status'], 'PASS')
    def test_arbitrary_text_and_secret_carriers_denied(self):
        for field in ('Completed', 'Blockers', 'NextDelta', 'Evidence', 'CI', 'Status', 'SHA', 'OwnerAuthorization', 'Lane'):
            for value in ('token=synthetic-secret', 'employee@example.invalid', 'SELECT * FROM employee', 'ProviderException text'):
                with self.subTest(field=field, value=value), self.assertRaises((ValueError, TypeError)):
                    hub.validate(record(**{field: [value] if field in ('Completed', 'Blockers', 'NextDelta', 'Evidence', 'CI') else value}))
    def test_extra_fields_denied(self):
        with self.assertRaises(ValueError): hub.validate(record(Token='synthetic'))
    def test_privileged_authority_denied(self):
        for authority in ('GO', 'MERGE', 'SQL', 'DEPLOY', 'MIGRATE', 'RESTORE', 'ENABLE', None):
            with self.subTest(authority=authority), self.assertRaises(ValueError):
                hub.validate(record(OwnerAuthorization=authority))
    def test_unsubstantiated_pass_denied(self):
        for change in ({'CI': []}, {'Evidence': []}, {'Blockers': ['SCHEMA_FIDELITY']}):
            with self.assertRaises(ValueError): hub.validate(record(**change))
    def test_boundaries_and_pure_hash(self):
        for change in ({'Version': True}, {'Sequence': True}, {'Sequence': 0}, {'CI': [True]}, {'Evidence': ['e'*64]*33},
                       {'Completed': ['NO_CHANGE']*17}, {'Evidence':['sha256:'+'e'*64]}, {'Evidence':['e'*64+'\n']},
                       {'CI': [100, 100]}, {'Evidence':['e'*64,'e'*64]}):
            with self.subTest(change=change), self.assertRaises(ValueError): hub.validate(record(**change))
    def test_duplicate_json_keys_and_nan_rejected(self):
        for text in ('{"Sequence":1,"Sequence":2}', '{"CI":[NaN]}'):
            with self.assertRaises(ValueError): schema.strict_loads(text)
    def test_actual_schema_drives_validator(self):
        revised = schema.load_schema()
        revised['properties']['Completed']['items']['enum'].remove('NONDEPLOY_VALIDATION')
        with patch.object(schema, 'load_schema', return_value=revised), self.assertRaises(ValueError):
            hub.validate(record())
    def test_schema_cannot_load_remote_refs_or_unknown_keywords(self):
        value = schema.load_schema()
        for change in ({'$ref': 'https://evil.invalid'}, {'unevaluatedProperties': False}, {'$schema': 'other'}):
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory)/'schema.json'; path.write_text(json.dumps(value | change))
                with self.assertRaises(ValueError): schema.load_schema(path)
    def test_historical_invalid_is_rejected_without_normalization(self):
        fixture = json.loads(Path('scripts/control_tower/fixtures/issue41-historical.json').read_text())
        old, invalid = fixture
        valid_record = schema.strict_loads(old['body'][len(hub.MARKER):])
        self.assertEqual(hub.validate(valid_record)['Sequence'], 1)
        bad = schema.strict_loads(invalid['body'][len(hub.MARKER):])
        with self.assertRaises(ValueError): hub.validate(bad)
        result = hub.scan_comments(fixture)
        row = result['latest']['WORK_A']
        self.assertEqual(row['comment_id'], 6216822321)
        self.assertEqual(row['schema_status'], 'INVALID')
        self.assertEqual(set(row['reasons']), {'SCHEMA_COMPLETED_ENUM','SCHEMA_BLOCKERS_ENUM','SCHEMA_NEXTDELTA_ENUM','SCHEMA_EVIDENCE_PATTERN'})
        self.assertNotIn('record', row)
        self.assertEqual(result['frontier']['WORK_A'], 3)
        self.assertEqual(result['history'][0]['schema_status'], 'VALID')
        data = hub.collect(Fake(fixture))
        self.assertEqual(data['lanes']['WORK_A']['handoff_status'], 'INVALID')
        self.assertEqual(data['lanes']['WORK_A']['gate'], 'HOLD')
        self.assertNotIn('P1_SHARED_RETRY', json.dumps(data))
    def test_valid_pass_is_bound_metadata_claim_only(self):
        data = hub.collect(Fake([comment(record())]))
        self.assertEqual(data['lanes']['WORK_A']['gate'], 'REPORTED_PASS')
        self.assertEqual(data['execution_authority'], 'HOLD')
        self.assertEqual(data['lanes']['WORK_A']['artifacts'][0]['id'], 500)
        self.assertEqual(data['lanes']['WORK_B']['source']['state'], 'MERGED_HISTORICAL')
        self.assertIn('WORK_D', data['lanes'])
    def test_missing_handoff_hold(self):
        self.assertTrue(all(v['gate'] == 'HOLD' for v in hub.collect(Fake())['lanes'].values()))
    def test_stale_sha_hold(self):
        data = hub.collect(Fake([comment(record(SHA='b'*40))]))
        self.assertIn('STALE_SHA', data['lanes']['WORK_A']['reasons'])
    def test_ci_mismatch_and_failed_ci_hold(self):
        for ids, status in (([101], 'success'), ([100], 'failure'), ([100], None)):
            fake = Fake([comment(record(CI=ids))]);fake.ci_status=status
            self.assertIn('CI_MISMATCH_OR_NOT_SUCCESS', hub.collect(fake)['lanes']['WORK_A']['reasons'])
    def test_artifact_digest_and_sha_and_expiry_mismatch(self):
        for field, value in (('evidence_hash','d'*64), ('artifact_sha','b'*40), ('expired',True), ('expired',None)):
            fake = Fake([comment(record())]);setattr(fake,field,value)
            self.assertIn('ARTIFACT_DIGEST_MISMATCH_OR_UNVERIFIED', hub.collect(fake)['lanes']['WORK_A']['reasons'])
    def test_unauthorized_author_and_bad_json_rejected(self):
        fake = Fake([comment(record(), 'untrusted'), {'id': 2, 'body': hub.MARKER+'{', 'user': {'login':'Terry4410'}}])
        self.assertEqual(hub.collect(fake)['rejected_records'], 2)
    def test_duplicate_event_same_comment_id_idempotent(self):
        one=hub.collect(Fake([comment(record())]));two=hub.collect(Fake([comment(record()),comment(record())]))
        self.assertEqual(hub.compare(two,one),[])
    def test_duplicate_sequence_even_identical_separate_comment_hold(self):
        data=hub.collect(Fake([comment(record()),comment(record(),number=2)]))
        self.assertIn('DUPLICATE_SEQUENCE',data['lanes']['WORK_A']['reasons'])
    def test_conflicting_same_event_id_fails_closed(self):
        with self.assertRaisesRegex(ValueError,'COMMENT_EVENT_CONFLICT'):
            hub.scan_comments([comment(record()),comment(record(SHA='b'*40))])
    def test_out_of_order_event_delivery_sorts_github_ids(self):
        a=comment(record(),number=1);b=comment(record(Sequence=2),number=2)
        self.assertEqual(hub.scan_comments([b,a]),hub.scan_comments([a,b]))
    def test_replayed_lower_sequence_is_visible_hold(self):
        data=hub.collect(Fake([comment(record(Sequence=3)),comment(record(Sequence=2),number=2)]))
        self.assertIn('REPLAYED_SEQUENCE',data['lanes']['WORK_A']['reasons'])
    def test_higher_sequence_resolves_conflict_preserving_history(self):
        data=hub.collect(Fake([comment(record()),comment(record(),number=2),comment(record(Sequence=2),number=3)]))
        self.assertEqual(data['lanes']['WORK_A']['handoff_status'],'VALIDATED')
        self.assertEqual(data['rejected_records'],1)
    def test_edited_records_invalid(self):
        c=comment(record());c['updated_at']='2026-10-16T09:01:00Z'
        self.assertIn('EDITED_RECORD',hub.scan_comments([c])['latest']['WORK_A']['reasons'])
    def test_sources_and_stream_movement_fail_closed(self):
        for field in ('moving','stream_moves'):
            fake=Fake();setattr(fake,field,True)
            with self.assertRaises(ValueError): hub.collect(fake)
    def test_exact_delta_and_stable_event_id(self):
        old=hub.collect(Fake([comment(record())]));fake=Fake([comment(record(Sequence=2,Evidence=['d'*64]))]);fake.evidence_hash='d'*64
        new=hub.collect(fake);delta=hub.compare(new,old)
        self.assertEqual([x['lane'] for x in delta],['WORK_A'])
        self.assertEqual(delta,hub.compare(new,old));self.assertEqual(hub.compare(new,new),[])
    def test_no_delta_for_timestamp_and_sequence_only(self):
        old=hub.collect(Fake([comment(record())]));new=copy.deepcopy(old)
        new['lanes']['WORK_A']['handoff']['record']['Sequence']=2
        new['lanes']['WORK_A']['source']['commit_at']='2026-10-16T09:01:00Z'
        new['lanes']['WORK_A']['source']['ci'][0]['updated_at']='2026-10-16T09:01:00Z'
        self.assertEqual(hub.compare(new,old),[])
    def test_source_change_notifies_dependent_audit_lane(self):
        old=hub.collect(Fake());new=copy.deepcopy(old)
        new['lanes']['WORK_A']['source']['sha']='b'*40
        self.assertEqual([x['lane'] for x in hub.compare(new,old)],['WORK_A','AUDIT'])
    def test_unmarked_raw_comments_never_exported(self):
        data=hub.collect(Fake([{'id':1,'body':'synthetic-secret employee SQL exception'}]))
        with tempfile.TemporaryDirectory() as directory:
            hub.write_report(data,{},Path(directory))
            text=(Path(directory)/'status.json').read_text()
            self.assertNotIn('synthetic-secret',text);self.assertNotIn('employee',text)
    def test_dashboard_contains_rejection_and_required_columns(self):
        fixture=json.loads(Path('scripts/control_tower/fixtures/issue41-historical.json').read_text())
        with tempfile.TemporaryDirectory() as directory:
            hub.write_report(hub.collect(Fake(fixture)),{},Path(directory))
            text=(Path(directory)/'dashboard.md').read_text()
            for item in ('6216822321','INVALID/HOLD','Completed','Blockers','Next Delta','Owner authorization','provenance:'):
                self.assertIn(item,text)
            self.assertNotIn('P1_SHARED_RETRY',text)
    def test_network_failure_suppressed(self):
        with patch('sys.argv',['hub.py']),patch.object(hub,'collect',side_effect=RuntimeError('synthetic-secret')),patch('builtins.print') as output:
            with self.assertRaises(SystemExit):hub.main()
            output.assert_called_once_with('HUB_COLLECTION_FAILED_HOLD')

class PublishTests(unittest.TestCase):
    def test_prepare_and_check_do_not_claim_handoff_valid(self):
        fake=Fake();plan=publish.prepare(fake,record());self.assertEqual(publish.check(fake,plan),plan)
        self.assertFalse(any('/comments/' in p for p in fake.paths))
    def test_validate_only_cli_cannot_claim_publication(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'record.json';path.write_text(json.dumps(record()))
            with patch('sys.argv',['hub.py','--validate',str(path)]),patch('builtins.print') as output:
                hub.main();output.assert_called_once_with('SCHEMA_VALID_ONLY')
    def test_successful_readback(self):
        fake=Fake();plan=publish.prepare(fake,record());fake.comments.append(comment(record()))
        result=publish.verify(fake,plan,1)
        self.assertEqual(result['result'],'HANDOFF_VALID');self.assertEqual(result['execution_authority'],'HOLD')
    def test_sequence_reserves_invalid_history(self):
        fixture=json.loads(Path('scripts/control_tower/fixtures/issue41-historical.json').read_text())
        with self.assertRaises(ValueError):publish.prepare(Fake(fixture),record(Sequence=3))
        plan=publish.prepare(Fake(fixture),record(Sequence=4))
        self.assertEqual(plan['record']['Sequence'],4)
    def test_source_ci_artifact_denied_before_publishing(self):
        for change in ({'SHA':'b'*40},{'CI':[101]},{'Evidence':['d'*64]},{'Completed':['UNKNOWN']}):
            with self.assertRaises(ValueError):publish.prepare(Fake(),record(**change))
    def test_plan_stales_when_another_lane_publishes(self):
        fake=Fake();plan=publish.prepare(fake,record());fake.comments.append(comment(record(Lane='WORK_C')))
        with self.assertRaises(ValueError):publish.check(fake,plan)
    def test_concurrent_publishers_duplicate_sequence_hold_both(self):
        fake=Fake();p1=publish.prepare(fake,record());p2=publish.prepare(fake,record(Evidence=['e'*64],Completed=['IMPLEMENTATION']))
        fake.comments += [comment(p1['record'],number=1),comment(p2['record'],number=2)]
        for plan,cid in ((p1,1),(p2,2)):
            with self.assertRaises(ValueError):publish.verify(fake,plan,cid)
        self.assertEqual(hub.collect(fake)['lanes']['WORK_A']['handoff_status'],'INVALID')
    def test_readback_body_tampering_unauthorized_and_wrong_issue(self):
        for change in ({'body':publish.body_for(record(SHA='b'*40))},{'user':{'login':'untrusted'}},{'issue_url':'https://api.github.com/repos/x/y/issues/1'}):
            fake=Fake();plan=publish.prepare(fake,record());fake.comments=[comment(record())|change]
            with self.assertRaises(ValueError):publish.verify(fake,plan,1)
    def test_readback_source_changed(self):
        fake=Fake();plan=publish.prepare(fake,record());fake.comments=[comment(record())];fake.moving=True
        with self.assertRaises(ValueError):publish.verify(fake,plan,1)
    def test_schema_hash_plan_tamper(self):
        fake=Fake();plan=publish.prepare(fake,record());plan['schema_sha256']='0'*64
        with self.assertRaises(ValueError):publish.check(fake,plan)
    def test_readback_network_failure_never_claims_valid(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'plan.json';path.write_text(json.dumps(publish.prepare(Fake(),record())))
            with patch('sys.argv',['publish.py','verify','--plan',str(path),'--comment-id','1']),patch.object(publish,'Client',side_effect=RuntimeError('secret')),patch('builtins.print') as output:
                with self.assertRaises(SystemExit):publish.main()
                output.assert_called_once_with('HANDOFF_PUBLISH_HOLD')

class BaselineTests(unittest.TestCase):
    def test_tampered_baseline_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'status.json';path.write_text(json.dumps({'snapshot':{'execution_authority':'GO'},'fingerprint':'bad'}))
            with patch('sys.argv',['hub.py','--previous',str(path)]),patch('builtins.print') as output:
                with self.assertRaises(SystemExit):hub.main()
                output.assert_called_once_with('HUB_COLLECTION_FAILED_HOLD')
    def test_valid_baseline_replay_has_no_events(self):
        data=hub.collect(Fake())
        with tempfile.TemporaryDirectory() as directory:
            first=hub.write_report(data,{},Path(directory));second=hub.write_report(data,first['snapshot'],Path(directory))
            self.assertEqual(first['baseline_mode'],'BOOTSTRAP');self.assertEqual(second['baseline_mode'],'PRIOR_SUCCESS');self.assertEqual(second['audit_queue'],[])
    def test_pagination_overflow_fails_closed(self):
        with patch.object(hub.Client,'get',return_value=[{}]*100),self.assertRaisesRegex(ValueError,'PAGINATION_LIMIT'):
            hub.Client().pages('issues/41/comments')
    def test_workflow_boundary(self):
        text=Path('.github/workflows/control-tower-hub.yml').read_text()
        for forbidden in ('write','id-token:','environment:','azure/','pull_request_target','workflow_run:','secrets.'):
            self.assertNotIn(forbidden,text)
        self.assertIn('persist-credentials: false',text);self.assertIn('cancel-in-progress: false',text);self.assertNotIn('schedule:',text)
    def test_schema_and_validator_lanes_agree(self):
        value=schema.load_schema();self.assertEqual(set(value['required']),hub.FIELDS)
        self.assertEqual(set(value['properties']['Lane']['enum']),set(hub.LANES))

class BaselineSelectionTests(unittest.TestCase):
    def fake(self):
        class Source:
            def get(self,path):
                return {'workflow_runs':[
                    {'id':id,'event':'push','head_branch':branch,'status':'completed','conclusion':'success',
                     'head_sha':'a'*40,'repository':{'full_name':hub.REPO}}
                    for id,branch in ((10,hub.BRANCH),(9,'wrong'),(8,hub.BRANCH))]}
            def pages(self,path,key=None):
                return [{'name':name,'expired':False,'workflow_run':{'head_sha':'a'*40}}
                        for name in ('control-tower-state-8-1','control-tower-state-8-2','control-tower-state-99-9','evil\nkey=value')] 
        return Source()
    def test_prior_success_latest_attempt_and_no_untrusted_output(self):
        self.assertEqual(baseline.select(self.fake(),10),('8','control-tower-state-8-2'))
    def test_expired_or_wrong_source_baselines_do_not_count(self):
        fake=self.fake();fake.pages=lambda *a,**k:[{'name':'control-tower-state','expired':True,'workflow_run':{'head_sha':'a'*40}}]
        self.assertEqual(baseline.select(fake,10),('',''))
        fake.pages=lambda *a,**k:[{'name':'control-tower-state','expired':False,'workflow_run':{'head_sha':'b'*40}}]
        self.assertEqual(baseline.select(fake,10),('',''))
    def test_legacy_baseline_supported(self):
        fake=self.fake();fake.pages=lambda *a,**k:[{'name':'control-tower-state','expired':False,'workflow_run':{'head_sha':'a'*40}}]
        self.assertEqual(baseline.select(fake,10),('8','control-tower-state'))
    def test_artifact_names_are_immutable_per_attempt(self):
        text=Path('.github/workflows/control-tower-hub.yml').read_text()
        self.assertIn('name: control-tower-state-${{ github.run_id }}-${{ github.run_attempt }}',text)
        self.assertNotIn('overwrite:',text)

class EventTests(unittest.TestCase):
    def environment(self):
        return {'GITHUB_ACTIONS':'true','GITHUB_REPOSITORY':hub.REPO,'GITHUB_EVENT_NAME':'push',
                'GITHUB_REF':'refs/heads/'+hub.BRANCH,'GITHUB_SHA':'a'*40}
    def test_authorized_local_and_ci_events(self):
        hub.guard_event({})
        for event in ('push','workflow_dispatch'):
            hub.guard_event(self.environment() | {'GITHUB_EVENT_NAME':event})
    def test_unauthorized_trigger_branch_repo_denied(self):
        for change in ({'GITHUB_EVENT_NAME':'pull_request'}, {'GITHUB_EVENT_NAME':'workflow_run'},
                       {'GITHUB_EVENT_NAME':'issue_comment'}, {'GITHUB_EVENT_NAME':'repository_dispatch'},
                       {'GITHUB_EVENT_NAME':'schedule'}, {'GITHUB_REF':'refs/heads/post-uat/v1.8.0'},
                       {'GITHUB_REF':'refs/heads/main'}, {'GITHUB_REPOSITORY':'evil/repo'}, {'GITHUB_SHA':'bad'}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                hub.guard_event(self.environment() | change)
    def test_unauthorized_cli_fails_before_network(self):
        with patch.dict('os.environ', self.environment() | {'GITHUB_EVENT_NAME':'workflow_run'}), patch('sys.argv',['hub.py']), patch.object(hub,'collect') as collect, patch('builtins.print') as output:
            with self.assertRaises(SystemExit): hub.main()
            collect.assert_not_called();output.assert_called_once_with('HUB_COLLECTION_FAILED_HOLD')

if __name__=='__main__':unittest.main()
