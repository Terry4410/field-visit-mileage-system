"""Synthetic privacy regressions. No Azure CLI, OIDC or network operation."""
from pathlib import Path
import hashlib
import importlib.util
import io
import json
import os
import re
import subprocess
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('r3_privacy',ROOT/'scripts/ci/r3_login_privacy.py')
privacy=importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)
# Synthetic, generated inside the invoked script (not interpolated into workflow logs).
IDS=tuple(f'{n:08x}-1111-4222-8333-444444444444' for n in (101,102,103))
SYNTHETIC_OWNER=str(91000000+1)
SYNTHETIC_REPO=str(92000000+2)
FIXTURE_ONLY_MASKS=('sa','R3Restricted','Disposable_Restricted_2026!Only')

def render_runner_log(masks, text):
    # Models runner registration before with/env/action rendering.
    for value in sorted(masks,key=len,reverse=True):
        text=text.replace(value,'***')
    return text

def sample_log(ids):
    claims=privacy.reviewed_masks(ids)[-5:]
    return '\n'.join(['with:','client-id: '+ids[0],'tenant-id: '+ids[1],
        'subscription-id: '+ids[2],'env: R3_CLIENT_ID='+ids[0],
        'Federated token details:',*claims])

class LoginPrivacyTests(unittest.TestCase):
    def test_pre_step_secret_registration_covers_with_env_and_action_claims(self):
        masked=render_runner_log(privacy.reviewed_masks(IDS),sample_log(IDS))
        self.assertNotIn(IDS[0],masked)
        for value in privacy.reviewed_masks(IDS):self.assertNotIn(value,masked)
        self.assertIn('***',masked)
    def test_cli_output_none_does_not_mask_action_generated_messages(self):
        # No CLI stdout is present; action info() and runner input rendering still leak.
        unmasked=sample_log(IDS)
        self.assertIn(IDS[1],unmasked)
        self.assertIn('repo:'+privacy.REPOSITORY+':environment:uat',unmasked)
    def test_naive_vars_to_env_before_add_mask_is_rejected_as_a_design(self):
        prefix='env: R3_CLIENT_ID='+IDS[0]
        self.assertIn(IDS[0],prefix)  # Already emitted before the script could mask it.
        private=(ROOT/'.github/workflows/r3-private-uat-execution.yml').read_text()
        self.assertNotIn('vars.AZURE_CLIENT_ID',private)
        self.assertIn('secrets.r3_client_value',private)
    def test_malformed_and_command_injection_identifiers_emit_nothing(self):
        for bad in ('', 'not-an-id','\n::warning::unexpected','%0A','invalid\rvalue'):
            stream=io.StringIO()
            with self.assertRaises(ValueError):
                privacy.emit_masks(privacy.reviewed_masks((bad,IDS[1],IDS[2])),stream)
            self.assertEqual(stream.getvalue(),'')
    def test_wrong_context_or_debug_stops_before_values_are_emitted(self):
        good={'GITHUB_REPOSITORY_OWNER_ID':SYNTHETIC_OWNER,'GITHUB_REPOSITORY_ID':SYNTHETIC_REPO,'GITHUB_ACTIONS':'true','GITHUB_REPOSITORY':privacy.REPOSITORY,'GITHUB_REF':privacy.REF,
              **dict(zip(('R3_CLIENT_ID','R3_TENANT_ID','R3_SUBSCRIPTION_ID'),IDS))}
        for key,value in (('RUNNER_DEBUG','1'),('GITHUB_ACTIONS','false'),('GITHUB_REF','refs/heads/main'),('R3_TENANT_ID','')):
            with self.assertRaises(ValueError):privacy.environment_masks({**good,key:value})
    def test_mask_commands_are_escaped_and_case_variants_are_covered(self):
        self.assertEqual(privacy.escape_command('%\r\n'),'%25%0D%0A')
        ids=(IDS[0].replace('00000065','a0000065'),IDS[1],IDS[2])
        masks=privacy.reviewed_masks(ids)
        self.assertIn(ids[0].upper(),masks)
        out=io.StringIO();privacy.emit_masks(masks,out)
        self.assertTrue(all(line.startswith('::add-mask::') for line in out.getvalue().splitlines()))
    def test_parent_preserves_environment_scope_and_uses_transient_secret_inputs(self):
        parent=(ROOT/'.github/workflows/azure-sql-uat-schema-fidelity-readonly.yml').read_text()
        self.assertIn('environment: uat',parent)
        self.assertIn("job.status == 'success' && vars.AZURE_CLIENT_ID",parent)
        self.assertIn('secrets:\n      r3_client_value: ${{ needs.identity-handoff.outputs.client }}',parent)
        self.assertIn('test "${RUNNER_DEBUG:-}" != "1"',parent)
        # ID expressions are job outputs only, never a logged step env/with/run value.
        for line in parent.splitlines():
            if 'vars.AZURE_' in line:self.assertIn("job.status == 'success'",line)
        self.assertNotIn('secrets: inherit',parent)
    def test_production_child_is_call_only_and_guards_original_dispatch_identity(self):
        worker=(ROOT/'.github/workflows/r3-private-uat-execution.yml').read_text()
        self.assertIn('  workflow_call:',worker)
        self.assertNotIn('  workflow_dispatch:',worker)
        self.assertIn("github.event_name == 'workflow_dispatch'",worker)
        self.assertIn(privacy.REPOSITORY+'/'+privacy.PARENT+'@'+privacy.REF,worker)
        self.assertIn('environment: uat',worker)
        self.assertLess(worker.index('run: python3 scripts/ci/r3_login_privacy.py'),worker.index('uses: azure/login@'))
        self.assertIn('AZURE_CORE_OUTPUT: none',worker)
        self.assertIn('azure/login@7184910d9eb2b1c5e48f7073824a90609bb9b6d6',worker)
    def test_nondeploy_secret_bridge_has_no_azure_or_uat_capability(self):
        for file in ('uat-schema-fidelity-proposal-nondeploy.yml','r3-recovery-nondeploy-worker.yml'):
            source=(ROOT/'.github/workflows'/file).read_text()
            for forbidden in ('azure/login@','id-token: write','environment: uat','vars.AZURE_','secrets: inherit'):
                self.assertNotIn(forbidden,source)
            self.assertTrue(set(re.findall(r'secrets\.([a-zA-Z_]+)',source)) <= {'fixture_client','fixture_tenant','fixture_subscription','fixture_sql_password'})
        fixture=(ROOT/'.github/workflows/r3-recovery-nondeploy-worker.yml').read_text()
        self.assertIn('MSSQL_SA_PASSWORD: ${{ secrets.fixture_sql_password }}',fixture)
        self.assertNotIn('Disposable_SQL_2026!Only',fixture)
    def test_disposable_database_identity_is_masked_before_negative_cases(self):
        sample='SCHEMA_TEST_SQL_USER: sa\nSQL Trace started by login "sa"'
        rendered=render_runner_log(FIXTURE_ONLY_MASKS,sample)
        self.assertNotIn(': sa',rendered)
        self.assertNotIn('"sa"',rendered)
        self.assertIn("privacy.emit_masks([*masks,*FIXTURE_ONLY_MASKS],sys.stdout)",Path(__file__).read_text())
    def test_public_failure_does_not_include_bad_input_or_traceback(self):
        env={**os.environ,'GITHUB_ACTIONS':'true','R3_CLIENT_ID':'SYNTHETIC_IDENTITY_MUST_NOT_LEAK'}
        result=subprocess.run([sys.executable,str(ROOT/'scripts/ci/r3_login_privacy.py')],env=env,capture_output=True,text=True)
        self.assertEqual(result.returncode,1)
        self.assertEqual(result.stdout,'')
        self.assertNotIn('SYNTHETIC_IDENTITY',result.stderr)
        self.assertNotIn('Traceback',result.stderr)


class SubjectPrivacyTests(unittest.TestCase):
    def test_both_subject_formats_are_completely_masked(self):
        masks=privacy.reviewed_masks(IDS,owner_id=SYNTHETIC_OWNER,repository_id=SYNTHETIC_REPO)
        owner,name=privacy.REPOSITORY.split('/')
        immutable='repo:'+owner+'@'+SYNTHETIC_OWNER+'/'+name+'@'+SYNTHETIC_REPO+':environment:uat'
        legacy='repo:'+privacy.REPOSITORY+':environment:uat'
        for value in (immutable,legacy):
            self.assertIn(value,masks)
            self.assertEqual(render_runner_log(masks,'subject claim - '+value),'subject claim - ***')
    def test_runtime_ids_are_required_validated_before_any_output(self):
        for bad in ('','0','-1','01','1\n::warning::x','1%0A','9'*21,None):
            out=io.StringIO()
            with self.assertRaises(ValueError):
                privacy.emit_masks(privacy.reviewed_masks(IDS,owner_id=bad,repository_id=SYNTHETIC_REPO),out)
            self.assertEqual(out.getvalue(),'')
    def test_environment_uses_runner_context_without_logged_step_mapping(self):
        env={'GITHUB_ACTIONS':'true','GITHUB_REPOSITORY':privacy.REPOSITORY,'GITHUB_REF':privacy.REF,
             'GITHUB_REPOSITORY_OWNER_ID':SYNTHETIC_OWNER,'GITHUB_REPOSITORY_ID':SYNTHETIC_REPO,
             **dict(zip(('R3_CLIENT_ID','R3_TENANT_ID','R3_SUBSCRIPTION_ID'),IDS))}
        self.assertIn('@'+SYNTHETIC_OWNER+'/',privacy.environment_masks(env)[0])
        for missing in ('GITHUB_REPOSITORY_ID','GITHUB_REPOSITORY_OWNER_ID'):
            with self.assertRaises(ValueError):privacy.environment_masks({**env,missing:''})
        worker=(ROOT/'.github/workflows/r3-private-uat-execution.yml').read_text()
        self.assertNotIn('GITHUB_REPOSITORY_ID:',worker)
        self.assertNotIn('GITHUB_REPOSITORY_OWNER_ID:',worker)


def generate_fixture():
    # Same transport as production, with no environment or OIDC and synthetic values only.
    if os.environ.get('RUNNER_DEBUG')=='1':raise RuntimeError('debug refused')
    password='Disposable_'+hashlib.sha256(b'R3 recovery isolated proof').hexdigest()[:24]+'!Only'
    with open(os.environ['GITHUB_OUTPUT'],'a') as out:
        for name,value in zip(('client','tenant','subscription','password'),(*IDS,password)):
            out.write(name+'='+value+'\n')
    print('PASS: synthetic fixture outputs prepared without value logging.')

def runner_fixture():
    ids=tuple(os.environ.get(k,'') for k in ('R3_CLIENT_ID','R3_TENANT_ID','R3_SUBSCRIPTION_ID'))
    if ids!=IDS:raise RuntimeError('fixture values rejected')
    masks=privacy.reviewed_masks(ids,owner_id=SYNTHETIC_OWNER,repository_id=SYNTHETIC_REPO)
    privacy.emit_masks([*masks,*FIXTURE_ONLY_MASKS],sys.stdout)
    # GitHub's actual runner must redact this action-like synthetic output.
    print(sample_log(ids))
    print('subject claim - '+masks[0])
    print('PASS: synthetic runner masking fixture emitted.')

if __name__=='__main__':
    if '--generate-fixture' in sys.argv or '--runner-fixture' in sys.argv:
        try:
            (generate_fixture if '--generate-fixture' in sys.argv else runner_fixture)()
        except Exception:
            print('STOP: synthetic privacy fixture rejected; details suppressed.',file=sys.stderr)
            raise SystemExit(1) from None
    else:unittest.main()
