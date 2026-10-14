# Option C Location write-path matrix — Batch 2

Scope: `Locations` identity/address/geocode fields and mutations of references to
those rows. This is a source inspection record, not a claim that the Option C
workflow is deployed. `ChangeRequests` remains a candidate schema and all
runtime flags remain off.

| Path | Fields or effect | Guard / finding | Batch 2 disposition |
|---|---|---|---|
| `V170LocationRepository.UpdateMaintenanceAsync` | City, district, address, Plus Code, tax ID, master note, geocode state | Previously wrote every field directly after RowVersion check. | Published location identity/address/tax changes now fail explicitly with `LOCATION_HIGH_RISK_CHANGE_REQUIRES_OPTIONC_APPROVAL`; low-risk master note edits remain direct and retain RowVersion protection. A functional request-submission route is still HOLD because candidate schema/policy is not deployable. |
| `V170LocationRepository.MergeAsync` (`MergeFields`) | Survivor name/type/team/address/tax and source merge state | Admin merge preview and RowVersion checks existed; published survivor fields could still be overwritten. | A merge touching an approved published source or survivor is denied pending canonical approval. Reference-only merge mode retains its existing behavior. |
| `V160FinalRepository.CreateManagedLocationAsync` | New identity/address fields | Creates inactive, `Pending` row; geocoding is pending. | Draft create, not a published Location overwrite. Retained. |
| `V160FinalRepository.UpdateManagedLocationAsync` | Name/team/type/address/tax and active state | `V180OptionCLocationWriteRules.EnsureDirectDraftUpdate` limits this path to drafts; RowVersion enforced. | No published write path found. Retained. |
| `V160FinalRepository.DeleteManagedLocationAsync` | Deletes a Location row | `EnsureUnpublishedDraft` plus reference impact checks. | Published/historically referenced rows cannot be directly deleted. |
| `WorkbookImportService.ConfirmAsync` — Location import | Creates new pending rows; existing updates include identity/address | Existing row is rejected when `ApprovalStatus == Approved` or active; pending update resets geocode/approval and deactivates. | Published direct update guard already present. |
| `WorkbookImportService.ConfirmAsync` — ProjectLocations import | Creates/updates project-to-location links | Changes project references, not Location identity/address. | Not a Location publication write. |
| `BackgroundJobService` geocoding | Latitude/longitude, geocode state/time | Previously rewrote coordinates on the same row while retaining Approved/Active state. | Confirmed bypass fixed: approved locations are skipped with an explicit Option C required result; only unpublished drafts proceed. |
| `V180MasterDataAdminRepository` | Deployment-site/location assignments, relocation history | Reads Location rows and writes assignment/history entities; no Location master-field assignment found. | No Location identity publication bypass found. |
| `V180MasterDataBulkWorkbookService` | Deployment-site LocationCode resolution/assignment | Resolves Location and changes site/location assignment records, not Location master fields. | No Location identity publication bypass found. |
| `V170ProjectLocationAdminRepository` | Project-to-location links | Writes `ProjectLocations`; Location rows are read for scope/eligibility. | Not a Location publication write. |
| `V180B3ChangeRequestService.SubmitAsync` / Option C writer | Canonical proposal and reviewed publication | Candidate service verifies schema but `PackageB:B3:Enabled=false`; publication path is isolated-test only. | No deployed endpoint/DI activation. This prevents unsafe substitution of test-only evidence for live authorization. |

## Remaining boundary

The high-risk maintenance/merge paths now fail closed rather than persisting an
unapproved Location mutation. They cannot yet create and return a canonical
ChangeRequest through the normal application API: the production schema and
UAT execution policy are not authorized, while the available Option C writer
requires an `OptionCIsolated_*` database and the isolated actor fixture. The
maintenance response therefore reports that approval is required and does not
claim the edit was saved. A future batch must provide the reviewed UAT schema,
live evidence adapter and transaction-owned submission service before enabling
that route.


## Superseding Scheme A Create/Excel write-path delta — 2026-10-14

- `V180OptionCCandidateService.CreateAsync` and the candidate POST
  `/api/v1/optionc-candidate/locations` implement an **isolated-only**
  transaction-owned Pending Location + canonical Request + Reservation +
  private Stage. It does not publish until independent Leader/Admin decisions.
  Exact-SHA `5ca1060423e9ef326b82b6cd6c1025250a0d578a`, CI
  `38534680973 — SUCCESS`.
- `WorkbookImportService`: the older row in this matrix described raw
  Pending Location creation as permitted. Under Scheme A, Excel `Create`
  must **not** do that. Preview now marks Create as
  `LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW`, and Confirm refuses
  it before processing a batch. Canonical Excel submission is a remaining
  implementation blocker.
- This delta does not authorize UAT/Production execution or feature flags.

## Background geocoding reservation boundary — 2026-10-14

Legacy `BackgroundJobService.ProcessGeocodingAsync` originally selected all
Pending, inactive Locations and could update the server-owned RowVersion of an
unpublished Scheme A CreateLocation reservation. The Recovery candidate now
requires catalog plus additive version proof and excludes **every** LocationId
referenced by `LocationCreateReservations` from its eligible geocoding drafts.
Abandoned reservations are also excluded; valid unreserved legacy drafts still
run. On older databases with no additive contract, it avoids referencing the
missing table. Partial candidate schema fails closed.

An additional disposable SQL / EF query regression checks reserved exclusion
and legal legacy draft eligibility. This change is a nondeploy safety control,
not Excel routing, UI readiness or Business UAT clearance. Latest verified
Picker + CreateLocation full HTTP E2E: SHA
`57d3faee175cd178a3481228159069efc596cf7c`, exact-SHA CI
`38535811600 — SUCCESS`. New Background test remains unverified until
its own Exact-SHA CI completes. HOLD / NO-GO.

## Legacy draft reserved-identity guard — 2026-10-14

Source inspection found that `V160FinalRepository`'s legacy
`UpdateManagedLocationAsync`, `DeactivateManagedLocationAsync`,
`GetManagedLocationDeleteImpactAsync` and `DeleteManagedLocationAsync`
could otherwise treat the same Scheme A CreateLocation reserved row as an
unpublished ordinary draft. An additive-schema-aware
`V180OptionCCreateReservationGuard` now fails closed before such operations
on **any** LocationId ever used by a CreateLocation reservation (including
Abandoned) while permitting unreserved legacy drafts. The same optional-schema
catalog proof is now shared with background geocoding; no missing-table SQL
query is compiled on older databases. Disposable SQL tests assert reserved
denial and unreserved draft compatibility.

Verified prior Background fence exact SHA
`d89ee8cefbc50984a72275e9b6e55a571ddcd088`,
CI `38536671419 — SUCCESS` (753 backend + 1 Admin E2E +
2 SQL rehearsal; frontend 24 files). This new legacy-guard change must
obtain its own Exact-SHA CI before a PASS claim. UAT/Production HOLD.

## Excel reserved-Pending update path — 2026-10-14

Source review identified a pending CreateLocation RowVersion bypass in the
legacy `WorkbookImportService`: Excel Update could target an inactive
Pending LocationCode already bound to `LocationCreateReservations`.
The Recovery-only patch invokes the same additive-schema-aware reservation
guard during **Preview**, during whole-batch **Confirm preflight** and
immediately before applying each Location Update. Preview displays
`LOCATION_CANONICAL_CREATE_RESERVATION_REQUIRES_REVIEW` for affected
rows; Confirm fails before batch writes or before a raced row mutation.
The existing fail-closed Excel Create policy and published Update denial
remain unchanged; unreserved legacy drafts remain eligible. The guard
does not replace full canonical Excel review routing; concurrency and real
Excel HTTP E2E are still HOLD until demonstrated.
Prior verified CI: `38537883997 — SUCCESS` (SHA
`9c32eca3c876755d3f8f660ec8f81f1ad6bdf359`).
This new Excel code is not PASS until its own Exact-SHA CI completes.
No UAT/Production changes or migrations.


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

## Scheme A legacy HTTP Create fail-closed delta — 2026-10-14

The old `POST /api/v1/managed-locations` path made inactive Pending
Customer Locations without canonical ChangeRequest, Reservation or
independent Leader/Admin review. On database schemas with the verified
`1.8.0-CREATE-LOWRISK` additive contract it is now **fail-closed**
(`LOCATION_LEGACY_CREATE_REQUIRES_CANONICAL_REVIEW`), rather than
claiming an unreviewed Pending row is a canonical submission.
No-change legacy database schemas without the additive contract keep
the prior draft path; non-Customer location categories are out of this
Customer Create scope. An isolated real HTTP/SQL negative test confirms
that legacy Create is rejected without a committed Location or Request.
This does NOT implement the full Legacy canonical routing or user-facing
business UAT. Flag remains OFF. Last verified before this patch:
`9d43b89cefae0042b37f6b709e10e8d861ec6933`,
Exact-SHA CI `38553823661 — SUCCESS`; the new delta must pass its
own Exact-SHA CI before acceptance. UAT/Production not touched.


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
