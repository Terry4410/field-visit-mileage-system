#!/usr/bin/env python3
"""Fail-closed static validation for Owner Pre-UAT migration 1800_010.

This validator does not execute SQL. It protects the background-mileage
CalculationReason DB forward-fix and its controlled UAT execution tooling.
"""

from __future__ import annotations

import hashlib
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UP = ROOT / "database/migrations/1800_010_background_mileage_reason_constraint/Up.sql"
VERIFY = ROOT / "database/migrations/1800_010_background_mileage_reason_constraint/Verify.sql"
README = ROOT / "database/migrations/1800_010_background_mileage_reason_constraint/README.md"
APPSETTINGS = ROOT / "backend/src/FieldVisit.Api/appsettings.json"
ORCHESTRATION = ROOT / "backend/src/FieldVisit.Application/V180GoogleMileageOrchestrationService.cs"
WORKFLOW = ROOT / ".github/workflows/azure-sql-uat-migration-1800-010.yml"
GRANT = ROOT / "database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_010.sql"
PERMISSION_VERIFY = ROOT / "database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_010.sql"
REVOKE = ROOT / "database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_010.sql"

EXPECTED_UP_BLOB = "3826dc73739a61ae1447ed049c3fc0c6ec02ad18"
EXPECTED_VERIFY_BLOB = "2070af58174bbe79ef4343fe40c1ead94f0718a2"
EXPECTED_UP_SHA256 = "aa56e3c1f847e252b089b572ab35a0aede65dc9dbd6d1d657ebc4c0ae6e775c8"
EXPECTED_VERIFY_SHA256 = "24443315adf01b8f55d9882e7db164abecc3d9a63c83b57f9a58ae7d09a74a42"


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


for path in (UP, VERIFY, README, APPSETTINGS, ORCHESTRATION, WORKFLOW, GRANT, PERMISSION_VERIFY, REVOKE):
    require(path.is_file(), f"missing required file: {path.relative_to(ROOT)}")

require(blob_sha(UP) == EXPECTED_UP_BLOB, "1800_010 Up.sql blob changed")
require(blob_sha(VERIFY) == EXPECTED_VERIFY_BLOB, "1800_010 Verify.sql blob changed")
require(sha256(UP) == EXPECTED_UP_SHA256, "1800_010 Up.sql SHA-256 changed")
require(sha256(VERIFY) == EXPECTED_VERIFY_SHA256, "1800_010 Verify.sql SHA-256 changed")

up = UP.read_text(encoding="utf-8")
verify = VERIFY.read_text(encoding="utf-8")
readme = README.read_text(encoding="utf-8")
appsettings = APPSETTINGS.read_text(encoding="utf-8")
orchestration = ORCHESTRATION.read_text(encoding="utf-8")
workflow = WORKFLOW.read_text(encoding="utf-8")
grant = GRANT.read_text(encoding="utf-8")
permission_verify = PERMISSION_VERIFY.read_text(encoding="utf-8")
revoke = REVOKE.read_text(encoding="utf-8")

require(re.search(r"(?mi)^\s*GO\s*$", up) is None, "1800_010 must remain one outer batch; GO is forbidden")
for marker, expected in (
    ("BEGIN TRANSACTION;", 1),
    ("COMMIT TRANSACTION;", 1),
    ("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", 1),
):
    require(up.count(marker) == expected, f"1800_010 transaction invariant failed: {marker}")

for marker in (
    "FieldVisit.SchemaMigration",
    "VersionNumber = N'1.8.0-009'",
    "VersionNumber = N'1.8.0-010'",
    "CK_RouteCalculationAttempts_Reason",
    "DROP CONSTRAINT CK_RouteCalculationAttempts_Reason",
    "CalculationReason = N'VisitorCalculate'",
    "CalculationReason = N'LeaderRetry'",
    "CalculationReason = N'CorrectionRecalculate'",
    "CalculationReason = N'BackgroundMileageJob'",
):
    require(marker in up, f"1800_010 Up.sql safety marker missing: {marker}")

compact_up = re.sub(r"\s+", "", up.lower())
for forbidden in (
    "updatedbo.routecalculationattempts",
    "deletefromdbo.routecalculationattempts",
    "insertdbo.routecalculationattempts",
):
    require(forbidden not in compact_up, f"1800_010 must not mutate RouteCalculationAttempts rows: {forbidden}")
require(up.count("DROP CONSTRAINT CK_RouteCalculationAttempts_Reason") == 1,
        "old CalculationReason constraint must be dropped exactly once")

compact_verify = re.sub(r"\s+", "", verify.lower())
for marker in (
    "VersionNumber = N'1.8.0-010'",
    "CK_RouteCalculationAttempts_Reason",
    "visitorcalculate",
    "leaderretry",
    "correctionrecalculate",
    "backgroundmileagejob",
    "is_disabled = 1 OR is_not_trusted = 1",
):
    require(re.sub(r"\s+", "", marker.lower()) in compact_verify, f"1800_010 Verify marker missing: {marker}")

require('"BackgroundMileageJob"' in orchestration,
        "application BackgroundMileageJob reason contract changed or missing")
require('CalculateSubmittedRouteForBackgroundAsync' in orchestration,
        "background submitted-route orchestration entry point missing")
require('"DbSchemaVersion": "1.8.0-010"' in appsettings,
        "runtime DbSchemaVersion must align to 1.8.0-010 candidate")

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
    "Apply only 1800_010 Up.sql",
    "Run exact 1800_010 Verify.sql",
    "RouteCalculationAttempts data fingerprint changed.",
    "STOP_FOR_REVIEW",
):
    require(marker in workflow, f"1800_010 workflow safety marker missing: {marker}")
require(workflow.count("-InputFile $up") == 1, "1800_010 workflow must invoke Up.sql exactly once")
require(workflow.count("-InputFile $verify") == 1, "1800_010 workflow must invoke Verify.sql exactly once")
require("Verify-v180-historical-fingerprints.sql" not in workflow,
        "1800_010 must use immediate pre/post fingerprints, not the stale frozen historical gate")
for forbidden in ("git push --force", "git push -f ", "--force-with-lease", "force push"):
    require(forbidden not in workflow.lower(), f"1800_010 workflow contains forbidden history rewrite marker: {forbidden}")

for marker in (
    "GRANT ALTER ON OBJECT::dbo.RouteCalculationAttempts TO [gh-fieldvisit-uat-migrate]",
    "GRANT INSERT ON OBJECT::dbo.SchemaVersions TO [gh-fieldvisit-uat-migrate]",
    "GRANT_PREPARED_FOR_1800_010",
    "1.8.0-009",
    "1.8.0-010",
):
    require(marker in grant, f"1800_010 grant marker missing: {marker}")
require("db_ddladmin ADD MEMBER" not in grant, "1800_010 must not grant db_ddladmin")
require("GRANT UPDATE" not in grant, "1800_010 must not grant UPDATE")

for marker in (
    "db_datareader",
    "HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'ALTER')",
    "HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT')",
    "1800_010_PERMISSION_GATE",
):
    require(marker.replace(" ", "") in permission_verify.replace(" ", ""),
            f"1800_010 permission verify marker missing: {marker}")

for marker in (
    "REVOKE ALTER ON OBJECT::dbo.RouteCalculationAttempts FROM [gh-fieldvisit-uat-migrate]",
    "REVOKE INSERT ON OBJECT::dbo.SchemaVersions FROM [gh-fieldvisit-uat-migrate]",
    "REVOKED_1800_010_ELEVATION",
):
    require(marker in revoke, f"1800_010 revoke marker missing: {marker}")

require("PREPARED ONLY / NOT EXECUTED" in readme,
        "1800_010 README must retain prepared-only execution state")
require("BackgroundMileageJob" in readme and "before the Google Routes provider is called" in readme,
        "1800_010 README must document the confirmed runtime root cause")

print("PASS 1800_010 background mileage reason constraint + immutable SQL + controlled workflow + least privilege")
print("MIGRATION_EXECUTED=NO")
