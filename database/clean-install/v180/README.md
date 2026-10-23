# Field Visit v1.8.0 — SQL Server 2022 Clean Install (NONDEPLOY candidate)

**Status: NONDEPLOY CANDIDATE. Full v1.8.0 Clean Install / IT Handover / Business UAT = HOLD.**
No Azure, Production or shared-UAT database may execute these disposable-only
candidate fragments. This package does **not** apply historical migrations.

## Immutable authority and proven isolated scope

- Accepted application RC: `9d431ea318fcec34f04f3cc36a8fb6d01d00c48a`.
- Empty isolated SQL Server **2022** database, created and dropped only by
  the GitHub Actions `clean-install` job; no Owner-local installation needed.
- EF Core model source generates the baseline for **57 mapped tables and 664
  mapped columns**. The runner checks table/column parity, FK/CHECK trust state,
  and an Organization insert/read with transaction rollback.
- `00_schema_versions.sql`: NEW target `dbo.SchemaVersions` DDL. It is
  deliberately empty on first install: NO fabricated historical migration
  rows or invented 1.5 provenance. The final consolidated version marker and
  this newly authored ledger shape still require independent acceptance.
- `build_sql_only_tables.py`: seven exact source-pinned SQL-only CREATE TABLE
  statements (SchemaMigrationDataBaselines plus six notification/outbox tables).
- `build_effective_triggers.py`: the **21 effective** v1.8.0-001…010 trigger
  definitions derived from dynamic `sp_executesql` source, with the 008
  MileageRateRules trigger supersession handled explicitly. The disposable
  database validates names, enabled state and stored-module presence.
- `build_optionc_sql_only_tables.py`: four SQL-only candidate table shapes,
  from 014 (publication stages, published versions), 017 (create
  reservations), 019 (merge details). These source scripts remain explicitly
  **NONEXECUTABLE/DISPOSABLE DESIGN**; extracted individual CREATE TABLE
  candidates are only tested in the fresh disposable SQL2022 database.
- `build_sql_only_indexes.py`: 13 selected extra source-pinned indexes for
  these SQL-only tables (seven notification/outbox, five 014, one 017).
  These **do not** prove all final indexes are complete.

- `00_schema_versions.sql`: a **new, empty** `dbo.SchemaVersions` target
  contract with **no false historical migration markers**. It does not
  pretend that original 1.5/1.7/1.8 migrations were executed.
- `20_route_reason_check.sql`: target v1.8.0-010 trusted
  `CK_RouteCalculationAttempts_Reason` (VisitorCalculate, LeaderRetry,
  CorrectionRecalculate, BackgroundMileageJob), directly installed without
  replaying an old predecessor migration.
- The final isolated SQL Server catalog is compared against the **exact
  scoped set of 69 dbo tables** (57 EF + 7 SQL-only core + 4 Option C
  candidate shapes + 1 new version ledger), not only per-layer counts.
- The *same disposable database* additionally executes a synthetic
  Organization/Team/Visitor/Leader/Role/Trip/AuditLog EF persistence smoke,
  then rolls back and asserts no synthetic business rows remain. This is
  **not** Browser/API authorization, self-approval prevention or RBAC proof.

- `CleanInstallOptionCIndexProof`: same disposable SQL2022 catalog
  verifies all **15 unique final A+C index definitions**, including correct
  source index name, table, key-column order, uniqueness, filtered predicate
  and enabled state. The nine indexes already installed by EF from 011/016
  are **not replayed**. Six SQL-only 014/017 indexes are cross-checked against
  the existing source-derived