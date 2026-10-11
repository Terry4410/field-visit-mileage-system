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
