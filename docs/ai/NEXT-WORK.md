# NEXT WORK TASK

## Model
Use **GPT-5.6 Luna**.

Reason: the remaining issue is now fully diagnosed by human read-only SQL evidence. This task is a narrow repository-validator correction plus one deterministic development-adapter update and one consolidated harness run. Do not redesign architecture.

## Repository / Branch
- Repository: `Terry4410/field-visit-mileage-system`
- Branch: `feature/uat-fasttrack-v180`
- Current branch head before this task: `a02ecce0c20cc934df83ec4d7e3ce591bf2f94b6`

## Human Gate Decision
**APPROVED**: correct the development compatibility validator and make the development-only `1800_005` adapter recognize the now-proven legacy predecessor definition, then perform **one** consolidated Development Schema Harness execution.

No original migration change, Azure UAT mutation, SQL permission change, GitHub Environment change, production action, or Epic B work is approved.

## Newly Proven Human Read-Only Evidence
Direct Azure SQL Query Editor inspection of `db-fieldvisit-uat` proved:

```text
ConstraintName: CK_MileageRateRules_VehicleType
Raw definition: ([VehicleType]='OTHER' OR [VehicleType]='MOTORCYCLE' OR [VehicleType]='CAR')
HasMotorcycle: YES
HasCar: YES
HasOther: YES
TotalRows: 2
MotorcycleRows: 2
CarRows: 0
OtherRows: 0
OutsideV18Rows: 0
```

The constraint is enabled and trusted (`is_disabled = 0`, `is_not_trusted = 0`).

Therefore the actual v1.7 predecessor semantics are now known exactly:

```text
Allowed legacy values = OTHER, MOTORCYCLE, CAR
Current stored values = MOTORCYCLE only
Target v1.8 values     = MOTORCYCLE, CAR
```

This is a recognized legacy-superset constraint with target-compatible current data.

## Current Harness Failure
Latest run: `34314342911`.

It failed before Azure extraction because:

`scripts/validate-v180-development-compat.sh`

still expects an obsolete literal diagnostic string:

```text
CK_MileageRateRules_VehicleType definition differs
```

while `1800_005.dev.sql` now uses different fail-closed diagnostics.

This is a repository static-validator defect, not an Azure or schema runtime failure.

## Objective
Fix both known blockers in one repository-only implementation batch:

1. remove the brittle validator dependency on the obsolete diagnostic wording
2. extend the development-only `1800_005` State B recognition to the **exact proven legacy predecessor semantics**

Then execute one consolidated harness run.

## Required 1800_005 State Classification
Primary file:

`database/development/v1.8.0/compat/1800_005.dev.sql`

### State A — already v1.8 equivalent
If the enabled/trusted constraint is semantically exactly:

```sql
VehicleType IN (N'Motorcycle', N'Car')
```

reuse it unchanged.

### State B — exact proven legacy superset
Recognize the actual predecessor definition as equivalent to:

```sql
VehicleType IN (N'Other', N'Motorcycle', N'Car')
```

including the proven OR-form/order:

```sql
([VehicleType]='OTHER' OR [VehicleType]='MOTORCYCLE' OR [VehicleType]='CAR')
```

The existing normalization currently removes brackets, parentheses, spaces and lowercases text, so the observed normalized form should be:

```text
vehicletype='other'orvehicletype='motorcycle'orvehicletype='car'
```

Recognize this exact legacy value-set semantics only. Equivalent ordering/Unicode-literal variants may be supported if implemented deterministically, but do not add generic permissive matching.

Before narrowing the constraint inside disposable Database A, require ALL of the following:

1. exact table/name/type match
2. constraint enabled and trusted
3. expression governs only `VehicleType`
4. raw + normalized definition retained in safe harness evidence
5. every existing row satisfies the v1.8 target set:

```sql
VehicleType IN (N'Motorcycle', N'Car')
```

6. `OutsideV18Rows = 0`

Then, **only inside disposable Development Database A**:

```sql
ALTER TABLE dbo.MileageRateRules
DROP CONSTRAINT CK_MileageRateRules_VehicleType;

ALTER TABLE dbo.MileageRateRules WITH CHECK ADD
    CONSTRAINT CK_MileageRateRules_VehicleType
    CHECK (VehicleType IN (N'Motorcycle', N'Car'));

ALTER TABLE dbo.MileageRateRules
CHECK CONSTRAINT CK_MileageRateRules_VehicleType;
```

Then verify the recreated constraint is enabled and trusted.

Do not transform, delete, or update business rows.

### State C — anything else
FAIL CLOSED.

Do not silently accept any additional VehicleType values or unrelated expression semantics.

## Static Validator Correction
File:

`scripts/validate-v180-development-compat.sh`

Remove/replace the brittle assertion that greps only for the obsolete human-readable error message.

Do **not** weaken validation.

Replace it with structural guard assertions proving that `1800_005.dev.sql` still contains all required safety controls, at minimum:

- raw predecessor definition capture
- normalized definition capture
- enabled/trusted constraint check
- exact recognized legacy signature/value-set handling for `OTHER + MOTORCYCLE + CAR`
- target-compatible row gate for `Motorcycle/Car`
- fail-closed behavior for unrecognized predecessor semantics
- exact drop of `CK_MileageRateRules_VehicleType`
- recreation with `WITH CHECK`
- target `Motorcycle/Car` check constraint
- post-create enabled/trusted verification

The validator should protect **behavioral safety invariants**, not exact diagnostic prose.

## Allowed Files
Only as necessary:

- `database/development/v1.8.0/compat/1800_005.dev.sql`
- `database/development/v1.8.0/compat/manifest.json`
- `scripts/validate-v180-development-compat.sh`
- related development-only validation scripts if strictly necessary
- `.github/workflows/v180-development-schema-harness.yml` only if required for safe diagnostic evidence
- `docs/ai/PROJECT-STATE.md` only after verified results

No application/API/frontend files.

## Source Immutability
Do not modify original `1800_001–007` Up.sql or Verify.sql files.

Protected `1800_001` hashes must remain exactly unchanged.

Update manifest SHA only for legitimate development compatibility files changed by this task.

## Pre-Push Validation
Before push, run locally/repository-side:

1. development compatibility hash validation
2. same-batch SQL risk scanner
3. production/UAT workflow isolation check
4. protected `1800_001` hash verification
5. no application/frontend file changes

If preflight fails, fix only the narrow repository defect and rerun local/static checks before any push.

Then make **one corrective commit/push**.

## Actions / Cost Guardrail
Allow the temporary exact-path push trigger to start **one** Development Schema Harness run.

Maximum for this task:
- one UAT schema-only extraction
- one disposable SQL Server container
- standard GitHub-hosted runner
- no manual rerun
- no second harness run

If the run fails anywhere, stop and report. Do not auto-correct and rerun.

An automatically triggered UAT Fast-Track verification from the push is acceptable; do not manually rerun it.

## Required Full Harness Matrix
The single run must attempt, in order:

- `1800_001 DEV APPLY` + original Verify
- `1800_002 DEV APPLY` + original Verify
- `1800_003 DEV APPLY` + original Verify
- `1800_004 DEV APPLY` + original Verify
- `1800_005 DEV APPLY` + original Verify
- `1800_006 DEV APPLY` + original Verify
- `1800_007 DEV APPLY` + original Verify
- Database A structural/cleanliness proof
- Database B empty reconstruction proof
- development/baseline Verify checks
- Fast Regression once

Do not declare full PASS if any required stage is skipped.

## Hard Boundaries
Do NOT:

- execute any 1800 migration or compatibility SQL against Azure UAT
- modify original migration artifacts
- change current temporary `VIEW DEFINITION`
- change Azure RBAC
- change firewall
- create Azure resources
- change GitHub Environment rules
- remove temporary feature-branch admission yet
- remove temporary harness push trigger yet
- merge to main
- deploy production
- start Epic B
- change application behavior
- make unrelated refactors

## Success Handling
If full harness passes, report:

```text
DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL
EPIC B = READY
```

Then STOP.

Do not cleanup or start Epic B in the same task.

Cleanup is a separate controlled step:
1. revoke temporary `VIEW DEFINITION`
2. remove `feature/uat-fasttrack-v180` from GitHub Environment `uat`
3. remove temporary harness push trigger

## Failure Handling
If any stage fails:
- fail closed
- do not start another Actions run
- preserve safe diagnostics
- identify exact migration/object/stage
- classify as validator, compatibility-adapter, harness/session, predecessor-schema, or real schema-design issue
- propose the narrowest correction
- stop

## End Report
Return only:

1. STATIC VALIDATOR CORRECTION
2. `1800_005` RAW + NORMALIZED PREDECESSOR DEFINITION
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

Begin now. Do not redesign architecture.