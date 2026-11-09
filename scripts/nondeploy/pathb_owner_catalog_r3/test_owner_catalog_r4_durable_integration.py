"""Focused offline regression only. No cloud, tokens, network or SQL."""
import contextlib
import io
import json
import tempfile
from pathlib import Path
from unittest import TestCase, mock
from zipfile import ZipFile
import hashlib

import build_owner_catalog_candidate as builder
import owner_catalog_azure_adapter_REVIEW_ONLY as adapter
import owner_catalog_durable_receipt_REVIEW_ONLY as durable
import owner_sql_catalog_child_REVIEW_ONLY as child
import owner_catalog_parent_REVIEW_ONLY as parent

OP = '12ab' * 8


class R4Integration(TestCase):
    def test_builder_stages_exact_four_files_and_hashes(self):
        with tempfile.TemporaryDirectory() as tmp:
            cand = Path(tmp)/'candidate.zip'
            binding = Path(tmp)/'binding.json'
            directory = Path(__file__).resolve().parent
            sha, bindsha = builder.build(directory, cand, binding)
            self.assertEqual(hashlib.sha256(cand.read_bytes()).hexdigest(), sha)
            self.assertEqual(hashlib.sha256(binding.read_bytes()).hexdigest(), bindsha)
            b = json.loads(binding.read_text())
            with ZipFile(cand) as z:
                self.assertEqual(set(z.namelist()), {
                    'manifest.json', 'owner_sql_catalog_child_REVIEW_ONLY.py',
                    'owner_catalog_durable_receipt_REVIEW_ONLY.py',
                    'pathb_catalog_readonly_REVIEW_ONLY.sql'})
                self.assertEqual(z.read('owner_catalog_durable_receipt_REVIEW_ONLY.py'),
                                 (directory/'owner_catalog_durable_receipt_REVIEW_ONLY.py').read_bytes())
            self.assertEqual(adapter.inspect_candidate(cand.read_bytes(), b)['contract'],
                             adapter.CONTRACT)
            adapter.validate_binding(b, directory/'owner_catalog_azure_adapter_REVIEW_ONLY.py',
                directory/'owner_catalog_integration_REVIEW_ONLY.py',
                directory/'owner_catalog_parent_REVIEW_ONLY.py')

    def test_no_consumed_r3_claim_or_root(self):
        self.assertIn('r4-durable', parent.MARKER)
        self.assertNotIn('r3-20261109', parent.MARKER)
        self.assertIn('fv-owner-catalog-r4-', adapter.REMOTE_PREFIX)
        self.assertEqual(child.CLAIM_PREFIX, '/home/LogFiles/fv-pathb-owner-catalog-r4-')
        self.assertIn('owner_catalog_durable_receipt_REVIEW_ONLY.py', adapter.REMOTE_STAGE)
        self.assertIn('owner_catalog_durable_receipt_REVIEW_ONLY.py', adapter.REMOTE_CLEANUP)

    def test_receipt_before_good_terminal(self):
        record = []
        def store(*args):
            record.append(args)
            return 'a'*64
        with mock.patch.object(child, 'durable_store', side_effect=store):
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                success = child.terminal('EMPTY_CATALOG_CANDIDATE', OP, 'b'*64, True)
        self.assertTrue(success)
        self.assertEqual(record, [(OP, 'b'*64, 'EMPTY_CATALOG_CANDIDATE', True)])
        self.assertIn('OWNER_SQL_CATALOG=EMPTY_CATALOG_CANDIDATE', out.getvalue())

    def test_failed_persistence_fails_closed(self):
        with mock.patch.object(child, 'durable_store',
                               side_effect=durable.ReceiptHold('fail')):
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                success = child.terminal('CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED',
                                         OP, 'b'*64, True)
        self.assertFalse(success)
        self.assertIn('HOLD_RECEIPT_PERSISTENCE', out.getvalue())
        self.assertNotIn('OWNER_SQL_CATALOG=CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED',
                         out.getvalue())

    def test_new_auth_decision_distinct(self):
        self.assertIn('R4_DURABLE', adapter.AUTH_DECISION)
        self.assertNotIn('R3_EXACT_SHA', adapter.AUTH_DECISION)
        self.assertIn('HOLD_RECEIPT_PERSISTENCE', parent.ALLOWED_RECEIPTS)
        self.assertIn('HOLD_RECEIPT_PERSISTENCE', adapter.SAFE_TERMINALS)

    def test_review_only_default(self):
        self.assertEqual(child.main(), 1)
        self.assertEqual(durable.main(), 1)
        self.assertEqual(adapter.main([]), 0)

    def test_no_sql_if_bad_operation(self):
        with mock.patch.object(child, 'durable_store') as save, mock.patch.object(
            child.subprocess, 'Popen') as popen:
            self.assertEqual(child.candidate_execute('/no', 'b'*64, '../bad',
                                                      'c'*64, True), 1)
        save.assert_not_called()
        popen.assert_not_called()

    def test_no_sql_if_blob_gate_false(self):
        with mock.patch.object(child, 'durable_store') as save, mock.patch.object(
            child.subprocess, 'Popen') as popen:
            self.assertEqual(child.candidate_execute('/no', 'b'*64, OP,
                                                      'c'*64, False), 1)
        save.assert_called_once()
        popen.assert_not_called()
