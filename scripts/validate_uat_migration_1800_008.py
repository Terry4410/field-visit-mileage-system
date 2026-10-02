#!/usr/bin/env python3
"""Fail-closed static validation for Owner Pre-UAT migration 1800_008.

This validator does not execute SQL. It protects the migration package,
the controlled UAT execution tooling, and runtime schema metadata.
"""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UP = ROOT / "database/migrations/1800_008_rate_manual_effective_dates/Up.sql"
VERIFY = ROOT / "database/migrations/1800_008_rate_manual_effective_dates/Verify.sql"
README = ROOT / "database/migrations/1800_008_rate_manual_effective_dates/README.md"
APPSETTINGS = ROOT / "backend/src/FieldVisit.Api/appsettings.json"
WORKFLOW = ROOT / ".github/workflows/azure-sql-uat-migration-1800-008.yml"
GRANT = ROOT / "database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_008.sql"
PERMISSION_VERIFY = ROOT / "database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_008.sql"
REVOKE = ROOT / "database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_008.sql"

EXPECTED_UP_BLOB = "41db08c641a36c7597bb7cf51f3ebe8709928c2a"
EXPECTED_VERIFY_BLOB = "98a67f9567756299c276492ca77142394a19abd1"
EXPECTED_UP_SHA256 = "d5d13d242898bbefa9a5adafcfd90487514a621631eeb4c2ee83b0bf3754e4fd"
EXPECTED_VERIFY_SHA256 = "3d326c8df9c39dcf4a450dd0179e7f2cbb39dba80e304105806216795a5a5143"
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


for path in (UP, VERIFY, README, APPSETTINGS, WORKFLOW, GRANT, PERMISSION_VERIFY, REVOKE):
    require(path.is_file(), f"missing required file: {path.relative_to(ROOT)}")

require(blob_sha(UP) == EXPECTED_UP_BLOB, "1800_008 Up.sql blob changed")
require(blob_sha(VERIFY) == EXPECTED_VERIFY_BLOB, "1800_008 Verify.sql blob changed")

up = UP.read_text(encoding="utf-8")
verify = VERIFY.read_text(encoding="utf-8")
readme = README.read_text(encoding="utf-8")
appsettings = APPSETTINGS.read_text(encoding="utf-8")
workflow = WORKFLOW.read_text(encoding="utf-8")
grant = GRANT.read_text(encoding="utf-8")
permission_verify = PERMISSION_VERIFY.read_text(encoding="utf-8")
revoke = REVOKE.read_text(encoding="utf-8")

require(re.search(r"(?mi)^\s*GO\s*$", up) is None, "1800_008 must remain one outer batch; GO is forbidden")
for marker, expected in (
    ("BEGIN TRANSACTION;", 1),
    ("COMMIT TRANSACTION;", 1),
    ("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;", 1),
):
    require(up.count(marker) == expected, f"1800_008 transaction invariant failed: {marker}")

for marker in (
    "FieldVisit.SchemaMigration",
    "VersionNumber = N'1.8.0-007'",
    "VersionNumber = N'1.8.0-008'",
    "CREATE OR ALTER TRIGGER dbo.TR_MileageRateRules_ProtectSeries",
    "FieldVisit.MileageRateSeries|ORG=",
    "@LockMode = N''Exclusive''",
    "@LockOwner = N''Transaction''",
    "@LockTimeout = 10000",
    "EffectiveTo < EffectiveFrom",
    "MileageRate active exact-series overlap detected.",
):
    require(marker in up, f"1800_008 Up.sql safety marker missing: {marker}")

trigger_pos = up.index("CREATE OR ALTER TRIGGER dbo.TR_MileageRateRules_ProtectSeries")
new_trigger = up[trigger_pos:].lower()
require("lead(" not in new_trigger, "new 1800_008 trigger must not derive EffectiveTo with LEAD")
require("update dbo.mileageraterules" not in new_trigger.replace("\n", " "), "new trigger must not rewrite MileageRateRules")
require("database-derived" not in new_trigger, "new trigger must not retain database-derived EffectiveTo contract")

outer_before_trigger = up[:trigger_pos].lower()
require("update dbo.mileageraterules" not in outer_before_trigger, "migration must not update existing MileageRateRules rows")
require("delete from dbo.mileageraterules" not in outer_before_trigger, "migration must not delete MileageRateRules rows")

compact_verify = verify.lower().replace(" ", "")
for marker in (
    "VersionNumber = N'1.8.0-008'",
    "sys.sp_getapplock",
    "fieldvisit.mileagerateseries",
    "r.effectiveto<r.effectivefrom",
    "activeexact-seriesoverlapdetected",
    "trigger 仍含自動衍生/改寫 EffectiveTo 邏輯",
):
    require(marker.lower().replace(" ", "") in compact_verify, f"1800_008 Verify.sql marker missing: {marker}")

for marker in (
    "workflow_dispatch:",
    "confirm_migration:",
    "confirm_runtime_gate:",
    "WRITE_QUIESCENCE_AND_RECOVERY_READY",
    'refs/heads/post-uat/v1.8.0',
    "environment: uat-migration",
    "APPROVED_MIGRATION_COMMIT_SHA",
    "gh-fieldvisit-uat-migrate",
    "rg-fieldvisit-uat",
    "sql-fieldvisit-jpe-uat",
    "db-fieldvisit-uat",
    f"EXPECTED_UP_SHA256: {EXPECTED_UP_SHA256}",
    f"EXPECTED_VERIFY_SHA256: {EXPECTED_VERIFY_SHA256}",
    f"EXPECTED_HISTORY_SHA256: {EXPECTED_HISTORY_SHA256}",
    "Apply only 1800_008 Up.sql",
    "Run exact 1800_008 Verify.sql",
    "MileageRateRules data fingerprint changed.",
    "STOP_FOR_REVIEW",
):
    require(marker in workflow, f"1800_008 workflow safety marker missing: {marker}")

require(workflow.count("-InputFile $up") == 1, "1800_008 workflow must invoke Up.sql exactly once")
require(workflow.count("-InputFile $verify") == 1, "1800_008 workflow must invoke Verify.sql exactly once")
require(workflow.count("-InputFile $history") == 1, "1800_008 workflow must invoke historical verifier exactly once")
require("force" not in workflow.lower(), "1800_008 workflow must not contain force semantics")
require("main" not in re.sub(r"1800_008_rate_manual_effective_dates", "", workflow), "1800_008 workflow must not target main")

for marker in (
    "ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate]",
    "GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate]",
    "GRANT_PREPARED_FOR_1800_008",
):
    require(marker in grant, f"1800_008 grant marker missing: {marker}")
require("GRANT UPDATE" not in grant, "1800_008 must not grant UPDATE")

for marker in (
    "db_datareader",
    "db_ddladmin",
    "HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'ALTER')",
    "1800_008_PERMISSION_GATE",
):
    require(marker in permission_verify.replace(" ", ""), f"1800_008 permission verify marker missing: {marker}")

for marker in (
    "REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate]",
    "ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate]",
    "REVOKED_1800_008_ELEVATION",
):
    require(marker in revoke, f"1800_008 revoke marker missing: {marker}")

require("PREPARED ONLY / NOT EXECUTED" in readme, "1800_008 README must retain prepared-only execution state")
require('"DbSchemaVersion": "1.8.0-008"' in appsettings, "runtime DbSchemaVersion must align to 1.8.0-008 candidate")

print("PASS 1800_008 immutable SQL + controlled workflow + least-privilege tooling + manual-date trigger contract")
print("MIGRATION_EXECUTED=NO")
