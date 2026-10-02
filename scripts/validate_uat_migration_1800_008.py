#!/usr/bin/env python3
"""Fail-closed static validation for Owner Pre-UAT migration 1800_008.

This validator does not execute SQL. It protects the prepared migration package
and the runtime schema metadata before promotion/deployment.
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

EXPECTED_UP_BLOB = "41db08c641a36c7597bb7cf51f3ebe8709928c2a"
EXPECTED_VERIFY_BLOB = "98a67f9567756299c276492ca77142394a19abd1"


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


for path in (UP, VERIFY, README, APPSETTINGS):
    require(path.is_file(), f"missing required file: {path.relative_to(ROOT)}")

require(blob_sha(UP) == EXPECTED_UP_BLOB, "1800_008 Up.sql blob changed")
require(blob_sha(VERIFY) == EXPECTED_VERIFY_BLOB, "1800_008 Verify.sql blob changed")

up = UP.read_text(encoding="utf-8")
verify = VERIFY.read_text(encoding="utf-8")
readme = README.read_text(encoding="utf-8")
appsettings = APPSETTINGS.read_text(encoding="utf-8")

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

require("PREPARED ONLY / NOT EXECUTED" in readme, "1800_008 README must retain prepared-only execution state")
require('"DbSchemaVersion": "1.8.0-008"' in appsettings, "runtime DbSchemaVersion must align to 1.8.0-008 candidate")

print("PASS 1800_008 immutable blobs + single-transaction + no-rate-row-rewrite + manual-date trigger contract")
print("MIGRATION_EXECUTED=NO")
