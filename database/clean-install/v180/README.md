# v1.8.0 Clean Install — Current Offline Candidate

**Status: PARTIAL / NOT AN IT INSTALLER / NOT APPROVED FOR UAT.**

This directory's CI candidate uses the exact accepted RC source
`9d431ea318fcec34f04f3cc36a8fb6d01d00c48a` to generate the EF Core relational
model DDL, install it into an empty disposable SQL Server 2022 database, check
EF table/column parity, and exercise one synthetic insert/read in a transaction
that is rolled back. Generated SQL, its source hash, and the test inventory are
published as a short-retention NONDEPLOY Actions artifact.

The runner creates only the fixed database `FieldVisitV180CleanInstall` on the
job's local SQL Server service, refuses to use it if it already exists, disables
pooling and retries, masks exceptions, and drops only the database it created in
its `finally` cleanup path. A cleanup failure makes the job fail.

## Deliberate scope limit

`AppDbContext.GenerateCreateScript()` does not include SQL-only tables,
triggers, procedural objects, migration data contracts, or the full ordered
reference/seed requirements. The verifier inventories CREATE TABLE and trigger
definitions in the RC's numbered migration sources and reports their source
locations. Those objects have **not** been installed by this candidate. The
workflow therefore reports `FULL_CLEAN_INSTALL=HOLD_SQL_ONLY_DDL_AND_TRIGGER_REVIEW_REQUIRED`
even when the isolated EF model baseline tests pass.

Do not run the generated model baseline as a production/UAT install, do not
apply migrations to an existing database, and do not infer that this artifact
proves historical compatibility or complete RBAC/security behavior. This
candidate creates no accounts, roles, employees, business records, passwords,
or historical migration markers.

The remaining engineering needed for a complete IT clean-install package is a
reviewed, deterministic DDL composition for the SQL-only objects and their
final trigger definitions, followed by empty SQL Server 2022 tests of the full
package and the agreed RC/runtime compatibility suite. No Azure/UAT operation
is part of this workflow.
