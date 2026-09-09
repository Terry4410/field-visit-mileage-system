# NEXT WORK TASK

## Conditional next task

Epic C — Deployment Site authoritative read/write and UI integration for v1.8.

Start this task only after the current B2-B2 implementation commit passes the automatic UAT Fast-Track. If B2-B2 fails, repair only the exact protected-regression failure first.

## Starting state after B2-B2 validation

- Branch: `feature/uat-fasttrack-v180`
- `EPIC B1 BACKEND FOUNDATION = READY`
- `EPIC B2-A1 AUTHORITATIVE PEOPLE WRITE = READY`
- `EPIC B2-A2 TEAM/CENTER LIFECYCLE WRITE = READY`
- `EPIC B2-B1 PEOPLE/MEMBERSHIP UI CUTOVER = READY`
- `EPIC B2-B2 TEAM/CENTER UI CUTOVER = READY` only after protected validation succeeds
- `FULL EPIC B = COMPLETE` only after that same validation succeeds
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## Epic C scope

Use the already designed `1800_003_deployment_sites` development schema as the target contract without changing the protected original migration in this batch unless a separately reviewed development-schema correction is proven necessary.

Implement authoritative v1.8 support for:

- DeploymentSites lifecycle master data under Center.
- DeploymentSiteLocationAssignments with history-preserving effective periods.
- TeamDeploymentSiteAssignments with Team/Center containment rules.
- EmploymentDeploymentSiteAssignments including one-primary enforcement.
- Required VisitTrip / snapshot integration only to the extent explicitly planned for Epic C.
- Admin read/write APIs, RowVersion concurrency, UI management, and permanent regression tests.

## Hard rules

- Do not modify protected `1800_001`.
- Do not silently rewrite original `1800_003` or any original Verify script; prove and review any development compatibility correction first.
- Do not alter Azure, reset UAT, run production migration, or expand permissions.
- Do not refactor unrelated Location, Project, Rate, Notification or Google behavior.
- Keep Final Clean Baseline not created/not frozen.
- Preserve effective-date inclusivity and history; no physical deletion of lifecycle history.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Validation

Use targeted tests plus the full protected validation and exactly one automatic UAT Fast-Track per approved implementation batch. Do not start Final Clean Baseline work yet.
