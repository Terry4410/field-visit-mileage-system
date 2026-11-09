import hashlib
import unittest
from owner_catalog_integration_REVIEW_ONLY import Controller,Hold

PAYLOAD=b'pinned-review-payload'
PIN=hashlib.sha256(PAYLOAD).hexdigest()

class Fake:
    def __init__(self, fail=None, outcome='EMPTY_CATALOG_CANDIDATE'):
        self.fail=fail;self.outcome=outcome;self.calls=[]
    def _call(self,name,ret=True):
        self.calls.append(name)
        if self.fail==name:raise RuntimeError(name)
        return ret
    def verify_owner_target(self):return self._call('verify')
    def claim_blob(self,sha):return self._call('claim')
    def stage(self,payload):return self._call('stage')
    def ready_noecho(self):return self._call('ready')
    def sql_token(self):self._call('token');return 'x'*120
    def transmit_once(self,t):assert t=='x'*120;return self._call('transmit')
    def receive_terminal(self):self._call('receipt');return self.outcome
    def cleanup_verified_owned(self):return self._call('cleanup')

class IntegrationTests(unittest.TestCase):
    def test_happy_path(self):
        f=Fake();r=Controller(f,PIN).run(PAYLOAD)
        self.assertEqual(r.phase,'TERMINAL');self.assertEqual(r.cleanup,'PASS')
        self.assertTrue(r.consumed and r.sql_token_sent)
        self.assertEqual(f.calls,['verify','stage','ready','claim','token','transmit','receipt','cleanup'])
    def test_bad_sha_prevents_preflight(self):
        f=Fake();r=Controller(f,PIN).run(b'wrong')
        self.assertEqual(f.calls,['cleanup']);self.assertFalse(r.consumed)
    def test_preclaim_failures_no_token(self):
        for failure in ['verify','stage','ready','claim']:
            with self.subTest(failure=failure):
                f=Fake(fail=failure);r=Controller(f,PIN).run(PAYLOAD)
                self.assertNotIn('token',f.calls)
                self.assertEqual(r.phase,'HOLD')
                self.assertEqual(r.consumed,failure=='claim')
    def test_postclaim_failures_are_consumed(self):
        for failure in ['token','transmit','receipt']:
            with self.subTest(failure=failure):
                f=Fake(fail=failure);r=Controller(f,PIN).run(PAYLOAD)
                self.assertTrue(r.consumed)
                self.assertEqual(r.sql_token_sent,failure!='token')
                self.assertEqual(r.phase,'HOLD')
    def test_unknown_receipt_rejected(self):
        f=Fake(outcome='UAT_GO');r=Controller(f,PIN).run(PAYLOAD)
        self.assertEqual(r.phase,'HOLD')
    def test_approved_catalog_69_not_uat_go(self):
        f=Fake(outcome='CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED')
        r=Controller(f,PIN).run(PAYLOAD)
        self.assertEqual(r.receipt,'CATALOG_69_CANDIDATE_POSTCHECK_REQUIRED')
    def test_never_replay(self):
        f=Fake();c=Controller(f,PIN);c.run(PAYLOAD)
        with self.assertRaises(Hold):c.run(PAYLOAD)
        self.assertEqual(f.calls.count('claim'),1)
    def test_cleanup_failure_not_successful_cleanup(self):
        f=Fake(fail='cleanup');r=Controller(f,PIN).run(PAYLOAD)
        self.assertEqual(r.cleanup,'HOLD')
    def test_bad_pin(self):
        with self.assertRaises(Hold):Controller(Fake(),'not-a-sha')
    def test_main_locked(self):
        from owner_catalog_integration_REVIEW_ONLY import main
        self.assertEqual(main(),1)
if __name__=='__main__':unittest.main()
