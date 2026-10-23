import hashlib
import unittest
from apply_rc_runtime_candidate import NEW_MAPPING, OLD_MAPPING, OverlayError, rewrite_source
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
if __name__ == "__main__":
    unittest.main()
