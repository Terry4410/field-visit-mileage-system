# Package B3 approval/apply — security implementation contract (NONEXECUTABLE)

**WORK-only | Issue #24 | 2026-10-11 | NOT AN ACTIVATION AUTHORIZATION**

## Completed in candidate

- A pure `V180B3ApprovalApplyPreflight.RequireValidatedProposal` rejects
  unsupported request kind/operation/risk/organization, non-Pending state,
  stale source RowVersion, source snapshot drift, malformed JSON, extra,
  duplicate, missing or non-string fields, no-op and invalid proposed values.
- Unit negatives cover malformed versions, source changes, foreign
  organization, unauthorized operation and tampered proposal JSON.
- The pure `PrepareImmutablePlan` now composes reviewer/decision validation, active published Customer source provenance, request/source RowVersion checks and before/after JSON audit snapshots; it does not write data.
- Null or malformed source/request RowVersions fail closed with controlled errors.
- This preflight **does not authorize a reviewer, update a Location,
  geocode, commit a transaction or emit a decision event**.
- `V180B3ChangeRequestService.ApproveAsync` still explicitly denies ALL.
  Feature flag remains OFF. No migration or deployment has occurred.

## Atomic executor design required before B3 can be enabled

Within ONE SQL Server transaction, with separately verified live Admin
identity and organization, verify:

1. Approved 011 catalog schema (all trusted FK/CHECK, expected indexes,
   RowVersion, no enabled request/event triggers), exact version and
   current session HR/role; deny unknown identities and cross-org requests.
2. Lock Pending request and owned published Location consistently,
   verify source RowVersion and the full before snapshot. Confirm source
   is active, approved Customer Location in the expected organization/team.
   Independently re-evaluate current team/record provenance.
3. Deny self-review; require bounded review reason, nonempty DecisionKey
   and current request RowVersion. Enforce database unique DecisionKey.
4. Validate JSON with strict allowlist, current duplicate/location safety,
   geocoding state and historical references. Geocoding may provide
   evidence but **never** implicitly publishes.
5. Apply only supported, explicitly approved fields to the published
   Location with optimistic RowVersion concurrency; no implicit
   cross-tenant change, soft/hard delete or historical rewrite.
6. Persist immutable before/after audit, Approved decision event,
   Applied event and final status in the **same transaction**. Every
   failure (including event/audit FK/constraint, SQL deadlock or timeout)
   rolls back location, request, event and audit together.
7. Return 409 only for documented known named duplicate and stale
   RowVersion cases; never convert arbitrary SqlException to success
   or expose SQL internals. A retry must re-read and reauthorize; do not
   replay the same DecisionKey into a second approval.


## Transaction implementation sequence — reviewable WORK-only blueprint

This is a design checklist, not an executable approval implementation. The
current service must continue to return `B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED`.

| Order | Operation inside one serializable transaction | Mandatory failure behavior |
| --- | --- | --- |
| 0 | Read-only schema readiness before opening the transaction; feature flag OFF blocks earlier | Missing/partial/untrusted 011 or missing indexes => 503, zero writes |
| 1 | Begin transaction; obtain live HR/internal identity and active Admin role, scoped to the caller's organization | Revoked/expired role or employment => deny and rollback |
| 2 | Load exactly one Pending request by public GUID **and** organization; acquire a stable request lock and use the same lock order in all decision paths | Unknown/foreign GUID => no existence disclosure |
| 3 | Validate request RowVersion, DecisionKey uniqueness, independent reviewer, request type/risk/operation, and bounded reason | Stale request => conflict; self-review => deny |
| 4 | Load source Location in same organization; check creator/team/active/approved Customer provenance, current membership, RowVersion and full before snapshot | Changed ownership, team, HR scope, source version or field => rollback |
| 5 | Revalidate proposed fields, duplicate/merge/geocode/history invariants against current transaction snapshot | Unresolved duplicate or unsafe history => deny; geocode never auto-publishes |
| 6 | Perform optimistic seven-field Location update using expected RowVersion and organization/source predicates | Exactly one affected row required; zero or multiple => rollback |
| 7 | Persist request transition to Applied and both Approved/Applied decision events, same DecisionKey only where schema permits; persist immutable before/after audit | Any audit/event constraint failure => rollback entire transaction |
| 8 | Commit only after all invariants and writes succeed; then return sanitized request status | Unknown SQL failure => fail closed, no success response |

**SQL-specific review notes:** SQL Server `rowversion` values are generated
by the engine and must never be assigned from client input. The request and
Location concurrency predicates must use the bytes observed inside the same
transaction. Use a documented lock acquisition order for Reject and Approve
to reduce deadlocks. A unique DecisionKey on the audit table is the ultimate
replay barrier; application prechecks are insufficient. Validate the actual
schema before choosing whether Approved and Applied events may share a key;
do not assume a uniqueness model. No external geocoding/API call belongs
inside the database transaction. If a response is lost after commit, retry
must read the existing decision state and must not apply again.

**Exit criteria before proposing an executable candidate:** A reviewed SQL
schema/EF mapping, explicit audit event contract, exact Location update
allowlist, race-safe duplicate rule, safe geocoding separation, manager-grant
provenance attestation, and negative SQL tests for rollback/ambiguous retry.
Until then, a pure preflight plan is not proof of permission or transaction
safety.


## 2026-10-11 schema-to-executor incompatibility — blocking decision

The currently validated **011 candidate is rejection-only**, not an approval
schema. Its trusted CHECK contracts accept request statuses `Pending` and
`Rejected` only, and event types `Submitted` and `Rejected` only.
Therefore the atomic executor outlined above **cannot** be safely enabled
against the current 011 catalog: inserting `Approved` / `Applied` events
or changing a request to `Applied` must fail the database constraints.

The current `V180B3SqlSafetyRules.CatalogCheckSql` deliberately rejects
catalog definitions containing an `Applied` request status. **Do not
silently expand the existing 011 migration, weaken the read-only gate, or
reinterpret a successful disposable SQL fixture as approval readiness.**

Before an executor is eligible for implementation or activation, a separate
Owner/IT-reviewed **forward-only schema revision** must define and test:

1. An explicit lifecycle: Pending -> Rejected **or** Pending -> Applied,
   with no intermediate externally visible `Approved` state unless it
   has a defined durable recovery path. No return to Pending.
2. Exactly one decision event carrying a non-null globally unique
   `DecisionKey` for each terminal decision. If both `Approved` and
   `Applied` events are required, only one may carry that key under
   the existing unique filtered index; link both via a stable
   `CorrelationId` and immutable request ID instead of duplicating
   `DecisionKey`.
3. An exact trusted CHECK definition for each lifecycle state and
   event kind, including reviewer, timestamp, nonblank reason,
   `AppliedAt` and independent actor requirements; no nullable
   SQL CHECK `UNKNOWN` loopholes.
4. A read-only version-specific catalog probe that rejects old 011
   shapes, disabled/untrusted checks, changed event uniqueness,
   unexpected triggers and partial upgrades.
5. A separately authorized migration sequence with rollback/recovery
   plan, including idempotent handling of an interrupted deployment;
   never mutate UAT or production during this WORK-only design stage.
6. Disposable SQL Server integration tests proving valid apply,
   invalid transitions, concurrent decisions, event write failure,
   rollback, duplicate decision keys, and ambiguous-response retry.
   Only then schedule the independent B4 formal 22-case acceptance.

**Decision:** Existing WORK code and current 011 draft remain
`ApproveAsync = DENY ALL`, `B3 Enabled = false`. No SQL is authorized
for execution by this section.


## Forward-only approval schema contract — proposed IA, NOT DDL

**Compatibility:** The existing candidate `1.8.0-011` remains
rejection-only. A future separately numbered revision must not reuse
`1.8.0-011` or change the historical meaning of that version. The
revision identifier and upgrade path require Owner/IT approval.

| Object | Required proposed contract | Failure gate |
| --- | --- | --- |
| ChangeRequests.Status | Pending, Rejected, Applied only; Pending is the sole nonterminal state | No Approved half-state, no reverse transition |
| ChangeRequests.ReviewedByUserId / ReviewedAt / ReviewReason | All NULL for Pending; all required for terminal states; reviewer differs from requester; bounded nonblank reason | SQL CHECK must explicitly handle NULL/UNKNOWN |
| ChangeRequests.AppliedAt | NULL for Pending/Rejected, NOT NULL for Applied | No partially applied status |
| ChangeRequestEvents.EventType | Submitted, Rejected, Applied; immutable append-only | No unrecognized event |
| ChangeRequestEvents.DecisionKey | NULL for Submitted, required for Rejected/Applied; one key per terminal decision | Filtered UNIQUE index enforces replay prevention |
| ChangeRequestEvents.CorrelationId | Required for every event; stable correlation for one atomic apply | Audit evidence cannot be disconnected |
| Locations.RowVersion | Engine-generated, compare expected version under same serializable transaction | Stale version => zero writes |
| Organization/team provenance | Live HR/Admin authorization, submitter membership, source org/team/creator checked under transaction | Revocation or cross-tenant drift => rollback |

**Proposed terminal write order (same transaction):**
Acquire request lock, then location lock; reauthorize; validate exact
request and location versions, JSON snapshots, geocode and duplicate
safety; update the seven allowlisted Location fields using the locked
source RowVersion; mark request Applied with reviewer/reason/timestamps;
insert exactly one Applied decision event with DecisionKey, source and
result snapshots; commit only after every write succeeds. If any write
fails, rollback all. There must be no intermediate committed Approved
state, and a retry must re-read the terminal state without applying
again.

**Transition matrix (candidate, not executable):**

| Current state | Action | Proposed state | Required outcome |
| --- | --- | --- | --- |
| Pending | Reject | Rejected | Existing independently reviewed rejection path |
| Pending | Apply | Applied | Future authorized atomic executor only |
| Pending | Submit duplicate | Pending unchanged | Unique pending index rejects duplicate |
| Rejected | Reject / Apply | Rejected unchanged | Deny, no new decision event |
| Applied | Reject / Apply | Applied unchanged | Deny/replay-safe response, no second update |
| Any | Unknown operation or foreign org | Unchanged | Deny without disclosure |

**Required SQL Server proof:** trusted CHECK constraints must be
evaluated against both NULL and malformed values; unique DecisionKey
must reject concurrent replay; failure to insert audit/event must
rollback location and request; stale RowVersion and revoked reviewer
must prevent writes; two concurrent reviewers cannot both commit.
The disposable SQL fixture is *not* equivalent to a formally approved
migration chain or B4 22/22 UAT.

**Open decisions for Owner/IT before any executable DDL:**
1. Choose the forward migration version and preflight predecessor chain.
2. Approve whether a single Applied decision event with before/after
   JSON is sufficient for immutable audit, or whether a second event
   without DecisionKey is required.
3. Define the authoritative duplicate/merge and geocoding safety gate,
   including which persisted evidence is considered valid.
4. Approve the effective manager-grant provenance attestation and
   test identities. VAL-B1-002 remains HOLD until independently proven.
5. Approve executable SQL and migration deployment separately; no
   inferred permission from this specification.

## Isolated verification still required

- Real disposable SQL Server 2022: one successful independent approval
  and atomic publish, simultaneous independent reviewers, DecisionKey
  replay, stale source/request, role revocation, failed audit/event write,
  transaction rollback, SQL deadlock/timeout, retry after ambiguous
  response, immutable snapshot and cross-org access.
- JWT HTTP E2E with real role/HR/team state and UI positive journey.
- Full B4 formal 22/22 acceptance on the approved target environment.
- Independently attested VAL-B1-002 leader grant; never infer authority
  from seeded historical assignments.
- Formal 1800_011 remains NONEXECUTABLE until separately authorized.
  Do not use EnsureCreated test schemas as migration-chain evidence.

**GO/NO-GO:** FULL PACKAGE B UAT = NO-GO. B3 OFF,
Approve/Apply DENY ALL, B4 0/22, Business UAT HOLD,
Production HARD HOLD.


## Owner 2026-10-11 controlled-completion authorization — execution boundary

The Owner authorized autonomous WORK-branch implementation, GitHub commits,
CI retries, disposable SQL Server testing, B2/B3/B4 hardening, evidence
mapping and IT handoff preparation. This **does not authorize** applying
1800_011, executing any forward migration against UAT/production, deploying,
promoting, enabling B3, or lifting any HOLD. Approval/apply must continue to
deny every call while the feature is off or the executor/schema remains
unapproved. The authorized design baseline is Pending -> Rejected / Applied,
with one atomic terminal decision and immutable evidence; the existing
rejection-only 011 contract remains unchanged.

### Single consolidated external decision packet (do not infer consent)

| External gate | Evidence / explicit decision required | Until received |
| --- | --- | --- |
| Forward-only schema | Owner/IT approve distinct version, lifecycle CHECKs, unique DecisionKey and correlation/audit contract, migration predecessor, failure recovery | Prepare nonexecutable DDL/design only; no DB execution |
| Manager grant VAL-B1-002 | Independent HR/IT attestation of effective role assignment, provenance, validity period and actual denied/allowed API cases | Leader elevated writes denied; grant HOLD |
| Publication safety | Owner/IT sign off duplicate/merge policy, geocode-to-publish separation, historical reference integrity and seven-field update allowlist | Approve/apply DENY ALL |
| Formal UAT environment | Authorized endpoint, test identities, verified HR/team records, approved migration plan and execution window | Disposable SQL evidence is partial; B4 formal 0/22 |
| Release activation | Separate approval for branch promotion, UAT backend/frontend deployment, feature flag and Business UAT start after evidence review | B3 OFF; Business UAT HOLD; Production HARD HOLD |

**Automation completion rule:** a green WORK CI is technical evidence, never
a B4 formal acceptance or release GO. Report each of the 22 scenarios with
its real API/HR/SQL execution and after-state evidence, identify independent
approver and exact source SHA, and keep any missing proof as PENDING.
Only independently authorized formal acceptance may change 0/22 or HOLD
status. Never invent test identities, production credentials, approver
attestations or environment permissions.


## Conditional UAT preparation authority — Owner, 2026-10-11

The Owner additionally authorizes **preparation only** for UAT migration,
backend/frontend deployment and the formal B4 22-case acceptance. This
extends the existing controlled WORK authorization to planning, dry-run
validation in disposable environments, deployment manifests, runbooks,
rollback/forward-recovery analysis, evidence mapping, and a consolidated
final execution-approval packet. It is **not** an instruction or permission
to execute any UAT migration, deploy, promote, turn B3 on, seed or change
formal UAT records, or change a release HOLD.

### Final combined execution approval packet — required before any UAT change

The execution request must identify, in one reviewable package:

1. Exact repository branch, candidate commit SHA/tree, immutable artifact
   versions, diff versus the protected baseline, and green required CI.
2. Reviewed SQL scripts, dependency order, predecessor/schema version,
   catalog and constraint checks, data impact, estimated lock/downtime,
   idempotency/failure behavior, backup/restore ownership and recovery steps.
3. Exact UAT database/server/environment target and authorized execution
   identity; never print or commit credentials, tokens or connection strings.
4. Backend/frontend release artifacts, deployment order, health checks,
   post-deploy smoke checks and safe application rollback plan.
5. Formal B4 22-case matrix with expected HTTP/SQL/HR after-state,
   independently authorized test actors, evidence owner and acceptance
   signatures; disposable fixture results are not formal UAT proof.
6. VAL-B1-002 independent HR/IT manager-role provenance and effective-date
   attestation, or an explicit unresolved HOLD.
7. B3 activation decision as a separate explicit yes/no gate after security
   and schema evidence; default remains OFF, Approve/Apply DENY ALL.
8. Go/no-go criteria, stop triggers, incident contacts, remaining risks,
   and an unambiguous Owner approval field for each proposed UAT action.

Until that packet is approved, **NO UAT SQL execution, NO deployment,
NO promotion, NO B3 activation, NO release GO**. Production remains
HARD HOLD regardless of any UAT decision. A conditional authorization
cannot substitute for independent HR/IT attestation.
