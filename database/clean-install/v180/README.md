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

- `build_optionc_final_checks.py`: 15 effective A+C final-state CHECKs
  sourced from 011/012/016/017/018/020, including the explicit 014→017
  published-Origin supersession. Only disposable SQL Server 2022 is allowed;
  source candidates are not approved deployable migrations.

- The accepted RC API is now built in CI before SQL DDL. The same
  disposable database is then used by the RC API on **loopback-only HTTP**
  for `/health/live`, `/health/ready` (real EF SQL CanConnect), and
  anonymous/invalid-token **401** on Visitor/Leader protected routes.
  The API process is terminated before DB teardown. This only proves
  boot/readiness/unauthenticated denial on the new database: it **does not**
  certify valid Visitor/Leader/Admin entitlements, API writes or full UAT.

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
   behavior—not only creation, object counts, names or enabled trigger state. The new scoped 69-table equality and 010 reason CHECK are stronger evidence but are not proof of all final A+C contracts.
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
