# Field Visit v1.8.0 — SQL Server 2022 Clean Install (NONDEPLOY candidate)

**Status: PARTIAL. Full v1.8.0 Clean Install / IT Handover / Business UAT = HOLD.**
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

Every layer writes a SHA-256 manifest, records original RC source paths and
file digests, refuses missing/duplicate/ambiguous objects, and exports only
NONDEPLOY evidence under the fixed PR source SHA. All generated SQL files,
manifests and checksums are in the CI artifact. A successful isolated job
proves only the explicitly printed PASS stages, never full release readiness.

## Required follow-on engineering (not yet certified)

1. Reconcile **all** application runtime SQL contracts with this consolidated
   candidate, including missing ALTER/ADD constraints and indexes in source
   migrations 1600/1700/1800, canonical A+C 011–020 final DDL, any stored
   modules, trigger behavior, defaults, history and version ledger. 013 stays
   **reserved**, not silently renumbered or installed. Do not spoof historical
   SchemaVersion rows.
2. Verify complete object definitions, FK/CHECK trust and business constraint
   behavior—not only creation, object counts, names or enabled trigger state.
3. Establish explicitly reviewed minimal reference/configuration seed and
   nonsecret Visitor/Leader/Admin test identity contract, with no real
   employee/production credentials.
4. Exercise necessary RC Browser/API/SQL workflows on **this same genuinely
   clean-installed DB**: Visitor trips and mileage Google/fallback; independent
   Leader/Admin review and self-approval denial; Location create/merge;
   snapshot/audit immutability, cross-org/team/ownership denial and role
   revocation. Existing separately accepted synthetic test suites cannot be
   relabeled as clean-build compatibility.
5. Package exact SQL Server 2022 installation and verification commands,
   least-privilege runtime SQL grants, source and artifact hashes, and
   installer recovery/partial failure behavior for independent corporate IT
   execution. Do not infer Production readiness from Azure test environment.

**Release gate stays HOLD until all above proofs exist.**

## Operational boundary

The runner requires fixed local Docker SQL2022 at `localhost,1433`, creates
only `FieldVisitV180CleanInstall`, refuses pre-existing database targets, and
drops only the database it created. Its service password is disposable CI
fixture-only, never a deployment or IT credential.

No Azure connection/wake, real UAT SQL, data deletion, migration, production
permission change, deployment, feature activation or Business UAT GO is
authorized by this branch or its CI artifact.
