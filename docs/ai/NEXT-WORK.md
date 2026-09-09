# NEXT WORK TASK

## Conditional next task

Epic C-B — Deployment Site management UI plus VisitTrip / VisitTripSnapshot integration required by `1800_003`.

Start only after the current Epic C-A backend-authority implementation commit passes the automatic UAT Fast-Track. If C-A fails, repair only the exact protected-regression failure first.

## Starting state after C-A validation

- Branch: `feature/uat-fasttrack-v180`
- `FULL EPIC B = COMPLETE`
- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY` only after protected validation succeeds
- `EPIC C-B UI / TRIP / SNAPSHOT INTEGRATION = NEXT` only after that success
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## C-B scope

- Add admin Deployment Site UI using the C-A v1.8 APIs.
- Manage Site lifecycle under Center with RowVersion conflict handling.
- Manage history-preserving Site↔Location assignments.
- Manage Team↔Deployment Site assignments subject to Team/Center containment.
- Manage Employment↔Deployment Site assignments including one-primary behavior.
- Add the `1800_003` VisitTrips `StartDeploymentSiteId` / `EndDeploymentSiteId` model and write-flow integration only where required by the existing trip workflow.
- Populate the `1800_003` Start/End Deployment Site snapshot fields when snapshots are created; historical snapshots remain immutable.
- Preserve current trip behavior when Deployment Site is not used unless the v1.8 business rule explicitly requires it.
- Add permanent frontend/backend/browser regression coverage.

## Hard rules

- Do not modify protected `1800_001`.
- Do not silently rewrite original `1800_003` or its Verify script.
- Do not alter Azure, reset UAT, run production migration, expand RBAC/firewall/SQL permissions, or run the Development Schema Harness during ordinary C-B implementation.
- Do not refactor unrelated Location, Project, Rate, Notification or Google behavior.
- Preserve effective-date inclusivity and history; no physical deletion of lifecycle history.
- Keep Final Clean Baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

After C-B targeted tests, full protected validation and exactly one automatic UAT Fast-Track succeed, report:

- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B UI / TRIP / SNAPSHOT INTEGRATION = READY`
- `FULL EPIC C = COMPLETE`
- `EPIC D = NEXT`

Do not start Final Clean Baseline work yet.
