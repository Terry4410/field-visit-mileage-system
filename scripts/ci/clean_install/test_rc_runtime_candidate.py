import hashlib
import unittest
from apply_rc_runtime_candidate import NEW_MAPPING, OLD_MAPPING, OverlayError, rewrite_source, rewrite_operational_guards
class RuntimeCandidateOverlayTests(unittest.TestCase):
    def test_exact_single_mapping_rewrites(self):
        source = b'prefix\\ne.ToTable("TeamLeaderAssignments");\\nsuffix\\n'
        result = rewrite_source(source, hashlib.sha256(source).hexdigest())
        self.assertEqual(result.count(NEW_MAPPING), 1)
        self.assertNotIn(OLD_MAPPING, result)
    def test_rejects_wrong_source_digest(self):
        source = b'e.ToTable("TeamLeaderAssignments");'
        with self.assertRaisesRegex(OverlayError, "SOURCE_HASH_MISMATCH"):
            rewrite_source(source, "0" * 64)
    def test_rejects_missing_or_duplicate_mapping(self):
        for source in (b'no mapping', OLD_MAPPING + b'\\n' + OLD_MAPPING):
            with self.subTest(source=source), self.assertRaisesRegex(
                    OverlayError, "MAPPING_CARDINALITY_MISMATCH"):
                rewrite_source(source, hashlib.sha256(source).hexdigest())
    def test_operational_published_only_and_legacy_edit_untouched(self):
        guard = b"using System;\\n    internal static async Task RequireUnreservedDraftAsync(\\n"
        repo = (b"before RequireUnreservedDraftAsync(db, id, ct);\\n"
                b"await V180OptionCCreateReservationGuard.RequireUnreservedDraftAsync(db, id, ct);\\n"
                b"        return await db.Locations.AsNoTracking()")
        updated_guard, updated_repo = rewrite_operational_guards(guard, repo)
        self.assertIn(b"ReservationStatus<>N'Published'", updated_guard)
        self.assertIn(b"pv.IsCurrent=1", updated_guard)
        self.assertIn(b"l.IsActive=1", updated_guard)
        self.assertIn(b"RequireUnreservedDraftAsync(db, id, ct);", updated_repo)
        self.assertIn(b"RequirePublishedOrUnreservedOperationalAsync(db, id, ct);", updated_repo)
    def test_operational_guard_fails_closed_without_exact_source(self):
        with self.assertRaises(OverlayError):
            rewrite_operational_guards(b"unrelated", b"unrelated")
        guard = b"    internal static async Task RequireUnreservedDraftAsync("
        with self.assertRaises(OverlayError):
            rewrite_operational_guards(guard, b"missing")
if __name__ == "__main__":
    unittest.main()
