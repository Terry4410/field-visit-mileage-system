# NEXT WORK TASK

## Task

Epic B2 — Organization / People / Team UI and write cutover.

## Starting state

- Branch: `feature/uat-fasttrack-v180`
- `EPIC B1 BACKEND FOUNDATION = READY`
- `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- `FINAL CLEAN BASELINE = NOT CREATED / NOT FROZEN`
- Existing v1.7 people/team contracts and write behavior remain the protected authority.

## Required first step

Perform a narrow impact analysis before changing code. Identify every shared component, API,
database dependency, role/permission dependency, and downstream workflow touched by switching
people/team writes from the v1.7 compatibility model to the v1.8 Person/Employment model.

## Scope

- Design the explicit B2 write cutover using stable identity keys only.
- Preserve Person as the long-lived human identity and Employment as the organization employment identity.
- Preserve the legacy UserId bridge while dependent v1.7 workflows still require it.
- Add optimistic concurrency using Base64 rowversion tokens.
- Extend targeted and protected regression tests before changing authoritative write behavior.
- Keep UI/API changes minimal and limited to the approved B2 workflow.

## Hard rules

- Never merge Person records by display name, email alone, or fuzzy matching.
- Effective periods use inclusive boundaries and fail closed on overlap or ambiguity.
- Do not modify protected `1800_001` artifacts or original `1800_001–007` migration scripts.
- Do not reset/mutate Azure UAT, alter security, run the schema harness, or deploy production.
- Do not refactor unrelated v1.7 behavior.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`.

## Completion state

Do not mark full Epic B complete until B2 targeted tests, the full protected regression suite,
and the one automatically triggered UAT Fast-Track validation all pass.
