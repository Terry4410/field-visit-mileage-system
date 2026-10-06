#!/usr/bin/env python3
"""Fail-closed static validation for Owner Pre-UAT migration 1800_009.

This validator does not execute SQL. It protects the one-stop DB constraint
forward-fix package, controlled UAT execution tooling, and runtime schema metadata.
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
    result = subprocess.run(
        ["git", "hash-object", str(path)],
        cwd=ROOT,
        check=True,
        text=True,
        capture_output=True,
    )
    return result.stdout.strip()


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
require("updatedbo.routecalculationattempts" not in compact_up, "1800_009 must not update RouteCalculationAttempts rows")
require("deletefromdbo.routecalculationattempts" not in compact_up, "1800_009 must not delete RouteCalculationAttempts rows")
require("insertdbo.routecalculationattempts" not in compact_up, "1800_009 must not insert RouteCalculationAttempts rows")
require(up.count("DROP CONSTRAINT CK_RouteCalculationAttempts_StopCount") == 1, "old StopCount constraint must be dropped exactly once")
require(up.count("CONSTRAINT CK_RouteCalculationAttempts_StopCount") == 3, "StopCount constraint references must remain exact")

compact_verify = re.sub(r"\s+", "", verify.lower())
for marker in (
    "VersionNumber = N'1.8.0-009'",
    "CK_RouteCalculationAttempts_StopCount",
    "stopcount>=1",
    "StopCount < 1",
    "is_disabled = 1 OR is_not_trusted = 1",
):
    require(re.sub(r"\s+", "", marker.lower()) in compact_verify, f"1800_009 Verify.sql marker missing: {marker}")

require("if (basis.Stops.Count < 1)" in orchestration, "application zero-stop prohibition changed or missing")
require('"DbSchemaVersion": "1.8.0-009"' in appsettings, "runtime DbSchemaVersion must align to 1.8.0-009 candidate")

for marker in (
    "workflow_dispatch:",
    "confirm_migration:",
    "confirm_runtime_gate:",
    "WRITE_QUIESCENCE_AND_RECOVERY_READY",
    "refs/heads/post-uat/v1.8.0",
    "environment: uat-migration",
    "APPROVED_MIGRATION_COMMIT_SHA",
    "gh-fieldvisit-uat-migrate",
    "rg-fieldvisit-uat",
    "sql-fieldvisit-jpe-uat",
    "db-fieldvisit-uat",
    f"EXPECTED_UP_SHA256: {EXPECTED_UP_SHA256}",
    f"EXPECTED_VERIFY_SHA256: {EXPECTED_VERIFY_SHA256}",
    f"EXPECTED_HISTORY_SHA256: {EXPECTED_HISTORY_SHA256}",
    "Apply only 1800_009 Up.sql",
    "Run exact 1800_009 Verify.sql",
    "RouteCalculationAttempts data fingerprint changed.",
    "STOP_FOR_REVIEW",
):
    require(marker in workflow, f"1800_009 workflow safety marker missing: {marker}")

require(workflow.count("-InputFile $up") == 1, "1800_009 workflow must invoke Up.sql exactly once")
require(workflow.count("-InputFile $verify") == 1, "1800_009 workflow must invoke Verify.sql exactly once")
require(workflow.count("-InputFile $history") == 1, "1800_009 workflow must invoke historical verifier exactly once")
for forbidden in ("git push --force", "git push -f ", "--force-with-lease", "force push"):
    require(forbidden not in workflow.lower(), f"1800_009 workflow contains forbidden history rewrite marker: {forbidden}")

for marker in (
    "ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]",
    "GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]",
    "GRANT_PREPARED_FOR_1800_009",
    "RouteCalculationAttempts",
    "1.8.0-008",
    "1.8.0-009",
):
    require(marker in grant, f"1800_009 grant marker missing: {marker}")
require("GRANT UPDATE" not in grant, "1800_009 must not grant UPDATE")

for marker in (
    "db_datareader",
    "db_ddladmin",
    "HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'ALTER')",
    "1800_009_PERMISSION_GATE",
):
    require(marker.replace(" ", "") in permission_verify.replace(" ", ""), f"1800_009 permission verify marker missing: {marker}")

for marker in (
    "REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate]",
    "ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]",
    "REVOKED_1800_009_ELEVATION",
):
    require(marker in revoke, f"1800_009 revoke marker missing: {marker}")

require("PREPARED ONLY / NOT EXECUTED" in readme, "1800_009 README must retain prepared-only execution state")
require("1 Visit Stop" in readme and "0 Visit Stops" in readme, "1800_009 README must document one-stop/zero-stop rule")

print("PASS 1800_009 one-stop DB constraint + immutable SQL + controlled workflow + least-privilege tooling")
print("MIGRATION_EXECUTED=NO")
