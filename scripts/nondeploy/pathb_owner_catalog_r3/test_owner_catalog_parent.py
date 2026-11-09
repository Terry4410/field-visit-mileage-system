import io
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch
import owner_catalog_parent_REVIEW_ONLY as p

class ParentContract(unittest.TestCase):
    def test_exact_targets(self):
        self.assertEqual(p.APP,'api-fieldvisit-pathb-uat')
        self.assertEqual(p.DATABASE,'db-fieldvisit-pathb-uat')
    def test_marker_new(self):
        self.assertIn('r4-durable-20261109',p.MARKER)
        self.assertNotIn('v3-',p.MARKER)
    def test_blob_conditional_create(self):
        q=p.claim_request('x'*120,'a'*64)
        self.assertEqual(q.get_method(),'PUT')
        self.assertEqual(q.get_header('If-none-match'),'*')
        self.assertTrue(q.full_url.startswith('https://stfvsc261026a.blob.core.windows.net/schema-evidence/'))
    def test_blob_non201_hold(self):
        class R:
            status=412
            def __enter__(self):return self
            def __exit__(self,*a):pass
        with self.assertRaises(p.Hold):p.commit_claim(p.claim_request('x'*120,'a'*64),lambda *a,**k:R())
    def test_blob_201_pass(self):
        class R:
            status=201
            def __enter__(self):return self
            def __exit__(self,*a):pass
        self.assertTrue(p.commit_claim(p.claim_request('x'*120,'a'*64),lambda *a,**k:R()))
    def test_blob_unknown_hold(self):
        with self.assertRaises(p.Hold):p.commit_claim(p.claim_request('x'*120,'a'*64),lambda *a,**k:1/0)
    def test_receipt_pass(self):
        x=b'OWNER_SQL_CATALOG=EMPTY_CATALOG_CANDIDATE\nINSTALLER=NOT_RUN\nDDL=NO\nRETRY=FORBIDDEN\n'
        self.assertEqual(p.extract_receipt(x),'EMPTY_CATALOG_CANDIDATE')
    def test_receipt_missing_hold(self):
        with self.assertRaises(p.Hold):p.extract_receipt(b'OWNER_SQL_CATALOG=EMPTY_CATALOG_CANDIDATE\n')
    def test_receipt_duplicate_hold(self):
        with self.assertRaises(p.Hold):p.extract_receipt(b'OWNER_SQL_CATALOG=HOLD_OPERATION\nOWNER_SQL_CATALOG=HOLD_OPERATION\nINSTALLER=NOT_RUN\nDDL=NO\nRETRY=FORBIDDEN')
    def test_ready_ack(self):
        self.assertTrue(p.verify_ready_boundary(b'FV_OWNER_ECHO_OFF=PASS\nFV_OWNER_TOKEN_READY:'))
    def test_ready_without_echo_hold(self):
        with self.assertRaises(p.Hold):p.verify_ready_boundary(b'FV_OWNER_TOKEN_READY:')
    def test_ready_echo_replay_hold(self):
        with self.assertRaises(p.Hold):p.verify_ready_boundary(b'FV_OWNER_ECHO_OFF=PASS\nFV_OWNER_TOKEN_READY:FV_OWNER_TOKEN_READY:')
    def test_identity_fail_closed(self):
        self.assertFalse(p.exact_identity({},{}))
    def test_token_invalid(self):
        with self.assertRaises(p.Hold):p.token_claims('not-a-token',{'https://database.windows.net/'})
    def test_ssh_host_pinning(self):
        with tempfile.TemporaryDirectory() as d:
            f=pathlib.Path(d)/'known_hosts';f.write_text('test')
            a=p.trusted_ssh_argv(43211,f)
            self.assertIn('StrictHostKeyChecking=yes',a)
    def test_main_locked(self):
        self.assertEqual(p.main(),1)

if __name__ == '__main__':unittest.main()
