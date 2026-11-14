"""Exact management command contract tests: fully mocked Azure CLI."""
import unittest
from unittest import mock
import pathb_select22_owner_management_fetch_nondeploy as owner


class ManagementCollectorTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.records = [
            {"id": "fake-subscription"},
            {"id": "fake-owner"},
            {"name": owner.APP},
            [{"name": "instance01234567"}],
            {"name": owner.SQL_RESOURCE},
        ]

    def fake(self, argv):
        self.calls.append(tuple(argv))
        return self.records[len(self.calls) - 1]

    def test_five_exact_reads_and_no_token_or_sql(self):
        with mock.patch.object(owner, "verify_snapshot",
                               return_value="OFFLINE_VERIFIED") as verify:
            self.assertEqual(owner.collect_exact_management(reader=self.fake),
                             "OFFLINE_VERIFIED")
            verify.assert_called_once()
        self.assertEqual(self.calls, list(owner.COMMANDS))
        self.assertEqual(len(self.calls), 5)
        self.assertTrue(all("get-access-token" not in item and "query" not in item
                            for command in self.calls for item in command))

    def test_failure_is_redacted_and_terminal(self):
        def fail(argv):
            self.calls.append(tuple(argv))
            raise RuntimeError("sensitive fake error")
        with self.assertRaisesRegex(
                owner.OwnerFetchHold, "^SELECT22_MANAGEMENT_FETCH_HOLD$"):
            owner.collect_exact_management(reader=fail)
        self.assertEqual(self.calls, [owner.OWNER_ACCOUNT])

    def test_bad_snapshot_holds_without_retry(self):
        with mock.patch.object(owner, "verify_snapshot",
                               side_effect=ValueError("fake sensitive")):
            with self.assertRaisesRegex(
                    owner.OwnerFetchHold, "^SELECT22_MANAGEMENT_FETCH_HOLD$"):
                owner.collect_exact_management(reader=self.fake)
        self.assertEqual(self.calls, list(owner.COMMANDS))

    def test_default_reader_does_not_invoke_azure(self):
        with self.assertRaisesRegex(
                owner.OwnerFetchHold, "^SELECT22_MANAGEMENT_FETCH_HOLD$"):
            owner.collect_exact_management()
        self.assertEqual(self.calls, [])

    def test_noncallable_reader_fails_before_any_action(self):
        with self.assertRaises(owner.OwnerFetchHold):
            owner.collect_exact_management(reader=None)
        self.assertEqual(self.calls, [])


class GraphOwnerCandidateTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.records = [
            {"id": "fake-subscription"}, {"id": "fake-owner"},
            {"name": owner.APP}, [{"name": "instance01234567"}],
            {"name": owner.SQL_RESOURCE},
        ]

    def fake(self, argv):
        self.calls.append(tuple(argv))
        return self.records[len(self.calls) - 1]

    def test_graph_me_exact_five_reads_and_no_legacy_replay(self):
        with mock.patch.object(owner, "verify_snapshot",
                               return_value="OFFLINE_ONLY") as verify:
            self.assertEqual(
                owner.collect_graph_me_management_once(reader=self.fake),
                "OFFLINE_ONLY")
            verify.assert_called_once()
        self.assertEqual(self.calls, list(owner.GRAPH_COMMANDS))
        self.assertNotIn(owner.OWNER_USER, self.calls)
        self.assertEqual(self.calls.count(owner.GRAPH_ME), 1)
        self.assertTrue(all("get-access-token" not in part
                            for command in self.calls for part in command))

    def test_graph_failure_stops_without_fallback_or_retry(self):
        def fake(argv):
            self.calls.append(tuple(argv))
            if tuple(argv) == owner.GRAPH_ME:
                raise RuntimeError("fake sensitive Graph access error")
            return self.records[len(self.calls) - 1]
        with self.assertRaisesRegex(
                owner.OwnerFetchHold, "^SELECT22_GRAPH_OWNER_HOLD$"):
            owner.collect_graph_me_management_once(reader=fake)
        self.assertEqual(self.calls, [owner.OWNER_ACCOUNT, owner.GRAPH_ME])

    def test_graph_invalid_owner_evidence_rejected(self):
        with mock.patch.object(owner, "verify_snapshot",
                               side_effect=ValueError("fake untrusted OID")):
            with self.assertRaisesRegex(
                    owner.OwnerFetchHold, "^SELECT22_GRAPH_OWNER_HOLD$"):
                owner.collect_graph_me_management_once(reader=self.fake)
        self.assertEqual(self.calls, list(owner.GRAPH_COMMANDS))

    def test_graph_default_reader_does_not_invoke_azure(self):
        with self.assertRaisesRegex(
                owner.OwnerFetchHold, "^SELECT22_GRAPH_OWNER_HOLD$"):
            owner.collect_graph_me_management_once()
        self.assertEqual(self.calls, [])

    def test_graph_noncallable_fails_before_any_reads(self):
        with self.assertRaisesRegex(
                owner.OwnerFetchHold, "^SELECT22_GRAPH_OWNER_HOLD$"):
            owner.collect_graph_me_management_once(reader=None)
        self.assertEqual(self.calls, [])


if __name__ == "__main__":
    unittest.main()
