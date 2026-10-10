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
| B3 Approval disabled independently |  ApproveAsync remains DENY ALL and classifies its prohibited executor as HTTP 403 if flag/schema were enabled | Verify all approval attempts produce zero location/decision/audit mutation; UAT feature remains OFF |
| Permissions | Live HR/role/org/ownership gates and offline unit negatives | API-level negative/IDOR tests against approved isolated SQL database |
| Environment | appsettings flag OFF; API disabled tests + CI tripwire | Explicit UAT flag OFF verification on **deployed** artifact and runtime environment overrides |

## Owner-authorized isolated real SQL Server fixture (NOT UAT)

On the controlled WORK branch, a separate GitHub Actions job starts a disposable Microsoft SQL Server 2022 Developer Edition container bound ONLY to 127.0.0.1:14335. It generates a one-run password, creates a fresh random `B3SqlFixture_<GUID>` database, exercises candidate B3 SQL Server catalog/uniqueness/FK/CHECK/RowVersion behavior, then drops the database and deletes the container. Its C# test fixture is NOT `1800_011` and must never be copied to UAT as a migration. The job uses **no Azure secrets or existing SQL Server**. SQL Server results are distinct artifacts and cannot certify the absent 011 schema in UAT, complete business authorization, HTTP E2E, all 22 B4 cases, or production readiness. Any failing fixture job fails the overall WORK CI. Full Business UAT and Production remain HOLD.

## Additional owner-authorized SQL concurrency cases

The disposable engine suite now covers valid independent rejection reopening
the Pending filtered unique slot while retaining audit history, concurrent
reviewers racing on one RowVersion with only one committed decision and event,
replay of a public request ID for another location, and organization-partitioned
Pending uniqueness. Organization index separation does **not** prove HR role,
team ownership or tenant access rights; independent API/runtime tests remain
required. No formal migration or existing database is involved.

## Owner-authorized disposable SQL Server runtime evidence

A separate real SQL Server 2022 job exercises the ephemeral candidate B3 schema (never the formal migration) and now also covers invalid independent reviews, corrupted Submitted/Rejected audit events, transaction rollback on failed event insert, untrusted FK, and latest-schema drift. It captures TRX in a dedicated SQL-only artifact and runs a fail-closed parser that checks per-case outcomes, totals, missing/skipped evidence, exact WORK SHA and explicit UAT/Production HOLD. The number of passing fixture tests is **not** a count of completed B4 cases on a deployed UAT database.

## Required B4 negative cases (NOT YET PASSED in SQL Server runtime)
| ID | Scenario | Expected result / invariant |
| --- | --- | --- |
| B4-SQL-01 | 011 absent, tables absent, partial tables, or missing/untrusted FK | B3 unavailable; no write, no partial schema repair |
| B4-SQL-02 | Latest version 010 or later than 011, or merely an older 011 row | Readiness DENIED |
| B4-SQL-03 | Disabled/hypothetical unique Pending index or DecisionKey index | Readiness DENIED |
| B4-SQL-04 | Column width/type/nullability or CHECK-constraint drift | Readiness DENIED; IA revision before enablement |
| B4-SQL-21 | Enabled AFTER/INSTEAD OF trigger on ChangeRequests / ChangeRequestEvents | Readiness DENIED; no implicit publish, email, audit replacement or other side effect |
| B4-SQL-22 | Rejected row with NULL/blank ReviewReason or self-reviewer; Pending row with review reason; Submitted event missing actor | Database CHECK must DENY all malformed rows and preserve audit history |
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

## B4 22-case evidence classification (isolated-only; NOT full B4 sign-off)

The columns distinguish **existence of an isolated SQL engine check** from an executed full B4 acceptance scenario. The new five fixture cases are provisional until their exact GitHub SHA/TRX is green; passing a fixture never changes a scenario to GO. "HTTP" requires separately authorized isolated API E2E with live HR/role/team/ownership data. "Independent" includes owner approval of candidate 011 execution or UAT gate as applicable. This matrix is a handoff mapping, not a waiver.

| ID | SQL 2022 fixture evidence | Offline / code evidence | Remaining HTTP/API E2E | Separate approval / blocked gate |
| --- | --- | --- | --- | --- |
| B4-SQL-01 | Partial: catalog, trusted FK/CHECK | Schema contract | Missing / partial 011 database negative paths | 011 migration NOT AUTHORIZED |
| B4-SQL-02 | Partial: latest-version drift | Latest 011 check | Enabled-feature readiness rejection | 011 migration NOT AUTHORIZED |
| B4-SQL-03 | Partial: duplicate keys, index disabled/filter drift | Named index contracts | 409 and 503 mapping | Feature OFF |
| B4-SQL-04 | Partial: CHECK trust, column-width drift | 28-column contract | Corrupted-schema readiness responses | 011 migration NOT AUTHORIZED |
| B4-SQL-05 | Partial: concurrent insert and atomic Submitted event | Serializable service ordering | Two actual API submitters / audit counts | Feature OFF; B4 HOLD |
| B4-SQL-06 | Partial: DecisionKey replay/rollback | Review idempotency checks | Duplicate key API 409 | Feature OFF |
| B4-SQL-07 | Partial: stale request RowVersion | Version parsing/EF unit tests | Stale entity and request 409 | Feature OFF |
| B4-SQL-08 | None (identity data not in fixture) | Visitor ownership gates | Same-team peer IDOR | B4 HOLD |
| B4-SQL-09 | Only org-partitioned unique index, NOT authorization | Cross-org scope guards | Cross-tenant read/write/queue IDOR | B4 HOLD |
| B4-SQL-10 | None (HR lifecycle not in fixture) | HR state/role checks | Real-time revocation API flow | B4 HOLD |
| B4-SQL-11 | None (team grant validity not in fixture) | Team grant fail-closed rules | Expiry/revocation API flow | Manager Grant BLOCKED |
| B4-SQL-12 | None (external identity not in fixture) | Role-origin checks | Forged Admin projection | B4 HOLD |
| B4-SQL-13 | Partial: self-review SQL CHECK | Independent reviewer gate | Dual-role requester API attempt | Feature OFF |
| B4-SQL-14 | None | Historical grant rejection gates | Backfilled-leader API negative | VAL-B1-002 BLOCKED |
| B4-SQL-15 | None (geocoding callbacks not in fixture) | Publish separation tests | Geocoding/pending race E2E | Feature OFF |
| B4-SQL-16 | Partial: Request/Event FK protects audit | Safe-delete unit checks | Snapshot/note/deployment reference deletion attempts | B4 HOLD |
| B4-SQL-17 | None (shared history endpoint not in fixture) | Scope query guards | Spoofed teamId API reads | B4 HOLD |
| B4-SQL-18 | Partial: CHECK rejects unknown codes/JSON | Supported payload preflight | Tampered queue/approval API requests | Approve/Apply DENY ALL |
| B4-SQL-19 | Partial: engine 2601/2627 failure proof | Exception-to-conflict mapping | API 409 vs unrelated 500/503 | Feature OFF |
| B4-SQL-20 | None (bulk endpoints not in fixture) | Admin revocation logic | Revoke Admin during bulk operation | B4 HOLD |
| B4-SQL-21 | Partial: trigger catalog rejection | Disabled executor tripwire | API disabled/readiness response | Feature OFF |
| B4-SQL-22 | Partial: self-review, reason, audit CHECK | Constraint definition tests | SQL-negative outcomes tied to actual API | 011 migration NOT AUTHORIZED |

**Outcome:** 22/22 scenarios have an evidence classification, **0/22 are declared fully accepted**. SQL-fixture coverage is partial and non-equivalent to approved API/UAT runtime acceptance. Any missing, skipped or failing named SQL test keeps this suite FAIL-CLOSED. SQL TRX, individual names and actual tested SHA take precedence over this review-only mapping.

## Automated future-regression prevention

CI now includes a named fail-closed gate
`B3_bounded_payload_before_SQL_transaction`. Every WORK push must prove
the service invokes Submit and Reject request preflight **after schema/flag
readiness but before** its serializable transaction and live-actor queries;
the input helper must retain bounded fields and canonical eight-byte
RowVersion decoding. Separate negative tests intentionally remove/move
these guards to demonstrate the CI refuses such regressions. This is a
source-level test, **not** SQL Server runtime evidence.

## Untrusted B3 request preflight (candidate only)

The candidate now bounds Submit and Reject payloads **before** opening their
serializable transaction or querying HR/team/location records, but only **after**
the feature/schema readiness gate. Zero/invalid location IDs, oversized raw
fields/reasons, invalid or noncanonical RowVersion and empty review decision
keys fail closed. Live HR/role/ownership, rowversions and independent review
are STILL revalidated inside the transaction. These offline tests are not
authorization to enable B3 and do not claim SQL Server runtime proof.

## Automated read-only GitHub Actions evidence

The isolated Package B WORK workflow runs on code, frontend, CI-script and Package-B review-document updates automatically. It archives JSON/Markdown gate evidence and xUnit TRX/frontend JUnit, with fail-closed aggregation. The preflight checks frozen Protected SHA, candidate ancestry, B3 OFF / Apply DENY ALL, no executable 011 migration, no historical migration edits, no unrelated release workflow edits, and rejects 011 workflow content hidden under misleading filenames. Negative CI-script unit tests exercise these gates. **This is not SQL Server UAT, an IT grant, or permission to deploy.**

## CI PASS vs release GO (strictly separate)

Every WORK push now archives one machine-readable combined CI evidence report and Markdown summary. The summary explicitly states that an offline PASS **never** grants Business UAT or Production GO. Its static authorization matrix records SQL Server runtime as NOT_TESTED, 011 as NOT_AUTHORIZED, Manager Grant provenance as BLOCKED, and B3 Approve/Apply as DENY ALL. The entry represents the approved package policy, **not a live deployed-environment attestation**. A separately authorized IT/Owner approval workflow must replace these HOLD states before any promotion or deployment.

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
