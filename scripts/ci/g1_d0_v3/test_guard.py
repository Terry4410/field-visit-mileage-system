"""Offline v3 authorization/history, inert registration and additive-source gates."""
import ast
import contextlib
import copy
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
spec = importlib.util.spec_from_file_location('new_v3_guard', HERE / 'guard.py')
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
SHA = 'b' * 40
COMMENT = dict(user=dict(login='Terry4410', id=170161890), author_association='OWNER',
               issue_url='https://api.github.com/repos/' + guard.REPO + '/issues/41',
               body=f'{guard.PREFIX} {SHA} {guard.WORKFLOW} ONE_WSS ZERO_SQL NO_RETRY')
ENV = dict(GITHUB_ACTIONS='true', GITHUB_REPOSITORY=guard.REPO, GITHUB_REF='refs/heads/post-uat/v1.8.0',
           GITHUB_EVENT_NAME='workflow_dispatch', GITHUB_ACTOR='Terry4410', GITHUB_TRIGGERING_ACTOR='Terry4410',
           GITHUB_RUN_ATTEMPT='1', GITHUB_SHA=SHA, APPROVED_SHA=SHA, G1_MODE='D0_V3_DIAGNOSTIC',
           ACCEPT_COST='false', GITHUB_RUN_ID='123', APPROVAL_COMMENT='9999999999')
RUN = dict(id=123, head_sha=SHA, head_branch='post-uat/v1.8.0', event='workflow_dispatch',
           path='.github/workflows/' + guard.WORKFLOW, run_attempt=1,
           actor=dict(login='Terry4410'), triggering_actor=dict(login='Terry4410'))
RUNS = dict(total_count=1, workflow_runs=[RUN])
DELTA = '\n'.join('A\t' + p for p in sorted(guard.ALLOWED))


class GuardTests(unittest.TestCase):
    def test_new_consent_cannot_reuse_v2_or_other_owner_issue_or_sha(self):
        self.assertTrue(guard.valid_approval(COMMENT, SHA))
        for change in [dict(body=COMMENT['body'].replace(guard.PREFIX, 'G1_D0_OBSERVATION_APPROVED')),
                       dict(body=COMMENT['body'].replace('v3.yml', 'v2.yml')),
                       dict(user=dict(login='Terry4410', id=7)), dict(author_association='MEMBER'),
                       dict(issue_url=COMMENT['issue_url'] + '0')]:
            self.assertFalse(guard.valid_approval(dict(COMMENT, **change), SHA))
        self.assertFalse(guard.valid_approval(COMMENT, 'c' * 40))

    def test_only_protected_owner_attempt1_exact_sha_mode_and_no_debug(self):
        self.assertTrue(guard.valid_context(ENV))
        for key, value in [('GITHUB_REF', 'refs/heads/main'), ('GITHUB_REF', 'refs/heads/work/other'),
                           ('GITHUB_EVENT_NAME', 'push'), ('GITHUB_RUN_ATTEMPT', '2'),
                           ('GITHUB_ACTOR', 'other'), ('GITHUB_TRIGGERING_ACTOR', 'other'),
                           ('APPROVED_SHA', 'c' * 40), ('G1_MODE', 'FULL_G1'), ('ACCEPT_COST', 'true'),
                           ('RUNNER_DEBUG', '1')]:
            self.assertFalse(guard.valid_context(dict(ENV, **{key: value})))

    def test_v3_history_is_separate_and_refuses_rerun_extra_or_wrong_run(self):
        self.assertTrue(guard.valid_runs(RUNS, ENV))
        for runs in [dict(total_count=0, workflow_runs=[]), dict(total_count=2, workflow_runs=[RUN, RUN]),
                     dict(total_count=2, workflow_runs=[RUN])]:
            self.assertFalse(guard.valid_runs(runs, ENV))
        for change in [dict(id=124), dict(run_attempt=2), dict(head_sha='c' * 40), dict(head_branch='main'),
                       dict(path='.github/workflows/g1-version-observation-v2.yml'), dict(event='push'),
                       dict(actor={'login': 'other'}), dict(triggering_actor={'login': 'other'})]:
            self.assertFalse(guard.valid_runs(dict(total_count=1, workflow_runs=[dict(RUN, **change)]), ENV))

    def test_source_delta_is_exact_additions_and_cannot_modify_v2_or_pr48(self):
        self.assertTrue(guard.valid_delta(DELTA))
        self.assertFalse(guard.valid_delta(DELTA.replace('A\t', 'M\t', 1)))
        self.assertFalse(guard.valid_delta(DELTA + '\nM\tscripts/ci/g1_bounded/guard.py'))
        self.assertFalse(guard.valid_delta(DELTA + '\nA\tbackend/extra.cs'))
        self.assertFalse(guard.valid_delta(DELTA.split('\n', 1)[1]))

    def test_guard_success_uses_only_new_history_and_exact_comment(self):
        out = io.StringIO()
        with patch.dict(os.environ, ENV, clear=True), patch.object(guard.subprocess, 'check_call'), \
             patch.object(guard.subprocess, 'check_output', return_value=DELTA), \
             patch.object(guard, 'get', side_effect=[COMMENT, RUNS]) as get, contextlib.redirect_stdout(out):
            self.assertEqual(guard.main(), 0)
        self.assertEqual(get.call_args_list[0].args, ('/issues/comments/9999999999',))
        self.assertEqual(get.call_args_list[1].args, ('/actions/workflows/g1-version-observation-v3.yml/runs?event=workflow_dispatch&per_page=100',))
        self.assertIn('PriorV3Dispatches=0', out.getvalue())

    def test_consumed_v2_comment_and_unknown_errors_stop_before_identity(self):
        for env, effect in [(dict(ENV, APPROVAL_COMMENT='6378991235'), None), (ENV, RuntimeError('SYNTHETIC_SECRET'))]:
            out = io.StringIO()
            with patch.dict(os.environ, env, clear=True), patch.object(guard.subprocess, 'check_call'), \
                 patch.object(guard.subprocess, 'check_output', return_value=DELTA), \
                 patch.object(guard, 'get', side_effect=effect) as get, contextlib.redirect_stdout(out):
                self.assertEqual(guard.main(), 1)
                if env['APPROVAL_COMMENT'] == '6378991235':
                    get.assert_not_called()
            self.assertNotIn('SYNTHETIC_SECRET', out.getvalue())

    def test_historical_v2_and_all_existing_sources_are_byte_unchanged(self):
        changed = subprocess.check_output(['git', 'diff', '--name-status', guard.BASE, 'HEAD'], cwd=ROOT, text=True)
        if changed:
            self.assertTrue(guard.valid_delta(changed))
        # Also catch uncommitted edits to any existing tracked file.
        changed = subprocess.check_output(['git', 'diff', '--name-status', guard.BASE, '--'], cwd=ROOT, text=True)
        self.assertTrue(all(line.startswith('A\t') and line[2:] in guard.ALLOWED for line in changed.splitlines()))
        for name in ['scripts/ci/g1_bounded/sql.ps1', 'scripts/ci/g1_bounded/probe/Program.cs',
                     'scripts/ci/g1_bounded/probe/G1Probe.csproj', 'scripts/ci/g1_bounded/guard.py',
                     '.github/workflows/g1-version-observation-v2.yml']:
            expected = subprocess.check_output(['git', 'show', guard.BASE + ':' + name], cwd=ROOT)
            self.assertEqual((ROOT / name).read_bytes(), expected)

    def test_remote_payload_has_no_configuration_network_sql_or_write_capability(self):
        source = (HERE / 'remote.py').read_text()
        tree = ast.parse(source)
        imports = {alias.name for node in ast.walk(tree) if isinstance(node, ast.Import) for alias in node.names}
        self.assertEqual(imports, {'hashlib', 'json'})
        for node in ast.walk(tree):
            if isinstance(node, ast.Attribute):
                self.assertNotIn(node.attr, {'environ', 'write_bytes', 'write_text', 'Popen', 'system', 'connect', 'unlink', 'mkdir'})
        self.assertNotIn('appsettings', source)
        self.assertNotIn('/environ', source)
        self.assertNotIn('ConnectionString', source)

    def test_main_registration_is_inert_and_has_no_credentials_oidc_or_checkout(self):
        source = (ROOT / 'docs/release/g1-d0-v3/main-registration.yml').read_text()
        self.assertIn('permissions: {}', source)
        self.assertIn('exit 1', source)
        for forbidden in ['environment:', 'secrets.', 'vars.', 'id-token:', 'uses:', 'checkout', 'python', 'curl', 'wget']:
            self.assertNotIn(forbidden, source)

    def test_offline_ci_has_no_live_permissions_environment_or_sdk(self):
        source = (ROOT / '.github/workflows/g1-d0-v3-offline.yml').read_text()
        for forbidden in ['workflow_dispatch:', 'environment:', 'secrets.', 'vars.', 'id-token:', 'setup-dotnet', 'setup-node', 'az login', 'd0.py >']:
            self.assertNotIn(forbidden, source)
        self.assertIn('unittest discover -s scripts/ci/g1_d0_v3', source)
        live = (ROOT / '.github/workflows/g1-version-observation-v3.yml').read_text()
        self.assertNotIn('  push:', live)
        self.assertNotIn('  pull_request:', live)
        self.assertIn('needs: [source, identity]', live)
        self.assertIn('uses: ./.github/workflows/g1-d0-v3-worker.yml', live)
        worker = (ROOT / '.github/workflows/g1-d0-v3-worker.yml').read_text()
        self.assertIn('timeout --signal=TERM --kill-after=5s 300s', worker)
        self.assertIn('AZURE_CLIENT_ID: ${{ secrets.client }}', worker)
        self.assertNotIn('sql.ps1', worker)
        self.assertNotIn('SqlServer', worker)

    def test_source_manifest_hashes_all_new_execution_files(self):
        # Manifest itself is tree/SHA-bound, not recursively self-hashed.
        manifest = HERE / 'checksums.sha256'
        entries = {}
        for line in manifest.read_text().splitlines():
            digest, name = line.split('  ', 1)
            self.assertEqual(hashlib.sha256((ROOT / name).read_bytes()).hexdigest(), digest)
            entries[name] = digest
        self.assertEqual(set(entries), guard.ALLOWED - {'scripts/ci/g1_d0_v3/checksums.sha256'})


if __name__ == '__main__':
    unittest.main()
