# 1.8.0-010 — Background mileage reason constraint correction

Status: **PREPARED ONLY / NOT EXECUTED**

## Purpose

PRE-UAT-005 runtime diagnosis confirmed a DB governance mismatch:

- the application creates submitted-route attempts with `CalculationReason = BackgroundMileageJob`;
- `dbo.CK_RouteCalculationAttempts_Reason` from 1.8.0-007 allows only
  `VisitorCalculate`, `LeaderRetry`, and `CorrectionRecalculate`;
- the background mileage job is therefore rejected while persisting the route-attempt audit row,
  before the Google Routes provider is called.

## Scope

This migration changes only:

- `dbo.CK_RouteCalculationAttempts_Reason`: add `BackgroundMileageJob` to the approved reason set;
- `SchemaVersions`: append `1.8.0-010`.

It does not update, delete, backfill, or recreate any `RouteCalculationAttempts` row.
It does not change trip data, submitted snapshots, employee master data, or Google provider code.

## Safety gates

- exact predecessor `1.8.0-009` required and must be the latest schema version;
- target `1.8.0-010` must be absent;
- the existing reason constraint must be present, enabled, trusted, contain the three predecessor reasons,
  and not already contain `BackgroundMileageJob`;
- existing rows must use only the three predecessor reasons;
- one outer transaction with `FieldVisit.SchemaMigration` application lock;
- Verify requires the replacement constraint to be enabled, trusted, and contain all four approved reasons.

## Execution workflow

The 1800_010 workflow uses immediate pre/post data fingerprints for `VisitTrips`,
`VisitTripSnapshots`, and `RouteCalculationAttempts`. The migration itself may only append one
`SchemaVersions` row and replace the named check constraint.

The workflow intentionally does not use the older frozen v1.7.2 historical fingerprint as the
success criterion for this forward-fix because that baseline is already known to differ from current
UAT runtime data for unrelated historical reasons. Any immediate pre/post data fingerprint change
still fails closed.

## Expected runtime effect

After application code and this schema version are aligned, a submitted mileage background job may
persist a `SubmittedSnapshot / BackgroundMileageJob` route attempt, call Google Routes, and continue
to the existing `SystemDistanceKm` persistence path.

## Execution

Scripts and workflow tooling only. Do not run against UAT until Control Tower separately authorizes
migration `1800_010` execution after validation, promotion, temporary permission gating, and
`WRITE_QUIESCENCE_AND_RECOVERY_READY`.
