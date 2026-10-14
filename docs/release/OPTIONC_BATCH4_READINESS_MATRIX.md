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
