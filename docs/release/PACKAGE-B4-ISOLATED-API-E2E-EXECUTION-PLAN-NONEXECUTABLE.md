# Package B4 — isolated HTTP/API E2E plan (NONEXECUTABLE)

**Issue #24 | WORK-only handoff | Authoritative Protected Source SHA: 68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2.**

This checklist is NOT executed evidence, a workflow, executable migration, authorization to flip the B3 flag, or an UAT/Production GO decision.

## Prerequisites / separation of duties

- B3 feature flag stays **OFF** on all deployed/UAT environments. Approval and Apply remain **DENY ALL**. No 1800_011 formal migration is authorized.
- Existing Owner SQL permission covers **ephemeral localhost SQL Server 2022 fixture only**. An HTTP harness requiring B3 enablement/011 must receive its own environment/Owner approval. Do not interpret this document as approval.
- Independent IT/Owner Manager Grant provenance **VAL-B1-002 BLOCKED**; no historic TeamLeaderAssignments or membership-derived manager powers.
- API tests use disposable identities and location records in a separately approved isolated environment; never use UAT or Production personal records. Authenticate through the real middleware and assert HTTP codes **and** database after-state. No test may simulate permissions by only changing a JWT claim.
- Keep request ID, actor ID, HR/role/team source effective times, Organization, target Location Owner, before/after RowVersion, audit/Event counts, response code, correlation, SHA and TRX/JUnit for each case.
- Failure to verify HTTP middleware, authorization, actor provenance or DB after-state => **HOLD**, even when unit or SQL tests pass.

## Phase 0 — permitted while B3 OFF (no database needed)

| Ref | Endpoint/action | Required status and evidence |
| --- | --- | --- |
| API-OFF-01 | POST locations, visitor and leader | Authenticated call returns 503 B3_DISABLED, no mutation |
| API-OFF-02 | GET mine | 503 B3_DISABLED; cannot expose pending JSON |
| API-OFF-03 | GET admin/pending | 503 B3_DISABLED; admin claim cannot read queue |
| API-OFF-04 | POST admin/{id}/reject | 503 B3_DISABLED; no rejection/decision event |
| API-OFF-05 | POST admin/{id}/approve | 503 B3_DISABLED; no approved/applied event or published Location update |
| API-OFF-06 | Unauthenticated requests across all routes | 401, no DB activity |
| API-OFF-07 | Wrong-role authenticated calls | 403, no DB activity |
| API-OFF-08 | Login disabled/expired HR record | Middleware must enforce existing user status policy |

The current Controller reflection and direct disabled tests are **offline source evidence**, not execution of the eight HTTP scenarios.

## Phase 0 implementation candidate — actual HTTP pipeline, B3 OFF

The WORK-only .NET test class `V180B3OffHttpPipelineTests` uses the
application's own ASP.NET Core HTTP routing, JWT bearer authentication,
role authorization and Controller with B3 **explicitly OFF**. It runs on
the in-memory TestServer, with background SQL workers disabled and an
unreachable local-only SQL connection. No database is used or created.

It covers unauthenticated 401 (five routes), wrong-role 403 (five role
and endpoint combinations), authorized but feature-disabled 503
B3_DISABLED (seven combinations) and invalid/expired JWT 401 (two
combinations). Results are **candidate only until a green CI report**,
and do not establish actual deployed UAT configuration, live HR
employment grants, or B4 full 22-case acceptance. API-OFF-08
(authoritative HR middleware) remains an outstanding E2E scenario.

## Verified TestServer Phase 0 results (WORK only)

GitHub Run [38094069834](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38094069834), exact SHA `2ece32dac5b2111846c3de9ef7a3c3dab5d5addf`: 19/19 real HTTP JWT-route-middleware checks PASS in isolated in-memory TestServer, B3 OFF, no DB; 702/702 backend, 92/92 frontend, 65/65 Python and 42/42 separate disposable SQL Server fixture checks. Cases cover five 401 unauthenticated, five wrong-role 403, seven disabled 503 and two invalid/expired JWT 401. The next WORK CI adds a fail-closed TRX named-case evidence requirement so missing/replaced tests cannot be hidden by a passing backend total. API-OFF-08 (live authoritative HR revocation) is still pending, all 22 B4 full scenarios remain PENDING, VAL-B1-002 BLOCKED, 1800_011 unauthorized, Apply/Approve DENY ALL, Business UAT HOLD, Production HARD HOLD.

## Next WORK-only expanded security regression suite

The candidate adds nine HTTP TestServer cases: wrong JWT audience,
incorrect JWT signature, not-yet-valid token, malformed bearer token
(all expected 401), and invalid HTTP method requests on all five
B3 endpoints (all expected 405). The CI named-case manifest now
requires **28/28 individually named** B3-OFF HTTP tests, independent
of the aggregate Backend total. These tests exercise the real
ASP.NET Core middleware and routing against an in-memory host,
without a B3 schema, SQL access, external UAT, or feature enablement.
They do not establish that an authenticated HR-ineligible user is
denied by *live* HR middleware (API-OFF-08 still pending).

Additional EF InMemory tests exercise live HR/role revocation and
requester/admin tenant query projections. These remain non-SQL
service/predicate evidence, not UAT HTTP authorization or B4 sign-off.
All 22 full B4 scenarios remain PENDING and all release gates HOLD.

## SQL Server 2022 EF query-scope integration (WORK-only)

Five new actual SQL Server query/EF concurrency tests execute the **same**
`V180B3QueueScopeRules.ForRequester` and `ForAdminPending` LINQ
predicates used by the candidate service against the disposable B3 SQL Server
fixture, not EF InMemory. They test peer-proposal denial, cross-organization
and Rejected-row queue exclusion, refusal of invalid actor scopes, read-only
rowversion/audit invariance, and stale EF tracked-update concurrency without
audit insertion. The named CI SQL evidence manifest now requires **47**
individual real-engine cases. They provide stronger B4 partial query
translation and data after-state evidence, **not** live authenticated API
requests or HR/Owner provenance. The fixture's identity records are minimal;
there is still no full 011 migration, API token/session flow, B3 enablement
or Manager Grant authorization.

## Fourth WORK-only query and live actor regression batch

The candidate now adds four actual EF Core / SQL Server 2022 query-scope
regressions in the disposable localhost database: Rejected history visible
to its original requester while absent from Pending Admin queue; BeforeJson
and EvidenceJson isolation against same-team peer and foreign organization;
repeated organization switching across SQL queries to avoid stale scope
reuse; and Admin organization-wide pending visibility without granting a
visitor access to another person's Mine queue. The SQL evidence manifest
requires **51 named engine cases**, each with an individual TRX record.

Five additional EF InMemory / live actor checks cover account moving to a
different organization after an existing identity was established, a legacy
internal profile changed to External, future-dated Leave not prematurely
revoking today's eligibility, future-dated Active not prematurely granting
access, and spoofed user ID refusal. These are isolated code/data tests,
**not proof of an operating HTTP HR middleware with deployed databases**.

These fixture additions do not alter the B3 feature flag, UAT schema,
Approve/Apply DENY ALL, historical manager grants or the blocked
VAL-B1-002 gate. Full B4 22-case acceptance and API-OFF-08 remain pending.

## B4 evidence traceability crosswalk (CI generated; RELEASE HOLD)

A non-deploy GitHub Actions step consumes the already validated disposable
SQL Server TRX evidence from the **same** WORK commit. It produces
.bci-only independent traceability in two files within the SQL-only artifact:

- b4-sql-coverage.json — 22 B4 IDs, each with an explicit partial SQL
  fixture test linkage (actual test name, test ID and execution ID) or an
  explicit NO_DIRECT_SQL_FIXTURE gap; SHA and TRX SHA-256 are retained.
- b4-sql-coverage.md — readable table of missing API/HR/business validation
  and the separate Owner/IT authorization blockers.

These files are evidence classification, NOT execution of the 22 full B4
cases. A matching SQL fixture test may support only a narrow predicate
(e.g., same-team requester queue *read* filtering), NOT a peer location
*write* denial. The report hardcodes **formal_b4_accepted=0/22**,
full API/HR/Business E2E NOT EXECUTED and Business UAT / Production HOLD.
Missing, duplicated, failed, forged or UAT-scoped SQL evidence makes the
coverage generator FAIL CLOSED. B3 remains OFF and Approve/Apply DENY ALL.

## WORK-only real HTTP Demo Login + HR policy integration (B3 OFF)

The isolated ASP.NET Core TestServer executes **actual**
POST /api/v1/auth/demo-login -> AuthController -> AuthService ->
UserRepository -> V170AccessControl, with HR effective dates stored
in a private EF InMemory database. The happy-path returns a real
JWT that authenticates at GET /api/v1/me; negative routes return 403
and no access token. The 11 individually named cases cover
Leave, Terminated, PreHire, missing HR state, future/expired
Active, future Leave timing, re-login after HR changes, invalid password
and unknown identity. The backend TRX evidence aggregator separately
requires all **11/11** named HR HTTP cases; missing/failed cases fail CI.

This is **more than a mock of HR policy**, but the database is *not*
Microsoft SQL Server and the tests do *not* exercise existing UAT
accounts, a real deployed session after HR revocation, Entra sign-in,
HR period persistence against formal Schema 011 or the B3 change
endpoints with flag ON. API-OFF-08 formal scope remains PENDING;
B4 22-case formal acceptance is 0/22, Manager Grant BLOCKED,
formal migration NOT AUTHORIZED and Business UAT / Production HOLD.

## New WORK-only live HR session guard — independent activation HOLD

### Confirmed code-path defect and isolation scope

Login uses AuthService / V170AccessControl to check HR on JWT issue.
Current authenticated /api/v1/me only reads the user profile, not live
HR eligibility; middleware checks JWT cryptographic/expiry properties
but cannot invalidate JWT when an HR record changes. A 12-case isolated
TestServer suite intentionally shows default-OFF old-JWT /me succeeds
after HR Leave (the unmitigated gap), then checks the opt-in candidate.

### Non-deploy mitigation candidate and tests

A new V180LiveSessionGuardMiddleware is registered **after routing +
authentication and before authorization**. It is guarded by
Auth:LiveSessionGuard:Enabled, explicitly **false** in checked-in
appsettings and enforced by the WORK CI guard. It is **not deployed or
activated in UAT**, and does not enable B3. When the candidate is ON
only in a private TestServer, every authenticated /api/v1 request
rechecks the persisted account enabled state, matching token vs
persisted organization, and existing live V170AccessControl HR rules.
A failure returns 403; query exceptions never invoke downstream APIs.
Unrecognized JWT user claims fail closed. Anonymous endpoints are
not converted into privileged endpoints.

12 individually named HTTP tests cover (a) legacy default-OFF
gap; (b) live active JWT 200; (c) stale JWT after Leave, Terminated,
PreHire each 403; (d) disabled user; (e) changed organization; (f)
expired active period; (g) future Leave not yet effective; (h) revoked
JWT on B3 Mine 403 while active B3 OFF returns 503; (i) public health;
(j) unauthenticated /me remains 401. Dedicated Python tests reject
missing, duplicated or forged HTTP TRX case IDs/outcomes.

The CI TRX parser checks the original 11 HR login HTTP cases
(`HTTP_*`) and the new 12 already-issued JWT cases (`SESSION_*`)
as **two disjoint named test groups** even though both are in one
xUnit class; additional Python negatives enforce that neither group
can substitute for a missing case in the other.

### Independent Owner/IT impact and enablement gate

This is a **candidate**, not deployment clearance. Enabling introduces
a database access + HR policy check for each authenticated API call.
IT/Owner must independently approve its scale/load/latency and
database outage fail-closed behavior, user organization changes,
external/Entra identity compatibility, role/projection revocation
(which is separate from this HR check), instrumentation and regression
scope. A JWT revoked by HR should not be silently allowed if the
database cannot be queried; unavailable infrastructure may result in
an error response, requiring an explicit availability/SLA decision.

Existing tokens can still rely on their old Role claims on endpoints
that do not independently re-evaluate roles: the HR session guard
alone does not certify current-role or Manager Grant correctness.
API-OFF-08 and 22 B4 formal scenarios remain PENDING; 011 formal
migration NOT AUTHORIZED, B3 OFF, Approve/Apply DENY ALL,
VAL-B1-002 BLOCKED, Business UAT HOLD, Production HARD HOLD.

## Live JWT role invalidation (candidate ONLY, default OFF)

The isolated opt-in V180LiveSessionGuardMiddleware now requires the complete
JWT role claim set to remain valid against active current UserRoles and Roles.
For accounts with v1.7 Identity Profiles it further intersects the
effective-dated UserRoleAssignments, including Role.IsActive. Missing,
expired, removed or stale role grants deny the old token; the user must
obtain a fresh token after a legitimate change. The no-profile legacy
account path rechecks its existing active UserRoles projection without
inventing historical grants.

An Internal user converted to an External identity may not keep a stale
Visitor/Leader/Admin JWT even if a role projection accidentally remains.
The independently authorized External Supervisor path requires current
Supervisor role assignment and external authorization effectiveness. It
does not grant supervisor exports, business writes, special data scopes,
or historical Manager authority.

Nine actual ASP.NET Core + EF InMemory HTTP tests explicitly cover:
default-OFF stale legacy role gap; opt-in denied legacy revoked roles,
deactivated catalogue roles, expired effective assignments, removed
role projection, and Internal-to-External conversion; and positive
modern-Internal, valid External Supervisor and expiration of an
External authorization. GitHub CI fails closed unless every exact
named test is Passed with an individual testId/executionId. Negative
Python checks ensure a fake test set cannot substitute for actual
role/security HTTP TRX evidence.

IT/Owner deployment assessment remains independent. This candidate
adds multiple DB reads per authenticated request; benchmark realistic
concurrency, SQL timeout/failure responses, Entra claims and active-role
header behavior, team membership/primary-site scope, Supervisor data
scope and exports before enabling. No role or Manager grants were
invented, and this is not a substitute for original SQL Schema 011
or the B4 22-case live UAT. All 22 remain PENDING, API-OFF-08 PENDING,
B3 OFF, Approve/Apply DENY ALL, Production HARD HOLD.

## Legacy/modern team snapshot and Supervisor Data Scope revocation (default OFF)

The application JWT carries (1) primary team_id and (2) a serialized
team_scopes list of TeamId/TeamName/IsPrimary. Those are **membership**
snapshots, not External Supervisor read DataScopes. In a disabled-feature
legacy flow, a signed, previously issued JWT may continue to be
recognized after a same-team revoke/change.

The isolated opt-in V180LiveSessionGuardMiddleware now loads the
**current authoritative user profile** on authenticated API calls,
requires JWT primary TeamId and each TeamId/IsPrimary pair to match
the current effective/legacy membership, and denies:
- revoked team assignment or expired effective-dated membership,
- switched primary team or modified team set,
- disabled, cross-organization or out-of-date teams,
- malformed/duplicated team scope claims.
It deliberately **does not** treat a simple team display-name change
as a permission revocation. When an account has no team membership,
the established role/session/HR checks still apply; this check
does not invent any missing Manager/Team authority.

For External Supervisor JWT, no DataScopes are serialized into the
token. The opt-in guard therefore **requires a current, effective**
UserDataScope; each organization-wide scope must match the actor's
organization, each team scope must belong to an active effective team
in the actor's organization, and unknown/malformed grants fail closed.
Removing/expiring all scopes causes old JWT requests to fail.
Changing from an Organization grant to an authorized Team grant
remains permitted but /api/v1/me must return the **current Team scope**,
not the former Organization grant. Export/download permissions and
actual query row partitioning still require separate tests and
the existing per-operation server-side authorization.

There are 16 new, individually named real ASP.NET Core TestServer tests:
9 Team JWT (legacy and modern, revoked, primary switch, inactive,
cross-org, renamed and positive paths) plus 7 Supervisor DataScopes
(organization, revoked, expired, wrong org, valid team switch, foreign
team, disabled team). Python CI verifies 16/16 exact names, Passed,
unique TRX testId/executionId and protected WORK SHA. This is **partial
in-memory HTTP evidence**, not formal B4 acceptance or UAT proof.

### Primary deployment site remains a separate authorization gap

The JWT does **not** contain a primary deployment-site entitlement.
EmploymentDeploymentSiteAssignments, official sites and start/end site
picker use effective-date/business-context data. This session-guard
work does not claim those APIs are secure by comparing JWT claims.
IT should run independent endpoint tests for primary-site effective
periods, archived sites, cross-team/center access and stale trip
drafts before broader business acceptance.

### Activation impact analysis

Every opt-in authenticated API request now incurs extra read queries
for HR, role, teams and (when applicable) Supervisor scope. Before
any activation, IT/Owner must review SQL performance under realistic
concurrency, transaction/data-change races between successive reads,
timeout/error fail-closed behavior, external and Microsoft Entra
profiles, legitimate profile migration, CORS/middleware order,
auditability, and per-endpoint export/data-scope enforcement.

Default appsettings guard stays **OFF**; UAT/Production NOT deployed;
B3 OFF, Approve/Apply DENY ALL, 1800_011 NOT AUTHORIZED,
VAL-B1-002 BLOCKED, B4 0/22, Business UAT HOLD,
Production HARD HOLD. No source promotion is authorized.

## Dated official deployment-site runtime hardening — WORK-only

Added one fail-closed production-code condition to
V180TripPersistenceRules.ResolveDraftSites: if
V180TripContextDto.EligibleForTrip is false, the system cannot
create or edit a trip draft through this method, even when a
TeamId was previously resolved and a stale official site ID is
supplied. This protects a Team with no valid Team-Center mapping,
an inactive Center/DeploymentSite, expired Location assignment,
or a site removed after a previous draft. Submit-time validation
remains independent and is not weakened.

A private EF InMemory fixture executes the **actual**
V180TripContextReader, dated lookups, official site cross-tenant
filtering, and persistence rules (not a mocked reader). Exactly
20 named tests are required in the backend TRX evidence:
valid primary; official alternate of the same organization but
outside the employee-specific eligible set (approved behavior);
cross-organization official picker exclusion; future/inactive/
expired Site and Center; expired site→Location link; inactive/
unapproved Location; cross-org employee Site; missing Team-Center
refusal; expired membership; unassigned Team; ambiguous primary,
Location and Team-Center assignment; missing Employment binding;
HR Leave on visit date; stale draft override fallback; and
explicit ineligible-context draft refusal.

In addition to the 20 real Reader-level tests, a second suite
runs the actual ASP.NET Core authenticated
GET /api/v1/trips/context?visitDate=2026-10-12&teamId=7 endpoint
with real TokenService JWT, AppJwt authentication, middleware,
TripService, V180TripContextReader and independent EF InMemory rows.
Six named end-to-end TestServer cases verify: valid primary
deployment-site response; disabled/future official Site returns
EligibleForTrip=false; missing TeamCenter mapping returns an
explicit NO_ELIGIBLE_DEPLOYMENT_SITE response; HR Leave on
the *visit date* (while the JWT is still valid today) returns
EMPLOYMENT_NOT_ACTIVE; and another Organization's official
Site is omitted from the official picker. This is not a
production/Azure SQL Server integration test, nor does it test
draft persistence or Google mileage in a deployed UAT.

The CI artifact requires all **6 separately named Site HTTP**
cases AND the **20 Reader-level cases**, with unique per-test
TRX identity, exact SHA and passed status, and verifies that
one suite cannot impersonate the other. Both evidence layers
retain zero B4 formal acceptance and all business/production
release holds.

The actual /api/v1/trips/context HTTP pipeline backed by a
Microsoft SQL Server deployment is still **not tested** by
this fixture. DB RowVersion, deployment migrations, historical
draft table updates, end-to-end Google mileage, and real concurrent
admin changes must be separately verified before Business UAT.

Tests bind to a frozen explicit 20-name manifest with individual
passed outcomes and unique TRX test/execution IDs; missing/duplicate/
failed cases force FAIL_CLOSED. SQL Server 51/51 in a separate
disposable SQL job is not joint API/database acceptance.
Official picker remains legitimately organization-scoped;
never silently limit all officially authorized sites to the
employee's home site. B3 OFF, LiveSessionGuard default OFF,
1800_011 NOT AUTHORIZED, B4 accepted 0/22, Business UAT HOLD,
Production HARD HOLD.

## NEW disposable SQL Server 2022 deployment-site proof (NO MIGRATION / NO UAT)

A separate WORK-only SQL test class now uses the **actual EF SQL Server
provider** and the real V180TripContextReader rather than the EF InMemory
provider. Every case creates a random B4SiteSql_* database only on the
existing private localhost SQL Server container (127.0.0.1:14335, master,
ephemeral SA secret), creates the test EF schema with EnsureCreated,
seeds a completely isolated employment/team/center/site/location context
and drops its database. The source code rejects other SQL hosts/catalogs,
and the runner never accepts Azure/UAT/Production connection strings.
This is NOT 1800_011 migration execution.

Six separately named SQL cases verify (1) valid primary Site plus real
SQL-enforced cross-organization official picker exclusion; (2) site
disabled after prior context and stale draft-site rejection with no
VisitTrip/AuditLog created; (3) effective-dated team membership expiry;
(4) duplicate *overlapping* SiteLocation periods with different start
dates (real SQL unique start-key intact) fail closed; (5) expiry of
site-to-Location mapping removes its official picker entry; and
(6) SQL rowversion-concurrent Site edit rejects stale writer, keeping
the previous committed state. These cases are **reader/database
integration**, not authenticated HTTP+SQL integration, so no actual
deployed runtime or business GO is claimed.

GitHub SQL job deliberately **preserves the existing 51 original
named B3 fixture tests unchanged** and executes these six B4-site
SQL cases as a second independently named TRX within the *same*
disposable SQL Server container. Independent validator binds the six
case IDs/outcomes/durations to the exact WORK SHA, stores TRX SHA-256,
rejects missing/failed/duplicate tests and has separate Python
negative tests. Original 51-case SQL evidence, 22-case B4 SQL map and
the formal 0/22 B4 acceptance remain independent and unchanged.

### Follow-up before UAT

The next technical gate is actual authenticated HTTP + real disposable
SQL in one TestServer/SQL integration, including DB-read timeout
fault injection, Azure-style retry and telemetry. The previous six
authenticated HTTP trip-context cases use EF InMemory, so they are
**not** the same evidence as the new six SQL reader/database cases.
Measured load, historical trip draft state, official-site change race
with writes, Entra ID real identity and migration-owned actual
schema remain pending. No branch promotion, source merge,
Azure DB touch or B3 activation is authorized.

**Controls: LiveSessionGuard OFF, B3 OFF, Manager Grant BLOCKED,
Approve/Apply DENY ALL, 1800_011 NOT EXECUTED, B4 accepted 0/22,
Business UAT HOLD, Production HARD HOLD.**

## Real AppJwt HTTP plus disposable SQL Server — WORK candidate only

A standalone FieldVisit.B4.SqlHttp.Tests project connects the real
ASP.NET Core TestServer, Demo login, TokenService-issued AppJwt,
the opt-in Session Guard, TripService and the actual SQL-backed
V180TripContextReader on one SQL Server 2022 database per case.
The allowed connection is strictly local CI server 127.0.0.1:14335,
SA and master only. Each test creates and destroys a random
B4SqlHttp_* database via EnsureCreated. UAT/Prod/Azure are prohibited.

Twelve real JWT+SQL HTTP cases now run as an exact, independently
verified named suite. The original six include site context;
the same old JWT after disabling the SQL deployment site; removal
of a role projection (403); effective-dated Team grant revocation
(403); active HR -> Leave after issuance (403); and an injected
local-only SQL connection failure (generic 500, no secret leak,
health live 200, zero SQL trip/audit writes). The last case changes
only the test host scoped DbContext destination to unused local
port 14336, timeout 1 second. It is NOT real Azure outage
simulation; IT must separately decide timeouts, 503, telemetry,
retry, operational health and error response handling.

The disposable SQL workflow runs THREE independent suites:
B3 original 51 unchanged, B4 dated site Reader 6, and B4 actual
HTTP + SQL 6. All three produce separate named TRX and independently
verified source-SHA-bound evidence. Twelve additional Python negative
tests explicitly reject incomplete, forged, failed or swapped HTTP/SQL
evidence. Each test uses local randomized fixture rows only.

### Dated official-site draft lifecycle and concurrency (new tests)

Six new authenticated SQL HTTP test cases build on the prior six
Session Guard/SQL fixtures. An actual visitor POST /api/v1/trips
creates a persisted Draft plus MileageCalculation, history event and
audit; tests assert exactly one create-history and audit event.
After a new SQL admin change disables the official site, both
PUT /trips/{tripId} and POST /trips/{tripId}/submit are denied
with no alteration to the persisted draft, status, locations,
history or audit counts. A second update using a stale If-Match
RowVersion returns HTTP 409 without any extra audit/history.
Two independently authenticated clients competing on the exact
same RowVersion must yield exactly one successful PUT and one 409,
and exactly one committed update/audit/history (no partial writer).
Finally an invalid official Site ID in POST is denied without
writing a trip or audit.

These tests do NOT change production code or enable approval.
No historical Submitted/Approved snapshot recreation is claimed;
snapshot/geocoding, large data sets, high-concurrency stress and
migration-owned deployed Azure SQL remain separate IT acceptance.
The independent SHA+TRX validator rejects any partial or missing
12-case result. The 51 B3 SQL and 6 Site Reader SQL manifests
remain separate and unchanged.

This is partial SQL + HTTP security evidence, NOT approval of any
formal B4 acceptance case, business testing, Entra integration,
real Azure schema, concurrent full trip writes or UAT deployment.
Protected post-uat SHA remains frozen. B3/LiveSessionGuard default
OFF; Approve/Apply DENY ALL; Manager Grant BLOCKED; 1800_011
NOT AUTHORIZED; B4 formally accepted 0/22;
Business UAT HOLD and Production HARD HOLD.

## Real SQL HTTP Submitted Snapshot transaction and schema caveat

Four additional SQL Server 2022 + real JWT authenticated HTTP cases
cover: valid draft submit with at least one approved visit stop and
claimed mileage, asserting ONE Submitted Snapshot and ONE snapshot
stop plus persisted status/history/audit together; historical snapshot
site code and visit stop address remaining unchanged after later SQL
site/location rename; missing stops/mileage preventing submission with
NO snapshot/history/audit mutation; and stale RowVersion preventing a
Submitted snapshot after a legitimate prior draft update.

The canonical SQL HTTP suite now has 16 individually named cases,
bound to independent TRX identities and the exact WORK source SHA.
The earlier 51 B3 SQL and 6 site-reader SQL cases are unchanged.

**Schema compatibility boundary:** EF Database.EnsureCreated in the
disposable CI database does NOT run the migration chain
1600_001..1800_010, and does not create SQL objects solely defined
in migration scripts such as dbo.TR_DeploymentSites_CenterPeriod.
Historical SQL Server triggers, constraints, data backfills,
permission grants and deployment-owned checks may therefore differ.
The supported evidence is a contract test for the EF runtime model
only, NOT a UAT-schema parity certificate. A true upgrade-path
migration rehearsal on an authorized *disposable* migration
baseline remains pending; this is NOT permission to apply 1800_011.

All controls unchanged: LiveSessionGuard opt-in test only/default OFF,
B3 OFF; Approve/Apply DENY ALL; Manager Grant BLOCKED;
B4 formally accepted 0/22; Business UAT HOLD, Production HARD HOLD.

## Phase 1 — separately authorized isolated API + SQL runtime (future only)

| B4 IDs | Scenario cluster | Required proof |
| --- | --- | --- |
| 01–04,21–22 | Missing/drifted 011 and physical SQL safety | 503 readiness, no hidden DDL repair, no partial writes |
| 05–07,19 | Concurrent submits, DecisionKey replays, RowVersion errors | One committed Pending/Submitted or Rejected pair, 409 for named duplicates, unrelated SQL errors fail closed |
| 08–09,17 | Peer same-team ownership and cross-org IDOR | 403/404, no disclosure of ProposedJson/notes/audit and no mutation |
| 10–12,14,20 | HR lifecycle, effective grants, external projection and bulk revocation | 403 after authoritative revocation; **Manager Grant still BLOCKED** |
| 13,18 | Independent review and unsupported operation tampering | Reject self/forged target and unknown codes, zero unauthorized events |
| 15–16 | Geocoding race and safe deletion histories | No auto publish; referenced notes/snapshot/audit/deployment records cannot be erased |

Before any execution beyond Phase 0, independently approve the exact isolated environment, dataset, feature controls, test executor scope and revert plan. Approval/Apply **not** in scope. Retain B4 22/22 as PENDING until full API/business acceptance and Owner sign-off.

## Expected gate state after review-only handoff

- SQL Server ephemeral fixture may PASS while the API plan remains **NOT_EXECUTED**.
- VAL-B1-002 = BLOCKED.
- 1800_011 formal migration = NOT_AUTHORIZED / NOT_EXECUTED.
- B3 Enabled = OFF; Approve/Apply = DENY ALL.
- Business UAT = HOLD; Production = HARD HOLD.

**NONEXECUTABLE DOCUMENT — NO RELEASE AUTHORIZATION.**


## 2026-10-11 P1/P2/P3 continuation — static parity inventory, no Migration execution

**Evidence starting point:** WORK `a3ffe287b873ce84023b22d693523cf5b9beebb4`,
protected `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2`,
[last fully verified run 38109478440](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38109478440):
804 backend / 92 frontend / 156 Python / 51 B3 SQL + 6 site SQL + 16 real JWT/HTTP SQL.
This is the *pre-continuation* checkpoint; never cite it as evidence that this new parity script ran.

### P1 — scoped 1800_003 comparison matrix

`scripts/ci/package_b_schema_parity.py` inventories
`database/migrations/1800_003_deployment_sites/Up.sql` against
`backend/src/FieldVisit.Infrastructure/AppDbContext.cs` without connecting to SQL,
executing Migration DDL or modifying any database. CI emits a named JSON/Markdown
artifact tied to the running GitHub SHA. The script fails closed if the expected
DDL/EF mapping or known SQL trigger vanishes. Passing means the **inventory is
valid with known differences**, not that the schemas are interchangeable.

| Area | Static assessment | Why / remaining validation |
| --- | --- | --- |
| Four deployment-site tables (names) | MATCH | Table naming only; type/nullability/FK catalog proof remains required |
| Four RowVersion declarations | MATCH | EF concurrency token vs migration ROWVERSION; verify actual catalog and stale HTTP behavior separately |
| Four named unique keys | MATCH | Name presence only; verify key order/index implementations and constraints against SQL catalog |
| Four effective-date CHECK constraints | DIFFERENCE | Present in Up.sql; absent from EF EnsureCreated CHECK definitions |
| Seven migration-only triggers | DIFFERENCE | Center Period, Site-Location overlap, Team-Site coverage/overlap, Employment-Site overlap/Primary, and parent-update protections do not run under EnsureCreated |
| FK rules and included indexes | NEEDS VALIDATION | EF model and DDL need full column/type/FK/delete/index include comparison |
| 1600/1700/1800_001..010 chain | BLOCKED | No authorized disposable *migration-baseline* rehearsal yet; do not substitute EnsureCreated |
| Trip / stop / snapshot / audit / status history | NEEDS VALIDATION | Current SQL HTTP data-after-state checks do not establish full migration/backfill/history parity |

### P2 — transaction and race assessment (not yet full closure)

`TripService.SubmitAsync` calls the history, audit and Submitted Snapshot
repositories, then issues a single `uow.SaveChangesAsync`. The existing SQL
HTTP tests prove the happy-path and selected pre-save negative paths in the
EF-created fixture. They do **not** prove a rollback after an actual SQL
failure inside the final write, multiple-stop ordering, repeated/concurrent
Submit interleavings or the migration-defined snapshot/index invariants.
The prior stale RowVersion result is a helpful single-writer check, not
two competing Submit transaction evidence.

Separate attention: `TripService.CreateAsync` performs its initial
`uow.SaveChangesAsync` to obtain the trip identity **before** inserting
MileageCalculation, History and Audit in a second SaveChanges. Absent an
external encompassing transaction, a failure between writes may leave a
Draft without complete audit/history/mileage. This is a **risk requiring
isolation testing and an explicit transaction-impact review**, not a
claim that production has already lost data. Do not silently wrap it in
a new transaction without compatibility and retry review.

### P3 — SQL failure and API compatibility assessment

The verified SQL HTTP outage test changes only a private TestServer's
connection to unused `127.0.0.1:14336` with a short timeout. It
produces a sanitized **HTTP 500** and denies access. It does not cover
actual command timeout, SQL deadlock victim/retry, transaction-write
rollback, idempotent replays, application logs/telemetry or Azure
network/database failures. Evaluate 503 semantics, frontend retry/display
and correlation IDs with IT before changing any response contract.
Never retry non-idempotent write requests without a stable business
idempotency key and audited after-state.

### Remaining ownership and release controls

- P1 catalog-level parity and full 1600..1800_010 chain: NEEDS VALIDATION / BLOCKED, respectively.
- P2 concurrent Submit, multi-stop ordering, write-failure rollback and stable SnapshotVersion: NEEDS VALIDATION.
- P3 command timeout, deadlock, retry safety and log-correlation: NEEDS VALIDATION.
- All new checks are WORK-only and non-deploy; their exact CI proof must be obtained separately.
- B3 OFF, LiveSessionGuard default OFF, Approve/Apply DENY ALL,
  VAL-B1-002 BLOCKED, 1800_011 NOT AUTHORIZED / NONEXECUTABLE DRAFT,
  B4 acceptance 0/22, Business UAT HOLD, Production HARD HOLD.


### Follow-up WORK candidate — 19 real JWT HTTP + disposable SQL cases

Three additional `FieldVisit.B4.SqlHttp.Tests` tests extend the frozen
16-case manifest to **19 individually named cases**: concurrent Submit
against the same RowVersion (exactly one submitted snapshot), simultaneous
Submit versus Draft Update (only one committed version), and deliberate
**test-only** SQL Server snapshot INSERT trigger failure verifying rolled
back trip status, history, audit and snapshots with generic error response.
The test-only trigger `TR_B4_Isolated_RejectSnapshot` is created solely
inside a random `B4SqlHttp_*` disposable database; no Migration file,
Azure database or deployed application is changed.

`scripts/ci/package_b_sql_http_evidence.py` rejects fewer than 19,
a duplicated test, a failed/aborted case, bad TRX identity or non-WORK
SHA. Its negative tests are aligned to 19 total / 7 Submit cases;
the B3 51 and dated-site 6 manifests remain separate.

**Candidate until a fully green GitHub Actions run at this exact commit:**
these assertions do not themselves establish PASS. Even when green,
actual deadlock/retry/idempotency, multi-stop ordering, full migration
schema parity and operational SQL timeouts remain unverified. The
previous 16/16 checkpoint continues to be the last verified SQL HTTP
count until replacement evidence is complete.


### P2 concurrent Submit defect isolated / narrow 409 correction candidate

Isolated SQL run [38120398771](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38120398771)
(on intermediate WORK SHA `7a12d9ede7ea897bc4e68c50fb911b4ba4e547f3`)
identified a reproducible rejected concurrent Submit path:
SQL error **2601**, unique index
`IX_VisitTripSnapshots_VisitTripId_SnapshotVersion` with duplicate (trip, version).
The API returned HTTP 500, so the new concurrency case failed. This
run is explicitly **FAILED / NOT usable as acceptance evidence**.
The other two new SQL cases (racing draft update, SQL snapshot
write-failure rollback) passed in that intermediate run; both must be
reproven with the whole suite on the final SHA.

`backend/src/FieldVisit.Api/Program.cs` now maps **only** a
`DbUpdateException` with SQL 2601/2627 naming that exact
snapshot unique index to a sanitized HTTP **409** with stable
`SNAPSHOT_VERSION_CONFLICT` detail. This leaves generic SQL, FK,
other unique-index, deadlock, timeout and network errors under their
existing handling. Impact: a losing concurrent Submit is now treated
as a user-recoverable conflict instead of an unexpected server error;
no API route/response schema, authorization or database contract is
changed. UI consumers already handle 409 for stale RowVersion, but
IT must still check localized retry/display behavior before deployment.

Additional P1 gap from read-only DDL inspection: migration
`1800_007_mileage_google_governance/Up.sql` contains **five
additional migration-only triggers** (route attempt/trip linkage,
selected geocoding, mileage route-trip and decision evidence, and
snapshot/route attempt-trip linkage). Migrations
`1800_009` and `1800_010` replace CHECK constraint definitions.
The automated static P1 report remains expressly scoped to
`1800_003`, not a comprehensive schema-diff or a formal
upgrade-path rehearsal. These other migration-owned invariants
remain **NEEDS VALIDATION** on a separately authorized disposable
migration baseline.


### 2026-10-11 continuation — P2 CreateDraft partial-commit gate (ANALYSIS ONLY)

Reconfirmed from `backend/src/FieldVisit.Application/TripService.cs`:
`CreateAsync` invokes `uow.SaveChangesAsync(ct)` immediately after
`trips.AddAsync`, then separately adds `MileageCalculation`, status
history and audit, and invokes a second `SaveChangesAsync`.
`IUnitOfWork` in `backend/src/FieldVisit.Application/Interfaces.cs`
exposes only `SaveChangesAsync`, not an explicit transaction boundary.
**RISK / NEEDS VALIDATION:** an exception between those two saves
could leave a persisted Draft and Stops without Mileage/History/Audit.
No claim is made that this has happened in Azure.

Required isolated regression before a behavioral change:
1. In the existing random `B4SqlHttp_*` SQL Server fixture, create a
   test-only AFTER INSERT trigger on `MileageCalculations` that throws
   a distinct error; issue a real JWT-authorized HTTP CreateDraft.
2. Inspect `VisitTrips`, `VisitTripStops`, `MileageCalculations`,
   `VisitTripStatusHistories` and `AuditLogs` after the failed HTTP.
   The desired invariant is **zero committed rows for the attempted trip**.
3. Only if this red test reproduces the split-commit defect, review an
   application transaction boundary that encompasses both saves,
   including generated trip ID, Stop relationships, retries and the
   `IUnitOfWork` interface compatibility. Implement and rerun the
   full isolated CI on WORK only; do not modify UAT/Production.
4. Keep SQL 500 sanitized for injected write failure; do not map
   arbitrary DB errors to 409 or auto-retry a non-idempotent Create.

Other P2/P3 coverage still pending: multiple-stop ordering, deadlock
victim, command timeout and retry idempotency. P1 `1800_003` static
inventory has known differences and is NOT full migration parity.
All B3/LiveSessionGuard default OFF, VAL-B1-002 BLOCKED,
Approve/Apply DENY ALL, B4 0/22, Business UAT HOLD,
Production HARD HOLD, 1800_011 NONEXECUTABLE.
