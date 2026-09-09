# NEXT WORK TASK

## Model
Use **GPT-5.6 Sol Medium**.

Reason: this is no longer a mechanical harness fix. The current blocker is a real predecessor-schema compatibility decision around `CK_MileageRateRules_VehicleType`; it requires careful SQL semantic validation, but the architecture and allowed correction are already decided.

## Repository / Branch
- Repository: `Terry4410/field-visit-mileage-system`
- Branch: `feature/uat-fasttrack-v180`
- Current verified head before this task: `ba7523e0b4627e9915aa2a611218e533d7fbd669`

## Human Gate Decision
**APPROVED**: make one narrowly scoped development-only correction for `1800_005` predecessor constraint compatibility and perform **one** new consolidated Development Schema Harness execution.

No original migration change, Azure UAT mutation, SQL permission change, GitHub Environment change, production action, or Epic B work is approved.

## Verified Current State
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

Azure UAT had one schema-only read and **no DDL/DML mutation**.

## Important Interpretation
Do **not** simply broaden the validator or ignore the predecessor constraint.

Original `1800_005` clearly intends the v1.8 rule:

```sql
CHECK (VehicleType IN (N'Motorcycle', N'Car'))
```

The actual v1.7 predecessor already contains the same-named constraint, but its exact raw definition has not yet been retained in evidence.

The repository UAT seed only inserts `Motorcycle`, so a legacy Motorcycle-only predecessor is plausible, but this is **not yet proven**. Treat it as a hypothesis, not a fact.

## Objective
Make the development compatibility layer deterministically handle only recognized predecessor states, while preserving fail-closed behavior for anything else.

Then allow one consolidated harness run to continue through `1800_007`, Database A, Database B, and Fast Regression if all stages pass.

## Allowed Scope
Primary file:

`database/development/v1.8.0/compat/1800_005.dev.sql`

Also allowed only as required:
- `database/development/v1.8.0/compat/manifest.json`
- development compatibility validation scripts
- `.github/workflows/v180-development-schema-harness.yml` only if needed to retain diagnostic evidence
- `docs/ai/PROJECT-STATE.md` only after verified results

Do not change application code.

## Required 1800_005 Compatibility Logic
Before altering the predecessor constraint, capture and print/store both:

1. the **raw** `sys.check_constraints.definition`
2. the **normalized** definition used for comparison

for:

`dbo.MileageRateRules.CK_MileageRateRules_VehicleType`

The harness evidence must retain this diagnostic text, but must not contain UAT business data.

Then classify the predecessor into exactly one of these states.

### STATE A — Already v1.8-equivalent
If the existing enabled/trusted constraint is semantically exactly equivalent to:

```sql
VehicleType IN (N'Motorcycle', N'Car')
```

including equivalent OR/order/parentheses/Unicode-literal representations:

- reuse it
- do not drop it
- continue

### STATE B — Recognized legacy Motorcycle-only predecessor
If the existing enabled/trusted constraint is semantically exactly equivalent to Motorcycle-only, for example:

```sql
VehicleType = N'Motorcycle'
```

or

```sql
VehicleType IN (N'Motorcycle')
```

then, **only inside the disposable development Database A**:

1. assert `dbo.MileageRateRules` contains **zero rows**
2. assert the exact constraint name/table/type is correct
3. assert the constraint is enabled and trusted
4. record the raw/normalized legacy definition to evidence
5. drop only `CK_MileageRateRules_VehicleType`
6. recreate the same-named trusted constraint as:

```sql
ALTER TABLE dbo.MileageRateRules WITH CHECK ADD
    CONSTRAINT CK_MileageRateRules_VehicleType
    CHECK (VehicleType IN (N'Motorcycle', N'Car'));
ALTER TABLE dbo.MileageRateRules CHECK CONSTRAINT CK_MileageRateRules_VehicleType;
```

Then continue the development adapter.

This is a **development-only predecessor evolution**. It must never execute against Azure UAT and must not alter the original migration source.

### STATE C — Anything else
If the predecessor definition is not exactly State A or State B:

- FAIL CLOSED
- do not drop/recreate anything
- report the exact raw and normalized definition
- stop the harness

Do not add generic permissive matching.

## Other Predecessor Constraints
Keep exact fail-closed validation for:

- `CK_Projects_DateRange`
- `CK_MileageRateRules_DateRange`
- `CK_MileageRateRules_Rate`

Do not relax them unless the same run proves a specific, equivalent normalization-only issue. If a semantic difference appears, stop rather than fixing another design conflict automatically.

## Source Immutability
Do not modify any original migration files:

- `database/migrations/1800_001.../Up.sql` / `Verify.sql`
- through `1800_007.../Up.sql` / `Verify.sql`

Protected `1800_001` hashes must remain exactly unchanged.

Update the development manifest SHA only for development adapter files that legitimately change.

## Static / Local Validation Before Push
Before pushing:

1. run development compatibility hash validation
2. run same-batch SQL risk scanner
3. verify production/UAT workflows cannot reference `database/development/v1.8.0/compat/`
4. verify `1800_001` protected hashes unchanged
5. verify no app/API/frontend files changed

Then create **one corrective commit/push**.

## Actions / Cost Guardrail
Allow the temporary exact-path push trigger to start **one** new harness run.

One UAT schema-only extraction maximum.
One disposable SQL Server container.
Standard GitHub-hosted runner only.

Do not manually dispatch or rerun another harness.
If this new run fails anywhere, stop and report.

An automatically triggered UAT Fast-Track verification from the push is acceptable; do not manually rerun it.

## Required Full Harness Matrix
If `1800_005` succeeds, continue in the same run:

- `1800_005 ORIGINAL VERIFY`
- `1800_006 DEV APPLY` + original Verify
- `1800_007 DEV APPLY` + original Verify
- Database A structural/cleanliness proof
- Database B empty reconstruction proof
- development/baseline Verify checks
- Fast Regression once

Do not declare PASS if any required stage is skipped.

## Hard Boundaries
Do NOT:
- execute any 1800 migration or compatibility SQL against Azure UAT
- modify original 1800 migration artifacts
- change temporary `VIEW DEFINITION` permission
- change Azure RBAC
- change firewall
- create Azure resources
- change GitHub Environment branch rules
- remove temporary feature-branch admission yet
- remove temporary harness push trigger yet
- merge to `main`
- deploy production
- start Epic B
- change application behavior
- make unrelated refactors

## Success Handling
If the full harness passes:

Report:

```text
DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL
EPIC B = READY
```

Then stop. Do not start cleanup or Epic B automatically.

Cleanup will be a separate controlled step:
1. revoke temporary `VIEW DEFINITION`
2. remove `feature/uat-fasttrack-v180` from GitHub Environment `uat`
3. remove temporary harness push trigger

## Failure Handling
If any stage fails:
- fail closed
- do not start another Actions run
- preserve raw diagnostic evidence for the failing schema object when safe
- classify as normalization-only, recognized predecessor evolution, compatibility-adapter defect, harness defect, or schema-design conflict
- propose only the narrowest next correction
- stop

## End Report
Return only:

1. `1800_005` PREDECESSOR RAW + NORMALIZED DEFINITION
2. PREDECESSOR STATE A/B/C CLASSIFICATION
3. DEVELOPMENT-ONLY CORRECTION APPLIED
4. SOURCE HASH / PROTECTED 001 STATUS
5. `1800_001–007` DEV APPLY + ORIGINAL VERIFY MATRIX
6. DATABASE A RESULT
7. DATABASE B RESULT
8. DEVELOPMENT SCHEMA STATUS
9. FAST REGRESSION RESULT
10. FILES CHANGED + WHY
11. ACTIONS RUN(S)
12. AZURE UAT MUTATION STATUS
13. COST / USAGE
14. REMAINING RISKS
15. EPIC B READINESS
16. CLEANUP NOW SAFE: YES/NO
17. HUMAN GATE, if any

Begin now. Do not redesign the architecture.
