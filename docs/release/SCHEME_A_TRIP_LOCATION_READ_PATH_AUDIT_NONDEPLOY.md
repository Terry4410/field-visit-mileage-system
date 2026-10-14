# Scheme A — Trip, mileage, report and export Location read-path audit

Status: **PARTIAL SOURCE REVIEW / HOLD / NO-GO**. This is a source-path
inventory, not exhaustive Browser/HTTP/SQL E2E or UAT execution evidence.

| Surface | Observed source guard | Remaining proof |
|---|---|---|
| Location Picker/search/nearby | `V170LocationRepository.AccessibleLocations` filters IsActive + Approved and organization/team; isolated HTTP/SQL test denies unassigned `teamId=999` on Search and Nearby and excludes a private reservation | PASS for pending reservation and unassigned-team cases in CI `38629143867`; full cross-org/valid cross-team/Project matrix remains |
| Managed Location list/search | `V160FinalRepository` uses `V180OptionCCreateReservationGuard.LegacyVisibleLocationsAsync` | HTTP privacy and pagination across all roles |
| Dashboard pending Location count | `V160FinalRepository.DashboardAsync` uses `LegacyVisibleLocationsAsync`; Recovery HTTP/SQL E2E proves a private pending reservation adds no count while one deliberately inserted legitimate legacy draft remains counted | PASS in exact-SHA CI `38629143867`; other dashboard role scopes still need broader matrix |
| Trip Context/One-stop | `V180TripContextReader` rejects inactive or non-Approved Location when resolving project/official assignment | Real Trip/One-stop submission and mileage negative matrix |
| Background geocoding | `BackgroundJobService.PendingDraftGeocodingCandidates` excludes canonical reservations where installed | Concurrent job/legacy mutation E2E |
| Historical Trip/Snapshot | `V160FinalRepository` retains stored stop/snapshot fields, with LocationId references | Historical name/address rendering after v2/v3 publication |
| Excel published Update | Preview captures source RowVersion; canonical submit creates private Stage and review request; legacy Confirm denies published writes | Update-only multirow, revocation, replay and final SQL E2E |
| Admin reports / exports | No claim of exhaustive review | Enumerate every report/export Location query and verify privacy and historical rendering |
| Trip create/edit/submit/mileage fallback | No claim of exhaustive review | Real HTTP/DI/SQL negative matrix, Google Maps success/fallback and snapshot identity |
| Project assignment/favorites | Picker filters active Approved; legacy reservation guards exist on specific write paths | Exhaustive assignment and favorites create/update HTTP matrix |

## Mandatory invariants

- A private canonical Pending/Abandoned Create reservation is never
  selectable, included in operational mileage routes, editable through
  legacy maintenance or counted as an actionable legacy pending draft.
- Approved/Active published Location is usable only within authorized
  organization/team scope.
- Historical TripStop/SnapshotStop LocationId and frozen display fields
  remain unchanged across Create/Update/Merge publication.
- A legitimate unreserved legacy draft remains visible to authorized
  maintenance and geocoding workflows, not to operational Picker.
- Direct Excel Confirm, background geocode or legacy update cannot
  publish canonical protected changes.
- Missing additive catalog/schema never silently relaxes authorization.

**Blocking evidence:** Search/Nearby and Dashboard reservation privacy now have real HTTP/SQL coverage for tested scopes. No exhaustive Trip create/edit/mileage/fallback,
Project assignment, report/export or cross-org/team Browser/SQL E2E has
been completed in this increment. Retain HOLD / NO-GO until proven.
