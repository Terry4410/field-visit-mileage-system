# v1.8.0 Migration Safety Review

Date: 2026-09-07. Review state: scripts prepared, no Azure SQL execution. Business baseline: approved `v1.8.0 Post-UAT Requirement Freeze & Impact Analysis`.

## Decision summary

No intentionally destructive migration is present. The package contains no `DROP TABLE`, `DROP COLUMN`, `TRUNCATE`, `DELETE`, master re-key, Location merge, Snapshot recalculation or update of existing Trip/Snapshot business values. Because no migration has been executed, this is a source-level safety assessment rather than an Azure SQL verification result.

The scripts deliberately stop rather than repair data when they encounter an unexpected schema, missing predecessor, duplicate Team code, effective-date overlap, invalid date range or unsupported rate vehicle code.

## Safety matrix

| Migration | Existing-data effect | Effective-date protection | Historical Trip/Snapshot protection | FK / index dependency | Destructive assessment |
|---|---|---|---|---|---|
| `1800_001` | Adds Organization/Team fields; creates Center and Team-Center tables; no Center backfill | Date checks plus Team-Center overlap trigger | Captures counts and SHA-256 fingerprints before schema additions; new Snapshot columns are nullable | Requires Organizations, Teams, Users and v1.7.2 Trip/Snapshot tables | Non-destructive; stops on duplicate TeamCode or partial schema |
| `1800_002` | One-to-one legacy User → Person/Employment; copies only existing v1.7 effective facts; links identity profile | Triggers cover employment status, same-role overlap, single Primary Team, leader/delegation periods | Adds nullable Employment fields only; does not update existing Trip/Snapshot rows | Depends on 001, Users/Roles and v1.7 User* assignment tables | Additive backfill; no guessed HR dates or name-based merge |
| `1800_003` | Creates empty Site master/assignment tables; no ownership backfill | Site relocation, Team-Site and one-primary Employment-Site triggers | Existing Trip/Snapshot rows remain null in new columns | Depends on Centers, Employments, Teams and Locations | Non-destructive; business Site backfill remains pending |
| `1800_004` | Adds nullable Location governance fields and deterministic normalized search columns; creates Team Note tables | Not applicable | No Location IDs, Trip stops or Snapshot addresses are changed | Self-FK and note FKs use `NO ACTION`; TaxId is non-unique | Non-destructive; explicitly excludes merge/cascade re-key |
| `1800_005` | Adds lifecycle/audit/concurrency fields; no Project, VisitType or rate value change | Project/rate date checks and active-rate overlap trigger | Existing rate snapshots and approved amounts unchanged | Search/as-of indexes; all new FKs `NO ACTION` | Non-destructive; stops on invalid/unknown legacy rate data |
| `1800_006` | Adds Employment notification preference (default enabled); seeds new event metadata; UAT disabled/Test with empty allowlist | Reminder-day validation | No Snapshot dependency or mutation | Outbox/log FKs use `NO ACTION`; dispatch and audit indexes | Non-destructive; no send operation or credential storage |
| `1800_007` | Adds route/geocode attempt and audit structures; existing mileage gets only new default false flag | Snapshot basis and vehicle-mode checks | Adds nullable coordinate/route evidence columns; no old distance/rate/amount update | Route attempts reference immutable submitted Snapshot for leader retry; all audit FKs `NO ACTION` | Non-destructive; no raw route payload, polyline or turn-by-turn persistence |

## Backfill and unresolved business data

Safe deterministic backfill is limited to `1800_002`. Each legacy `UserId` becomes its own Person and Employment so similarly named people are never merged. Existing User employment/role/team effective rows are copied as-is. If a legacy User has no authoritative employment status period, none is invented; the result is labelled as legacy-compatible until HR data is supplied.

The following still require authoritative Business/IT data and are not invented by SQL:

- Center codes and initial Team-Center effective periods.
- Deployment Site codes, locations, Team availability and Employment primary assignments.
- Hire/termination/status dates missing from existing HR data.
- Actual Car and Motorcycle rates and their effective dates.
- UAT email allowlist, sender/provider, Google project/billing/quota and key restrictions.

## Effective-date rules

All period tables use inclusive `EffectiveFrom`/`EffectiveTo`. An open end is treated as `9999-12-31` for overlap detection. Adjacent records therefore use `next.EffectiveFrom = prior.EffectiveTo + 1 day`; the same date cannot belong to two periods. Application services must apply the same rule and return a business validation error before the database trigger is reached.

Primary uniqueness is interval-based, not merely current-state based:

- one Team-Center assignment per Team at a date;
- one Employment status at a date;
- one Primary Team per Employment at a date;
- one Location per Deployment Site at a date;
- one Primary Deployment Site per Employment at a date;
- one active rate per Organization + VehicleType at a date.

Multiple effective leaders for the same Team are allowed. Delegations are effective-dated and do not erase the original leader assignment.

## Historical immutability verification

`1800_001/Up.sql` records pre-change maximum IDs, row counts and SHA-256 fingerprints over the v1.7.2 columns of Trips, Snapshots and Snapshot Stops. `1800_001/Verify.sql` compares the same bounded records and fails if any row was deleted or any original value changed. New rows with higher IDs do not alter the protected baseline.

During a future migration run, application writes must be paused or otherwise placed behind the migration gate. Execute `1800_001/Verify.sql` after every migration and again after `1800_007`; a count-only check is not sufficient for final evidence.

## Stop conditions for future execution

Stop and request review if any of the following occurs:

- target identity, subscription, resource group, SQL server or `DB_NAME()` differs from approved UAT;
- current schema is not exactly the expected predecessor;
- any migration reports partial/manual schema objects;
- duplicate codes, unknown vehicle types, invalid date ranges or overlap checks fail;
- historical fingerprint differs;
- a migration would require deleting, re-keying, truncating, overwriting or auto-correcting existing business data;
- network access fails or would require a firewall/security relaxation;
- available SQL permission exceeds or falls short of the separately approved migration role design.

No Rollback script is included because dropping the additive model after data is copied or v1.8 activity starts is not safely reversible. Use a reviewed forward-fix or an approved UAT restore point.
