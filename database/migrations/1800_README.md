# v1.8.0 versioned migrations — review package

Status: **scripts only / never executed**. These files must not be applied until the user separately approves the Azure SQL UAT migration stage.

The sole business baseline is `v1.8.0 Post-UAT Requirement Freeze & Impact Analysis`. The scripts are additive and preserve the v1.7.2 compatibility tables and all existing Trip/Snapshot values.

## Required order

| Order | Folder | Prerequisite | Main result |
|---|---|---|---|
| 1 | `1800_001_organization_center_team_lifecycle` | `1.7.0-008` | Center, Team lifecycle, effective Team-Center history and historical-data fingerprint baseline |
| 2 | `1800_002_person_employment_role_membership` | `1.8.0-001` | Person, Employment, status, role, Team membership, leaders and delegation |
| 3 | `1800_003_deployment_sites` | `1.8.0-002` | Site master, relocation history, Team-Site and Employment-Site assignment |
| 4 | `1800_004_location_governance` | `1.8.0-003` | TaxId, master note, inactivation/duplicate marker and Team Location Note audit |
| 5 | `1800_005_project_visit_rate_lifecycle` | `1.8.0-004` | Project/Visit Type lifecycle and effective Motorcycle/Car rates |
| 6 | `1800_006_notification_framework` | `1.8.0-005` | Mandatory Transaction semantics, optional Reminder preference, UAT Test/Disabled/empty allowlist, outbox/log |
| 7 | `1800_007_mileage_google_governance` | `1.8.0-006` | Provider request audit and company-approved mileage evidence without unapproved Google-output retention |

Every `Up.sql` is single-transaction, obtains the same SQL application lock, checks its exact predecessor, refuses reapplication and stops on a partially modified schema. Every `Verify.sql` throws on failure so GitHub Actions can act as a hard gate.

## Backfill policy

- `1800_001` captures counts and SHA-256 fingerprints of the current v1.7.2 `VisitTrips`, `VisitTripSnapshots` and `VisitTripSnapshotStops` before adding lifecycle schema. It does not assign legacy Teams to invented Centers.
- `1800_002` creates exactly one Person and one Employment for each eligible legacy User. It copies only existing EmployeeNo, Email and existing effective-dated status/role/team facts. It does not merge people by name and leaves unknown HireDate/TerminationDate as `NULL`.
- Existing identity profiles receive the deterministic Employment link. Existing Trips and approved Snapshots are not assigned or rewritten; only new nullable columns are added for v1.8 writes.
- `1800_003` does not invent Center/Site/Location ownership. Business-approved Site master and assignment data must be loaded later through application or approved import flows.
- `1800_004` does not merge, re-key or deactivate any Location. TaxId is deliberately indexed but not unique.
- `1800_005` keeps every existing rate value and date. It stops if unknown vehicle codes or overlapping active periods are found; it does not normalize them automatically.
- `1800_006` creates `OptionalEmailNotificationEnabled`; Transaction/System events do not honor it, Reminder events do. UAT starts with an empty allowlist and a disabled `Test` policy. No email is sent.
- `1800_007` sets only new governance defaults (`ManualFallbackUsed = 0`) and does not change existing distances, rates, amounts or Snapshot content. It does not persist Google-derived coordinates, route distance/duration, provider request ID, polyline, turn-by-turn or raw response.

## Rollback policy

No `Rollback.sql` is supplied. Although the migrations are additive, dropping their tables or columns after backfill or v1.8 writes would destroy data and audit evidence. A reviewed forward-fix is safer than an unconditional rollback. Database recovery, if needed during the future UAT execution, must use the IT-approved restore point and a separately approved recovery decision.

## Future UAT execution gate (not performed)

Before applying any `Up.sql`, the separately approved pipeline must:

1. Keep `gh-fieldvisit-uat` unchanged at `Reader + db_datareader`; use the distinct migration identity `gh-fieldvisit-uat-migrate` only after separate approval.
2. Require `post-uat/v1.8.0`, a separate GitHub `uat-migration` environment, `workflow_dispatch`, Required approval and OIDC.
3. Prove subscription, resource group, SQL server and `DB_NAME() = db-fieldvisit-uat`.
4. Prove current latest schema is the exact predecessor of the single selected migration.
5. Capture/confirm an IT-approved UAT restore point and prevent concurrent application writes during Up + Verify.
6. The first future executable unit is fixed to `1800_001/Up.sql` followed by `1800_001/Verify.sql` (including historical fingerprint verification), then stop for Review. `1800_002` is not selectable and requires a later, separate approval; never execute `001`–`007` in one run.
7. Stop immediately on any `THROW`; do not edit data, broaden firewall rules or expand database roles to make the run pass.

The existing `gh-fieldvisit-uat` principal remains intentionally limited to `Reader + db_datareader` for read-only checks. These scripts must not be run until the separate, least-privilege migration execution design is reviewed and approved; no role increase is part of this package.
