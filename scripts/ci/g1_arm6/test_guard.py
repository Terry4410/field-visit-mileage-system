"""Strict GitHub-only guard + inert entry point audit, no online calls."""
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import guard

ROOT = pathlib.Path(__file__).resolve().parents[3]
SHA = 'a' * 40


def context():
    return dict(GITHUB_ACTIONS='true', GITHUB_REPOSITORY=guard.REPO,
                GITHUB_REF='refs/heads/post-uat/v1.8.0', GITHUB_EVENT_NAME='workflow_dispatch',
                GITHUB_ACTOR='Terry4410', GITHUB_TRIGGERING_ACTOR='Terry4410', GITHUB_RUN_ATTEMPT='1',
                RUNNER_DEBUG='0', GITHUB_SHA=SHA, APPROVED_SHA=SHA,
                G1_MODE='SCM_ARM6_READONLY', ACCEPT_COST='false', GITHUB_RUN_ID='1234')


class GuardTests(unittest.TestCase):
    def test_owner_exact_line_new_authority_not_old_consent(self):
        line = f'{guard.PREFIX} {SHA} {guard.WORKFLOW} MAX6_GET ZERO_SCM ZERO_SQL NO_RETRY'
        c = dict(user=dict(login='Terry4410', id=170161890), author_association='OWNER',
                 issue_url='https://api.github.com/repos/' + guard.REPO + '/issues/41', body='Title\n' + line)
        self.assertTrue(guard.approval(c, SHA))
        for field, replacement in [('author_association', 'NONE'), ('issue_url', 'bad')]:
            wrong = c.copy(); wrong[field] = replacement; self.assertFalse(guard.approval(wrong, SHA))
        self.assertFalse(guard.approval(dict(c, body='G1_SCM_ARM_READONLY_APPROVED ' + SHA), SHA))
        self.assertFalse(guard.approval(c, 'b' * 40))

    def test_exact_immutable_branch_and_one_shot(self):
        e = context()
        self.assertTrue(guard.context(e))
        for field, changed in [('GITHUB_REF', 'refs/heads/main'), ('GITHUB_RUN_ATTEMPT', '2'),
                               ('GITHUB_ACTOR', 'stranger'), ('APPROVED_SHA', 'b' * 40),
                               ('G1_MODE', 'D0_V3_DIAGNOSTIC'), ('ACCEPT_COST', 'true')]:
            wrong = e.copy(); wrong[field] = changed; self.assertFalse(guard.context(wrong))
        run = dict(id=1234, head_sha=SHA, head_branch='post-uat/v1.8.0', run_attempt=1,
                   path='.github/workflows/' + guard.WORKFLOW, event='workflow_dispatch',
                   actor={'login': 'Terry4410'}, triggering_actor={'login': 'Terry4410'})
        self.assertTrue(guard.one_shot({'total_count': 1, 'workflow_runs': [run]}, e))
        self.assertFalse(guard.one_shot({'total_count': 2, 'workflow_runs': [run]}, e))
        self.assertFalse(guard.one_shot({'total_count': 1, 'workflow_runs': [dict(run, run_attempt=2)]}, e))

    def test_only_ten_additions_accepted(self):
        changes = '\n'.join('A\t' + f for f in sorted(guard.ALLOWED))
        self.assertEqual(len(guard.ALLOWED), 10)
        self.assertTrue(guard.delta(changes))
        self.assertFalse(guard.delta(changes.replace('A\t', 'M\t', 1)))
        self.assertFalse(guard.delta(changes + '\nA\tbackend/src/FieldVisit.Api/Program.cs'))
        self.assertFalse(guard.delta(changes.splitlines()[0]))

    def test_main_stub_and_offline_are_inert(self):
        stub = (ROOT / 'docs/release/g1-arm6/main-registration.yml').read_text()
        self.assertIn('permissions: {}', stub)
        self.assertIn('exit 1', stub)
        for bad in ('azure/login', 'id-token: write', 'environment:', 'https://management.azure.com', 'actions/checkout'):
            self.assertNotIn(bad, stub)
        offline = (ROOT / '.github/workflows/g1-arm6-offline.yml').read_text()
        self.assertIn('NONDEPLOY', offline)
        for bad in ('workflow_dispatch:', 'id-token: write', 'environment: uat', 'azure/login', 'AZURE_CLIENT_ID:'):
            self.assertNotIn(bad, offline)

    def test_live_executor_has_no_scm_sql_change_endpoint(self):
        worker = (ROOT / '.github/workflows/g1-arm6-worker.yml').read_text()
        self.assertIn('workflow_call:', worker)
        self.assertIn('id-token: write', worker)
        for bad in ('az webapp', 'sqlcmd', 'websocket', 'azure/webapps-deploy', 'curl ', 'POST ', 'PUT '):
            self.assertNotIn(bad, worker)
        arm = (ROOT / 'scripts/ci/g1_arm6/arm6.py').read_text()
        for bad in ('sys.stderr.write(', 'print(token)', 'print(claims)', 'subprocess.', 'websocket.', 'os.system(', 'requests.'):
            self.assertNotIn(bad, arm)

    def test_manifest_covers_source_execution_files(self):
        manifest = (ROOT / 'scripts/ci/g1_arm6/checksums.sha256').read_text().splitlines()
        target = set(guard.ALLOWED) - {'scripts/ci/g1_arm6/checksums.sha256', 'docs/release/g1-arm6/REVIEW.md'}
        paths = {x.split('  ', 1)[1] for x in manifest}
        self.assertEqual(paths, target)


if __name__ == '__main__':
    unittest.main()
