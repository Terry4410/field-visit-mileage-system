# Option C UAT Execution Authorization Package

## Status

- Recovery branch: `work/v180-optionc-final-gates-recovery-r1`
- Engineering candidate SHA: `2ce6108f9e091c32ea3a6099a9040196bb0a1e6c`
- Last exact-SHA CI: `38518256406 — SUCCESS`
- Scope: preparation and read-only preflight only
- UAT/Production migration, deployment and flag activation: NOT EXECUTED

This package is an approval package, not an execution authorization.

## Scheme A engineering delta — still NO-GO (2026-10-14)

The Recovery branch includes a **disposable-only** CreateLocation/LowRisk
canonical SQL contract, atomic publication writer, live-authorized
Serializable orchestrator, and an isolated-candidate
`POST /api/v1/optionc-candidate/requests/{id}/publish-reviewed` route.

These do not authorize UAT SQL, UAT deployment, feature activation or
Business UAT. Positive publication tests use disposable SQL-seeded requests;
end-to-end HTTP submission, Pending Location exclusion from all operational
queries, Excel/legacy/background canonical routing, rejection/rollback
matrices and actual UAT target read-only preflight remain HOLD.

The older SHA/CI in the Status section above is historical and **must not**
be interpreted as verification of this delta. Refer to the current
Recovery exact-SHA CI for engineering verification; until it passes,
this delta is unverified.

**Owner decision: NOT READY FOR UAT EXECUTION AUTHORIZATION REVIEW.**

## Target identification gate

A preflight may run only after Owner/IT supplies all of the following:

- Exact UAT SQL server FQDN
- Exact UAT database name
- Approved GitHub Environment and read-only identity
- Target allow-list record or change ticket
- Backup timestamp, retention, restore owner and recovery point
- Business/UAT deployment window and rollback contact

Environment name alone is insufficient. The current repository workflow `.github/workflows/azure-sql-readonly-uat-smoke.yml` is read-only but is branch-guarded to `post-uat/v1.8.0`; no Recovery-branch bypass is authorized.

Current result: **UAT target preflight NOT RUN — target not specified in this authorization message.**

## Read-only schema preflight

Run only with `ApplicationIntent=ReadOnly`, no transaction writes, and capture output:

1. Confirm server/database identity and reject master/model/msdb/tempdb.
2. Confirm connectivity identity has no DDL/DML permission.
3. Confirm latest SchemaVersions and exact dependency order:
   `1.8.0-010 -> 1.8.0-011 -> 1.8.0-012 -> 1.8.0-014`.
4. Confirm canonical tables, columns, trusted CHECK constraints, unique indexes and foreign keys.
5. Confirm `013` remains absent/unmodified.
6. Confirm additive audit candidate is not partially applied, or is fully applied with both event types and trusted constraints.
7. Confirm Stage and published-version catalog compatibility.
8. Confirm no duplicate pending Request, DecisionKey or conflicting Location authority.
9. Confirm stable LocationId and historical Trip/Snapshot references.
10. Record schema fingerprints, row counts, audit counts, operator, UTC timestamp and target identity.

Any mismatch is a STOP condition. Do not repair in UAT during preflight.

## Additive migration dependency and recovery

Disposable candidate source:

`docs/release/optionc-canonical-sql-draft/audit/StageEvidenceAuditEvents.disposable.sql`

Required future execution order:

1. Read-only target and backup gate.
2. Canonical 011.
3. Canonical 012.
4. Canonical 014 and verified backfill.
5. Additive Stage evidence audit candidate.
6. Application deployment and read-only health checks.
7. Separately approved feature activation.

The additive candidate preserves existing events and extends only the EventType and DecisionState constraints. It is not migration 013.

Rollback policy:

- Before execution: stop and restore the verified pre-change backup.
- After execution with no application activity: stop, preserve evidence, and restore the verified backup.
- After any application activity: do not run a destructive reverse migration or drop constraints containing evidence rows; disable flags, preserve audit, reconcile requests, and restore only under separately approved recovery procedure.
- Unknown COMMIT: fresh non-pooled read-only reconciliation; never blind retry.

## Deployment and flag sequence

Future controlled sequence, subject to separate approval:

1. Backup and read-only schema preflight PASS.
2. Apply approved database changes in exact order.
3. Verify schema fingerprints and audit invariants.
4. Deploy backend.
5. Run backend health and authorization read-only checks.
6. Deploy frontend.
7. Keep all flags OFF during smoke validation.
8. Enable backend candidate approval only after Owner approval.
9. Enable frontend approval only after backend verification.
10. Run controlled smoke cases.
11. Freeze evidence and proceed to Business UAT.

Disable sequence:

1. Frontend approval flag OFF.
2. Backend Option C and candidate flags OFF.
3. Keep audit, Request, Stage and published history intact.
4. Reconcile uncertain decisions.
5. Restore only with explicit recovery approval.

Current flags remain OFF:
`OptionC:Approval:Enabled=false`, `OptionC:Candidate:Enabled=false`, `PackageB:B3:Enabled=false`, frontend approval=false.

## 22-case Business UAT execution plan

Business UAT is to be performed only after authorized UAT deployment; it is not a prerequisite to reviewing deployment authorization.

| Case | Role | Scenario | Expected result | Evidence |
|---|---|---|---|---|
| OC-01 | Visitor | Submit high-risk address change | Pending, no publication | Request/Stage |
| OC-02 | Leader | Submit team-owned change | Pending, no publication | Request/Stage |
| OC-03 | Visitor | Terminated requester submits | Denied | HR status |
| OC-04 | Leader | Leave-status requester submits | Denied | HR status |
| OC-05 | Admin | Review queue scope | Same org/team only | Queue |
| OC-06 | Admin | Self-approval | Denied | Audit unchanged |
| OC-07 | Admin | Cross-organization review | Denied | Scope |
| OC-08 | Admin | Cross-team review | Denied | Scope |
| OC-09 | Admin | Approve ready request | Applied atomically | Request/Stage/Audit |
| OC-10 | Admin | Reject pending request | Rejected/Abandoned | Request/Stage/Audit |
| OC-11 | Admin | Duplicate DecisionKey | Replay denied | Unique event |
| OC-12 | Admin | Concurrent decisions | One winner | Serializable lock |
| OC-13 | Admin | Requester role revoked | Decision denied | Live role |
| OC-14 | Admin | Reviewer role revoked | Decision denied | Live role |
| OC-15 | Admin | Team reassignment | Denied | Live scope |
| OC-16 | Admin | Missing HR evidence | Fail closed | Auth evidence |
| OC-17 | Admin | Missing geocode evidence | Publish denied | Stage |
| OC-18 | Admin | Missing duplicate evidence | Publish denied | Stage |
| OC-19 | Admin | Unknown COMMIT | Reconcile only | Fresh read |
| OC-20 | Visitor | Own status query | Correct state | Status API |
| OC-21 | Visitor/Leader | Low-risk MasterNote edit | Direct save works | Location |
| OC-22 | Admin | Location/history preservation | Version 1 and history intact | SQL evidence |

Business users must record actor, target, timestamp, expected result, actual result, evidence link and sign-off for every case. No automated rehearsal is Business UAT PASS.

## Owner final execution approval sheet

The following must be checked before a separate execution approval is issued:

- [ ] Exact UAT target supplied and allow-listed
- [ ] Read-only schema/version/dependency preflight PASS
- [ ] Backup and restore evidence reviewed
- [ ] Additive migration checksum and rollback owner confirmed
- [ ] Deployment order and flag sequence approved
- [ ] Recovery contact and unknown-commit procedure confirmed
- [ ] Backend/frontend candidate artifacts identified by exact SHA
- [ ] Business UAT 22-case schedule and users assigned
- [ ] Explicit Owner authorization to execute UAT migration
- [ ] Explicit Owner authorization to deploy
- [ ] Explicit Owner authorization to activate flags

Until every applicable gate is checked, status is **HOLD / NO-GO**.

## Current recommendation

Engineering candidate: PASS in isolated SQL/HTTP.

UAT execution authorization review: **READY TO REVIEW AFTER TARGET IDENTIFICATION**.

Actual UAT execution: NOT AUTHORIZED by this package.

Business UAT: NOT RUN and no Business UAT GO declared.


## UAT migration register and controlled candidate matrix — 2026-10-14

The repository's executable register currently ends at database/migrations/1800_010.
There are no executable 1800_011, 1800_012, or 1800_014 entries in that register.
The candidate SQL under docs/release/optionc-canonical-sql-draft/ is reviewable/nondeploy
source and must not be copied directly into UAT without DBA conversion, target preflight,
checksum review, and separate execution authorization. Migration 1800_013 remains reserved.

| Candidate | Dependency / order | Git blob fingerprint | Runtime SHA-256 |
|---|---|---|---|
| 1800_011 Up.sql | after 1800_010; then Verify.sql | 480ceeab7b98593a67bdc9813ca057009221ce21 | generated by CI script |
| 1800_012 Up.sql | after verified 1800_011; then Verify.sql | 0906ac48b96729d314cdd06e61fd130043ed464c | generated by CI script |
| 1800_014 Up.sql + Backfill.sql | after verified 1800_012; then Verify.sql | e0d036caaf9b07f9d2896836ce43509fe7d83f4b + dbad0e0655566a82a689010bcd6a57f239a6dccb | generated by CI script |
| MergeDetails additive | after 014 and approved schema review | c4fc89581a848fb73f382837ab8416abfcb78258 | generated by CI script |
| StageEvidenceAuditEvents additive | after Stage schema and approved audit review | b637c208f4556ab9d6ba31a7711c1a4b5a3eed35 | generated by CI script |

Supporting Verify.sql/README/Preflight artifacts are covered by the same static hash script. No SHA-256 value is claimed until generated from the exact checked-out bytes.

## Read-only preflight implementation

Candidate: docs/release/optionc-canonical-sql-draft/preflight/UAT_ReadOnly_Preflight.sql.
It checks exact server/database identity, system-database rejection, SchemaVersions,
canonical dependency completeness, trusted FK/CHECK constraints, unique indexes,
partial migration state, orphan/history integrity, row counts and a deterministic
catalog fingerprint. It is SELECT/THROW only and requires Entra/ApplicationIntent=ReadOnly.
The existing workflow branch guard remains post-uat/v1.8.0; no Recovery bypass was added.

Result in this batch: **UAT target identification NOT RUN**. The repository does not expose
authorized environment values or secrets, and no exact server/database, allow-list/change
ticket, read-only identity, backup timestamp/retention, restore owner/recovery evidence,
deployment window or rollback contact was supplied. This is a STOP/HOLD, not an inferred PASS.

## IT action checklist

1. Supply exact UAT SQL server FQDN and database name through the approved channel.
2. Confirm uat GitHub Environment, Entra read-only principal and target allow-list/change ticket.
3. Provide backup timestamp/retention, restore owner, tested recovery point and rollback contact.
4. Run the existing post-uat/v1.8.0 guarded read-only workflow with the candidate SQL and archive output.
5. Resolve any STOP result before any separately authorized migration/deployment request.

## Readiness decision

Engineering preparation: PASS (Recovery-only, flags OFF, no UAT writes).
Read-only UAT preflight: NOT RUN.
Backup/restore gate: NOT VERIFIED.
Business UAT: NOT RUN by design.
Current status: **HOLD — NOT READY FOR OWNER EXECUTION REVIEW until target and backup evidence are supplied.**

## Authoritative Recovery evidence — 2026-10-14

- Recovery branch: work/v180-optionc-final-gates-recovery-r1
- Authoritative preparation SHA: 3012835e728d0e3882a061d920cd93916811c8f9
- Exact-SHA Recovery CI: 38513216745 — SUCCESS
- Hash/preflight contract, backend/frontend regression, disposable SQL Server 2022 Option C E2E, and canonical 011/012/014 rehearsal completed on this SHA.
- UAT target identification and read-only UAT preflight: NOT RUN; no exact authorized target or secrets were available.
- Backup/restore evidence: NOT VERIFIED.
- Flags remain OFF; no UAT/Production SQL write, migration, deployment, promotion, merge, or Business UAT GO occurred.
- Decision: HOLD pending the IT action checklist and separate Owner execution authorization.


## Leader first-level review readiness

- Visitor Location requests require an independent Leader first-level review before any high-risk Admin publication; low-risk direct application remains disabled until a controlled application workflow is separately verified.
- Evidence is persisted in the additive `ChangeRequestLeaderReviews` table and canonical `ChangeRequestEvents` (`LeaderApproved`/`LeaderRejected`); no second approval ledger is introduced.
- Dependency candidate: canonical `011 -> 012 -> 014 -> LeaderReviewDetails.disposable.sql`; additive script is disposable-only and requires `OptionCIsolated_*`. Migration 013 is reserved and unchanged.
- Exact Recovery evidence: `2ce6108f9e091c32ea3a6099a9040196bb0a1e6c` / CI `38518256406 SUCCESS`.
- UAT target/read-only preflight: NOT RUN; Business UAT: NOT RUN; all production/UAT flags remain OFF.


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


## Scheme A authoritative nondeploy checkpoint — 2026-10-14

- Verified Recovery implementation SHA: `5ca1060423e9ef326b82b6cd6c1025250a0d578a`.
- Exact-SHA CI: `38534680973 — SUCCESS`; backend **753 PASS**, additional
  SQL Admin E2E **1 PASS**, SQL 011/012/014 rehearsal **2 PASS**,
  frontend **24 test files PASS** and production build PASS.
- Real disposable SQL Server 2022 HTTP/DI CreateLocation E2E now passes:
  Visitor submission → server-owned identity and inactive Pending reservation →
  private Stage → independent live-appointed Leader review →
  application-owned geocode and duplicate evidence → independent Admin approval →
  atomic V1 publication. A second request is independently Leader-rejected
  and its Stage/reservation are Abandoned without published history.
- Negative evidence: self-review denied; cross-org submission denied;
  duplicate Pending create denied; premature publication denied; canonical
  Submitted/LeaderApproved/GeocodeEvidenceRecorded/
  DuplicateReviewEvidenceRecorded/Approved/Applied events and historical
  Trip/Snapshot reference preservation verified.
- The next candidate extends the same test to prove actual HTTP Picker
  exclusion before publication and inclusion after it; **do not count that
  extension PASS until its own Exact-SHA CI succeeds**.
- Excel Location Create remains fail-closed in Preview and Confirm;
  legacy published mutation/direct-note routes are guarded. Full Excel routing,
  broad Legacy/Background audit, all UI roles and all operational read-path
  proofs remain incomplete.
- No UAT/Production SQL writes, migration, deployment, promotion, merge,
  flag activation, UAT-target preflight or Business UAT was performed.
  **Scheme A HOLD / NO-GO; NOT READY for UAT execution authorization review.**
  This current checkpoint supersedes older 2026-10-14 engineering status
  paragraphs where the CreateLocation HTTP flow is described as absent.

### Security / Business UAT case expansion (preparation only)

| Case | Role | Test | Engineering evidence / UAT result |
|---|---|---|---|
| SA-CR-01 | Visitor | CreateLocation HTTP pending request and private Stage | Isolated PASS / UAT NOT RUN |
| SA-CR-02 | Visitor | Same identity submitted twice | Isolated PASS (duplicate denied) / UAT NOT RUN |
| SA-CR-03 | Cross-org | Submit without team/org scope | Isolated PASS (denied) / UAT NOT RUN |
| SA-CR-04 | Visitor | Self-review of CreateLocation | Isolated PASS (denied) / UAT NOT RUN |
| SA-CR-05 | Leader/Admin | Independent first and final review, evidence and V1 publication | Isolated PASS / UAT NOT RUN |
| SA-CR-06 | Leader | Reject CreateLocation, abandon Stage/reservation | Isolated PASS / UAT NOT RUN |
| SA-CR-07 | Visitor | Pending invisible in Picker/favorite/trip/mileage/reports | Picker candidate CI PENDING; others HOLD / UAT NOT RUN |
| SA-CR-08 | Visitor/Admin | Live HR/team revocation, replay and concurrent duplicate requests | Existing partial tests; expanded Create-specific matrix HOLD |
| SA-EX-01 | Excel | No direct unreviewed Location Create | Fail-closed PASS / canonical import not implemented |
| SA-LG-01 | Legacy | No published direct note/identity write | Fail-closed candidate / complete matrix HOLD |
| SA-BG-01 | Background | No unpublished direct publication | Preliminary guard / complete matrix HOLD |



## Authoritative Scheme A recovery checkpoint — 2026-10-14 / Verified 0dfb975

**Recovery only**: `work/v180-optionc-final-gates-recovery-r1`

- Exact engineering SHA: `0dfb9757650d5276e9ee7c078c89ec227b60f8a5`
- GitHub Actions Exact-SHA: `38537429814 — SUCCESS`
- Backend main suite: **753 PASS**. Additional isolated Admin E2E: **1 PASS**.
  Canonical 011/012/014 disposable SQL rehearsal: **2 PASS**.
  Frontend: **24 test files PASS**, production build **PASS (no deploy)**.
- Real disposable SQL/DI/HTTP CreateLocation transaction exercised:
  Visitor submit with server-owned stable LocationId + Pending reservation /
  private Stage; independent live-appointed Leader review; Admin geocode /
  duplicate evidence; independent Admin final atomic V1 publication.
  The same test includes duplicate pending denial, self-review and cross-org
  denial, premature publication denial, independent Leader rejection to
  Abandoned Stage / reservation, canonical event audit and historical
  Trip/Snapshot reference preservation.
- Picker/search HTTP now tested: Pending reserved identity is not selectable;
  after approved publication it becomes visible; adding pending favorite is
  denied. CI `38535811600` confirmed this test after correction of its
  old JWT test fixture.
- **Bypass guards**: Excel Location Create Preview/Confirm is fail-closed;
  legacy published direct updates/notes fail-closed; background geocoding
  excludes all canonical reserved LocationIds; legacy draft update,
  deactivation, delete impact and permanent delete call the same
  additive-schema-aware reservation guard, including abandoned reservations.
  Ordinary unreserved legacy Pending drafts remain eligible.
- **Scope of evidence**: reservation guard is covered by isolated SQL/EF
  negative tests plus successful API build. A complete direct HTTP test for
  every legacy mutation endpoint, concurrent Create-vs-legacy request,
  all background job modes, and all operational trip/mileage/report queries
  is **not** yet claimed.
- **Remaining HOLD blockers**: comprehensive Create-specific concurrent
  submission / replay / revocation / rollback HTTP matrix; comprehensive
  Pending inclusion/exclusion audit in every trip, mileage, export and report;
  Excel canonical import workflow; legacy/background canonical routing (beyond
  present fail-closed safety); Visitor/Leader/Admin approval UI; exact UAT target
  identification, read-only target preflight, backup/restore evidence, DBA
  migration/dependency plan sign-off, authorized UAT deployment and Business UAT.
- **Authorization boundary**: no UAT/Production SQL writes or migrations, no
  UAT target preflight, no deployment, flag activation, promotion, merge, or
  Business UAT GO; migration 013 untouched. This is nondeploy engineering
  evidence, **not** UAT execution authorization or Business UAT success.
- **Owner decision**: **SCHEME A HOLD / NO-GO**. Older status fragments in
  these evolving release documents are historical and superseded by this
  Exact-SHA checkpoint.
