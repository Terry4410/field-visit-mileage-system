# NEXT WORK TASK

## Model
Use **GPT-5.6 Luna**.

Reason: the architecture decision is now complete. Human read-only SQL inspection confirmed the current UAT `MileageRateRules` data is target-compatible (`VehicleType` currently returns only `MOTORCYCLE`). This task is now a narrow development-adapter correction plus one controlled harness execution. Do not redesign.

## Repository / Branch
- Repository: `Terry4410/field-visit-mileage-system`
- Branch: `feature/uat-fasttrack-v180`
- Current verified head before this task: `ba7523e0b4627e9915aa2a611218e533d7fbd669`

## Human Gate Decision
**APPROVED**: correct only the development compatibility handling for the legacy `CK_MileageRateRules_VehicleType` predecessor and perform **one** consolidated Development Schema Harness execution.

No original migration change, Azure UAT mutation, SQL permission change, GitHub Environment change, production action, or Epic B work is approved.

## Human-Verified Read-Only Evidence
Direct Azure SQL Query Editor inspection of `db-fieldvisit-uat` established:

1. `dbo.MileageRateRules` currently returns only:

```text
VehicleType
MOTORCYCLE
```

No `CAR`, `OTHER`, or other current row value was observed in the distinct-value result shown by the human operator.

2. `CK_MileageRateRules_VehicleType` exists on `dbo.MileageRateRules`.

3. The constraint is enabled and trusted (`is_disabled = 0`, `is_not_trusted = 0`).

4. Human metadata inspection visibly showed the legacy constraint definition contains at least `OTHER` and `MOTORCYCLE`, so it is **not** already equivalent to the v1.8 target rule.

This means the current blocker is a recognized predecessor semantic evolution, not an unknown data conflict.

## Verified Harness State
Latest harness run: `34312854385`.

PASS:
- `1800_001 DEV APPLY` + original Verify
- `1800_002 DEV APPLY` + original Verify
- `1800_003 DEV APPLY` + original Verify
- `1800_004 DEV APPLY` + original Verify

FAIL:
- `1800_005 DEV APPLY`

Exact failure:

```text
Msg 53809
Development compatibility failed:
CK_MileageRateRules_VehicleType definition differs
from intended Motorcycle/Car semantics.
```

SKIPPED:
- `1800_005 ORIGINAL VERIFY`
- `1800_006`
- `1800_007`
- Database A final proof
- Database B empty proof
- harness Fast Regression

Azure UAT had one schema-only read and no DDL/DML mutation.

## Target v1.8 Rule
Original `1800_005` clearly intends:

```sql
CHECK (VehicleType IN (N'Motorcycle', N'Car'))
```

The development adapter must produce that target semantics inside the disposable development database only.

## Approved Compatibility Logic for 1800_005
Primary file:

`database/development/v1.8.0/compat/1800_005.dev.sql`

Before any change, capture into harness evidence:

- raw `sys.check_constraints.definition`
- normalized definition
- constraint enabled/trusted flags
- counts grouped by `VehicleType`

Do not expose row-level business data.

Then apply this exact decision logic.

### STATE A — Already v1.8-equivalent
If the existing enabled/trusted constraint is semantically equivalent to:

```sql
VehicleType IN (N'Motorcycle', N'Car')
```

reuse it and continue.

### STATE B — Recognized legacy VehicleType constraint with target-compatible current data
If all of the following are true:

1. the object is exactly `dbo.MileageRateRules.CK_MileageRateRules_VehicleType`
2. it is a CHECK constraint
3. it is enabled and trusted
4. its expression governs only `VehicleType` values, not unrelated columns or side effects
5. the raw/normalized legacy definition is captured in evidence
6. **every existing row in `dbo.MileageRateRules` already satisfies the v1.8 target set**:

```sql
VehicleType IN (N'Motorcycle', N'Car')
```

then, only inside disposable Development Database A:

1. drop only `CK_MileageRateRules_VehicleType`
2. recreate the same-named trusted constraint with the v1.8 target definition
3. use `WITH CHECK` so SQL Server validates all existing rows
4. explicitly `CHECK CONSTRAINT`
5. verify it is trusted and enabled
6. continue the development adapter

The current human evidence indicates this state should be applicable because the current distinct data value is only `MOTORCYCLE`.

**Do not require the table to be empty.**
The safety invariant is stronger and more relevant: every existing row must satisfy the new target rule, and `WITH CHECK` must validate it.

### STATE C — Anything unsafe or ambiguous
FAIL CLOSED if:

- any row has a VehicleType outside `Motorcycle` / `Car`
- the existing constraint references any unrelated column
- the constraint is disabled or untrusted
- object identity/type is unexpected
- exact replacement cannot be proven safe

Do not transform row values.
Do not delete rows.
Do not update UAT data.

## Other Predecessor Constraints
Keep existing fail-closed validation for:

- `CK_Projects_DateRange`
- `CK_MileageRateRules_DateRange`
- `CK_MileageRateRules_Rate`

Do not relax unrelated checks.

## Source Immutability
Do not modify any original migration files `1800_001` through `1800_007`.

Protected `1800_001` hashes must remain unchanged.

Only development adapter/manifest/diagnostic validation changes are allowed.

## Allowed Files
Only as necessary:

- `database/development/v1.8.0/compat/1800_005.dev.sql`
- `database/development/v1.8.0/compat/manifest.json`
- development compatibility validation scripts
- `.github/workflows/v180-development-schema-harness.yml` only if required for non-sensitive diagnostic evidence
- `docs/ai/PROJECT-STATE.md` only after verified result

No frontend/API/application behavior changes.

## Static Validation Before Push
Before push:

1. development compatibility hash validation PASS
2. same-batch SQL scanner PASS
3. verify production/UAT workflows cannot call `database/development/v1.8.0/compat/`
4. verify protected `1800_001` unchanged
5. verify no application/frontend files changed

Then make **one corrective commit/push**.

## Actions / Cost Guardrail
Allow the existing temporary exact-path push trigger to launch **one** new Development Schema Harness run.

One UAT schema-only extraction maximum.
One disposable SQL Server container.
Standard GitHub-hosted runner only.

Do not manually rerun on failure.
Do not start a second harness run.

An automatic UAT Fast-Track verification from the push is acceptable; do not manually rerun it.

## Required Full Harness Matrix
If `1800_005` succeeds, continue in the same run:

- `1800_005 ORIGINAL VERIFY`
- `1800_006 DEV APPLY` + original Verify
- `1800_007 DEV APPLY` + original Verify
- Database A structural/cleanliness proof
- Database B empty reconstruction proof
- development/baseline Verify checks
- Fast Regression once

Do not report full PASS if anything is skipped.

## Hard Boundaries
Do NOT:

- execute any 1800 migration or compatibility SQL against Azure UAT
- modify original 1800 migration artifacts
- change current temporary `VIEW DEFINITION`
- change Azure RBAC
- change firewall
- create Azure resources
- change GitHub Environment rules
- remove temporary feature-branch admission yet
- remove temporary harness push trigger yet
- merge to `main`
- deploy production
- start Epic B
- change application behavior
- make unrelated refactors

## Success Handling
If the full harness passes, report exactly:

```text
DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL
EPIC B = READY
```

Then stop.

Do not perform cleanup or start Epic B in the same task.

Cleanup is a separate controlled step:
1. revoke temporary `VIEW DEFINITION`
2. remove `feature/uat-fasttrack-v180` from GitHub Environment `uat`
3. remove temporary harness push trigger

## Failure Handling
If any stage fails:

- fail closed
- do not start another Actions run
- retain safe diagnostic evidence
- identify exact failing migration/object
- classify as compatibility-adapter, harness/session, predecessor-schema, or real schema-design issue
- propose only the narrowest next correction
- stop

## End Report
Return only:

1. `1800_005` RAW + NORMALIZED PREDECESSOR DEFINITION
2. HUMAN DATA EVIDENCE CONFIRMATION
3. STATE A/B/C CLASSIFICATION
4. DEVELOPMENT-ONLY CORRECTION APPLIED
5. SOURCE HASH / PROTECTED 001 STATUS
6. `1800_001–007` DEV APPLY + ORIGINAL VERIFY MATRIX
7. DATABASE A RESULT
8. DATABASE B RESULT
9. DEVELOPMENT SCHEMA STATUS
10. FAST REGRESSION RESULT
11. FILES CHANGED + WHY
12. ACTIONS RUN(S)
13. AZURE UAT MUTATION STATUS
14. COST / USAGE
15. REMAINING RISKS
16. EPIC B READINESS
17. CLEANUP NOW SAFE: YES/NO
18. HUMAN GATE, if any

Begin now. Do not redesign the architecture.
