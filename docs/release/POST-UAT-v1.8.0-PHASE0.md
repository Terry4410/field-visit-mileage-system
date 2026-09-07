# v1.8.0 Phase 0 — baseline inspection and impact plan

Date: 2026-09-07. Status: pre-development verification; no Epic is complete.

## Authorization and sole business baseline

Business baseline: uploaded 外訪系統_v1.8.0_Post-UAT_Requirement_Freeze_Impact_Analysis(1).docx.
The user's current development instruction approves progressing beyond the document's Draft / discussion-only stage. It does not change the frozen business rules or expand functional scope.
Repository baseline: v1.7.0-uat-candidate at 2fc51db91922ff95b605eec3b8127cd7b8c4e641, confirmed against GitHub branch metadata and a local clone.
New branch: post-uat/v1.8.0, created from that exact commit.
No main or baseline changes, no Production deployment, no merge to main.

## Inspection evidence and validation boundaries

- Repository tree: 206 tracked files, no AGENTS.md.
- Frontend: React 18.3.1 / TypeScript / Vite; local npm ci, npm test and npm run build passed at the unchanged business baseline: 11 files / 45 tests.
- Backend: ASP.NET Core net8.0; Application / Domain / Infrastructure / API projects. EF Core SQL Server mapping.
- Backend test source: 25 test files, 130 Facts and 9 Theories with 32 InlineData rows; estimated 162 cases. Actual executed count must come from TRX.
- Local .NET SDK is unavailable. SDK download ended with network approval cancellation. Use isolated GitHub-hosted verification without Azure credentials, DB access or deployment.
- No Actions run was returned for the exact baseline SHA at inspection time. Success on another commit or a packaging-only workflow is not evidence that this baseline passed backend or lifecycle tests.
- Playwright defines 9 smoke cases (health, 7 account navigation checks, one 390x844 viewport shell check), and 1 write/cleanup lifecycle case; only Chromium is configured. Missing demo password causes 8 smoke cases to skip. Skips cannot count as passed UAT.
- Existing lifecycle uses visitor01 / leader01 / admin01 / gov01 and creates then deletes its marked test trip, including its generated snapshots. It must never target existing business snapshots.
- Current Google route and geocoding providers are Mock. Live Google support/fallback has not been verified.
- Latest repository migration: 1700_008_location_promotion_history_action, using dbo.SchemaVersions and prerequisite 1.7.0-007. Pilot manifest reports historic 1.7.0-008 verification, not a current DB observation.
- Live UAT schema version, database identity and historical row fingerprints remain unverified.
- Existing API and Pages workflows trigger on main or manual dispatch; API deployment uses publish profile, not a SQL migration identity. Neither applies migrations.
- README / SCHEMA_MAPPING / deployment docs retain older v1.5/v1.6 wording; pilot manifest and package metadata differ from visible v1.7.2 business release. Do not use those labels to override the approved commit.

## Planned changes and dependencies

Paths below are proposed, not implemented files. Names may be refined without changing scope.

| Epic | Existing files principally affected | Proposed new files / capabilities | Dependency and regression gate |
|---|---|---|---|
| A Query & low-risk UX | frontend/src/pages/{UnifiedQueryPage,AdminPage,TeamManagementPage,VisitorPage}.tsx; frontend/src/{App,v160,types}; backend Application V160FinalContracts/Interfaces/Service; Infrastructure V160FinalRepository; API V160FinalController | query date/debounce helpers and tests; paged query contracts; VisitType reorder endpoint | Baseline build/tests first. No schema mutation. Keep historical query/export and authorization. Fields introduced by B/D gain their filters in their owning Epic. |
| B Organization / People / Team | Domain Entities/V170IdentityAccessEntities; AppDbContext; V170PeopleAdmin contracts/service/repository/writer/bulk; AuthService/Auth/AccessControl; TripService; TeamManagementPage/AdminPage | Center, Person, Employment, status periods, team-center history, leader/delegation services/controllers/UI and tests | A passed; migrations 001 then 002. Preserve Users and historical identities; never merge by matching names. Validate as-of dates, overlap, concurrency, same-day primary uniqueness and organization scope. |
| C Deployment Site | TripService/contracts, VisitorPage, AppDbContext, snapshot repository/entities, reports | site master, team-site and employment-site assignments, picker, as-of resolver and tests | B passed; migration 003. No fabricated legacy Center/Site attribution. Submitted/approved historical evidence preserved. |
| D Location / Project / Rate | Location APIs/repositories/picker; MasterAndReportServices; WorkbookImportService; AdminPage; mileage rate repository | TeamLocationNote/audit, duplicate warning, preview/confirm flows, effective rate validation and tests | C passed; migrations 004 then 005. No location merge or transaction cascade; same legal entity may have multiple locations. |
| E Email notification framework | transaction services, DI/hosted worker, Admin navigation | settings, transactional outbox, delivery log, fixed templates, whitelist policy and tests | D passed; migration 006. Uses B leader/delegation/employment data and C/D events. Commit transaction/outbox atomically; delivery failure never rolls back successful business transaction. |
| F Google mileage | Providers, BackgroundJobService, Trip/Leader services, correction contracts/repository, Visitor/Leader UI, snapshots | single-route Google provider, snapshot route basis, fallback/retry/invalidation policy and tests | E passed; migration 007. Depends on C submitted stop/site basis, D vehicle/date rates, E events. IT-managed credentials only; live TWO_WHEELER support requires verification. |
| G Supervisor retirement | App/SupervisorPage/login/people assignment endpoints and role validation; uat tests | retired-role rules and historical access regression tests | F passed. Remove new assignment and UI, preserve UserRole/Audit/Export/DataScope/history. Any persistent role-status change must be included in an approved versioned migration, never ad hoc SQL. |

Every Epic: impact analysis before changes, frontend build, backend build/test, regression, commit, evidence report. No next Epic until preceding gate passes.

## Required SQL ordering

| Sequence | Versioned migration folder | Prerequisite |
|---|---|---|
| 1800_001 | organization_center_team_lifecycle | Live baseline schema verified; expected 1.7.0-008 |
| 1800_002 | person_employment_role_membership | 1800_001 |
| 1800_003 | deployment_sites | 1800_002 |
| 1800_004 | location_governance | 1800_003 |
| 1800_005 | project_visit_rate_lifecycle | 1800_004 |
| 1800_006 | notification_framework | 1800_005 |
| 1800_007 | mileage_google_governance | 1800_006 |

Every folder requires Up.sql and Verify.sql. Rollback.sql only where genuinely safe. Do not replay historic migrations/seeds/cleanup. Use explicit version ledger ordering, fail on missing predecessors or changed applied checksums. No existing approved snapshot backfill that changes its contents.

## Azure SQL migration in UAT Actions — feasible, prerequisites unresolved

Proposed sequence: build/tests → UAT identity/schema preflight → migration Up + Verify in fixed order → historical compatibility verification → API and frontend deployment from the same SHA → Playwright/mobile/role/snapshot/fallback checks.

- Separate UAT environment and fixed allowed repository/branch, subscription/resource group/server/database; validate actual target with Azure metadata and SQL DB_NAME(), not a name suffix alone.
- Prefer Entra federated OIDC identity scoped to this repository's UAT environment. Azure resource permissions and SQL database principal/DDL permissions must both be established. Existing App Service publish profile does not authorize Azure SQL migration.
- Use the existing approved DB network route. Never enable public access, broaden firewalls or disable TLS/certificate validation to make CI pass.
- Migration job serialized with no cancellation mid-migration; lock/check schema version. SQL errors and Verify failures must fail the job and block deployment.
- Capture pre-existing Trip/Snapshot/SnapshotStop keys and content fingerprints, then verify unchanged historical records, FK/index/check constraints and period overlaps. Counts alone do not prove immutability. Protect concurrent changes with a consistent verification boundary.
- No automatic destructive change or unsafe rollback. Stop for the user's approval when an actual migration entails destructive data change.
- Keep credentials in approved secret storage only; do not log connection strings, tokens or personal data. Publish redacted result summaries and version evidence.
- The current repository does not include a full v1.5 creation baseline; a disposable SQL integration DB cannot be honestly claimed without an approved schema fixture or sanitized baseline.
- Production and main deployment remain outside this work.

Sources:
- https://learn.microsoft.com/en-us/azure/azure-sql/database/connect-github-actions-sql-db?view=azuresql
- https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure

## Outstanding access and IT/business decisions

Current tools can access GitHub. No Azure connector was found in plugin discovery. Azure login/approved SQL execution route still required.
Frozen document section 14 defers actual vehicle rates, Google project/billing/quota/key, email provider/sender/text, code naming convention and log retention to IT/Business. Do not invent operational configuration. Framework development can proceed independently where these choices do not affect frozen rules.
