# Field Visit v1.8.0 — IT install & recovery handover (nondeploy source bundle)

**Current decision: HOLD for IT installation and Business UAT deployment.** This artifact is a reproducible, source-bound candidate, not a signed permission to deploy, drop/reset Azure UAT or apply migrations to a shared database.

## Authoritative chain

- Immutable accepted runtime RC: `9d431ea318fcec34f04f3cc36a8fb6d01d00c48a`. Never edit/rewrite this commit.
- The companion candidate API is derived from this RC by `apply_rc_runtime_candidate.py`, which records the exact trigger compatibility and published-reservation read overlays.
- Every delivered SQL layer has its own SHA-256 in `it-sql-install-order.json`; `checksums.sha256` covers the complete nondeploy bundle. Validate hashes before handling any database.
- `model-baseline-manifest.json` records the inspected SQL2022 schema scope and bound `1.8.0-CREATE-LOWRISK` capability contract. Migration 013 remains RESERVED. Historical 001–020 applied markers are **not** fabricated.
- No credentials, Azure resource identifiers or real employee/test account passwords are included in this bundle.

## SQL Server 2022 clean installation — controlled checklist for IT

1. Obtain separate, exact-environment written approval; record intended SQL host/database, create-only scope, backup/recovery plan and nominated least-privilege operator.
2. Prepare an **empty** SQL Server 2022 database and verify it is not a production/shared/live UAT target until that separate consent exists. Never use this candidate on an existing database.
3. Verify `checksums.sha256` and all per-layer file hashes in `it-sql-install-order.json`. Stop on any missing/mismatched path or unexpected schema/version row.
4. The ordered SQL layers are: EF model baseline → empty SchemaVersions → seven SQL-only core tables → effective 010 triggers → four Option C source-bound tables → SQL-only indexes → 009/010 route CHECKs → 007 CHECKs → 001–004 CHECKs → 005 bounded indexable master facets (new-target, source-snapshot lengths) → 005 source CHECKs/indexes → final 011–020 scoped CHECKs → atomic 017 capability proof/record. Use `it-sql-install-order.json` for exact filenames/order/digests. **The same SQL layers are also replayed independently through sqlcmd into another fresh disposable SQL2022 DB and checked with `50_scoped_clean_install_verify.sql`. This scoped proof is not full 011–020 or production grants certification.**

   Every ODBC sqlcmd connection must use **`-I -b`**. `-I` sets `QUOTED_IDENTIFIER ON`; its ODBC default is OFF, which causes SQL Server **1934** when creating filtered indexes. Keep ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT and CONCAT_NULL_YIELDS_NULL ON and NUMERIC_ROUNDABORT OFF. These are session requirements, not a reason to remove indexes or relax constraints. Triggers must also be created with QUOTED_IDENTIFIER ON. CI includes an OFF→1934 / ON→success regression in an empty disposable database. Raw provider text is suppressed and removed.
5. Use bundled **read-only** `50_scoped_clean_install_verify.sql` after any separately authorized new installation. It tests bounded table/trigger/constraint trust and 017 capability. A full object-definition, dates/roles, permissions and 011–020 fidelity review remains mandatory beyond the scoped verifier.
   Independent `sqlcmd` replay must use an explicit per-connection prelude: `ANSI_NULLS`, `ANSI_PADDING`, `ANSI_WARNINGS`, `ARITHABORT`, `CONCAT_NULL_YIELDS_NULL`, `QUOTED_IDENTIFIER` ON; `NUMERIC_ROUNDABORT` OFF; and `sqlcmd -I`. Missing these options can cause SQL 1934 despite successful SqlClient installation. The prologue does not create schema or migration markers.
6. Run the released API with application-secret injection, an appropriately scoped SQL principal, and candidate flags default-OFF. The original RC and separately derived binary are distinct; use matching manifest hashes.

   The disposable HTTP/API proof uses a separate SQL login/user: object-scoped SELECT/INSERT/UPDATE/DELETE on the installed dbo application tables, explicit denial of writes to SchemaVersions and SchemaMigrationDataBaselines, no sysadmin/db_owner membership, no ALTER/CREATE TABLE, and an actual rejected CREATE TABLE. Installation and test seeding retain a separate disposable administrator connection. Credentials exist only in process memory. This proves the covered runtime paths operate without installation privileges; the final production object/operation permission matrix and real target identity remain separate gates.
7. Prepare test-only Visitor/Leader/Admin accounts without embedding passwords. Execute real Browser→API→SQL business/UAT cases, including cross-org isolation, independent approvals, Google failure/manual fallback and rollback before asking Owner for GO.
8. Verify `frontend-candidate-nondeploy.zip` and `frontend-candidate-manifest.json` against this exact candidate SHA and immutable RC SHA. It is built in normal Vite production mode with Option C UI activation OFF and Demo login; no GitHub Pages deployment occurs. This build and the matching API hash do not certify all 25 frozen Business UAT cases, Browser acceptance, live deployment or recovery.

## Recovery / stop conditions

- The NONDEPLOY pipeline rehearses COPY_ONLY/CHECKSUM backup, RESTORE VERIFYONLY, restoration into a separately named fresh disposable SQL2022 database, DBCC CHECKDB and the same read-only scoped verifier. `independent-install-recovery-evidence.json` is emitted only after all of those succeed. This proves recovery of the empty candidate schema and capability row; it does not prove Azure backup identity, live business-data recovery or application rollback after deployment.
- `release-candidate-binding.json` seals the common candidate source SHA, immutable RC, workflow run, API/Frontend/SQL manifests and recovery result. It verifies both binary ZIP hashes and every ordered SQL hash before upload. `source-code-immutable-rc.zip` plus the pinned overlay script reproduces the candidate source while preserving the original RC. The clean-install generators remain available at the exact PR candidate commit.

- Before any separately approved change to an existing UAT environment, confirm a recoverable, independently verified backup/restore point and a rollback target of **API + Frontend + DB together**. Preserve the exact backup SHA/date and operator approval.
- On failed installation of a **new disposable database only**, stop writers, preserve sanitized failure evidence and discard/recreate only that explicitly authorized disposable database. Do not partially replay or inject missing SchemaVersions history.
- On failure after UAT deployment, disable admission/feature exposure safely and restore the tested application+schema pair under a separate bounded Owner approval. Do not apply this offline candidate directly to Azure UAT.
- Any missing 011–020 contract, trigger-backed EF write incompatibility, role boundary, schema verification or tested recovery keeps the release **HOLD**.

This document and manifest describe the candidate's installable SQL layers and precise open gates. They do not claim final release certification, Production readiness, or authorization for external SQL execution.
