# NEXT WORK TASK

## Task

Epic B2-B2 — Team / Center / TeamCenter frontend cutover to v1.8 lifecycle APIs.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `EPIC B1 BACKEND FOUNDATION = READY`
- `EPIC B2-A1 AUTHORITATIVE PEOPLE WRITE = READY`
- `EPIC B2-A2 TEAM/CENTER LIFECYCLE WRITE = READY`
- `EPIC B2-B1 PEOPLE/MEMBERSHIP UI CUTOVER = READY`
- `FULL EPIC B = NOT COMPLETE`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## Scope

- Cut Team master create/update/deactivate UI from legacy adapter routes to `/api/v1/admin/v180/teams` and use Base64 rowversion tokens.
- Add Center lifecycle management UI using `/api/v1/admin/v180/centers`.
- Add TeamCenterAssignment management UI using `/api/v1/admin/v180/team-center-assignments`.
- Preserve inclusive effective dates, organization/lifecycle validation, overlap protection and conflict messaging.
- Keep People/Employment membership UI on the B2-B1 v1.8 path.
- Add targeted frontend/backend regression coverage and run the full protected suite.

## Hard rules

- Do not modify original `1800_001–007` migration or Verify scripts.
- Do not alter Azure, reset UAT, run the Development Schema Harness, or deploy production.
- Do not refactor unrelated trip, location, project, rate, notification or Google behavior.
- Keep the final clean baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

On successful B2-B2 validation, report:

- `EPIC B2-B2 TEAM/CENTER UI CUTOVER = READY`
- `FULL EPIC B = COMPLETE`
- `EPIC C = NEXT`
