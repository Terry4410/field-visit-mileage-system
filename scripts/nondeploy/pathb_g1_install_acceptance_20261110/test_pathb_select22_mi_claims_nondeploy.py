"""Pure offline MI claims cases, no tokens or network."""
from dataclasses import replace
import unittest

import pathb_select22_mi_preflight_nondeploy as p
import pathb_select22_mi_claims_nondeploy as m


class MiClaimsTests(unittest.TestCase):
    def setUp(self):
        self.scope = p.MiReviewInputs(
            p.RG, p.APP, p.SERVER, p.DATABASE, p.ACCOUNT, p.CONTAINER,
            p.CLAIM_KEY, p.QUERY_SHA, p.OWNER_SHA,
            "12345678-1234-1234-1234-123456789abc",
            "12345678-1234-1234-1234-123456789abd",
            p.STORAGE_AUD, p.SQL_AUD)
        self.now = 1800000000
        self.claims = m.UntrustedClaims(
            self.scope.mi_principal_id, self.scope.mi_tenant_id,
            p.STORAGE_AUD, self.now-2, self.now+3600)

    def inspect(self, scope=None, claims=None, audience="storage"):
        return m.offline_claims_review(
            self.scope if scope is None else scope,
            self.claims if claims is None else claims,
            audience=audience, now=self.now)

    def test_valid_synthetic_shape_still_no_auth_or_live(self):
        r = self.inspect()
        self.assertEqual(r["untrusted_claims_shape"], "PASS")
        self.assertEqual(r["jwt_signature"], "NOT_VERIFIED")
        self.assertEqual(r["azure_mi_principal_binding"], "NOT_PROVEN")
        self.assertEqual(r["owner_execution_grant"], "NOT_AUTHORIZED")
        self.assertEqual(r["live_release"], "HOLD")

    def test_storage_and_sql_audiences_must_be_separate(self):
        with self.assertRaises(m.MiClaimsHold):
            self.inspect(audience="sql")
        r = self.inspect(claims=replace(self.claims, aud=p.SQL_AUD), audience="sql")
        self.assertEqual(r["live_release"], "HOLD")

    def test_wrong_mi_oid_or_tenant_rejected(self):
        for key in ("oid", "tid"):
            with self.subTest(key=key):
                with self.assertRaises(m.MiClaimsHold):
                    self.inspect(claims=replace(self.claims, **{key: "00000000-0000-0000-0000-000000000000"}))

    def test_expired_future_or_excessive_token_lifetime_rejected(self):
        values = (replace(self.claims, exp=self.now+299),
                  replace(self.claims, nbf=self.now+31),
                  replace(self.claims, nbf=self.now-90000))
        for c in values:
            with self.subTest(c=c):
                with self.assertRaises(m.MiClaimsHold):
                    self.inspect(claims=c)

    def test_bad_types_or_scope_rejected(self):
        for c in (replace(self.claims, exp=True), replace(self.claims, oid=None)):
            with self.assertRaises(m.MiClaimsHold):
                self.inspect(claims=c)
        with self.assertRaises(m.MiClaimsHold):
            self.inspect(scope=replace(self.scope, sql_sha256="0"*64))

    def test_caller_flags_cannot_release(self):
        with self.assertRaisesRegex(m.MiClaimsHold, "^MI_LIVE_AUTHENTICATION_NOT_IMPLEMENTED$"):
            m.release_live(self.claims, verified=True, owner_granted=True,
                           blob_rbac=True, sql_select=True)


if __name__ == "__main__":
    unittest.main()
