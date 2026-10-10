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
