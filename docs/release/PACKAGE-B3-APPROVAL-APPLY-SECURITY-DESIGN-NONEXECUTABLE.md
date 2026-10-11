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
reinterpret a successful 55-case disposable fixture as approval readiness.**

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
