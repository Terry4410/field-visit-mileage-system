"""Offline negative cases only; does not connect to Azure or SQL."""
import importlib.util
import pathlib
import unittest

p = pathlib.Path(__file__).with_name("g1_bridge_guard.py")
spec = importlib.util.spec_from_file_location("bridge", p)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)

class Guards(unittest.TestCase):
    def test_diagnostic_is_not_api(self):
        self.assertEqual(bridge.classify("DIAGNOSTIC_OIDC"),
                         "DIAGNOSTIC_ONLY_API_PRINCIPAL_UNKNOWN")

    def test_api_principal_substitution_denied(self):
        with self.assertRaises(bridge.Stop):
            bridge.classify("DIAGNOSTIC_OIDC", claims_api_principal=True)

    def test_unreviewed_identity_denied(self):
        with self.assertRaises(bridge.Stop):
            bridge.classify("API_DEFAULT_CONNECTION")

    def test_live_access_denied(self):
        for overrides in ({"azure_access": True}, {"sql_open": True}, {"authorization": True}):
            with self.subTest(overrides=overrides):
                with self.assertRaises(bridge.Stop):
                    bridge.decide_live(**overrides)

    def test_pinned_source(self):
        sha = "a" * 40
        self.assertEqual(bridge.verify_source(bridge.REPO, bridge.BRANCH, sha,
                         bridge.BASE, ["scripts/ci/g1_bridge_guard.py"]), "SOURCE_PINNED")

    def test_source_mismatch_denied(self):
        cases = [
            ("wrong", bridge.BRANCH, "a"*40, bridge.BASE, list(bridge.ALLOWED)),
            (bridge.REPO, "refs/heads/post-uat/v1.8.0", "a"*40, bridge.BASE, list(bridge.ALLOWED)),
            (bridge.REPO, bridge.BRANCH, "bad", bridge.BASE, list(bridge.ALLOWED)),
            (bridge.REPO, bridge.BRANCH, "a"*40, "b"*40, list(bridge.ALLOWED)),
            (bridge.REPO, bridge.BRANCH, "a"*40, bridge.BASE, ["database/migrations/x.sql"]),
            (bridge.REPO, bridge.BRANCH, "a"*40, bridge.BASE, []),
        ]
        for case in cases:
            with self.subTest(case=case[:4]):
                with self.assertRaises(bridge.Stop):
                    bridge.verify_source(*case)

if __name__ == "__main__":
    unittest.main()
