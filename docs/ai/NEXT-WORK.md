# NEXT WORK TASK

## Task

Epic B2-A2 — Complete Team / Center / TeamCenter lifecycle backend validation.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `EPIC B1 BACKEND FOUNDATION = READY`
- `EPIC B2-A1 AUTHORITATIVE PEOPLE WRITE = READY`
- `EPIC B2-A2 TEAM/CENTER LIFECYCLE WRITE = IMPLEMENTED / VALIDATION PENDING`
- `EPIC B2-B UI CUTOVER = BLOCKED ON B2-A2 VALIDATION`
- `FULL EPIC B = NOT COMPLETE`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## Scope

- Run the targeted B2-A2 tests and full protected validation with .NET 8/NuGet dependencies available.
- If local validation passes, make the single approved implementation commit and push it to `feature/uat-fasttrack-v180`.
- Observe only the automatically triggered UAT Fast-Track run; do not manually rerun it.
- Mark A2 READY and B2-B NEXT only when that run succeeds.

## Hard rules

- Do not modify original `1800_001–007` migration or Verify scripts.
- Do not alter Azure, reset UAT, run the Development Schema Harness, or deploy production.
- Do not refactor unrelated APIs, trip, location, project, rate, notification or Google behavior.
- Keep the final clean baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

On successful A2 validation, report `EPIC B2-A2 TEAM/CENTER LIFECYCLE WRITE = READY` and
`EPIC B2-B UI CUTOVER = NEXT`; do not mark full Epic B complete.
