# 1700_002_location_scale

## Purpose

v1.7 Phase 2 Location Scale foundation.

This migration introduces a separate government/open-data candidate layer and
personal Location favorites without changing the existing trip, mileage,
approval or approved Snapshot models.

## Tables

- `GovernmentLocationSources`
- `GovernmentLocationSourceAreas`
- `GovernmentLocationMasters`
- `UserFavoriteLocations`

## Source-of-truth rule

`GovernmentLocationMasters` is **not** the application Location source of
truth.

Government/open-data changes must be cached and marked for review. They must
not automatically overwrite:

- `Locations`
- `VisitTripStops`
- `VisitTripSnapshots`
- `VisitTripSnapshotStops`

A later application workflow may explicitly review/match a government
candidate to an existing or new `Location`.

## Service-area rule

Government data coverage is explicitly configured through
`GovernmentLocationSourceAreas`.

v1.7 must not assume every source covers all of Taiwan.

## Favorites

`UserFavoriteLocations` is personal-only:

- unique by `UserId + LocationId`
- ordered by `SortOrder`
- never shared across users

Recent locations are intentionally **not** stored in a separate table; they
will be derived from the user's actual trip history in Phase 2C.

## Deployment state

**NOT YET APPLIED.**

Do not execute this migration in Azure SQL until:

1. Phase 2 backend foundation has built successfully.
2. Unit tests have passed.
3. UAT preflight has passed.
4. `1.7.0-001` is confirmed in `SchemaVersions`.

After this migration is first applied to UAT it becomes immutable. Any later
database change must use a new forward migration.
