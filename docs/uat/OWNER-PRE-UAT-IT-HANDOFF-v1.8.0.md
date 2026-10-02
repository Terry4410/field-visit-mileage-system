# Field Visit v1.8.0 — Owner Pre-UAT Change Map / IT Handoff

Status: **SOURCE IMPLEMENTED + VALIDATED / OWNER PRE-UAT RUNTIME NOT YET RELEASED**

## 1. Control boundary

- Repository: `Terry4410/field-visit-mileage-system`
- Authoritative pre-change baseline: `post-uat/v1.8.0 @ c51e91c7c0e672f749271c6768bd169889d7164c`
- Consolidated work branch: `work/v180-owner-preuat-consolidated`
- Latest validated application SHA: `f15812111d2bca4289a33e535fd7f11d5accae3d` — Post-UAT Run `36972456821` / #57 **SUCCESS**
- Latest validated migration-tooling SHA before this documentation-only handoff sync: `f6a0d603bb022ef066fb30115448fa6e01fbbccb` — Owner Pre-UAT Run `36972923304` / #11 **SUCCESS**
- Consolidated lineage remains **pure forward / fast-forward only** from the original baseline; use the final promoted `post-uat/v1.8.0` HEAD as the release-source identifier.
- UAT deployed application source before this package: `2fe86208da6d8c10767fd6309a6d467e5448d92a`
- UAT schema before this package: `1.8.0-007`
- Business User Access: **HOLD / NOT YET DISTRIBUTED**
- Production: **OUT OF SCOPE**

The Owner requested a small number of maintainable changes rather than one branch/version per issue.
The implementation is therefore grouped by domain, reuses existing v1.8 schema wherever possible,
and avoids parallel models.

## 2. Consolidated source history

| Commit | Domain | Main scope |
|---|---|---|
| `765cda054562385b9a383be66a27ce70e52d02e6` | Trip / Login / Rate application rules | Login cleanup, deployment-site display, vehicle choice, manual fallback semantics, explicit rate dates |
| `cce4087664e3b78c1ee8a9b10943983cd5eab9b1` | Google mileage / Leader | Server-side Google providers, background routing, mileage source, batch trip approval |
| `0e0c070b3d9b057d7a5afbdfc59aa5f5ab212ea0` | Location | Shared maintenance, TaxId/note search, audit, duplicate preview/merge |
| `1f9ed6a4c179a1af64f18ccaa91dc8947a1ec493` | Personnel / Team / Correction / Rate UI | HR facts, access source-of-truth, Team bulk placement, batch correction close, explicit rate UI |
| `780ee0fd86a65695cbc61de2616e3738ea85b476` | Finalization | 1.8.0-008 migration package, Location merge operational-reference reconciliation, IT handoff |
| `2dc1505f684ef37834805a97f4db50de3baf6393` | Governance correction | 1800_008 static gate + Post-UAT hook + runtime schema metadata 1.8.0-008 |
| `aefb180afcac84fb00419382576944282e651ce9` | Documentation correction | Explicitly marks 1800_008 PREPARED ONLY / NOT EXECUTED |
| `02a0cb665855da88ef992f70e0ea9d0d456f74a3` | IT handoff sync | Final pre-deployment evidence sync |
| `01b01cd188b2d46a304ff4964d129d28a0f01413` | Test-only correction | Restores backend test compilation: xUnit imports + F-B fake repository interface parity |
| `f15812111d2bca4289a33e535fd7f11d5accae3d` | UAT test-only correction | Aligns Epic A permission-modal locator with the completed 人事資料 / 權限 UI split |
| `d15e8fd7f5576fc4c10fecf2aa83007af294335b` | Migration execution tooling | Adds controlled 1800_008 workflow + least-privilege Grant/Verify/Revoke tooling |
| `f6a0d603bb022ef066fb30115448fa6e01fbbccb` | Migration tooling validator correction | Narrows the no-history-rewrite static guard so PowerShell module `-Force` is not misclassified |

Owner Pre-UAT validation workflow:
`.github/workflows/owner-preuat-v180-verify.yml`

Deployment-stage validation evidence:
- Original application package validation Run `36970320261`: **SUCCESS**
- Backend test-compilation correction: Owner Pre-UAT Run `36971901133` / #8 **SUCCESS**
- Permission-modal browser test correction: Owner Pre-UAT Run `36972375119` / #9 **SUCCESS**
- Exact promoted application source `f15812111d2bca4289a33e535fd7f11d5accae3d`: Post-UAT Run `36972456821` / #57 **SUCCESS**
  - migration/recovery/static gates: PASS
  - frontend tests/build: PASS
  - backend build/tests: PASS — 353 tests, 0 failed
  - Epic A isolated browser regression: PASS
- Controlled 1800_008 tooling `f6a0d603bb022ef066fb30115448fa6e01fbbccb`: Owner Pre-UAT Run `36972923304` / #11 **SUCCESS**
- This handoff sync commit is documentation-only and must pass the same Owner/Post-UAT gates before it becomes the final promoted source.

## 3. PRE-UAT issue mapping

| Issue | Implementation | DB impact |
|---|---|---|
| PRE-UAT-001 | Login displays `v1.8.0 UAT` | None |
| PRE-UAT-002 | Removed four demo-role buttons and old prefilled credentials | None |
| PRE-UAT-003 | Shared Location maintenance + audited Team notes/history + note search | Reuses 1.8.0-004 |
| PRE-UAT-004 | Visitor/Leader/Admin address maintenance; before/after AuditLog; geocoding invalidation | Reuses 1.8.0-004/007 |
| PRE-UAT-005 | Visitor sees default start/end Deployment Site from Trip Context | Reuses 1.8.0-003 |
| PRE-UAT-006 | Google success vs ManualFallback only; source visible/auditable | Reuses 1.8.0-007 |
| PRE-UAT-007 | Location TaxId search/maintenance | Reuses 1.8.0-004 |
| PRE-UAT-008 | Motorcycle/Car choice, default Motorcycle, stored on Trip and snapshot path | Reuses existing v1.8 fields |
| PRE-UAT-009 | Leader individual + batch trip approval | Existing batch API wired to UI |
| PRE-UAT-010 | HR master fields + multiple effective-dated leave/status periods; login fail-closed | Reuses 1.8.0-002 |
| PRE-UAT-011 | Existing preview/confirm bulk tool moved to Team member maintenance | No new importer/schema |
| PRE-UAT-012 | Create/edit auto duplicate detection (TaxId/name/address/Plus Code, including small text variation) + Admin 3-way review (different / use existing / field-select merge) + side-by-side impact preview; historical Trip/Snapshot not rewritten and survivor query resolves source history | Reuses 1.8.0-004 duplicate marker + LocationApprovalHistory + AuditLog |
| PRE-UAT-013 | Admin owns vehicle/start/end/rate/rule; no neighboring date rewrite | **Requires 1.8.0-008** |
| PRE-UAT-014 | Single + selected batch correction close; stale/mixed state blocks before batch | Existing correction schema |

## 3A. Confirmed Owner request reconciliation

The deployment package is checked against the Owner-confirmed requests, not only the issue titles:
- Login: UAT version shown; demo-role buttons removed.
- Mileage: Google success uses provider distance; Google failure permits manual fallback only; Leader remains final approver and supports individual/batch approval.
- Location maintenance: Visitor/Leader/Admin shared maintenance, TaxId/Plus Code/address/master note, Team note author/time history, searchable note history and before/after audit.
- Duplicate governance: automatic create/edit candidate detection, Admin manual review, three review outcomes, merge safeguards/master-field choice, immutable historical Trip/Snapshot and survivor history resolution.
- Rate governance: Admin explicitly maintains EffectiveFrom/EffectiveTo; same-vehicle active overlap is blocked; EffectiveTo < EffectiveFrom is blocked; different vehicle series may overlap; neighboring periods are never silently rewritten.
- Google Maps UAT: server-side configuration only; no API key in Git/source. Runtime activation remains a deployment gate until the UAT App Service settings are applied and smoke-tested.

## 4. Database strategy

No new general-purpose tables were introduced for Owner Pre-UAT.

Existing v1.8 schema is reused:

- `Employment / EmploymentStatusPeriods` — personnel and leave/termination status;
- `DeploymentSites / EmploymentDeploymentSiteAssignments` — trip start/end defaults;
- `Locations.TaxId / MasterNote / DuplicateOfLocationId` — Location governance;
- `TeamLocationNotes / TeamLocationNoteHistory` — note/history;
- `RouteCalculationAttempts / MileageGovernanceEvents` — Google/fallback audit;
- `AuditLogs` — address and merge before/after evidence.

One new migration is required:

`database/migrations/1800_008_rate_manual_effective_dates`

Current migration state: **PREPARED ONLY / NOT EXECUTED**.

Static fail-closed validator:
`scripts/validate_uat_migration_1800_008.py`

The validator is executed by both Owner Pre-UAT CI and Post-UAT verification and locks the
prepared Up/Verify blobs plus the no-row-rewrite/manual-date trigger contract.

It replaces only the MileageRate series trigger contract. It performs no data rewrite and adds no
table/column. It must be executed separately under the existing migration governance before the
new explicit EffectiveTo UI is released to UAT.

## 5. Google Maps UAT configuration

### Server-side design

The API key is **never** embedded in frontend code or committed to Git.

Runtime application settings:

- `Providers__Route=Google`
- `Providers__Geocoding=Google`
- `GoogleMaps__ApiKey=<SECRET>`

Optional defaults are already in `backend/src/FieldVisit.Api/appsettings.json`:

- Routes base URL: `https://routes.googleapis.com`
- Geocoding base URL: `https://geocode.googleapis.com`
- timeout: 20 seconds
- maximum intermediate waypoints: 25

Routes call:
`POST https://routes.googleapis.com/directions/v2:computeRoutes`

Headers:
- `X-Goog-Api-Key`
- `X-Goog-FieldMask: routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline`

Geocoding v4 call:
`GET https://geocode.googleapis.com/v4/geocode/address/{encoded-address}`

Header:
- `X-Goog-Api-Key`

Vehicle mapping:
- Motorcycle → `TWO_WHEELER`
- Car → `DRIVE`

Google currently lists Taiwan (TW) as supported for two-wheeled Routes API coverage.

### Secret handling

For Owner UAT, a personal Google API key may be used temporarily, but:
- do not paste it into source, chat, issue text, or commit history;
- enter it directly into Azure App Service application settings;
- restrict the key in Google Cloud to **Routes API** and **Geocoding API**;
- use a dedicated company-owned key before production handoff.

The current main deployment workflow deploys application bytes but does not manage App Service
settings. Therefore enabling Google in UAT is an explicit environment configuration step, not a
source-code step.

## 6. Mileage behavior

Normal path:
1. Visitor submits a >=2-stop trip.
2. Background mileage job builds basis from submitted snapshot and deployment-site start/end.
3. Google route succeeds → source `GoogleMapsAPI`, distance shown to Leader.
4. Leader approves provider suggestion or adjusts distance; approval evidence is stored.

Fallback path:
1. Google route fails.
2. If Visitor supplied positive manual fallback → source `ManualFallback`, Leader may approve.
3. If no manual fallback exists → Trip is returned with instruction to add fallback mileage.

Google provider output is not treated as company-approved mileage until Leader approval.

## 7. Personnel behavior

For users linked to v1.8 Employment:
- current access status is resolved from `EmploymentStatusPeriods`;
- HireDate in the future is treated as PreHire;
- TerminationDate on/before business date is treated as Terminated;
- current Leave / Terminated / PreHire blocks login server-side;
- legacy `UserEmploymentPeriods` is only a compatibility fallback for legacy users without EmploymentId.

UI separates:
- HR facts: EmployeeNo, DisplayName, Email, HireDate, TerminationDate, effective-dated status history;
- Access facts: roles/team scopes/admin enabled.

## 8. Location duplicate / merge semantics

PRE-UAT-012 is fail-closed and human-reviewed:
- create/edit automatically compares TaxId, location name, address and Plus Code; normalized small text variations are also candidates;
- a single matching field only marks **suspected duplicate** and never auto-rejects, auto-deletes or auto-merges;
- suspected duplicate remains a publish gate until Admin review;
- Admin has three explicit outcomes: **confirm different locations**, **use existing master unchanged**, or **merge with field-by-field survivor choices**;
- a “different location” decision is persisted with both record signatures; unchanged pairs are not repeatedly prompted, but a later master-data change invalidates that evidence naturally;
- merge preview shows source/survivor side-by-side plus Trip, Snapshot, Project, Favorite, note-history, government-master and current/future deployment-site impact;
- current/future deployment-site dependency blocks merge;
- source Location is retained as historical evidence and marked duplicate/inactive;
- Trip/Stop/Snapshot historical Location references are not rewritten;
- current operational Project/Favorite/Government references follow the survivor;
- survivor maintenance view includes source note/audit evidence, preserving original location identity, author and timeline;
- trip query by the survivor master can resolve historical rows that still hold the source LocationId;
- merge AuditLog records source/survivor before/after master values, mode, reason, actor/time and rebound counts.

## 9. Runtime gates still required

Before Owner tests the new package in UAT:

Application-source gate is satisfied at `f15812111d2bca4289a33e535fd7f11d5accae3d`
with Post-UAT Run `36972456821 = SUCCESS`.
Controlled migration-tooling gate is satisfied at `f6a0d603bb022ef066fb30115448fa6e01fbbccb`
with Owner Pre-UAT Run `36972923304 = SUCCESS`.

Controlled 1800_008 artifacts:
- `.github/workflows/azure-sql-uat-migration-1800-008.yml`
- `database/migrations/security/uat/Grant-gh-fieldvisit-uat-migrate-1800_008.sql`
- `database/migrations/security/uat/Verify-gh-fieldvisit-uat-migrate-1800_008.sql`
- `database/migrations/security/uat/Revoke-gh-fieldvisit-uat-migrate-1800_008.sql`
- `scripts/validate_uat_migration_1800_008.py`

1. **Promote the final documentation/tooling lineage** by fast-forward only after its exact Owner Pre-UAT validation succeeds.
2. **Execute migration 1.8.0-008** only through the controlled workflow after exact-SHA approval, least-privilege elevation, and the explicit `WRITE_QUIESCENCE_AND_RECOVERY_READY` gate; verify schema becomes 1.8.0-008 and STOP_FOR_REVIEW on drift/failure.
3. **Deploy backend + frontend** from the exact final promoted SHA.
4. Configure Azure UAT App Service settings:
   - `Providers__Route=Google`
   - `Providers__Geocoding=Google`
   - `GoogleMaps__ApiKey=<secret>`
5. Restart/verify API readiness.
6. Run nondestructive smoke.
7. Run Owner Pre-UAT focused scenarios:
   - Motorcycle Google route;
   - Car Google route;
   - forced Google failure + manual fallback;
   - Location note/address/TaxId audit;
   - Location auto duplicate flag → Admin different/use-existing/field-select merge; verify source history remains queryable through survivor;
   - Leave/Termination login denial;
   - explicit rate date overlap rejection;
   - Leader batch trip approval;
   - Admin batch correction close.

Do not distribute to Business Users until Owner explicitly closes Owner Pre-UAT.

## 10. IT maintenance principles

- One consolidated branch; domain-oriented commits.
- No secret values in source.
- Reuse existing schema and services; no parallel HR/Location/Google models.
- One new DB delta only, because PRE-UAT-013 conflicts with an existing database trigger.
- Historical Trip/Snapshot evidence is immutable.
- Production activation remains a separate IT-approved activity.
