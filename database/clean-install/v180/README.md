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
  the existing source-derived DDL. Original 011/014/016/017 file SHA-256
  is retained in the clean-install manifest; no new migration marker is
  inserted. This is a scoped index proof, not full 011–020 certification.
- `build_optionc_final_checks.py`: 15 effective A+C final-state CHECKs
  sourced from 011/012/016/017/018/020, including the explicit 014→017
  published-Origin supersession. Only disposable SQL Server 2022 is allowed;
  source candidates are not approved deployable migrations.

- A clean-install capability row is now written only after the disposable
  SQL2022 run verifies the pinned 017 source digest and exact
  `LocationCreateReservations` column facets, primary key, four foreign key
  targets/delete actions/trust states, trusted status CHECK, required unique
  key, dependencies, and the scoped final CHECK/index set. Its value is
  `1.8.0-CREATE-LOWRISK`; the row description identifies it as a clean-install
  capability and includes a contract SHA-256. It is **not** evidence that a
  historical migration ran and no 001–020 history is fabricated.
- The workflow publishes a separately identified RC-derived API candidate
  after applying the exact `UseSqlOutputClause(false)` overlay. It runs the
  published package—not an old RC DLL path—through loopback HTTP against this
  same clean-installed database. The package is default-OFF, rejects embedded
  connection/auth secrets, is zipped deterministically, and is read back with
  per-file and package SHA-256 checks before artifact upload. Only the test
  process sets Candidate ON and `IsolatedSqlTest`; neither setting is embedded
  in the candidate package.
- The added HTTP path covers Visitor Location Create/Submit, appointed Leader
  queue/review, Admin evidence and independent final approval, Admin
  self-approval denial with no partial publication writes, plus temporary and
  approved-location Trip creation/submission. Synthetic actors/data remain
  inside the disposable database and it is dropped by the runner.

- The accepted RC is immutable. CI publishes a separately identified
  RC-derived runtime candidate with the pinned `UseSqlOutputClause(false)`
  mapping, boots that exact publish directory on **loopback-only HTTP**, and
  points it at the same clean SQL2022 database. The candidate tests readiness,
  anonymous/invalid-token denial, SQL-backed Demo login for all three roles,
  Visitor temporary-trip create/submit and approved-location Trip creation,
  Visitor Location Create/Submit, appointed Leader review, Admin evidence and
  independent publication, and Admin self-approval denial with no partial
  publication writes. API process and SQL database are removed after the run.
  This is focused candidate evidence; it does **not** prove all 25 frozen UAT
  cases, Live target compatibility, UAT deployment, or Business UAT GO.

Every layer writes a SHA-256 manifest, records original RC source paths and
file digests, refuses missing/duplicate/ambiguous objects, and exports only
NONDEPLOY evidence under the fixed PR source SHA. All generated SQL files,
manifests and checksums are in the CI artifact. A successful isolated job
proves only the explicitly printed PASS stages, never full release readiness.

## Isolated runtime trigger-compatibility candidate

The accepted RC source and artifact remain unchanged. The disposable CI job
builds a separate source-pinned runtime candidate which changes only the
`TeamLeaderAssignments` EF mapping to
`UseSqlOutputClause(false)`. The accepted 1800_002 source installs an enabled
trigger on this table; SQL Server rejects the trigger-incompatible EF OUTPUT
form (error 334).

The overlay validates the accepted RC SHA, exact original and candidate file
SHA-256 values, and exactly one target mapping. The job restores the immutable
RC checkout after compiling the candidate; its overlay manifest and script are
retained in the NONDEPLOY artifact. The fresh-database fixture now creates the
team-leader assignment through EF with the trigger enabled.

This is disposable compatibility evidence only. It does not replace or change
the accepted RC, establish full API approval flows, complete the full schema,
authorize deployment, or permit Business UAT.

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
5. Produce a standalone, version-bound IT install/verify/recovery package with
   exact execution order, least-privilege runtime SQL permissions, source and
   artifact hashes, and partial-failure recovery behavior. The current CI
   artifact contains source-derived SQL layers and the tested candidate API,
   but is **not yet** an IT-ready installer or permissions/SOP package. Do not
   infer Production readiness from disposable SQL tests.

**Release gate stays HOLD until all above proofs exist.**

## Operational boundary

The job starts one disposable SQL Server 2022 container on
`localhost,1433` with a run-generated password that is registered for log
masking before container startup and is not stored in source or the artifact.
It creates only `OptionCIsolated_FieldVisitV180CleanInstall`, refuses a pre-existing database
target, drops only the database it created, and explicitly removes the
container in an always-run cleanup step. The password is not a deployment or
IT credential.

No Azure connection/wake, real UAT SQL, data deletion, migration, production
permission change, deployment, feature activation or Business UAT GO is
authorized by this branch or its CI artifact.
