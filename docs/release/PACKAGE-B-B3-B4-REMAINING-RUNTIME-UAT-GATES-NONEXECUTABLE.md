# Package B — remaining B3/B4 runtime acceptance matrix (REVIEW ONLY)

**Issue #24 | WORK branch only | No migration, deployment or feature activation authorization.**

This file is a handoff/checklist, **not** executable SQL, a test-run report or a GO decision. Static .NET/EF tests are necessary but are not evidence for SQL Server transactional correctness. UAT and Production remain HOLD.

## Authoritative policy
- Low-risk permitted edits may be directly saved only under validated current organization/team/record ownership; high-risk changes require independent Admin authorization.
- Visitor may write only an actually owned Location with a current authorized team. Team membership / picker visibility is never permission to change a peer's Location.
- Leader may manage team-owned records only after **VAL-B1-002 independently attested manager-grant provenance**. Historical/backfilled TeamLeaderAssignments are NOT authority.
- B3 Approval / Apply remains DENY ALL even if an operator misconfigures the flag. Both application/API and migration go-live require separate Owner authorization.
- This candidate supports Location / UpdatePublishedLocation / High only. Unknown types, unknown statuses or untrusted source identity fail closed.

## Preflight contract — IT must independently certify before running anything
| Gate | Current evidence | Required remaining proof |
| --- | --- | --- |
| Protected source immutability | Dedicated WORK branch; verify fixed protected SHA | Owner-confirmed promotion/change window |
| 011 schema predecessor | Nonexecutable IA requires actual latest 010 | Exact LIVE DB identity and latest SchemaVersions 1.8.0-010 read-only evidence |
| 011 schema objects | Candidate EF model and offline metadata tests only | Separate Owner migration authorization; SQL indexes, 28 columns, six trusted NO ACTION FKs, required CHECK constraints |
| Index/ROWVERSION | Read-only query source and static text tests | SQL Server catalog execution and EF runtime behavior verified by IT |
| B3 unavailable response | Checked-in endpoint returns 503 when flag OFF; Schema readiness faults mapped to 503 | Validate enabled-but-invalid schema/permission failure at isolated runtime; no SQL repair or retries |
| Permissions | Live HR/role/org/ownership gates and offline unit negatives | API-level negative/IDOR tests against approved isolated SQL database |
| Environment | appsettings flag OFF; API disabled tests + CI tripwire | Explicit UAT flag OFF verification on **deployed** artifact and runtime environment overrides |

## Required B4 negative cases (NOT YET PASSED in SQL Server runtime)
| ID | Scenario | Expected result / invariant |
| --- | --- | --- |
| B4-SQL-01 | 011 absent, tables absent, partial tables, or missing/untrusted FK | B3 unavailable; no write, no partial schema repair |
| B4-SQL-02 | Latest version 010 or later than 011, or merely an older 011 row | Readiness DENIED |
| B4-SQL-03 | Disabled/hypothetical unique Pending index or DecisionKey index | Readiness DENIED |
| B4-SQL-04 | Column width/type/nullability or CHECK-constraint drift | Readiness DENIED; IA revision before enablement |
| B4-SQL-05 | Two parallel submissions for same organization/location | Exactly one Pending, one Submitted event and one conflict; no orphaned audit |
| B4-SQL-06 | Reuse one DecisionKey against same/different request concurrently | Exactly one valid decision event; replay 409, no double transition |
| B4-SQL-07 | Stale entity RowVersion or stale request RowVersion | 409; location/request/audit unaffected |
| B4-SQL-08 | Visitor requests peer's location within same team | 403; no Pending row, no audit writes |
| B4-SQL-09 | Cross-organization requester/admin and foreign location/request GUID | No cross-org view/edit/review; 403/404, no data disclosure |
| B4-SQL-10 | Expired role, disabled account, Leave/Terminated/PreHire | 403 and no mutation after revocation |
| B4-SQL-11 | Inactive/expired team or revoked UserTeamScope/assignment/membership | 403 and no change request submitted |
| B4-SQL-12 | External identity with anomalous Admin role projection | 403; no privileged write |
| B4-SQL-13 | Requester attempts to review own change (including dual role) | 403; no Approved/Rejected event |
| B4-SQL-14 | Leader with only a backfilled, unattested managerial assignment | DENY, no implicit Team-wide write |
| B4-SQL-15 | Geocoding completion/failure arrives while review is Pending | No implicit Approved, IsActive or published-state transition |
| B4-SQL-16 | Permanent deletion when Snapshot/notes/geocoding/deployment/audit references exist | Deletion DENIED; history remains intact |
| B4-SQL-17 | Reader spoofs teamId on shared Location/history endpoint | No other-team/cross-org note or audit disclosure |
| B4-SQL-18 | Unknown EntityKind/OperationCode/RiskCode or tampered ProposedJson | Not visible as supported queue item; cannot be approved/applied |
| B4-SQL-19 | SqlException 2601/2627 vs unrelated constraint/deadlock error | Known named B3 duplicates 409; unknown error fail closed, no false success |
| B4-SQL-20 | Business HR/Role/Team/Project/OfficialSite bulk operations after Admin role revoked | All sensitive writes denied; audit and dependent history unchanged |

## Transaction and history acceptance evidence
- Create uniquely identified, disposable records only inside a **separately approved isolated SQL test database**, never UAT business records without explicit authorization.
- Capture transaction correlation IDs, request/public IDs, precise before/after RowVersions, row counts, status transitions, index/FK/check metadata, full CI SHA and test logs.
- Confirm no residual Pending duplicate, orphan event, audit replacement, hidden cross-org disclosure, geocoding auto-publish or cross-tenant write.
- Any failed case = HOLD; do not retry destructive SQL or edit live data as an ad hoc fix.
- If schema 011 has data, never drop B3 tables or erase audit to roll back. IT/Owner must approve a forward fix or recoverable restore after impact reconciliation.

## Outstanding independent approvals
1. **VAL-B1-002:** IT/Owner provenance decision for actual Leader managed-team grants.
2. **1800_011:** Final IA, column/CHECK/index/FK contract, backup plan and separate migration authorization.
3. **B4:** Permission to provision a disposable isolated SQL environment and run the non-production concurrency/API matrix.
4. **Feature / release:** Independent deployment, B3 flag enablement, Business UAT GO and eventual Production GO.

**As authored:** NONEXECUTABLE REVIEW MATRIX — no runtime test outcomes implied.
