# Path B VIS01 — source-bound draft create runtime SQL permissions (NONDEPLOY REVIEW)

**Accepted source:** `d6c220877b9976b6478d85b5171d4e06293595ae`.
**Live state:** pilotv01/pilotl01/pilota01 login PASS, API /health/ready PASS (2026-11-19), existing eight login SELECT GRANT catalog PASS; `TRIP_CONTEXT_EMPLOYMENT_MISSING` blocks VIS01. This matrix is CODE ANALYSIS ONLY; no Azure runtime privilege inventory or new DML/GRANT approval.

## Draft-create path: minimal SQL ACL
`backend/src/FieldVisit.Application/TripService.cs` CreateAsync lines 24-82:
* ResolveAsync uses `V180TripContextReader.cs`: 10 context tables already in locked context ACL; existing UserIdentityProfiles, Users, Teams already covered by prior live eight-table grants.
* CheckOverlapAsync -> TripRepository.FindOverlapsAsync (Repositories.cs 388-394) SELECT `VisitTrips`.
* BuildStopsAsync -> GetOperationalLocationAsync: SELECT `Locations` and, **only where present and enabled**, optional `LocationCreateReservations`, plus SELECT `SchemaVersions` to distinguish installed optional contract; if populated Stop has ProjectId/VisitTypeId, SELECT `Projects`/`VisitTypes`.
* INSERT new draft `VisitTrips` plus `VisitTripStops`, EF `SaveChanges`.
* INSERT `MileageCalculations`, append `VisitTripStatusHistory` and `AuditLogs`, second `SaveChanges`.
* GetDtoAsync -> SELECT `VisitTrips`, `VisitTripStops`, `Locations`, `MileageCalculations`; profile/team already known and context reader invoked for display. No route snapshot SELECT for Draft. **Important: SQL Server real behavioral test proved that EF-style INSERT ... OUTPUT INSERTED.Identity on history/audit tables raises SQL 229 with INSERT-only; restrict SELECT to only their identity columns, not entire tables.**

| dbo table | SELECT | INSERT | UPDATE | DELETE | Reason |
|---|---|---|---|---|---|
| VisitTrips | YES | YES | NO | NO | overlap, draft create, immediately read |
| VisitTripStops | YES | YES | NO | NO | draft stops and GET include |
| MileageCalculations | YES | YES | NO | NO | new calculation and GET include |
| VisitTripStatusHistory | Identity column only | YES | NO | NO | EF OUTPUT INSERTED.VisitTripStatusHistoryId; no full-row SELECT |
| AuditLogs | Identity column only | YES | NO | NO | EF OUTPUT INSERTED.AuditLogId; no full-row SELECT |
| Projects | YES | NO | NO | NO | optional project selection and validation |
| VisitTypes | YES | NO | NO | NO | optional visit type selection and validation |
| SchemaVersions | YES | NO | NO | NO | optional location-reservation schema detection |
| LocationCreateReservations | CONDITIONAL | NO | NO | NO | only if canonical optional reservation table truly exists and enabled |
| ProjectLocations | OPTIONAL | NO | NO | NO | only project-linked locations UI; not required for bare draft |
| MileageRateRules | OPTIONAL | NO | NO | NO | mileage fee/rate UI; not required for bare draft |

**Not included in VIS01 base-create grant:** `UPDATE/DELETE` on trip/stop/calculation, background processing, financial approvals, snapshots, worker tables. Add *only after observed UAT behavior or confirmed next-case source requirements.* This limits scope and shortens owner Azure window.

**Important caveats:** Run-time EF filtered indexes, database triggers, schemas, column permissions, optional tables, and App identity-API token effective privileges are not certified by this source-only inventory. A draft can require additional permissions if selected workflow differs (temporary location, submitted flow, post-create route). Never elevate to db_owner/db_datareader/db_datawriter to suppress SQL229. These rights must be granted only to existing contained MI principal after dual Object/Client ID gate, SQL target/TLS guard, catalog rights inventory, no prior partial grant, role/DENY closure, and separately approved exact SHA. No Azure call in this review.

**Separate P0 prerequisite:** minimal synthetic VIS01 context seed already scoped and SQL2022-disposable tested in this Draft PR; review-only file starts with unconditional THROW and is NOT for Azure execution. Preserve original 20-row Login Seed.
