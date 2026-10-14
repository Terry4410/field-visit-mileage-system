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


## Scheme A verified nondeploy checkpoint — 2026-10-14

**Authoritative verified engineering source:** `work/v180-optionc-final-gates-recovery-r1`
at `21e12c8342b86bea99eccb1cb1d4dee78b152b23`.
**Exact-SHA Recovery CI:** `38553015599 — SUCCESS`.
Backend main suite **753 PASS**, separate Admin isolated E2E **1 PASS**,
canonical 011/012/014 disposable SQL rehearsal **2 PASS**;
frontend **24 test files PASS** and nondeploy production build PASS.
This is isolated engineering evidence, **NOT** UAT readiness authorization.

Verified isolated CreateLocation path: Visitor HTTP submission → server-owned
LocationId → inactive Pending reservation and private Stage → independent,
live-appointed Leader first-level review → independent authorized Admin Stage
read and geocode/duplicate evidence → final reviewed atomic publication →
Version 1 current, canonical events and historical Trip/Snapshot references.
Denied: self-review, cross-organization, premature Admin Stage/evidence,
premature publication, unreviewed Picker/favorite access, duplicate Pending
submission and simultaneous same-identity HTTP creates (exactly one commits).
Rejected request abandons Stage/reservation without publishing; a new request
can resubmit the same identity with a **different LocationId** while retaining
its rejected historical evidence.

Safety controls: Excel Location Create fail-closed; Excel Pending Update
Preview, Confirm preflight and apply-time block canonical reserved LocationIds;
legacy published note/identity writes fail-closed; legacy draft
update/deactivate/delete blocked for reserved CreateLocation IDs; background
geocoding ignores all reserved IDs. Ordinary *unreserved* legacy drafts remain
eligible. This is NOT completed canonical Excel/Legacy/Background routing.

Candidate frontend includes gated Create/my-requests, authenticated Leader
first-level review and Admin evidence/reviewed publication UI. The flags
`OPTIONC_APPROVAL_UI_ENABLED`,
`OPTIONC_LEADER_REVIEW_UI_ENABLED`,
`OPTIONC_CREATE_LOCATION_UI_ENABLED` are all **false**; candidate backend
is also isolated-only and disabled by default. No Browser Business UAT run.

**Unresolved mandatory HOLD blockers**: Excel canonical import routing;
exhaustive Legacy/Background route-level HTTP fail-closed matrix; all
trip/mileage/report/export Pending visibility and reference enforcement;
Create-specific role/team/HR revocation, concurrent cancel/review and
commit-ack replay/rollback expansions; browser role-scope Business UAT; full
security and migration signoff, target identification/read-only preflight
and backup/rollback proof. Keep Scheme A **HOLD / NO-GO**.

No UAT/Production migration or SQL write, no UAT target preflight, no deploy,
feature activation, promotion/merge or Business UAT GO was performed in this
Recovery batch. Migration 013 remained untouched. Earlier checkpoints in this
document are retained for history but superseded by this verified SHA.

### Scheme A isolation security negative matrix (engineering only)

| Case | Expected security invariant | Evidence |
|---|---|---|
| Visitor self Leader-review | Denied | isolated HTTP PASS |
| Cross-org Create | Denied | isolated HTTP PASS |
| Two simultaneous identical Create | Exactly one pending identity, no extra Request/Stage | isolated HTTP PASS |
| Admin Stage read before Leader | Denied | isolated HTTP PASS |
| Admin geocode before Leader | Denied | isolated HTTP PASS |
| Visitor Stage read | Denied | isolated HTTP PASS |
| Approved Create Picker visibility | Pending hidden, Approved visible | isolated HTTP PASS |
| Leader rejects Create | Stage/reservation Abandoned; new request permitted | isolated HTTP PASS |
| Excel create/published update | No direct write | candidate guard; full HTTP matrix HOLD |
| Excel update of reserved pending | Preview and Confirm blocked | guard + CI; HTTP matrix HOLD |
| Background/Legacy reserved writes | Blocked | SQL/EF + CI; route-level matrix HOLD |
| Create reviewer/requester revocation | Fail closed | partial existing tests; full Create HTTP matrix HOLD |
| Create replay / lost commit acknowledgement | Exactly-once and reconcilable | existing writer coverage; Create HTTP expansion HOLD |
| Trip/mileage/report pending references | No leakage | Picker/favorite proven; other paths HOLD |


## Scheme A controlled engineering checkpoint — 2026-10-14 / Legacy HTTP and Create revocation

**Last exact engineering SHA:** `8e0c30ab78bb4fcffd9d2a9df1b946be2b4df866`
on the **only** authorized Recovery branch
`work/v180-optionc-final-gates-recovery-r1`.
**Exact-SHA GitHub Actions Run:** `38560339144 — SUCCESS`.
Nondeploy CI log: backend **753 PASS**, separate Option C Admin SQL
E2E **1 PASS**, canonical 011/012/014 disposable rehearsal **2 PASS**,
frontend **24 test files PASS**, frontend build PASS, explicit NO-GO gate PASS.

### New verified safety delta

- Under the *verified additive* `1.8.0-CREATE-LOWRISK` contract,
  `POST /api/v1/managed-locations` can no longer directly create an
  unreviewed Customer Location. Expected error:
  `LOCATION_LEGACY_CREATE_REQUIRES_CANONICAL_REVIEW` (422).
  Old databases **without** this additive contract retain the existing
  legacy draft behavior; this is not an assertion of UAT/Production
  readiness or current deployment.
- A valid org/team-scoped HTTP actor is used for the Legacy create denial,
  and an org/team-scoped Admin for Update / delete-impact / permanent
  delete denial against a canonical reserved LocationId.
  SQL assertions confirm no unauthorized Location or Request insertion,
  no source RowVersion drift and no reservation mutation.
- CreateLocation HTTP test revalidates *live* Leader appointment,
  requester HR status and Admin role following successful independent
  Leader approval. Revoked actors cannot obtain a privileged Stage
  read; Stage remains Pending and no GeocodeEvidenceRecorded is emitted.
- Leader-originated CreateLocation is allowed to enter Pending, but
  a sole appointed Leader cannot self-review and Admin cannot bypass
  the missing independent Leader review, publish or reject through
  the generic Admin endpoint. No published Version is created.
- Canonical Create/LowRisk/HighRisk/MergeFields coverage and historical
  preservation from earlier verified E2E remain regression-tested.
  Pending Picker exclusion, Excel Preview/Confirm reservation guard,
  Background geocoder reservation exclusion and old draft write guards
  remain on the Recovery branch.
- New negative cases use disposable `OptionCIsolated_*` SQL Server
  databases, not UAT or Production. Status = engineering validation,
  **not** authorized Business UAT.

### Remaining HOLD — do not infer GO

1. Excel true canonical Request+review batch routing (current Customer
   Create must fail closed).
2. Exhaustive Legacy/Background HTTP job-route audit including update
   races and all location categories.
3. Pending-location exclusion throughout Trip, mileage calculation,
   exports, report queries and reconciliation (Picker alone insufficient).
4. Create-specific lost-commit replay/rollback and all concurrent
   reviewer/revocation permutations.
5. Browser-tested Visitor/Leader/Admin UI and cross-team role
   matrix; frontend candidate approval flags still OFF.
6. DBA-reviewed UAT schema/dependency/migration sequence, exact UAT
   target, backup/restore and separate Owner execution authorization.

**Restrictions unchanged:** migration 013 untouched; no UAT/Production
SQL writes, migration, deployment, promotion, feature enablement,
target preflight or Business UAT GO. **SCHEME A HOLD / NO-GO.**
Earlier checkpoint paragraphs remain historical and are superseded
by this verified Exact-SHA checkpoint.


## Authoritative new read/write bypass checkpoint — 2026-10-14

**Verified code SHA:** `27b084d016eda41fbd2dda5555aa99da392c7377`,
**Exact-SHA CI:** `38564555552 — SUCCESS`.
Recovery branch only: `work/v180-optionc-final-gates-recovery-r1`.

New protections established and SQL/HTTP tested:

- `GET /api/v1/managed-locations` and
  `/managed-locations/search`: no private Pending/Abandoned
  canonical CreateLocation reservation rows; successful Published rows
  reappear after independent review.
- `GET /api/v1/locations/pending`: excludes canonical private
  reservation rows. Inactive ordinary non-reserved drafts remain
  eligible. The dedicated SQL fixture adds minimal old Trip columns
  only within `OptionCIsolated_*` to exercise this legacy route.
- `PUT /api/v1/locations/{id}` and
  `POST /api/v1/locations/{id}/promote`: canonical reserved
  LocationIds fail closed. Already Approved non-reserved locations
  also fail closed via `EnsureUnpublishedDraft` before source mutation.
  RowVersion stays unchanged in the negative HTTP cases.
- Excel Locations template now states that Create is blocked until
  canonical Excel routing and that published/reserved updates cannot
  bypass review.
- Isolated CreateLocation HTTP/SQL already proves private Pending
  Picker exclusion and approved publication inclusion; normal Trip
  save requires Active + Approved and Trip Context requires the same.
  Complete runtime report, mileage, import and all Trip request
  combinations are **not yet** claimed.

Additional positive compatibility test in next SHA confirms unreserved
legacy Pending drafts continue to appear to authorized users in
maintenance and Pending worklists, while canonical reserved items do
not. Do not count this new positive assertion as PASS until its own
Exact-SHA CI succeeds.

**HOLD blockers:** Excel canonical batch submission; exhaustive
Legacy/Background API and concurrency matrix; complete Trip/mileage/
report/export Pending reference E2E; Create lost-commit replay and
revocation permutations; browser role UAT; UAT schema + DBA migration
dependency review and explicit Owner deployment authorization.
All flags OFF, no main/post-uat writes, no migration 013 changes,
no UAT/Production SQL writes or deployments, no UAT-target preflight,
no Promotion/Merge, no Business UAT GO. **SCHEME A HOLD / NO-GO.**


## Scheme A authoritative Excel canonical Create review checkpoint — 2026-10-14

**Verified exact source** `708cdc5c81ab0d143724fcb42cf1e6b8a3911ccf`
on `work/v180-optionc-final-gates-recovery-r1`.
**Exact-SHA CI** `38586022534 — SUCCESS`; backend **754 PASS**,
additional isolated Admin SQL E2E **1 PASS**, canonical 011/012/014
disposable SQL rehearsal **2 PASS**, frontend **25 test files PASS**
and build PASS. Recovery release gate remains explicit **NO-GO**.

### Completed in disposable OptionCIsolated SQL only

- A separate candidate-only HTTP command
  `POST /api/v1/optionc-candidate/imports/{importBatchId}/submit-creates-for-review`
  consumes validated legacy Excel/CSV preview Create rows; it does not
  invoke unreviewed legacy Confirm or enable any UAT endpoint.
- Each row commits server-owned LocationId, **inactive Pending** Location,
  canonical HighRisk CreateLocation Request, Reservation, private Stage,
  Submitted event and Excel ImportBatchItem status in one **Serializable**
  transaction. Event details preserve ImportBatchId and ImportBatchItemId.
- Batch preflight refuses cross-owner/cross-org, mixed Update,
  unexpected errors, missing team or physical address. Subsequent live
  HR/role/team authorization is performed by the canonical Create flow.
- Two-row successful batch has exactly two independent Requests/Stages
  and Submit events; subsequent retry recovers the same Request IDs
  using canonical audit without making new identities.
- A second-row pending duplicate fails closed after a valid first row.
  The first row stays traceable as PartiallySubmittedForReview; failed
  second row creates no Request/Reservation/Stage. Retrying does not
  duplicate the first row.
- A real `.csv` HTTP upload is normalized to `.xlsx`, previewed, and
  submitted into the candidate workflow. Preview never treats a private
  pending reservation as NoChange; Create rows without team/address are
  blocked before submission. Abandoned historical identities do not
  falsely count as active unchanged Locations.
- Gated frontend AdminImportPanel displays the canonical reviewed-create
  action, counts and Request IDs. Flag
  `OPTIONC_EXCEL_CREATE_UI_ENABLED=false`, along with all other
  Option C approval/Create/Leader UI flags. Candidate backend flag remains
  OFF by default.

### Mandatory migration/authorization boundary

The canonical Excel flow requires the **additive status CHECK constraint
contract** `1.8.0-EXCEL-CANONICAL-STATUS` (not deployed). The current
`docs/release/optionc-canonical-sql-draft/excel-import/ExcelImportStatusContract.disposable.sql` is a NONEXECUTABLE
UAT/Production draft explicitly restricted to disposable
`OptionCIsolated_*` databases. The backend fails closed if the marker
and enabled ImportBatches/ImportBatchItems status constraints are missing.
This prerequisite must be included in a future DBA-reviewed migration
dependency plan with exact target/schema proof, backups, rollback and
separate Owner authorization. Never enable frontend/backend flags first,
and never replace status constraints directly on UAT/Production.

### Remaining HOLD

- Excel **published Location Update** and broader mixed-operation
  canonical batch routing remain unimplemented; such rows continue to
  fail closed. Excel Create is reviewed submission only, not publication.
- Complete route-level Excel/Legacy/Background write-path matrix,
  all live Trip/mileage/report/export pending exclusion and historical
  read invariants, remaining Create concurrency/unknown-commit
  permutations and Browser role-based Business UAT remain incomplete.
- No UAT/Production SQL writes, migrations, target preflight, deploy,
  feature activation, promotion/merge, or Business UAT GO was performed.
  Migration 013 untouched. **Scheme A HOLD / NO-GO.**
