# Batch 4 readiness matrix delta

- OC-01 to OC-22 remain ready for Business User execution; technical candidate checks are PASS, Business execution is NOT RUN.
- MergeFields cases require the additive detail schema and merge-specific writer before any UAT approval.
- Geocoding failure/retry/duplicate evidence remain isolated Stage tests only.
- Frontend candidate route wiring is present but the approval flag remains OFF.


## Batch 5C evidence update — 2026-10-14

Engineering verification is complete through Recovery CI. Merge lost-ack evidence is disposable-only and does not authorize UAT/Production execution. UAT target preflight and Business UAT remain NOT RUN.


## Final Engineering Closure — 2026-10-14

Authoritative Recovery SHA `eb31fda053d8564a0d8aaa0dff2966966c41cb91`; exact-SHA CI `38507636467` SUCCESS. Engineering gates PASS in isolated disposable SQL/HTTP. Remaining HOLD: application-owned geocode/duplicate evidence transition and UAT target preflight. Business UAT remains NOT RUN; feature flags remain OFF.


## Evidence service update — 2026-10-14

Recovery SHA `05ebca0748da49ae88a4403b5365ab3e1a1427c4`; exact-SHA CI `38509844660` SUCCESS. Application-owned evidence HTTP/DI E2E is PASS for live authorization, Stage RowVersion/address binding, stale replay denial, revocation denial, PendingGeocode → PendingDuplicateReview → ReadyForPublish, and no Location publication. Remaining HOLD is the missing canonical immutable evidence-event contract in 012; UAT target preflight and Business UAT are NOT RUN. Flags remain OFF.


## Canonical audit contract closure — 2026-10-14

Recovery SHA `2084e6d470264826ac359fa91ce1c633bc30e475`; exact-SHA CI `38511099449` SUCCESS. Application-owned geocode and duplicate-review evidence now have canonical immutable event types and same-transaction audit writes in isolated SQL. EvidenceKey replay/collision rollback, RowVersion binding, live authorization, revocation and cross-scope protections pass. Engineering readiness for Owner review of a future UAT execution authorization: PASS. UAT target preflight: NOT RUN. Business UAT: NOT RUN. Flags and execution policies remain OFF.


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

**Verified exact source** \`708cdc5c81ab0d143724fcb42cf1e6b8a3911ccf\`
on \`work/v180-optionc-final-gates-recovery-r1\`.
**Exact-SHA CI** \`38586022534 — SUCCESS\`; backend **754 PASS**,
additional isolated Admin SQL E2E **1 PASS**, canonical 011/012/014
disposable SQL rehearsal **2 PASS**, frontend **25 test files PASS**
and build PASS. Recovery release gate remains explicit **NO-GO**.

### Completed in disposable OptionCIsolated SQL only

- A separate candidate-only HTTP command
  \`POST /api/v1/optionc-candidate/imports/{importBatchId}/submit-creates-for-review\`
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
- A real \`.csv\` HTTP upload is normalized to \`.xlsx\`, previewed, and
  submitted into the candidate workflow. Preview never treats a private
  pending reservation as NoChange; Create rows without team/address are
  blocked before submission. Abandoned historical identities do not
  falsely count as active unchanged Locations.
- Gated frontend AdminImportPanel displays the canonical reviewed-create
  action, counts and Request IDs. Flag
  \`OPTIONC_EXCEL_CREATE_UI_ENABLED=false\`, along with all other
  Option C approval/Create/Leader UI flags. Candidate backend flag remains
  OFF by default.

### Mandatory migration/authorization boundary

The canonical Excel flow requires the **additive status CHECK constraint
contract** \`1.8.0-EXCEL-CANONICAL-STATUS\` (not deployed). The current
\`docs/release/optionc-canonical-sql-draft/excel-import/
ExcelImportStatusContract.disposable.sql\` is a NONEXECUTABLE
UAT/Production draft explicitly restricted to disposable
\`OptionCIsolated_*\` databases. The backend fails closed if the marker
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
