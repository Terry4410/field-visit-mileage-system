# Option C Batch 3 — UAT Readiness / Nondeploy Evidence

Status: **READY FOR OWNER REVIEW / NOT AUTHORIZED TO EXECUTE**

This document is a preparation artifact only. It does not authorize migration, deployment, feature activation, Business UAT, or production execution.

## Schema preflight plan

Exact order for a future authorized disposable/UAT rehearsal:

1. Validate server/database target against the separately approved target allow-list.
2. Confirm backup timestamp, restore owner, recovery point, and rollback contact.
3. Run read-only catalog checks for 011, 012, and 014 prerequisites.
4. Confirm latest SchemaVersions is exactly 1.8.0-010 before 011.
5. Apply 011, verify ChangeRequests and ChangeRequestEvents, then stop on any drift.
6. Apply 012, verify trusted constraints, unique DecisionKey, and atomic status guards.
7. Apply 014 only after 012 verification; verify Stage and PublishedVersions catalog.
8. Run deterministic backfill verification; require zero ambiguity and stable LocationId.
9. Record checksums, row counts, audit counts, and operator identity.
10. Any failure: rollback/restore according to the approved recovery runbook; never blind-retry an uncertain COMMIT.

Current disposable rehearsal: canonical 011/012/014 PASS on isolated SQL only. UAT/Production execution: NOT RUN.

## Activation and rollback plan

- Keep `OptionC:Approval:Enabled=false`, `OptionC:Candidate:Enabled=false`, `PackageB:B3:Enabled=false`, and frontend approval false.
- Future activation order: database preflight and backup -> backend deployment -> backend health/read-only checks -> frontend deployment -> controlled flag enablement -> smoke test.
- Disable order: frontend approval flag -> backend candidate/approval flags -> preserve audit and published versions -> investigate before any rollback.
- Never retry an unknown COMMIT; reconcile using the approved request/decision key on a fresh connection.
- Rollback requires explicit Owner approval, verified backup, and a tested restore path. No destructive reverse migration is assumed.

## Option C / Business UAT 22-case readiness matrix

All rows are **NOT RUN** until Business Users execute them in authorized UAT.

| Case | Role | Scenario | Expected gate | Evidence |
|---|---|---|---|---|
| OC-01 | Visitor | Submit high-risk address change | Pending, no publication | Request/Stage |
| OC-02 | Leader | Submit permitted team-owned change | Pending, no publication | Request/Stage |
| OC-03 | Visitor | Requester HR termination | Submission denied | HR evidence |
| OC-04 | Leader | Requester leave status | Submission denied | HR evidence |
| OC-05 | Admin | Independent queue visibility | Same-org/team only | Queue scope |
| OC-06 | Admin | Self-approval | Denied | Audit unchanged |
| OC-07 | Admin | Cross-organization review | Denied | Scope response |
| OC-08 | Admin | Cross-team review | Denied | Scope response |
| OC-09 | Admin | Approve ready request | Applied atomically | Request/Stage/Audit |
| OC-10 | Admin | Reject pending request | Rejected + Abandoned | Request/Stage/Audit |
| OC-11 | Admin | Duplicate DecisionKey | Replay denied | Event uniqueness |
| OC-12 | Admin | Concurrent decisions | One winner | Serializable lock |
| OC-13 | Admin | Requester role revocation | Decision denied | Live role read |
| OC-14 | Admin | Reviewer role revocation | Decision denied | Live role read |
| OC-15 | Admin | Team reassignment | Scope denied | Live team read |
| OC-16 | Admin | Missing HR evidence | Fail closed | Authorization audit |
| OC-17 | Admin | Geocode evidence missing | Publish denied | Stage status |
| OC-18 | Admin | Duplicate evidence missing | Publish denied | Stage status |
| OC-19 | Admin | Unknown COMMIT | Reconcile only | Fresh read |
| OC-20 | Visitor | Own Pending/Rejected/Applied status | Correct state shown | Status API |
| OC-21 | Visitor/Leader | Low-risk MasterNote edit | Direct save remains usable | Location audit |
| OC-22 | Admin | LocationId/history preservation | Version 1 + Trip/Snapshot intact | SQL evidence |

## Explicit HOLD items

MergeFields canonical approval, production/UAT route activation, UAT migration, deployment, Business User execution, and GO declaration remain HOLD.


## UAT execution preflight authorization — 2026-10-14

Preparation-only authorization is recorded. The consolidated Owner execution package is `OPTIONC_UAT_EXECUTION_AUTHORIZATION_PACKAGE.md`. UAT target identification and read-only schema/backup preflight remain NOT RUN because no exact UAT server/database target was supplied. This does not block review of the future execution authorization package, but it blocks actual UAT execution.


## UAT release-readiness finalization — 2026-10-14

Actual repository inspection confirms formal executable migrations stop at 1800_010;
Option C 011/012/014 and additive schemas are candidate-only. The Recovery branch now
contains a read-only preflight candidate and static SHA-256 fingerprint script. The
existing post-uat/v1.8.0 guard is preserved. No exact UAT target or backup evidence is
available, so UAT preflight is NOT RUN and the package remains HOLD; Business UAT is NOT RUN.

## Authoritative Recovery evidence — 2026-10-14

- Recovery branch: work/v180-optionc-final-gates-recovery-r1
- Authoritative preparation SHA: 3012835e728d0e3882a061d920cd93916811c8f9
- Exact-SHA Recovery CI: 38513216745 — SUCCESS
- Hash/preflight contract, backend/frontend regression, disposable SQL Server 2022 Option C E2E, and canonical 011/012/014 rehearsal completed on this SHA.
- UAT target identification and read-only UAT preflight: NOT RUN; no exact authorized target or secrets were available.
- Backup/restore evidence: NOT VERIFIED.
- Flags remain OFF; no UAT/Production SQL write, migration, deployment, promotion, merge, or Business UAT GO occurred.
- Decision: HOLD pending the IT action checklist and separate Owner execution authorization.


## Leader first-level review delta

- Canonical request remains the only ledger; `ChangeRequestLeaderReviews` is an additive detail table with unique `(ChangeRequestId, ReviewStage)` and replay-protected DecisionKey.
- Disposable order is `011 -> 012 -> 014 -> LeaderReviewDetails.disposable.sql`; the additive script is isolated to `OptionCIsolated_*` and never modifies migration 013.
- Leader decision rechecks requester/reviewer live HR, effective role projection, organization/team scope, appointment or delegation, independence, and current Request RowVersion in a Serializable transaction.
- Recovery evidence: `2ce6108f9e091c32ea3a6099a9040196bb0a1e6c`, CI `38518256406` SUCCESS. UAT target preflight and Business UAT remain NOT RUN.


## Final Leader review engineering closure — Recovery 81413aa394a19b08e3e697a5a85bba2992e422eb

- Exact-SHA CI: `38521449544 — SUCCESS`。
- Backend `737 tests PASS; frontend `24 test files and build PASS。
- Real disposable SQL/HTTP/DI E2E PASS: Visitor submit → independent live-appointed Leader approval → application-owned geocode evidence → duplicate evidence → independent Admin approval → atomic publication, Version 2 current / Version 1 historical, canonical audit and historical references preserved。
- Additive audit compatibility PASS: Submitted / Rejected / Approved / Applied / GeocodeEvidenceRecorded / DuplicateReviewEvidenceRecorded / LeaderApproved / LeaderRejected all remain permitted; rerun returns controlled STOP `51083)。
- No UAT/Production SQL writes, migration, deployment, promotion, feature activation or migration 013 modification。
- Remaining HOLD: CreateLocation/LowRisk controlled application path, Excel/legacy/background canonical routing, Leader rejection/concurrency/revocation positive HTTP matrix expansion, UAT target preflight and Business UAT。


## Scheme A closure checkpoint — 2026-10-14

- Recovery HEAD: `8ac7d9b5b1005019ab668e19ec994445dec4ca55` (DI composition correction; exact CI pending).
- Candidate Option C services are now explicitly registered in the API host. Each service still fails closed unless `OptionC:Candidate:Enabled=true`, the SQL target is loopback isolated `OptionCIsolated_*`, and catalog proof succeeds. Deployed flags remain OFF.
- Existing real disposable HTTP/DI/SQL Leader flow and negative security/concurrency/revocation tests remain retained; no UAT/Production SQL write, migration, deployment, promotion, merge, or Business UAT GO occurred.
- Scheme A blocker remains precise: canonical CreateLocation/LowRisk application publication is not implemented because the verified atomic writer only supports existing high-risk published Location updates/merges. Excel/legacy/background writers therefore remain fail-closed for published changes; implementing a new writer without an additive schema and identity/reference contract would be unsafe.
- UAT target preflight and Business UAT remain NOT RUN.
