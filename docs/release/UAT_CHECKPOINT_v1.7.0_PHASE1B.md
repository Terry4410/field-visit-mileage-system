# v1.7.0 UAT Checkpoint — Phase 1B

Date: 2026-08-14
Branch: v1.7.0-uat-candidate
Commit: 8b0d384
Application Version: 1.7.0-uat-candidate
DB Schema Version: 1.7.0-001

## Database

- Migration `1700_001_identity_access` applied to Azure SQL UAT
- Migration Verify: PASS
- SchemaVersions `1.7.0-001`: PASS
- Users.EmployeeNo nullable: PASS
- EmployeeNo filtered unique index: PASS
- Six v1.7 identity/access tables: PASS
- UserDataScopes target constraint: PASS
- Constraint enabled/trusted: PASS
- Invalid UserDataScope rows: 0
- Identity backfill: Users 10 / IdentityProfiles 10
- RoleAssignments: 10
- TeamAssignments: 12
- DataScopes: 2
- Capabilities: 0 (expected; explicit Supervisor export permission model)

## Application

- Full API Build: PASS
- Unit Tests: 30 / 30 PASS
- GitHub Actions Run: #16 PASS
- Azure App Service deployment: PASS

## Runtime Smoke Test

`/health`
- status: ok
- version: 1.7.0-uat-candidate
- schema: 1.7.0-001

`/health/ready`
- status: ready
- version: 1.7.0-uat-candidate
- schema: 1.7.0-001

## Migration Policy

`1700_001_identity_access` is now deployed and immutable.

Any subsequent database change must use a new forward migration.
