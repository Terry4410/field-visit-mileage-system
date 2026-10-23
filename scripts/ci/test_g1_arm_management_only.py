import datetime as dt
import os
import sys
import unittest
from unittest.mock import patch, MagicMock

sys.path.insert(0, os.path.dirname(__file__))
import g1_arm_management_only as g

SUB = "00000000-1111-2222-3333-444444444444"
NOW = dt.datetime(2026, 10, 23, 1, 0, tzinfo=dt.timezone.utc)


def good_pages():
    urls = g.planned_paths(SUB)
    d = {k: {"id": v.split("?", 1)[0]} for k, v in urls.items() if k != "deployments"}
    d["api"]["properties"] = {"state": "Running", "siteConfig": {"connectionStrings": "DO_NOT_PRINT"}}
    d["database"]["properties"] = {"status": "Online", "earliestRestoreDate": "2026-10-22T04:00:00Z"}
    d["retention"]["properties"] = {"retentionDays": 7}
    d["deployments"] = {"value": [{"properties": {"author": "DO_NOT_PRINT"}}]}
    return d


class Review(unittest.TestCase):
    def test_six_known_gets(self):
        urls = g.planned_paths(SUB)
        self.assertEqual(len(urls), 6)
        self.assertTrue(all(v.startswith("/subscriptions/" + SUB) for v in urls.values()))
        self.assertTrue(all(v.endswith(("2021-04-01", "2024-04-01", "2025-01-01", "2025-05-01")) for v in urls.values()))

    def test_reject_wrong_subscription(self):
        with self.assertRaises(g.Stop):
            g.planned_paths("bad")

    def test_good_readonly_evidence_still_release_hold(self):
        d = g.classify(good_pages(), SUB, NOW)
        self.assertEqual(d["G1A"], "OBSERVED_MANAGEMENT_ONLY")
        self.assertEqual(d["PITR"], "CONFIRMED_METADATA_ONLY")
        self.assertEqual(d["SqlPrincipal"], "UNKNOWN")
        self.assertEqual(d["Recovery"], "HOLD")
        self.assertEqual(d["BusinessUAT"], "NO_GO")
        self.assertFalse("DO_NOT_PRINT" in str(d))

    def test_target_drift_rejected(self):
        d = good_pages()
        d["server"]["id"] += "/other"
        with self.assertRaises(g.Stop):
            g.classify(d, SUB, NOW)

    def test_pagination_refused(self):
        d = good_pages()
        d["deployments"]["nextLink"] = "https://evil.test"
        with self.assertRaises(g.Stop):
            g.classify(d, SUB, NOW)

    def test_unknown_retention_is_hold_not_pass(self):
        d = good_pages()
        d["retention"]["properties"]["retentionDays"] = None
        self.assertEqual(g.classify(d, SUB, NOW)["PITR"], "HOLD")

    def test_bad_retention_type_rejected_for_pass(self):
        d = good_pages()
        d["retention"]["properties"]["retentionDays"] = True
        self.assertEqual(g.classify(d, SUB, NOW)["PITR"], "HOLD")

    def test_future_restore_time_hold(self):
        d = good_pages()
        d["database"]["properties"]["earliestRestoreDate"] = "2026-11-01T00:00:00Z"
        self.assertEqual(g.classify(d, SUB, NOW)["PITR"], "HOLD")

    def test_missing_deployment_list_rejected(self):
        d = good_pages()
        d["deployments"] = {}
        with self.assertRaises(g.Stop):
            g.classify(d, SUB, NOW)

    def test_unallowlisted_url_never_sent(self):
        r = g.FixedArmReader(SUB, "dummy")
        with self.assertRaises(g.Stop):
            r.get("/subscriptions/other/resourceGroups/other?api-version=2021-04-01")
        self.assertEqual(r.calls, 0)

    def test_request_budget_blocks_before_http(self):
        r = g.FixedArmReader(SUB, "dummy")
        r.calls = g.MAX_GETS
        with self.assertRaises(g.Stop):
            r.get(next(iter(r.paths.values())))

    def test_deadline_blocks_before_http(self):
        r = g.FixedArmReader(SUB, "dummy")
        r.started -= g.MAX_SECONDS + 1
        with self.assertRaises(g.Stop):
            r.get(next(iter(r.paths.values())))

    def test_runner_context_rejected_without_live_access(self):
        with patch.dict(os.environ, {"GITHUB_ACTIONS": "false"}, clear=True):
            with self.assertRaises(g.Stop):
                g.private_azure_context()

    def test_no_interactive_or_unsafe_network_operations(self):
        from pathlib import Path
        source = Path(g.__file__).read_text(encoding="utf-8")
        self.assertNotIn("Invoke-Sqlcmd", source)
        self.assertNotIn("docker run", source)
        self.assertNotIn("subprocess.Popen", source)
        self.assertNotIn('method="POST"', source)
        self.assertNotIn('method="PUT"', source)
        self.assertNotIn('method="DELETE"', source)
        self.assertNotIn('print(pages', source)
        self.assertNotIn('print(token', source)

    def test_no_redirect_auto_follow(self):
        with self.assertRaises(g.Stop):
            g.NoRedirect().redirect_request(None,None,None,None,None,None)

if __name__ == "__main__":
    unittest.main()
