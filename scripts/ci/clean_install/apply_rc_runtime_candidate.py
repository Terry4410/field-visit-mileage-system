#!/usr/bin/env python3
"""Apply one source-pinned disposable SQL Server trigger compatibility overlay."""
from __future__ import annotations
import argparse
import hashlib
import json
import subprocess
from pathlib import Path
EXPECTED_RC_SHA = "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a"
SOURCE_PATH = Path("backend/src/FieldVisit.Infrastructure/AppDbContext.cs")
EXPECTED_SOURCE_SHA256 = "dcbc2fc34c091310d49005c5280a9fb07de32312056b1757a5b88b746c6e1377"
EXPECTED_CANDIDATE_SHA256 = "40d7ce4079e634b2d048eb9badc209518a622dfe7bbb7a5f1c80d325e580d77f"
OLD_MAPPING = b'e.ToTable("TeamLeaderAssignments");'
NEW_MAPPING = b'e.ToTable("TeamLeaderAssignments", table => table.UseSqlOutputClause(false));'
OVERLAY_ID = "team-leader-assignment-trigger-output-clause-disabled-v1"
class OverlayError(ValueError):
    pass
def rewrite_source(source: bytes, expected_sha256: str) -> bytes:
    if hashlib.sha256(source).hexdigest() != expected_sha256:
        raise OverlayError("SOURCE_HASH_MISMATCH")
    if source.count(OLD_MAPPING) != 1:
        raise OverlayError("MAPPING_CARDINALITY_MISMATCH")
    candidate = source.replace(OLD_MAPPING, NEW_MAPPING, 1)
    if candidate == source:
        raise OverlayError("MAPPING_NO_CHANGE")
    return candidate
def main() -> int:
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--rc-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        root = args.rc_root.resolve()
        actual_rc = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"],
                                            stderr=subprocess.DEVNULL, text=True).strip()
        if actual_rc != EXPECTED_RC_SHA:
            raise OverlayError("RC_SOURCE_MISMATCH")
        context = root / SOURCE_PATH
        original = context.read_bytes()
        if hashlib.sha256(original).hexdigest() != EXPECTED_SOURCE_SHA256:
            raise OverlayError("SOURCE_HASH_MISMATCH")
        candidate = rewrite_source(original, EXPECTED_SOURCE_SHA256)
        candidate_sha = hashlib.sha256(candidate).hexdigest()
        if candidate_sha != EXPECTED_CANDIDATE_SHA256:
            raise OverlayError("CANDIDATE_HASH_MISMATCH")
        args.output.mkdir(parents=True, exist_ok=True)
        temp = context.with_name(context.name + ".candidate.tmp")
        temp.write_bytes(candidate)
        temp.replace(context)
        manifest = {
            "format": "field-visit-clean-install-rc-runtime-overlay-v1",
            "rcSourceSha": EXPECTED_RC_SHA, "overlayId": OVERLAY_ID,
            "sourcePath": SOURCE_PATH.as_posix(),
            "acceptedSourceSha256": EXPECTED_SOURCE_SHA256,
            "candidateSourceSha256": EXPECTED_CANDIDATE_SHA256,
            "overlayScriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "changes": ["TeamLeaderAssignments EF mapping uses UseSqlOutputClause(false)"],
            "deploymentStatus": "NONDEPLOY_DISPOSABLE_CANDIDATE_ONLY"
        }
        (args.output / "runtime-candidate-overlay.json").write_text(
            json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print("CLEAN_INSTALL=RC_RUNTIME_TEAM_LEADER_TRIGGER_OUTPUT_COMPAT_CANDIDATE_PASS")
        return 0
    except OverlayError as exc:
        print("CLEAN_INSTALL=RC_RUNTIME_OVERLAY_" + str(exc))
        return 1
    except Exception:
        print("CLEAN_INSTALL=RC_RUNTIME_OVERLAY_UNEXPECTED")
        return 1
if __name__ == "__main__":
    raise SystemExit(main())
