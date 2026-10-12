# v1.8.0 versioned migrations

## Current UAT state

- `1.8.0-001` through `1.8.0-007`: **APPLIED / VERIFIED in UAT**
- Current UAT schema before Owner Pre-UAT delta: **1.8.0-007**
- `1.8.0-008`: **PREPARED ONLY / NOT EXECUTED**

The original 001–007 migration packages remain immutable historical evidence. Owner Pre-UAT
introduced one new database contract change only: PRE-UAT-013 requires MileageRate effective
date ranges to be administrator-owned instead of database-derived.

## Required order

| Order | Folder | Prerequisite | Main result | UAT state |
|---|---|---|---|---|
| 1 | `1800_001_organization_center_team_lifecycle` | `1.7.0-008` | Center, Team lifecycle, historical fingerprint baseline | Applied |
| 2 | `1800_002_person_employment_role_membership` | `1.8.0-001` | Person, Employment, status, role, Team membership | Applied |
| 3 | `1800_003_deployment_sites` | `1.8.0-002` | Deployment Site and assignment history | Applied |
| 4 | `1800_004_location_governance` | `1.8.0-003` | TaxId, master note, duplicate marker, audited Team Location Notes | Applied |
| 5 | `1800_005_project_visit_rate_lifecycle` | `1.8.0-004` | Project/Visit Type lifecycle and Motorcycle/Car rate series | Applied |
| 6 | `1800_006_notification_framework` | `1.8.0-005` | Notification framework | Applied |
| 7 | `1800_007_mileage_google_governance` | `1.8.0-006` | Google/provider audit and company-approved mileage evidence | Applied |
| 8 | `1800_008_rate_manual_effective_dates` | `1.8.0-007` | PRE-UAT-013: explicit rate date ranges; no automatic neighboring-date rewrite | **Prepared only** |

## 1.8.0-008 scope

`1800_008` intentionally changes only `dbo.TR_MileageRateRules_ProtectSeries`.

It does **not** add/drop tables or columns and does **not** update existing rate rows.
It preserves:

- physical-delete protection;
- canonical MOTORCYCLE/CAR validation;
- exact-series `sp_getapplock`;
- `UPDLOCK + HOLDLOCK` serialization;
- valid date-range validation;
- active same-series overlap rejection.

It removes the 1.8.0-005 trigger behavior that used `LEAD(EffectiveFrom)` to rewrite
`EffectiveTo` and rejected administrator-authored non-null `EffectiveTo`.

## Historical-data policy

- Existing Trips and Snapshots are never rewritten by 1.8.0-008.
- Existing MileageRateRule date/value rows are preserved exactly during migration.
- Historical approved rate snapshots are not recalculated.
- Earlier migration packages remain the historical evidence of how the current schema was built.

## Execution governance for 1.8.0-008

Do not execute `1800_008/Up.sql` merely because the source branch is ready.

A separately authorized UAT migration execution must:

1. use the existing `uat-migration` governance and migration identity;
2. prove exact target database `db-fieldvisit-uat`;
3. prove current latest schema is exactly `1.8.0-007`;
4. keep application writes quiesced for the short trigger replacement + verify window;
5. run only `1800_008_rate_manual_effective_dates/Up.sql`;
6. immediately run its `Verify.sql`;
7. stop on any THROW or unexpected trigger/source drift;
8. record exact run ID and resulting schema version before application deployment.

No rollback script is supplied. If execution fails before commit, the transaction rolls back.
After a successful commit, any later correction must be a reviewed forward-fix.
