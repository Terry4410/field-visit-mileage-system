# UAT010 × Package B — Canonical schema and migration collision registry

**Status: NONEXECUTABLE DESIGN; Owner selected Option C, NOT Migration approval.**

| Version | Original candidates | Option C decision | Deployment |
|---|---|---|---|
| 1.8.0-010 | Existing Visitor-only UAT schema | Reported UAT baseline; catalog preflight still required | Previously deployed; no action here |
| 1.8.0-011 | UAT010 local ZIP `1800_011_uat010_location_change_governance` **versus** Package B `database/candidates/1800_011_b3_change_requests_candidate` | **COLLISION.** Retire UAT010 standalone path; canonical `ChangeRequests` and `ChangeRequestEvents` only. Source Package B draft needs new Option C reviewed immutable SQL. | HOLD |
| 1.8.0-012 | Package B `1800_012_b3_atomic_apply_candidate` | Draft extension for Applied, Approved/Applied events and DecisionKey. Requires compatibility review with unified 011. | HOLD |
| 1.8.0-013 | Package B `1800_013_val_b1_002_leader_grant` | RESERVED. Do not reuse. | HOLD |

## Canonical runtime contract
- `dbo.ChangeRequests` is the **only** request table; no parallel `dbo.LocationChangeRequests`.
- `dbo.ChangeRequestEvents` append-only audit; filtered unique Pending per (OrganizationId, EntityKind, EntityId); filtered unique DecisionKey.
- Current Option C source includes a copy of Package B **disabled** B3 domain/EF/preflight/executor candidate. **No B3 controller or service DI registration**, `PackageB:B3:Enabled=false`, `OptionC:Approval:Enabled=false`, `ApproveAsync` throws `B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED`.
- The isolated baseline SQL Server fixture exercises existing model invariants only. It does **not** run Option C 011/012 Up/Verify or atomic apply concurrency.
- No SQL migration candidate is included as an executable workflow step. GitHub CI is nondeploy and rejects changes under `database/migrations/` or `database/candidates/`.

## Before creating the reviewed Option C 011/012 candidate
1. Owner/IT confirm exact schema contract and published-location version/swap policy.
2. Read-only Azure UAT preflight: actual latest SchemaVersion, tables, indexes, constraints, triggers, permissions and drift.
3. Compare UAT010 ZIP against Package B 011/012/013; calculate immutable SQL SHA-256 for approved candidate and rollback.
4. Rehearse Up/Verify/reject rerun/partial drift/unique race/atomic request-location rowversion/rollback on disposable SQL Server 2022.
5. Require separate explicit Owner UAT migration approval; then Backend and Frontend approvals, each exact-SHA gated. Production stays HOLD.

**DO NOT** run UAT010 standalone 011, overwrite Package B scripts, promote to `post-uat/v1.8.0`, or dispatch Azure migration from this branch.


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

### Migration dependency / environment restriction

Disposable-only evidence exercises canonical draft 011/012/014 and additive
Leader Review / CreateLowRisk contract. This does NOT establish UAT schema
compatibility or migration sequencing approval. Migration 013 must not be
changed; no UAT/Production migration executed. Require explicit Owner and
DBA review of actual target schema, index/constraint proofs, backup/rollback
plan and exact execution sequence before any separate future UAT authorization.
Any UAT target preflight remains NOT RUN and not authorized.
