# Project state

- Phase: `UAT FAST-TRACK DEVELOPMENT`
- Current workstream: `DEVELOPMENT SCHEMA HARNESS`
- Working branch: `feature/uat-fasttrack-v180`
- Target release: `v1.8.0`
- Protected trace point: `post-uat/v1.8.0@e0c18ac69e55a5f559114e59ed343cb20dd51eb6`
- UAT data policy: transactional/test data is disposable until Final UAT Candidate Freeze; real personnel data must not be committed.
- Database strategy: fresh installations use one validated latest-release baseline; environments with retained production data use reviewed versioned migrations.
- Protected baseline: existing validated behavior changes only when unavoidable, with impact analysis and expanded regression coverage.
- Test gates: Fast Regression for ordinary work; Full AI Validation plus Human UAT before candidate freeze.

## Baseline states

- `DEVELOPMENT BASELINE`: **MUTABLE**. It is the isolated schema harness used for Epic B-G coding, integration, and automated regression. It may change when implementation proves a schema correction is necessary and is not an IT handover artifact.
- `FINAL RELEASE CLEAN BASELINE`: **NOT YET CREATED / NOT FROZEN**. It is created only after Epic B-G and Full AI Validation, then proven on an empty database, SHA-locked, and handed to IT.

The development harness uses one schema-only extraction of the current trusted UAT v1.7 structure when available, then evaluates the ordered 1.8 migrations only in disposable SQL. It never mutates `db-fieldvisit-uat`.

## Cost guardrail (hard rule)

Use the minimum Azure read-only connections and GitHub Actions runs. Do not create or scale paid resources, repeatedly wake Azure SQL, or rerun successful jobs. Any destructive Azure/database action, privilege expansion, or production operation remains behind a Human Gate.

## Reconciled live UAT state

Evidence captured 2026-09-08 UTC:

- `api-fieldvisit-uat` (`api-fieldvisit-uat-cxauf4g8fzdyfsd9...`) is Running.
- SQL firewall rule `terry-temp-1800-001-execution` is not present.
- `db-fieldvisit-uat` is Online.
- `dbo.SchemaVersions`: `1.7.0-008 = 1`, `1.8.0-001 = 0`, all `1.8.0-* = 0`.
- `gh-fieldvisit-uat-migrate`: role `db_datareader`; direct `CONNECT`; effective `CONNECT/SELECT` only for checked scopes.
- Temporary rights absent: `db_ddladmin`, `db_datawriter`, `db_securityadmin`, `db_owner`, schema `INSERT`, table `UPDATE`, `CONTROL DATABASE`, and `ALTER ANY ROLE`.

## Active blockers

- `database/baseline/v1.8.0/schema.sql` is deliberately non-executable until a latest-schema snapshot is generated and validated in an isolated disposable SQL environment.
- The existing repository has migrations from prior releases but no complete clean-install foundation script.
- A destructive Azure UAT reset remains behind Human Gate A.

## Next recommended batch

Complete the isolated v1.8.0 baseline build/verification, then implement the existing Epic B Organization / People / Team slice with minimal changes and protected v1.6/v1.7 regression coverage.

## Human gates

- Destructive Azure/database action.
- RBAC, firewall, or SQL privilege expansion.
- Material change to protected working behavior.
- Irreversible architecture choice that cannot be inferred safely.
- Final UAT Candidate, Release Freeze, Production, or IT handover GO.

`GO EXECUTE 1800_001` remains reserved for the preserved migration workflow and is not authorization for Fast-Track work.
