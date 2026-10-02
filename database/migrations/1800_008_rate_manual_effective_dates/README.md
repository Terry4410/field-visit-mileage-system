# 1.8.0-008 — MileageRate manual effective-date governance

## Purpose

Owner Pre-UAT requirement PRE-UAT-013 changes the MileageRate lifecycle contract:

- Administrator explicitly owns **EffectiveFrom** and **EffectiveTo**.
- The system must **not** rewrite neighboring rate-version dates.
- Motorcycle and Car are independent series.
- Within the same Organization + VehicleType series, active date ranges must not overlap.
- Historical Trip/Snapshot rate evidence is never recalculated by this migration.

## Scope

This migration changes **only** `dbo.TR_MileageRateRules_ProtectSeries` and inserts
`SchemaVersions = 1.8.0-008`.

It does not add/drop tables or columns and does not update existing MileageRateRules rows.

## Preserved protections

- physical DELETE prohibited;
- canonical MOTORCYCLE/CAR enforcement;
- exact-series `sp_getapplock` using Exclusive / Transaction / 10-second timeout;
- `UPDLOCK + HOLDLOCK` serialization;
- EffectiveTo >= EffectiveFrom;
- active period overlap rejection.

## Removed behavior

The 1.8.0-005 trigger derived EffectiveTo with `LEAD(EffectiveFrom)`, rewrote neighboring
rows, and rejected administrator-authored non-null EffectiveTo. That behavior conflicts with
PRE-UAT-013 and is removed here.

## Execution

Scripts only. Do not run against UAT until Control Tower separately authorizes migration
1.8.0-008 execution with the existing migration identity / environment governance.
