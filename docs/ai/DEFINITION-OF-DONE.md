# Definition of Done

Every change must satisfy all applicable items.

## Scope and impact

- Requested behavior and out-of-scope behavior are stated.
- Shared components, API contracts, database objects, roles/permissions, and downstream workflows are identified before coding.
- The diff is minimal; no unrelated refactor, rename, UI redesign, API/schema/permission change, or aesthetic cleanup is included.
- If a shared dependency changes, the reason is unavoidable, all potentially affected protected features are listed, and regression coverage is extended first.

## Evidence required in completion report

- Changed-file list and the necessity of every file.
- Targeted tests for the requested behavior.
- Regression tests for affected existing functionality.
- Full protected critical regression result.
- Any skipped validation and its explicit blocker.

## Test layers

- Unit tests where logic is isolated.
- API/integration tests for contracts and persistence behavior.
- Fixed Critical Regression Suite.
- Role-based E2E for Visitor, Leader, Admin, and retained historical Supervisor behavior while it exists.
- Data-integrity, boundary, negative/error-path, duplicate, concurrency, and unusual-sequence scenarios where applicable.
- AI-generated scenario expansion before Final UAT Candidate.
- Final Human UAT for real workflow usability and business correctness.

## Hard outcomes

- An unexpected protected behavior change is `REGRESSION FAIL`, even if the replacement appears cleaner.
- `BUG -> FIX -> PERMANENT REGRESSION TEST`: every reproducible fixed defect has a permanent regression test.
- Database baseline and reset changes pass static safety validation and isolated database verification before any Azure execution request.
- No secrets or real employee personal data are committed.
- Required Human Gates are recorded and honored.

## Cost guardrail (hard rule)

- Use the minimum GitHub Actions runs and standard runners; do not rerun successful jobs without a failure-specific reason.
- Use at most one necessary read-only Azure schema extraction for this harness; never create, reset, scale, or replace Azure resources.
- Stop at a Human Gate before any destructive database action, privilege/security expansion, or production deployment.
