import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import hub


def record(**changes):
    value = {'Version': 1, 'Lane': 'WORK_A', 'SHA': 'a' * 40, 'CI': [100], 'Status': 'PASS',
             'Completed': ['NONDEPLOY_VALIDATION'], 'Blockers': [], 'NextDelta': ['OWNER_REVIEW'],
             'OwnerAuthorization': 'NONDEPLOY_ONLY', 'Evidence': ['e' * 64], 'Sequence': 1}
    value.update(changes)
    return value


def comment(value, identity='Terry4410', number=1):
    return {'id': number, 'user': {'login': identity}, 'body': hub.MARKER + json.dumps(value)}


class Fake:
    def __init__(self, comments=None):
        self.comments = comments or []
        self.paths = []
        self.moving = False
    def get(self, path):
        self.paths.append(path)
        if path.startswith('pulls/'):
            sha = 'b' * 40 if self.moving and self.paths.count(path) > 1 else 'a' * 40
            return {'head': {'sha': sha, 'repo': {'full_name': hub.REPO}}, 'state': 'open', 'merged': False}
        if path.startswith('actions/runs?'):
            return {'total_count': 1, 'workflow_runs': [{'id': 100, 'head_sha': 'a' * 40, 'conclusion': 'success'}]}
        if path.startswith('git/ref/'):
            return {'object': {'sha': 'f' * 40}}
        raise ValueError('UNEXPECTED_ENDPOINT')
    def pages(self, path):
        return self.comments


class ContractTests(unittest.TestCase):
    def test_valid_record(self):
        self.assertEqual(hub.validate(record())['Status'], 'PASS')
    def test_arbitrary_text_and_secret_carriers_denied(self):
        for field in ('Completed', 'Blockers', 'NextDelta', 'Evidence', 'CI', 'Status', 'SHA', 'OwnerAuthorization', 'Lane'):
            for value in ('token=synthetic-secret', 'employee@example.invalid', 'SELECT * FROM employee', 'ProviderException text'):
                with self.subTest(field=field, value=value), self.assertRaises((ValueError, TypeError)):
                    hub.validate(record(**{field: [value] if field in ('Completed', 'Blockers', 'NextDelta', 'Evidence', 'CI') else value}))
    def test_extra_fields_denied(self):
        with self.assertRaises(ValueError):
            hub.validate(record(Token='synthetic'))
    def test_privileged_authority_denied(self):
        for authority in ('GO', 'MERGE', 'SQL', 'DEPLOY', 'MIGRATE', 'RESTORE', 'ENABLE', None):
            with self.subTest(authority=authority), self.assertRaises(ValueError):
                hub.validate(record(OwnerAuthorization=authority))
    def test_unsubstantiated_pass_denied(self):
        for change in ({'CI': []}, {'Evidence': []}, {'Blockers': ['SCHEMA_FIDELITY']}):
            with self.assertRaises(ValueError):
                hub.validate(record(**change))
    def test_boundaries(self):
        for change in ({'Sequence': True}, {'Sequence': 0}, {'CI': [True]}, {'Evidence': ['e'*64]*33}, {'Completed': ['NO_CHANGE']*17}):
            with self.assertRaises(ValueError):
                hub.validate(record(**change))
    def test_valid_pass_is_claim_only(self):
        data = hub.collect(Fake([comment(record())]))
        self.assertEqual(data['lanes']['WORK_A']['gate'], 'REPORTED_PASS')
        self.assertEqual(data['execution_authority'], 'HOLD')
    def test_missing_handoff_hold(self):
        self.assertTrue(all(v['gate'] == 'HOLD' for v in hub.collect(Fake())['lanes'].values()))
    def test_stale_sha_hold(self):
        data = hub.collect(Fake([comment(record(SHA='b'*40))]))
        self.assertIn('STALE_SHA', data['lanes']['WORK_A']['reasons'])
    def test_wrong_sha_ci_id_hold(self):
        data = hub.collect(Fake([comment(record(CI=[101]))]))
        self.assertIn('CI_UNVERIFIED', data['lanes']['WORK_A']['reasons'])
    def test_unauthorized_author_and_bad_json_rejected(self):
        fake = Fake([comment(record(), 'untrusted'), {'id': 2, 'body': hub.MARKER + '{', 'user': {'login': 'Terry4410'}}])
        self.assertEqual(hub.collect(fake)['rejected_records'], 2)
    def test_duplicates_are_idempotent(self):
        one = hub.collect(Fake([comment(record())]))
        two = hub.collect(Fake([comment(record()), comment(record(), number=2)]))
        self.assertEqual(hub.compare(two, one), [])
    def test_equal_sequence_conflict_hold(self):
        data = hub.collect(Fake([comment(record()), comment(record(Evidence=['d'*64]), number=2)]))
        self.assertIn('SEQUENCE_CONFLICT', data['lanes']['WORK_A']['reasons'])
    def test_older_sequence_cannot_override(self):
        data = hub.collect(Fake([comment(record(Sequence=3)), comment(record(Sequence=2, SHA='b'*40), number=2)]))
        self.assertEqual(data['lanes']['WORK_A']['handoff']['record']['Sequence'], 3)
    def test_source_moves_fails_closed(self):
        fake = Fake()
        fake.moving = True
        with self.assertRaisesRegex(ValueError, 'SOURCE_MOVED_RETRY'):
            hub.collect(fake)
    def test_exact_delta_and_stable_event_id(self):
        old = hub.collect(Fake([comment(record())]))
        new = hub.collect(Fake([comment(record(Sequence=2, Evidence=['d'*64]))]))
        delta = hub.compare(new, old)
        self.assertEqual([x['lane'] for x in delta], ['WORK_A'])
        self.assertEqual(delta, hub.compare(new, old))
        self.assertEqual(hub.compare(new, new), [])
    def test_source_change_notifies_audit_lane(self):
        old = hub.collect(Fake())
        new = copy.deepcopy(old)
        new['prs']['33']['sha'] = 'b'*40
        self.assertEqual([x['lane'] for x in hub.compare(new, old)], ['WORK_A', 'AUDIT'])
    def test_unmarked_raw_comments_never_exported(self):
        data = hub.collect(Fake([{'id': 1, 'body': 'synthetic-secret employee SQL exception'}]))
        with tempfile.TemporaryDirectory() as directory:
            hub.write_report(data, {}, Path(directory))
            text = (Path(directory)/'status.json').read_text()
            self.assertNotIn('synthetic-secret', text)
            self.assertNotIn('employee', text)
    def test_failed_ci_hold(self):
        fake = Fake([comment(record())])
        base_get = fake.get
        def get(path):
            value = base_get(path)
            if path.startswith('actions/runs?'):
                value['workflow_runs'][0]['conclusion'] = 'failure'
            return value
        fake.get = get
        self.assertIn('CI_UNVERIFIED', hub.collect(fake)['lanes']['WORK_A']['reasons'])
    def test_workflow_boundary(self):
        text = Path('.github/workflows/control-tower-hub.yml').read_text()
        for forbidden in ('write', 'id-token:', 'environment:', 'azure/', 'pull_request_target', 'workflow_run:', 'secrets.'):
            self.assertNotIn(forbidden, text)
        self.assertIn('persist-credentials: false', text)
        self.assertIn('cancel-in-progress: false', text)
        self.assertNotIn('schedule:', text)
    def test_network_failure_suppressed(self):
        with patch('sys.argv', ['hub.py']), patch.object(hub, 'collect', side_effect=RuntimeError('synthetic-secret')), patch('builtins.print') as output:
            with self.assertRaises(SystemExit):
                hub.main()
            output.assert_called_once_with('HUB_COLLECTION_FAILED_HOLD')

if __name__ == '__main__':
    unittest.main()

class BaselineTests(unittest.TestCase):
    def test_tampered_baseline_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'status.json'
            path.write_text(json.dumps({'snapshot': {'execution_authority': 'GO'}, 'fingerprint': 'bad'}))
            with patch('sys.argv', ['hub.py', '--previous', str(path)]), patch('builtins.print') as output:
                with self.assertRaises(SystemExit):
                    hub.main()
                output.assert_called_once_with('HUB_COLLECTION_FAILED_HOLD')
    def test_valid_baseline_replay_has_no_events(self):
        data = hub.collect(Fake())
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            first = hub.write_report(data, {}, output)
            second = hub.write_report(data, first['snapshot'], output)
            self.assertEqual(first['baseline_mode'], 'BOOTSTRAP')
            self.assertEqual(second['baseline_mode'], 'PRIOR_SUCCESS')
            self.assertEqual(second['audit_queue'], [])
    def test_comment_pagination_overflow_fails_closed(self):
        client = hub.Client()
        with patch.object(client, 'get', return_value=[{}] * 100):
            with self.assertRaisesRegex(ValueError, 'PAGINATION_LIMIT'):
                client.pages('issues/41/comments')
    def test_schema_and_validator_codes_agree(self):
        schema = json.loads(Path('docs/control-tower/handoff.schema.json').read_text())
        self.assertEqual(set(schema['required']), hub.FIELDS)
        self.assertEqual(set(schema['properties']['Completed']['items']['enum']), hub.CODES)
        self.assertEqual(set(schema['properties']['Lane']['enum']), set(hub.LANES))
    def test_baseline_selection_excludes_wrong_branch_and_current(self):
        import runpy
        fake = Fake()
        def get(path):
            if 'workflows/' in path:
                return {'workflow_runs': [
                    {'id': 9, 'event': 'push', 'head_branch': 'evil', 'repository': {'full_name': hub.REPO}},
                    {'id': 10, 'event': 'push', 'head_branch': 'work/control-tower-hub-20261016', 'repository': {'full_name': hub.REPO}},
                    {'id': 8, 'event': 'push', 'head_branch': 'work/control-tower-hub-20261016', 'repository': {'full_name': hub.REPO}}]}
            return {'artifacts': [{'name': 'control-tower-state', 'expired': False}]}
        fake.get = get
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory) / 'output'
            with patch.dict('os.environ', {'GITHUB_RUN_ID': '10', 'GITHUB_OUTPUT': str(out)}), patch.object(hub, 'Client', return_value=fake):
                runpy.run_path('scripts/control_tower/baseline.py')
            self.assertEqual(out.read_text(), 'run_id=8\n')
