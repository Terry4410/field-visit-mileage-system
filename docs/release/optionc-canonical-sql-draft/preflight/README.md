# Option C UAT read-only preflight

This is a reviewable SQL candidate, not an executable migration. It is intentionally
parameterized with the exact approved server/database and must run only from the existing
post-uat/v1.8.0-guarded workflow, using Entra OIDC and ApplicationIntent=ReadOnly.

It performs identity, SchemaVersions, catalog constraint, partial-migration, row-count,
historical-reference and deterministic catalog-fingerprint checks. It contains no DDL,
DML, restore, repair or migration statements. A mismatch is a STOP condition; the script
must not be used to repair a target.

The Recovery branch does not contain an authorized exact UAT target, environment values,
allow-list/change-ticket, backup/restore evidence, or permission to bypass the branch guard.
Therefore target execution is intentionally NOT RUN in this batch. Before any separately
authorized UAT execution, IT must supply the exact server/database, approved read-only
Entra principal, target allow-list/change ticket, backup/retention/restore evidence, and
deployment/rollback contacts.

Candidate order: target/backup gate -> 011 -> Verify -> 012 -> Verify ->
014/backfill -> Verify -> LocationMergeDetails additive -> StageEvidenceAuditEvents additive ->
backend/frontend deployment. Migration 1800_013 remains reserved and is never reused.

## Phase-aware disposable SQL verification

The nondeploy CI includes
`V180OptionCSqlDraftRehearsalTests.Uat_readonly_preflight_executes_at_010_011_012_014_and_additive_audit_phases`.
It executes this exact preflight SQL in a fresh `OptionCIsolated_*` localhost SQL
Server 2022 database at schema phases 010, 011, 012, 014, and after the
additive canonical audit constraint extension. It also verifies STOP behavior
for an unexpected parallel audit table and an incorrect database identity.

This test substitutes SQLCMD target placeholders **only in its disposable
harness**; it does not exercise the Azure Entra read-only identity or execute
the guarded UAT workflow. A passing CI test proves isolated phase
compatibility, **not** that the exact UAT target, permissions, backups,
rollback, or Owner execution authorization have been verified. The UAT
read-only preflight remains NOT RUN until those independent gates are met.
