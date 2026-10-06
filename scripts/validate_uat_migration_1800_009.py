#!/usr/bin/env python3
"""Fail-closed static validation for Owner Pre-UAT migration 1800_009.

This validator does not execute SQL. It protects the immutable one-stop DB
forward-fix package while allowing later v1.8.0 forward-fix schema metadata.
"""

from __future__ import annotations

import hashlib
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UP = ROOT / "database/migrations/1800_009_one_stop_route_attempt_constraint/Up.sql"
VERIFY = ROOT / "database/migrations/1800_009_one_stop_route_attempt_constraint/Verify.sql"
README = ROOT / "database/migrations/1800_009_one_stop_route_attempt_constraint/README.md"
APPSETTINGS = ROOT / "backend/src/FieldVisit.Api/appsettings.json"
ORCHESTRATION = ROOT / "backend/src/FieldVisit.Application/V180GoogleMileageOrchestrationService.cs"
WORKFLOW = ROOT / ".github/workflows/azure-sql-uat-migration-1800-009.yml"
GRANT = ROOT / "database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_009.sql"
PERMISSION_VERIFY = ROOT / "database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_009.sql"
REVOKE = ROOT / "database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_009.sql"
HISTORY = ROOT / "database/migrations/security/uat/Verify-v180-historical-fingerprints.sql"

EXPECTED_UP_BLOB = "bc8a24af9aec289e2364a12cec3eb31c07c1f334"
EXPECTED_VERIFY_BLOB = "c83ecafdc6310c40d401479857812d31fa696b22"
EXPECTED_UP_SHA256 = "2a43a20be088887c082721b8facaf153a655b488fc2c6566c1e4d5ccad17f625"
EXPECTED_VERIFY_SHA256 = "68c8d1b2595bbc4724c02b8bedc20d605498715eaf3ddcecb500f399f036df38"
EXPECTED_HISTORY_SHA256 = "1d7f77dc200590c2ba3b989ed340442d835f8e02be9edbb57af68ae5e4e9cba0"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"BLOCKED: {message}")


def blob_sha(path: Path) -> str:
    return subprocess.run(
        ["git", "hash-object", str(path)],
        cwd=ROOT, check=True, text=True, capture_output=True
    ).stdout.strip()


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


for path in (UP, VERIFY, README, APPSETTINGS, ORCHESTRATION, WORKFLOW, GRANT, PERMISSION_VERIFY, REVOKE, HISTORY):
    require(path.is_file(), f"missing required file: {path.relative_to(ROOT)}")

require(blob_sha(UP) == EXPECTED_UP_BLOB, "1800_009 Up.sql blob changed")
require(blob_sha(VERIFY) == EXPECTED_VERIFY_BLOB, "1800_009 Verify.sql blob changed")
require(sha256(UP) == EXPECTED_UP_SHA256, "1800_009 Up.sql SHA-256 changed")
require(sha256(VERIFY) == EXPECTED_VERIFY_SHA256, "1800_009 Verify.sql SHA-256 changed")
require(sha256(HISTORY) == EXPECTED_HISTORY_SHA256, "historical fingerprint verifier changed")

up = UP.read_text(encoding="utf-8")
verify = VERIFY.read_text(encoding="utf-8")
readme = README.read_text(encoding="utf-8")
appsettings = APPSETTINGS.read_text(encoding="utf-8")
orchestration = ORCHESTRATION.read_text(encoding="utf-8")
workflow = WORKFLOW.read_text(encoding="utf-8")
grant = GRANT.read_text(encoding="utf-8")
permission_verify = PERMISSION_VERIFY.read_text(encoding="utf-8")
revoke = REVOKE.read_text(encoding="utf-8")

require(re.search(r"(?mi)^\s*GO\s*$", up) is None, "1800_009 must remain one outer batch; GO is forbidden")
for marker, expected in (
    ("BEGIN TRANSACTION;", 1),
    ("COMMIT TRANSACTION;", 1),
    ("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", 1),
):
    require(up.count(marker) == expected, f"1800_009 transaction invariant failed: {marker}")

for marker in (
    "FieldVisit.SchemaMigration",
    "VersionNumber = N'1.8.0-008'",
    "VersionNumber = N'1.8.0-009'",
    "CK_RouteCalculationAttempts_StopCount",
    "stopcount>=2",
    "DROP CONSTRAINT CK_RouteCalculationAttempts_StopCount",
    "CHECK (StopCount >= 1)",
    "StopCount < 1",
):
    require(marker in up, f"1800_009 Up.sql safety marker missing: {marker}")

compact_up = re.sub(r"\s+", "", up.lower())
for forbidden in (
    "updatedbo.routecalculationattempts",
    "deletefromdbo.routecalculationattempts",
    "insertdbo.routecalculationattempts",
):
    require(forbidden not in compact_up, f"1800_009 data mutation forbidden: {forbidden}")

compact_verify = re.sub(r"\s+", "", verify.lower())
for marker in (
    "VersionNumber = N'1.8.0-009'",
    "CK_RouteCalculationAttempts_StopCount",
    "stopcount>=1",
    "StopCount < 1",
    "is_disabled = 1 OR is_not_trusted = 1",
):
    require(re.sub(r"\s+", "", marker.lower()) in compact_verify, f"1800_009 Verify marker missing: {marker}")

require("if (basis.Stops.Count < 1)" in orchestration, "application zero-stop prohibition changed or missing")
schema_match = re.search(r'"DbSchemaVersion"\s*:\s*"1\.8\.0-(\d{3})"', appsettings)
require(schema_match is not None and int(schema_match.group(1)) >= 9,
        "runtime DbSchemaVersion must not regress below 1.8.0-009")

for marker in (
    "workflow_dispatch:",
    "confirm_migration:",
    "confirm_runtime_gate:",
    "WRITE_QUIESCENCE_AND_RECOVERY_READY",
    "refs/heads/post-uat/v1.8.0",
    "environment: uat-migration",
    "APPROVED_MIGRATION_COMMIT_SHA",
    f"EXPECTED_UP_SHA256: {EXPECTED_UP_SHA256}",
    f"EXPECTED_VERIFY_SHA256: {EXPECTED_VERIFY_SHA256}",
    f"EXPECTED_HISTORY_SHA256: {EXPECTED_HISTORY_SHA256}",
    "Apply only 1800_009 Up.sql",
    "Run exact 1800_009 Verify.sql",
    "STOP_FOR_REVIEW",
):
    require(marker in workflow, f"1800_009 workflow safety marker missing: {marker}")
require(workflow.count("-InputFile $up") == 1, "1800_009 workflow must invoke Up.sql exactly once")
require(workflow.count("-InputFile $verify") == 1, "1800_009 workflow must invoke Verify.sql exactly once")
require(workflow.count("-InputFile $history") == 1, "1800_009 workflow must invoke historical verifier exactly once")

for marker in (
    "ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]",
    "GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]",
    "GRANT_PREPARED_FOR_1800_009",
):
    require(marker in grant, f"1800_009 grant marker missing: {marker}")
require("1800_009_PERMISSION_GATE" in permission_verify, "1800_009 permission gate marker missing")
for marker in (
    "REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate]",
    "ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]",
    "REVOKED_1800_009_ELEVATION",
):
    require(marker in revoke, f"1800_009 revoke marker missing: {marker}")

require("PREPARED ONLY / NOT EXECUTED" in readme, "1800_009 README must retain prepared-only execution state")

print("PASS 1800_009 immutable one-stop DB forward-fix package")
print("MIGRATION_EXECUTED=NO")
