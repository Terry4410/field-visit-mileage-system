# Package B3 approval/apply — security implementation contract (NONEXECUTABLE)

**WORK-only | Issue #24 | 2026-10-11 | NOT AN ACTIVATION AUTHORIZATION**

## Completed in candidate

- A pure `V180B3ApprovalApplyPreflight.RequireValidatedProposal` rejects
  unsupported request kind/operation/risk/organization, non-Pending state,
  stale source RowVersion, source snapshot drift, malformed JSON, extra,
  duplicate, missing or non-string fields, no-op and invalid proposed values.
- Unit negatives cover malformed versions, source changes, foreign
  organization, unauthorized operation and tampered proposal JSON.
- The pure `PrepareImmutablePlan` now composes reviewer/decision validation, active published Customer source provenance, request/source RowVersion checks and before/after JSON audit snapshots; it does not write data.\n- Null or malformed source/request RowVersions fail closed with controlled errors.\n- This preflight **does not authorize a reviewer, update a Location,
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
