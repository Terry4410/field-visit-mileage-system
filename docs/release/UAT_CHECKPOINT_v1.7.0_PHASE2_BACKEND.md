# v1.7.0 Phase 2 Backend UAT Checkpoint

Date: 2026-08-14

Branch: `v1.7.0-uat-candidate`

Deployment Commit:

`847c5e4 chore: prepare v1.7 location api UAT deployment`

## Status

**PASS — Backend/API functional checkpoint**

This checkpoint covers the v1.7 Location Scale backend and Azure SQL
integration. It is not the final v1.7 end-user UAT checkpoint.

## Database

Migration:

`1700_002_location_scale`

Status:

- Azure SQL UAT preflight: PASS
- Migration applied: PASS
- Verify.sql: PASS
- Schema version: `1.7.0-002`
- Migration state: FROZEN / IMMUTABLE

Created tables:

- `GovernmentLocationSources`
- `GovernmentLocationSourceAreas`
- `GovernmentLocationMasters`
- `UserFavoriteLocations`

Initial Verify counts:

- Sources: 0
- SourceAreas: 0
- GovernmentLocations: 0
- Favorites: 0

The migration must not be edited or re-executed.
Any subsequent database change must use a new forward migration.

## Build and Tests

- Full API Build: PASS
- Application Unit Tests: 52 / 52 PASS
- Failed: 0
- Skipped: 0

Known non-blocking warnings:

- `ReportDocumentService.cs` CS8604
- `V170InternalUserAccessRulesTests.cs` xUnit2031
- GitHub Actions Node.js runtime deprecation notice

## Azure UAT Deployment

Workflow:

`Build Test and Deploy API to Azure UAT`

Run:

`#17`

Result:

PASS

Runtime:

- `/health`: PASS
- `/health/ready`: PASS
- App version: `1.7.0-uat-candidate`
- DB schema: `1.7.0-002`

## Location API Smoke Tests

### Search

`GET /api/v1/locations/search`

PASS

Confirmed:

- Server-side paging
- Active + Approved Location filtering
- Organization / Team access scope
- Keyword search
- Azure SQL execution

Keyword test:

`中寮`

Returned:

`中寮就業服務台`

### Favorites

Endpoints tested:

- `GET /api/v1/locations/favorites`
- `POST /api/v1/locations/{locationId}/favorite`
- `DELETE /api/v1/locations/{locationId}/favorite`

Test Location:

`LocationId = 48`

Result:

- Add: PASS
- Read: PASS
- Delete: PASS
- Read after delete: PASS

This confirms actual Azure SQL read/write against
`UserFavoriteLocations`.

### Recent

`GET /api/v1/locations/recent`

PASS

Confirmed real Azure SQL execution of:

`VisitTripStops`
→ `VisitTrips`
→ exclude Cancelled
→ group by LocationId
→ latest VisitDate
→ Accessible Locations

Example returned Location:

`信義就業服務台`

### Nearby

`GET /api/v1/locations/nearby`

Functional PASS

Test used the stored coordinates of:

`信義就業服務台`

Result:

`distanceKm = 0`

Confirmed:

- Coordinate validation
- Accessible Location filtering
- SQL candidate ordering
- Haversine distance calculation
- Top-N result
- No hard 5 km radius
- No user GPS persistence

## Known UAT Limitation — Geocoding

Real-world coordinate accuracy has NOT yet been validated.

Current runtime still uses the Mock geocoding implementation.

Therefore the Nearby checkpoint confirms API / SQL / distance-calculation
behavior, but not production-grade address-to-coordinate accuracy.

Production Geocoding Provider activation requires a later IT / integration
gate and separate geographic accuracy UAT.

## Protected Core

Phase 2 backend did not modify the protected Trip / Mileage / Approval /
Snapshot core workflow.

## Next Phase

Proceed to:

- Phase 2E — Smart Location Picker UI
- Phase 2F — Visitor integration

Frontend integration must consume the v1.7 APIs instead of loading the full
Location master into the browser.
