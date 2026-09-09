# NEXT WORK TASK

## Model
Use **GPT-5.6 Sol Medium**.
Reason: the architecture and correction strategy are already decided; this task requires focused SQL/migration compatibility implementation but does not need High reasoning.

## Repository / Branch
- Repository: `Terry4410/field-visit-mileage-system`
- Branch: `feature/uat-fasttrack-v180`

## Approved Decision
Use a **development-only migration compatibility layer** to unblock the mutable v1.8 Development Schema Harness.

Do **not** modify or supersede production migration artifacts in this task. Final production migration hardening will happen later before Final Release Candidate / IT handover.

Do not re-evaluate this architecture.

## Confirmed Predecessor Analysis
Actual v1.7 schema was extracted successfully from `db-fieldvisit-uat` and published into disposable SQL Server.

Confirmed issues:

- `1800_001`: SQL Server same-batch compilation defect: `Teams.EffectiveFrom` / `EffectiveTo` are added and then referenced statically later in the same batch.
- `1800_001`: actual v1.7 already contains `UX_Teams_Organization_TeamCode`; the migration attempts to create the same index again.
- `1800_002`: high risk of same-batch reference to newly-added `UserIdentityProfiles.EmploymentId`.
- `1800_004`: high risk of same-batch reference to newly-added `DuplicateOfLocationId` and related computed/index structures.
- `1800_005`: actual v1.7 already contains `CK_MileageRateRules_VehicleType` and semantically overlapping date/rate constraints.
- `1800_007`: high risk of same-batch references to newly-added MileageCalculation / Snapshot governance columns.
- `1800_003` and `1800_006` currently appear lower risk.

Protected `1800_001` hashes remain unchanged.

## Objective
Implement a deterministic, container-only compatibility layer so this can succeed:

`trusted v1.7 schema -> development compatibility evolution -> original Verify.sql files -> complete v1.8 development schema -> empty DB B proof -> Fast Regression`

This is **DEVELOPMENT tooling only** and must never become the production upgrade path automatically.

## Design
Create an explicit development-only structure, preferably under:

`database/development/v1.8.0/compat/`

Use clear audited files such as `1800_001.dev.sql`, `1800_002.dev.sql`, etc., or an equally explicit design.

Do not generate fragile runtime SQL by parsing arbitrary migration files. Prefer explicit reviewed development adapters derived from the intended schema outcome.

## Source Immutability
Do not modify original `Up.sql` / `Verify.sql` files for `1800_001` through `1800_007` in this task.

At minimum, `1800_001` must remain protected exactly.

Pin source migration SHA-256 values in a development manifest. If a source migration changes unexpectedly, fail closed until reviewed.

## Compatibility Rules
For same-batch compilation defects, use explicit safe execution boundaries such as separate batches or controlled dynamic SQL without changing intended schema semantics.

For predecessor objects that already exist, never merely check object name. Validate the intended definition before reuse.

For indexes validate at least uniqueness, ordered key columns, key direction if relevant, filter definition, and included columns.

For constraints validate object/table, constraint type, and normalized intended semantics.

If exact compatibility cannot be proven, fail closed. Do not silently drop/recreate predecessor objects.

## 1800_001 Requirements
The development adapter must:

- add Organization lifecycle fields
- add Team lifecycle fields
- create Team effective-date checks safely after column creation
- validate existing `UX_Teams_Organization_TeamCode`
- reuse that index only if it is exactly compatible with UNIQUE `(OrganizationId, TeamCode)`
- create Center / TeamCenterAssignment structures
- create required trigger
- add snapshot Center/Team fields
- preserve intended `SchemaVersions` result
- allow the **original** `1800_001/Verify.sql` to pass

## 1800_002 / 004 / 005 / 007
Apply the same principle and resolve all already-identified deterministic compilation or duplicate-object problems before another CI run.

`1800_003` and `1800_006` may use the original `Up.sql` directly if proven safe, or use a pass-through compatibility entry.

Always run each **original Verify.sql** after the development evolution step.

## Permanent Regression Guards
Add validation that detects future risks including:

- unsafe same-batch references to newly-added columns where deterministically detectable
- known predecessor duplicate object conflicts
- drift between original migration hashes and the development compatibility manifest
- production/UAT workflows accidentally referencing the development compatibility layer
- development compatibility SQL accidentally targeting Azure UAT

The development adapter must only be callable from the disposable schema harness.

## Harness Update
Update `.github/workflows/v180-development-schema-harness.yml` so the disposable SQL phase uses the development compatibility layer.

Azure portion remains one read-only schema-only extraction from UAT. All schema evolution occurs only inside disposable SQL Server.

No original `Up.sql` or compatibility SQL may execute against Azure UAT.

## CI / Cost Guardrail
Do all possible static/repository checks before push.

Target **one new harness Actions run** only.

One Azure schema-only extraction maximum.

Do not create Azure resources, change paid tier, firewall, RBAC, SQL grants, GitHub Environment policy, or deploy to production.

Do not rerun unrelated successful workflows manually.

## Required Execution Matrix
The harness must report individually for all seven migrations:

- DEV APPLY
- ORIGINAL VERIFY

Do not report PASS if any stage was skipped.

## Database A Proof
After all seven, verify intended structures for Organization/Center/Team, Person/Employment, employment status/roles/memberships, Team leader/delegation, Deployment Sites, Location governance, Project/Visit Type/Rate, Notifications, and Mileage/Google governance.

No real UAT business data and no environment principals.

## Database B Proof
Generate a schema-only v1.8 DEVELOPMENT artifact from Database A, install it into a completely empty Database B, then run development Verify, applicable baseline structural Verify, and principal/data cleanliness checks.

## Fast Regression
Run Fast Regression once after schema proof. Existing Protected Baseline must remain green. No application behavior changes are authorized.

## Non-Regression Hard Rule
No unrelated refactor. No API/frontend changes. No Epic B implementation. No Azure UAT schema/data mutation. No RBAC/firewall/SQL permission changes. No GitHub Environment policy change. No main merge. No production deployment. Protected `1800_001` hashes must remain unchanged.

## Success Criteria
PASS only if source migration hashes validate; actual v1.7 predecessor is reconstructed; compatibility layer applies all `001-007`; every original Verify passes; Database A proof passes; Database B clean-install proof passes; no principal/business-data leaks; Fast Regression passes; protected `1800_001` remains unchanged; Azure UAT remains unchanged; Development Baseline remains MUTABLE / NON-FINAL.

If any migration fails: **STOP**. Do not automatically start another Actions run. Report exact failure and proposed fix.

## End Report
Return only:

1. COMPATIBILITY LAYER DESIGN
2. SOURCE HASH / PROTECTED 001 STATUS
3. 1800_001-007 DEV APPLY + ORIGINAL VERIFY MATRIX
4. DATABASE A RESULT
5. DATABASE B RESULT
6. DEVELOPMENT SCHEMA STATUS
7. FAST REGRESSION RESULT
8. FILES CHANGED + WHY
9. ACTIONS RUN(S)
10. AZURE UAT MUTATION STATUS
11. COST / USAGE
12. REMAINING MIGRATION RISKS
13. EPIC B READINESS
14. CLEANUP ACTIONS NOW SAFE
15. HUMAN GATE, if any

If fully PASS: STOP. Do not start Epic B in the same task.
