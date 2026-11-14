"""Synthetic MI scope contract: no Azure, network, Blob, Token or SQL."""
from dataclasses import replace
import unittest
import pathb_select22_mi_preflight_nondeploy as mi


class MiReviewTests(unittest.TestCase):
    def setUp(self):
        self.scope = mi.MiReviewInputs(
            mi.RG, mi.APP, mi.SERVER, mi.DATABASE, mi.ACCOUNT, mi.CONTAINER,
            mi.CLAIM_KEY, mi.QUERY_SHA, mi.OWNER_SHA,
            "12345678-1234-1234-1234-123456789abc",
            "12345678-1234-1234-1234-123456789abd",
            mi.STORAGE_AUD, mi.SQL_AUD)

    def test_static_review_never_claims_live_permissions(self):
        result = mi.offline_review(self.scope)
        self.assertEqual(result["static_scope"], "PASS")
        self.assertEqual(result["principal_kind"], "APP_MI_NOT_OWNER")
        self.assertEqual(result["live_release"], "HOLD")
        self.assertEqual(result["mi_authenticated"], "NOT_PROVEN")
        self.assertEqual(result["storage_data_plane_grant"], "NOT_PROVEN")
        self.assertEqual(result["sql_select_grant"], "NOT_PROVEN")

    def test_target_query_and_claim_cannot_drift(self):
        edits = {"resource_group": "other", "app": "other",
                 "server": "other", "database": "other",
                 "storage_account": "other", "storage_container": "other",
                 "claim_key": "other", "sql_sha256": "f"*64,
                 "owner_oid_sha256": "f"*64,
                 "storage_audience": "other", "sql_audience": "other"}
        for key, value in edits.items():
            with self.subTest(key=key):
                with self.assertRaisesRegex(mi.MiPreflightHold, "^MI_STATIC_SCOPE_HOLD$"):
                    mi.offline_review(replace(self.scope, **{key: value}))

    def test_unknown_principal_rejected(self):
        for value in ("", "not-an-object-id", None):
            with self.subTest(value=value):
                with self.assertRaisesRegex(mi.MiPreflightHold, "^MI_PRINCIPAL_SHAPE_HOLD$"):
                    mi.offline_review(replace(self.scope, mi_principal_id=value))

    def test_fabricated_authorization_flags_cannot_release(self):
        with self.assertRaisesRegex(mi.MiPreflightHold, "^MI_LIVE_PARENT_NOT_RELEASED$"):
            mi.release_live(self.scope, owner_approved=True,
                            mi_rbac_ok=True, sql_grant_ok=True, claim_ok=True)

    def test_blob_first_order_preserved(self):
        self.assertLess(mi.REQUIRED_ORDER.index("BLOB_READBACK"),
                        mi.REQUIRED_ORDER.index("SQL_TOKEN_ONCE"))
        self.assertLess(mi.REQUIRED_ORDER.index("SQL_TOKEN_ONCE"),
                        mi.REQUIRED_ORDER.index("SELECT22_ONCE"))


if __name__ == "__main__":
    unittest.main()
