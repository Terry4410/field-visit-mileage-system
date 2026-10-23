# Package A — Owner-approved report export access policy (2026-10-17)

Status: **APPROVED POLICY PRINCIPLES / IMPLEMENTATION AND SECURITY ACCEPTANCE HOLD**  
Repository: `Terry4410/field-visit-mileage-system`  
Scope: Draft PR #43, **NONDEPLOY_ONLY**. This document is not authorization for a protected merge, UAT SQL, migration, deployment, permission elevation, or feature-flag change.

## Owner decision 1 — HR and sensitive columns

- HR access must be an **independently validated live role or capability**, not inferred from Admin, UI visibility, JWT alone, or use of a shared workbook implementation.
- Exports must enforce **server-side report-level, row-level, and column-level grants**, based on current HR status, effective role, organization, team, ownership, and historical authorization.
- Visitor may export only authorized own trip rows and explicitly permitted columns. Leader may export only authorized team rows and permitted columns. Admin remains bounded by assigned organization and any narrower effective grants. HR may receive sensitive personnel/access history **only after a separately defined, live-validated HR grant**.
- Personnel and employment histories, role/login and data-scope histories, location tax ID/address/master note, and trip notes, subsidy and correction fields require an explicit report-by-role column catalog before expansion. **No implicit grants** are approved by this policy.
- All sheets (business, raw, mapping and legacy) must obey the same authorized row/column projection. The raw sheet and `legacy=true` must not become bypasses. Direct API requests and parameter tampering must not widen scope.
- Unknown role, missing grants, ambiguous organization/team, or unsupported history must **fail closed**. No sensitive filters, tokens, SQL, or business rows in errors or audit logs.

**Still to be specified by engineering/Owner review:** exact HR capability naming, source-of-truth implementation and exact per-report/per-role field catalog. Do not invent these grants or release sensitive exports pending those decisions.

## Owner decision 2 — Leader historical trip/mileage exports

A historical row is exportable to a Leader only when **both** conditions hold:

1. The trip's authoritative **historical team assignment on the trip date** falls within the Leader's authorized historical business scope; and
2. At download time, the Leader still has **effective live management authority for that team**, with active HR/account/role eligibility.

Current membership alone does not prove historical entitlement; prior membership alone does not create permanent access. A requested team ID, visitor ID, date range, or token claim is never sufficient authorization.

Use trusted effective-dated history/snapshot and live scope in the server-side query. If reliable historical membership evidence is missing, contradictory or not supported by schema, block that row/export (fail closed); never fall back to all history.

Retain the existing denial of unbounded/past Leader export until the rule is implemented and verified by HTTP → real application DI → disposable SQL tests.

## Owner decision 3 — Visit Type ownership and export

Do **not** presume the Visit Type master is globally exportable. Before enabling its export, determine and document whether it is:

- system-global shared master data, with an explicitly approved global visibility policy; or
- organization-owned master data, with enforced organization scope.

Until ownership and query authorization are proven, keep the endpoint **blocked for all roles**. Return a controlled, sanitized, stable policy-denial API response rather than an unhandled exception/HTTP 500. Do not create an organization key or widen access by assumption.

## Required verification before Package A security acceptance

- Real HTTP/API → application services → disposable SQL, using independently authenticated Visitor, Leader, Admin, and HR (only once an HR grant actually exists), including anonymous and unauthorized requests.
- Cross-organization/team and other-user direct URL/parameter attacks; live HR, role, team and organization revocation during export.
- Effective-date boundary cases: transferred Visitor, former/current Leader, reassigned team, historical immutable Trip/Snapshot, current Leader authority revoked.
- All workbook modes: business/raw/field-map sheets, `legacy=true`, trip workbook, existing multi-table histories; ensure no unauthorized column/row leakage or formula injection.
- Visit Type policy denial (controlled status and code) and absence of cross-org leakage.
- Exact-SHA CI, reproducible synthetic fixtures, final-state SQL assertions and artifact digests.

## Compatibility and scope boundaries

Keep the single-table bilingual three-sheet format and legacy compatibility. The trip/mileage workbook and multi-table personnel/location history exports remain format HOLD until table boundaries, mappings, downstream consumers and column grants are verified. Do not flatten historical tables or break operational import/export templates.

Do not modify WORK A, WORK C, WORK D or protected branches. PR #43 stays Draft/Unmerged. No UAT SQL, deployment, migration, restore, privilege or flag changes. Issue #41 Package A machine handoff remains pending a legitimate Hub lane; do not impersonate another lane.

## Acceptance states

- Owner policy principles: **APPROVED** (2026-10-17).
- Implementation of HR role/capability and exact field grants: **HOLD**.
- Leader historical effective-date rule: **APPROVED POLICY / IMPLEMENTATION HOLD**.
- Visit Type ownership classification: **HOLD / DENY EXPORT**.
- Real cross-role HTTP/SQL security evidence: **HOLD**.
- Protected merge / UAT release: **NO-GO**.
