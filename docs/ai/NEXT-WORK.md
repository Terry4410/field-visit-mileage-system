# NEXT WORK TASK

## Current task

Epic C-B2-B — Validate Trip / Deployment Site / immutable Snapshot integration.

The C-A backend authority is READY after automatic UAT Fast-Track run `34364217670`.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `FULL EPIC B = COMPLETE`
- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B1 DEPLOYMENT SITE MANAGEMENT UI = READY` (`34370111646`)
- `EPIC C-B2-A AS-OF TRIP CONTEXT = READY` (`34372269130`)
- `EPIC C-B2-B TRIP / SNAPSHOT INTEGRATION = IMPLEMENTATION CANDIDATE / VALIDATION PENDING`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## C-B2-B scope

- Persist server-authoritative Employment, Team and Start/End Deployment Sites for new v1.8 trips.
- Revalidate authoritative context immediately before submit.
- Create immutable Submitted snapshots; build v1.8 Approved snapshots only by copying the latest Submitted basis.
- Preserve legacy v1.7 trips and correction snapshot copy-forward compatibility.
- Integrate visitor Site selection without using admin APIs or changing mileage formulas.

## Hard rules

- Do not modify protected `1800_001`.
- Do not silently rewrite original `1800_003` or its Verify script.
- Do not alter Azure, reset UAT, run production migration, expand RBAC/firewall/SQL permissions, or run the Development Schema Harness during ordinary C-B implementation.
- Do not refactor unrelated Location, Project, Rate, Notification or Google behavior.
- Preserve effective-date inclusivity and history; no physical deletion of lifecycle history.
- Keep Final Clean Baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

After C-B2-B tests, full protected validation and automatic UAT Fast-Track succeed, report:

- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B1 DEPLOYMENT SITE UI = READY`
- `EPIC C-B2-A AS-OF TRIP CONTEXT = READY`
- `EPIC C-B2-B TRIP / SNAPSHOT INTEGRATION = READY`
- `FULL EPIC C = COMPLETE`
- `EPIC D-A LOCATION GOVERNANCE PREFLIGHT = NEXT`

Do not begin the Final Clean Baseline or any Azure operation in this task.
