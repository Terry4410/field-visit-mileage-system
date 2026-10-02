# Field Visit v1.8.0 — Owner Pre-UAT Change Map / IT Handoff

Status: **SOURCE IMPLEMENTED / OWNER PRE-UAT RUNTIME NOT YET RELEASED**

## 1. Control boundary

- Repository: `Terry4410/field-visit-mileage-system`
- Authoritative pre-change baseline: `post-uat/v1.8.0 @ c51e91c7c0e672f749271c6768bd169889d7164c`
- Consolidated work branch: `work/v180-owner-preuat-consolidated`
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

Owner Pre-UAT validation workflow:
`.github/workflows/owner-preuat-v180-verify.yml`

Latest domain validation before final DB-contract package:
- Run `36968794895`: **SUCCESS**
- frontend tests: PASS
- frontend build: PASS
- backend build: PASS
- backend regression: PASS

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
| PRE-UAT-012 | Duplicate candidates + Admin preview/confirm merge; historical Trip/Snapshot not rewritten | Reuses 1.8.0-004 duplicate marker + AuditLog |
| PRE-UAT-013 | Admin owns vehicle/start/end/rate/rule; no neighboring date rewrite | **Requires 1.8.0-008** |
| PRE-UAT-014 | Single + selected batch correction close; stale/mixed state blocks before batch | Existing correction schema |

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

## 8. Location merge semantics

Admin merge is conservative:
- requires survivor selection, preview and explicit reason/confirmation;
- source Location is retained as historical evidence and marked duplicate/inactive;
- Trip/Stop/Snapshot historical Location references are not rewritten;
- current/future deployment-site dependency blocks merge;
- merge is recorded in AuditLog.

Historical note/history evidence remains attached to the source record so original authorship and
timeline are not destroyed.

## 9. Runtime gates still required

Before Owner tests the new package in UAT:

1. **Promote source** only after final consolidated CI passes.
2. **Execute migration 1.8.0-008** under separate controlled authorization; verify schema becomes 1.8.0-008.
3. **Deploy backend + frontend** from exact promoted SHA.
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
   - Location duplicate preview/merge;
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
