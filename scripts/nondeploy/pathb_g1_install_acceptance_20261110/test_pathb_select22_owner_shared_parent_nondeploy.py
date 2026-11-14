"""Exact Owner shared-stage integration plan, wholly offline."""
from dataclasses import replace
import unittest
import pathb_select22_owner_shared_parent_nondeploy as p


class SharedParentTests(unittest.TestCase):
    def test_plan_binds_existing_stage_and_original_claim(self):
        plan = p.plan()
        self.assertEqual(plan.shared_root, "/home/fv-select22-transfer-20261114")
        self.assertEqual(plan.sql_sha, p.SQL_SHA)
        self.assertEqual(plan.blob_key, p.KEY)
        self.assertEqual(plan.stage_sha, p.STAGE_SHA)
        review = p.review_frozen(plan)
        self.assertEqual(review["static_bindings"], "PASS")
        self.assertEqual(review["live_parent"], "HOLD")
        self.assertEqual(review["trusted_secret_stdin_transport"], "NOT_IMPLEMENTED")

    def test_every_frozen_binding_rejects_drift(self):
        p0 = p.plan()
        for field in p0.__dataclass_fields__:
            with self.subTest(field=field):
                change = ("unexpected",) if field == "target" else "unexpected"
                with self.assertRaisesRegex(p.SharedParentHold, "OWNER_SHARED_PARENT_SCOPE_HOLD"):
                    p.review_frozen(replace(p0, **{field: change}))

    def test_exact_claim_readback_required_before_sql_token(self):
        seq = p.STEPS
        self.assertLess(seq.index("RECEIVER_READY_SHARED_STAGE_SHA"),
                        seq.index("BLOB_CONDITIONAL_PUT_ONCE"))
        self.assertLess(seq.index("BLOB_CONDITIONAL_PUT_ONCE"),
                        seq.index("BLOB_ETAG_AND_EXACT_BODY_GET_ONCE"))
        self.assertLess(seq.index("BLOB_ETAG_AND_EXACT_BODY_GET_ONCE"),
                        seq.index("SQL_TOKEN_AFTER_BLOB_READBACK_ONCE"))
        self.assertLess(seq.index("SQL_TOKEN_AFTER_BLOB_READBACK_ONCE"),
                        seq.index("SELECT22_FIXED_SQL_ONCE"))

    def test_forged_pass_or_token_callbacks_never_release(self):
        with self.assertRaisesRegex(p.SharedParentHold,
                                    "OWNER_SHARED_LIVE_PARENT_NOT_RELEASED"):
            p.release_live(p.plan(), owner=True, blob=True,
                           token_provider=lambda: "FAKE",
                           sql_provider=lambda: "FAKE")

    def test_no_live_implementation_or_retry_field(self):
        review = p.review_frozen(p.plan())
        self.assertEqual(review["owner_new_one_shot_approval"], "NOT_GRANTED")
        self.assertEqual(review["reupload_or_restage"], "FORBIDDEN")
        self.assertNotIn("RETRY", " ".join(p.STEPS))


if __name__ == "__main__":
    unittest.main()
