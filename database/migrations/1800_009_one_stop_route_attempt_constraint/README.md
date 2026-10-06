# 1.8.0-009 — One-stop route-attempt constraint correction

Status: **PREPARED ONLY / NOT EXECUTED**

## Purpose

PRE-UAT-005 confirmed the business rule:

- Start + at least **1 Visit Stop** + End is a valid route.
- 0 Visit Stops remains prohibited.

Application validation already follows this rule, but migration 1.8.0-007 created
\`CK_RouteCalculationAttempts_StopCount\` as \`StopCount >= 2\`. A one-stop route therefore
fails while the route-attempt audit row is being persisted, before Google Routes is called.

## Scope

This migration changes only:

- \`dbo.CK_RouteCalculationAttempts_StopCount\`: \`StopCount >= 2\` → \`StopCount >= 1\`
- \`SchemaVersions\`: append \`1.8.0-009\`

It does not update, delete, or backfill any \`RouteCalculationAttempts\` rows.
It does not change Google provider code, trip data, employee master data, or snapshots.

## Safety gates

- exact predecessor \`1.8.0-008\` required;
- exact existing constraint definition \`StopCount >= 2\` required;
- existing constraint must be enabled and trusted;
- existing rows with \`StopCount < 1\` block execution;
- one outer transaction with \`FieldVisit.SchemaMigration\` application lock;
- Verify requires the replacement constraint to be enabled, trusted, and exactly \`StopCount >= 1\`.

## Expected runtime effect

- 1 Visit Stop: route-attempt persistence allowed.
- 0 Visit Stops: still rejected by application validation and by the DB constraint.
- Existing rows: unchanged.

## Execution

Scripts and workflow tooling only. Do not run against UAT until Control Tower separately
authorizes migration \`1800_009\` execution after promotion and runtime/recovery gates.
