# NEXT WORK TASK

## Task

Epic B2-A2 — Team / Center / TeamCenter lifecycle backend write APIs.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `EPIC B1 BACKEND FOUNDATION = READY`
- `EPIC B2-A1 AUTHORITATIVE PEOPLE WRITE = READY`
- `EPIC B2-B UI CUTOVER = NOT STARTED`
- `FULL EPIC B = NOT COMPLETE`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- Azure UAT remains pre-v1.8.

## Scope

- Add Team, Center and TeamCenterAssignment lifecycle writes using the schema defined by protected `1800_001`.
- Require Base64 rowversion optimistic concurrency on updates.
- Apply inclusive effective dates and fail closed on overlap, organization mismatch or ambiguous current assignments.
- Preserve existing v1.7 Team routes as compatibility adapters where required.
- Keep all frontend UI unchanged; B2-B remains a later batch.
- Add targeted tests and the full protected regression suite.

## Hard rules

- Do not modify original `1800_001–007` migration or Verify scripts.
- Do not alter Azure, reset UAT, run the Development Schema Harness, or deploy production.
- Do not refactor unrelated APIs, UI, trip, location, project, rate, notification or Google behavior.
- Keep the final clean baseline not created/not frozen.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

On successful A2 validation, report `EPIC B2-A2 TEAM/CENTER LIFECYCLE WRITE = READY` and keep
`EPIC B2-B UI CUTOVER = NEXT`; do not mark full Epic B complete.
