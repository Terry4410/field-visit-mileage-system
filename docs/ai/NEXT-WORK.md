# NEXT WORK TASK

## Current task

Epic C-B2-A — Validate and publish the as-of Trip Context foundation candidate.

The C-A backend authority is READY after automatic UAT Fast-Track run `34364217670`.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `FULL EPIC B = COMPLETE`
- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B1 DEPLOYMENT SITE MANAGEMENT UI = READY` (`34370111646`)
- `EPIC C-B2-A AS-OF TRIP CONTEXT = IMPLEMENTATION CANDIDATE / VALIDATION PENDING`
- `EPIC C-B2-B TRIP / SNAPSHOT INTEGRATION = NOT STARTED`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## C-B2-A scope

- Add a visitor-safe `GET /api/v1/trips/context` read path.
- Resolve UserId→UserIdentityProfile→EmploymentId and all status/team/site facts as of VisitDate.
- Intersect Employment-Site and Team-Site assignments and require one effective Site Location.
- Add exact `1800_003` VisitTrip and VisitTripSnapshot domain/EF fields without changing writes.
- Add permanent regression coverage and preserve existing Trip Create/Update/Submit/Approve behavior.
- Do not begin Visitor UI, Trip persistence, snapshot production or Google mileage work in this batch.

## Hard rules

- Do not modify protected `1800_001`.
- Do not silently rewrite original `1800_003` or its Verify script.
- Do not alter Azure, reset UAT, run production migration, expand RBAC/firewall/SQL permissions, or run the Development Schema Harness during ordinary C-B implementation.
- Do not refactor unrelated Location, Project, Rate, Notification or Google behavior.
- Preserve effective-date inclusivity and history; no physical deletion of lifecycle history.
- Keep Final Clean Baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

After C-B2-A targeted tests, full protected validation and exactly one automatic UAT Fast-Track succeed, report:

- `EPIC C-A DEPLOYMENT SITE BACKEND AUTHORITY = READY`
- `EPIC C-B1 DEPLOYMENT SITE UI = READY`
- `EPIC C-B2-A AS-OF TRIP CONTEXT = READY`
- `EPIC C-B2-B TRIP / SNAPSHOT INTEGRATION = NEXT`
- `FULL EPIC C = NOT COMPLETE`

Do not begin C-B2-B or Final Clean Baseline work in the C-B2-A task.
