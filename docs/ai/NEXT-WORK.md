# NEXT WORK TASK

## Model
Use **GPT-5.6 Luna**.

Reason: architecture and correction strategy are already decided. This is a narrow harness/session configuration fix plus one controlled CI execution. Do not redesign anything.

## Repository / Branch
- Repository: `Terry4410/field-visit-mileage-system`
- Branch: `feature/uat-fasttrack-v180`
- Current known head: `b93e095d453d92fcf183ca2c52870d91e48822e6`

## Human Gate Decision
**APPROVED**: apply the minimal disposable-harness SQL session-options correction and perform **one** new consolidated harness execution.

No other permission, security, Azure, production, or migration-artifact change is approved.

## Verified Current State
The development-only compatibility layer is implemented.

Harness result so far:

- `1800_001 DEV APPLY`: PASS
- `1800_001 ORIGINAL VERIFY`: PASS
- `1800_002 DEV APPLY`: FAIL
- `1800_002 ORIGINAL VERIFY`: SKIPPED
- `1800_003-007`: SKIPPED
- Database B: NOT RUN
- Harness Fast Regression: NOT RUN

Exact failure:

`CREATE INDEX failed because SET option 'QUOTED_IDENTIFIER' has incorrect settings.`

The first failing object is filtered index `UX_Persons_LegacyUserId` in `1800_002.dev.sql`.

This is classified as a **disposable harness session-configuration defect**, not a schema-design defect.

## Approved Correction
Implement deterministic SQL session SET options for development compatibility execution.

Create a clearly development-only file, e.g.:

`database/development/v1.8.0/compat/session-options.sql`

with exactly the required deterministic session settings:

```sql
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
```

The session-options file must execute in the **same sqlcmd connection** as each development apply script.

Do not run it in a separate sqlcmd process and assume the settings persist.

A safe pattern is equivalent to:

```bash
cat database/development/v1.8.0/compat/session-options.sql "$apply" | "${sqlcmd[@]}" -I
```

or another deterministic same-session implementation.

Use `sqlcmd -I` defensively as well.

## Hash / Manifest Requirement
Pin the development session-options file in the existing development compatibility manifest or equivalent validation so unexpected changes fail closed.

Do not alter any original migration Up.sql or Verify.sql hashes.

Protected `1800_001` must remain byte-for-byte unchanged.

## Harness Scope
Update only what is necessary for deterministic session configuration and its validation.

Then perform all possible local/static checks before push.

Make **one corrective commit/push**.

Allow the temporary harness push trigger to launch **one** new Development Schema Harness run.

Do not manually start a second harness run.

Do not rerun on failure. If the run fails at any migration or proof stage, stop and report the exact failure.

## Required Full Harness Proof
The new run must attempt, in order:

- `1800_001 DEV APPLY` + original Verify
- `1800_002 DEV APPLY` + original Verify
- `1800_003 DEV APPLY` + original Verify
- `1800_004 DEV APPLY` + original Verify
- `1800_005 DEV APPLY` + original Verify
- `1800_006 DEV APPLY` + original Verify
- `1800_007 DEV APPLY` + original Verify
- Database A structural / cleanliness proof
- Database B empty reconstruction proof
- development/baseline Verify checks
- Fast Regression once

Do not declare PASS if any required stage is skipped.

## Hard Boundaries
Do NOT:

- modify original `1800_001-007` migration Up.sql / Verify.sql
- modify protected migration hashes
- execute compatibility SQL against Azure UAT
- execute original 1800 migration SQL against Azure UAT
- change SQL grants, including current temporary VIEW DEFINITION
- change Azure RBAC
- change firewall
- create Azure resources
- change GitHub Environment policy
- remove temporary feature-branch admission yet
- remove temporary harness push trigger yet
- merge to main
- start Epic B
- make API/frontend/application behavior changes
- perform unrelated refactoring

Azure access remains one read-only schema-only extraction maximum for this run.

## Cost Guardrail
Target one corrective push and one harness run.

An automatic UAT Fast-Track verification caused by the branch push is acceptable; do not manually rerun it.

No paid resource/tier changes.

## Success Handling
If the full harness passes:

- report `DEVELOPMENT SCHEMA = READY / MUTABLE / NON-FINAL`
- report `EPIC B = READY`
- stop
- do not perform cleanup automatically
- do not start Epic B

Cleanup will be handled as a separate controlled step:

1. revoke temporary `VIEW DEFINITION`
2. remove `feature/uat-fasttrack-v180` from GitHub `uat` Environment
3. remove temporary harness push trigger

## Failure Handling
If any stage fails:

- fail closed
- do not modify original migration source
- do not start another Actions run
- identify whether it is harness/session, compatibility-adapter, predecessor-definition, or schema-design failure
- propose the narrowest correction
- stop

## End Report
Return only:

1. SESSION-OPTIONS CORRECTION
2. FILES CHANGED + WHY
3. SOURCE HASH / PROTECTED 001 STATUS
4. 1800_001-007 DEV APPLY + ORIGINAL VERIFY MATRIX
5. DATABASE A RESULT
6. DATABASE B RESULT
7. DEVELOPMENT SCHEMA STATUS
8. FAST REGRESSION RESULT
9. ACTIONS RUN(S)
10. AZURE UAT MUTATION STATUS
11. COST / USAGE
12. REMAINING RISKS
13. EPIC B READINESS
14. CLEANUP NOW SAFE: YES/NO
15. HUMAN GATE, if any

Begin now. Do not redesign the architecture.
