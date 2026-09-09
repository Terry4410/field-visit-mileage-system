# NEXT WORK TASK

## Current task

Epic C-B1 — Validate and publish the Deployment Site management UI candidate.

The C-A backend authority is READY after automatic UAT Fast-Track run `34364217670`.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `FULL EPIC B = COMPLETE`
- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B1 DEPLOYMENT SITE MANAGEMENT UI = IMPLEMENTATION CANDIDATE / VALIDATION PENDING`
- `EPIC C-B2 TRIP / SNAPSHOT INTEGRATION = BLOCKED ON C-B1 VALIDATION`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## C-B1 scope

- Add admin Deployment Site UI using the C-A v1.8 APIs.
- Manage Site lifecycle under Center with RowVersion conflict handling.
- Manage history-preserving Site↔Location assignments.
- Manage Team↔Deployment Site assignments subject to Team/Center containment.
- Manage Employment↔Deployment Site assignments including one-primary behavior.
- Add permanent frontend/backend regression coverage and preserve the existing browser suite.
- Do not begin C-B2 VisitTrip or snapshot integration in this batch.

## Hard rules

- Do not modify protected `1800_001`.
- Do not silently rewrite original `1800_003` or its Verify script.
- Do not alter Azure, reset UAT, run production migration, expand RBAC/firewall/SQL permissions, or run the Development Schema Harness during ordinary C-B implementation.
- Do not refactor unrelated Location, Project, Rate, Notification or Google behavior.
- Preserve effective-date inclusivity and history; no physical deletion of lifecycle history.
- Keep Final Clean Baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

After C-B1 targeted tests, full protected validation and exactly one automatic UAT Fast-Track succeed, report:

- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B1 DEPLOYMENT SITE UI = READY`
- `EPIC C-B2 TRIP / SNAPSHOT INTEGRATION = NEXT`
- `FULL EPIC C = NOT COMPLETE`

Do not begin C-B2 or Final Clean Baseline work in the C-B1 task.
