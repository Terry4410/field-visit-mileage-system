import base64
import hashlib
import json
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

import owner_catalog_azure_adapter_REVIEW_ONLY as a
from build_owner_catalog_candidate import build


ROOT = Path(__file__).resolve().parent


def jwt(claims):
    def enc(value):
        return base64.urlsafe_b64encode(json.dumps(value).encode()).rstrip(b'=').decode()
    return enc({'alg': 'none'}) + '.' + enc(claims) + '.signature'


class AdapterContractTests(unittest.TestCase):
    def built(self, directory):
        candidate = directory / 'candidate.zip'
        binding = directory / 'binding.json'
        build(ROOT, candidate, binding)
        return candidate, binding, json.loads(binding.read_text())

    def test_deterministic_build_and_full_inspection(self):
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            c1, b1, binding1 = self.built(Path(first))
            c2, b2, binding2 = self.built(Path(second))
            self.assertEqual(c1.read_bytes(), c2.read_bytes())
            self.assertEqual(b1.read_bytes(), b2.read_bytes())
            manifest = a.inspect_candidate(c1.read_bytes(), binding1)
            self.assertEqual(manifest['sql_calls_max'], 1)
            self.assertEqual(manifest['ddl_dml'], 'FORBIDDEN')

    def test_candidate_tamper_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            candidate, _, binding = self.built(Path(directory))
            raw = bytearray(candidate.read_bytes())
            raw[-10] ^= 1
            with self.assertRaises(Exception):
                a.inspect_candidate(bytes(raw), binding)

    def test_binding_controls_exact_current_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            _, _, binding = self.built(Path(directory))
            a.validate_binding(binding, ROOT / 'owner_catalog_azure_adapter_REVIEW_ONLY.py',
                               ROOT / 'owner_catalog_integration_REVIEW_ONLY.py',
                               ROOT / 'owner_catalog_parent_REVIEW_ONLY.py')
            binding['max_sql_calls'] = 2
            with self.assertRaises(a.Hold):
                a.validate_binding(binding, ROOT / 'owner_catalog_azure_adapter_REVIEW_ONLY.py',
                                   ROOT / 'owner_catalog_integration_REVIEW_ONLY.py',
                                   ROOT / 'owner_catalog_parent_REVIEW_ONLY.py')

    def test_authorization_expiry_and_exact_hashes(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            candidate, binding_path, binding = self.built(directory)
            adapter = ROOT / 'owner_catalog_azure_adapter_REVIEW_ONLY.py'
            instance = 'instance-12345678'
            now = int(time.time())
            auth = {
                'decision': a.AUTH_DECISION, 'approved_release': a.RELEASE,
                'resource_group': a.RG, 'app': a.APP, 'sql_server': a.SQL_SERVER,
                'database': a.DATABASE, 'marker': a.MARKER, 'ddl_dml': 'FORBIDDEN',
                'max_duration_seconds': 600, 'max_ssh_sessions': 2,
                'max_storage_tokens': 1, 'max_sql_tokens': 1,
                'max_storage_puts': 1, 'max_sql_calls': 1,
                'automatic_retry': False, 'operation_id': '1' * 32,
                'expires_at_epoch': now + 1200,
                'candidate_sha256': hashlib.sha256(candidate.read_bytes()).hexdigest(),
                'binding_sha256': hashlib.sha256(binding_path.read_bytes()).hexdigest(),
                'adapter_sha256': hashlib.sha256(adapter.read_bytes()).hexdigest(),
                'instance_id_sha256': hashlib.sha256(instance.encode()).hexdigest(),
            }
            paths = {'adapter': adapter}
            a.validate_authorization(auth, paths, candidate.read_bytes(),
                                     binding_path.read_bytes(), instance, now)
            auth['expires_at_epoch'] = now - 1
            with self.assertRaises(a.Hold):
                a.validate_authorization(auth, paths, candidate.read_bytes(),
                                         binding_path.read_bytes(), instance, now)

    def test_cli_token_counter_blocks_replay(self):
        cli = a.AzureCLI()
        token = 'x' * 120
        fake = type('CP', (), {'returncode': 0, 'stdout': token.encode(), 'stderr': b''})()
        with patch('subprocess.run', return_value=fake):
            self.assertEqual(cli.token('https://storage.azure.com/', 'storage'), token)
            with self.assertRaises(a.Hold):
                cli.token('https://storage.azure.com/', 'storage')
            self.assertEqual(cli.token('https://database.windows.net/', 'sql'), token)
            with self.assertRaises(a.Hold):
                cli.token('https://database.windows.net/', 'sql')

    def test_token_claims_owner_tenant_audience_and_ttl(self):
        claims = {'oid': 'owner', 'tid': 'tenant',
                  'aud': 'https://database.windows.net/', 'nbf': int(time.time()) - 10,
                  'exp': int(time.time()) + 1200}
        token = jwt(claims)
        with patch('owner_catalog_parent_REVIEW_ONLY.OWNER_SHA', hashlib.sha256(b'owner').hexdigest()), \
             patch('owner_catalog_parent_REVIEW_ONLY.TENANT_SHA', hashlib.sha256(b'tenant').hexdigest()):
            self.assertTrue(a.token_claims(token, a.SQL_AUDIENCE, 600))
            claims['aud'] = 'https://storage.azure.com/'
            with self.assertRaises(Exception):
                a.token_claims(jwt(claims), a.SQL_AUDIENCE, 600)
            claims['aud'] = 'https://database.windows.net/'
            claims['nbf'] = int(time.time()) + 60
            with self.assertRaises(Exception):
                a.token_claims(jwt(claims), a.SQL_AUDIENCE, 600)

    def test_exact_marker_line_rejects_partial_and_old_buffer(self):
        ssh = object.__new__(a.SSH)
        ssh.buf = b'prefix FV_OWNER_STAGE_READY=abc\r\n'
        ssh.deadline = time.monotonic() + 1
        ssh.password = b'never'
        ssh.proc = type('P', (), {'poll': lambda self: 0})()
        ssh.fd = -1
        with patch.object(a.SSH, '_read', side_effect=a.Hold()):
            with self.assertRaises(a.Hold):
                ssh.expect_line(b'FV_OWNER_STAGE_READY=abc', .01)

    def test_python_command_rejects_shell_metacharacter(self):
        with self.assertRaises(a.Hold):
            a.python_command('pass', ['safe', "bad';id"])

    def test_management_preflight_requires_exact_app_and_unique_instance(self):
        class CLI:
            def az_json(self, args):
                if args[:2] == ['account', 'show']:
                    return {}
                if args[:3] == ['ad', 'signed-in-user', 'show']:
                    return {}
                if args[:2] == ['webapp', 'show']:
                    return {'name': a.APP, 'state': 'Running',
                            'id': '/subscriptions/sub/resourceGroups/' + a.RG +
                                  '/providers/Microsoft.Web/sites/' + a.APP}
                return [{'id': 'exact-instance-id', 'name': 'worker-instance-01'}]
        with patch.object(a, 'exact_identity', return_value=True), \
             patch.object(a, 'SUB_SHA', hashlib.sha256(b'sub').hexdigest()), \
             patch.object(a, 'run', return_value=b'--instance --port --timeout'):
            self.assertEqual(a.management_preflight(CLI(), 'sub', 'exact-instance-id'),
                             'worker-instance-01')
            with self.assertRaises(a.Hold):
                a.management_preflight(CLI(), 'sub', 'different-instance')

    def test_local_journal_is_exclusive_and_never_reset(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'once.json'
            a.consume_journal(path, '1' * 32, 'a' * 64, 'b' * 64)
            with self.assertRaises(Exception):
                a.consume_journal(path, '1' * 32, 'a' * 64, 'b' * 64)
            self.assertEqual(json.loads(path.read_text())['status'], 'ATTEMPTED')

    def test_remote_cleanup_is_exact_owned_root_only(self):
        self.assertIn("re.fullmatch(r'/tmp/fv-owner-catalog-r4-[0-9a-f]{32}'", a.REMOTE_CLEANUP)
        self.assertIn("st.st_uid!=os.geteuid()", a.REMOTE_CLEANUP)
        self.assertIn("if refs>1", a.REMOTE_CLEANUP)
        self.assertNotIn('*', a.REMOTE_CLEANUP)

    def test_no_live_sql_or_azure_on_import_or_default_main(self):
        source = (ROOT / 'owner_catalog_azure_adapter_REVIEW_ONLY.py').read_text()
        self.assertIn("if not args.execute:", source)
        self.assertIn('AUTHORIZATION_LOCKED_NONDEPLOY', source)
        self.assertNotIn('shell=True', source)

    def test_default_main_is_nonlive(self):
        self.assertEqual(a.main([]), 0)

    def test_execute_failure_is_sanitized(self):
        with patch.object(a, 'execute', side_effect=RuntimeError('secret raw error')):
            self.assertEqual(a.main(['--execute', '--candidate', 'c', '--binding', 'b',
                                     '--authorization', 'a', '--journal', '/tmp/j',
                                     '--subscription', 's', '--instance-id', 'i']), 1)


if __name__ == '__main__':
    unittest.main()
