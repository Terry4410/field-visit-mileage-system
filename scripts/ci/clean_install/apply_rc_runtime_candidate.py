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
OVERLAY_ID = "team-leader-trigger-and-published-location-operational-guard-v2"
GUARD_PATH = Path("backend/src/FieldVisit.Infrastructure/V180OptionCCreateReservationGuard.cs")
REPOSITORY_PATH = Path("backend/src/FieldVisit.Infrastructure/Repositories.cs")
GUARD_INSERT_MARKER = b"    internal static async Task RequireUnreservedDraftAsync("
REPOSITORY_OLD_CALL = (
    b"await V180OptionCCreateReservationGuard.RequireUnreservedDraftAsync(db, id, ct);"
    b"\n        return await db.Locations.AsNoTracking()"
)
REPOSITORY_NEW_CALL = (
    b"await V180OptionCCreateReservationGuard.RequirePublishedOrUnreservedOperationalAsync(db, id, ct);"
    b"\n        return await db.Locations.AsNoTracking()"
)
OPERATIONAL_GUARD = '''    /// <summary>
    /// Canonically Published reservations are usable as Trip stops only after
    /// the independent review, authoritative Location state AND current
    /// publication ledger all agree. Legacy maintenance keeps the stricter
    /// RequireUnreservedDraftAsync guard; this never permits editing.
    /// </summary>
    internal static async Task RequirePublishedOrUnreservedOperationalAsync(
        AppDbContext db, int locationId, CancellationToken ct)
    {
        if (locationId <= 0)
            throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_LOCATION_INVALID");
        if (!await HasValidatedSchemaAsync(db, ct)) return;
        var sql = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_RESERVATION_SQLSERVER_REQUIRED");
        await using var check = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.LocationCreateReservations cr WITH(HOLDLOCK)
                WHERE cr.LocationId=@location
                  AND (cr.ReservationStatus<>N'Published'
                    OR NOT EXISTS (
                        SELECT 1 FROM dbo.Locations l WITH(HOLDLOCK)
                        WHERE l.LocationId=cr.LocationId
                          AND l.OrganizationId=cr.OrganizationId
                          AND l.TeamId=cr.TeamId
                          AND l.LocationType=N'Customer'
                          AND l.IsActive=1
                          AND l.ApprovalStatus=N'Approved')
                    OR NOT EXISTS (
                        SELECT 1 FROM dbo.LocationPublishedVersions pv WITH(HOLDLOCK)
                        WHERE pv.LocationId=cr.LocationId
                          AND pv.ChangeRequestId=cr.ChangeRequestId
                          AND pv.IsCurrent=1))
            ) THEN 1 ELSE 0 END
            """, sql);
        check.Parameters.AddWithValue("@location", locationId);
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) != 0)
            throw new InvalidOperationException(
                "LOCATION_CANONICAL_CREATE_RESERVATION_REQUIRES_REVIEW");
    }

'''.encode("utf-8")
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
def rewrite_operational_guards(guard_source: bytes, repository_source: bytes) -> tuple[bytes, bytes]:
    # Immutable accepted RC source is already git-SHA pinned. Reject any
    # changed, absent or duplicated method/call; don't relax legacy edits.
    if guard_source.count(GUARD_INSERT_MARKER) != 1:
        raise OverlayError("GUARD_METHOD_CARDINALITY")
    if b"RequirePublishedOrUnreservedOperationalAsync" in guard_source:
        raise OverlayError("GUARD_ALREADY_PATCHED")
    if repository_source.count(REPOSITORY_OLD_CALL) != 1:
        raise OverlayError("REPOSITORY_OPERATIONAL_CALL_CARDINALITY")
    revised_guard = guard_source.replace(
        GUARD_INSERT_MARKER, OPERATIONAL_GUARD + GUARD_INSERT_MARKER, 1)
    revised_repository = repository_source.replace(
        REPOSITORY_OLD_CALL, REPOSITORY_NEW_CALL, 1)
    # The legacy tracking/edit path must stay strictly unreserved-only.
    if revised_repository.count(
        b"RequireUnreservedDraftAsync(db, id, ct)") != 1:
        raise OverlayError("LEGACY_EDIT_GUARD_CHANGED")
    return revised_guard, revised_repository
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
        guard_path = root / GUARD_PATH
        repo_path = root / REPOSITORY_PATH
        guard_original = guard_path.read_bytes()
        repo_original = repo_path.read_bytes()
        guard_candidate, repo_candidate = rewrite_operational_guards(
            guard_original, repo_original)
        args.output.mkdir(parents=True, exist_ok=True)
        for path, data in ((context, candidate),
                           (guard_path, guard_candidate),
                           (repo_path, repo_candidate)):
            temp = path.with_name(path.name + ".candidate.tmp")
            temp.write_bytes(data)
            temp.replace(path)
        manifest = {
            "format": "field-visit-clean-install-rc-runtime-overlay-v1",
            "rcSourceSha": EXPECTED_RC_SHA, "overlayId": OVERLAY_ID,
            "sourcePath": SOURCE_PATH.as_posix(),
            "acceptedSourceSha256": EXPECTED_SOURCE_SHA256,
            "candidateSourceSha256": EXPECTED_CANDIDATE_SHA256,
            "overlayScriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "changes": [
                "TeamLeaderAssignments EF mapping uses UseSqlOutputClause(false)",
                "Canonical Published Location can be read by operational Trip only when Location, reservation and current publication ledger agree; legacy maintenance remains denied"
            ],
            "operationalGuard": {
                "guardPath": GUARD_PATH.as_posix(),
                "acceptedGuardSha256": hashlib.sha256(guard_original).hexdigest(),
                "candidateGuardSha256": hashlib.sha256(guard_candidate).hexdigest(),
                "repositoryPath": REPOSITORY_PATH.as_posix(),
                "acceptedRepositorySha256": hashlib.sha256(repo_original).hexdigest(),
                "candidateRepositorySha256": hashlib.sha256(repo_candidate).hexdigest(),
            },
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
