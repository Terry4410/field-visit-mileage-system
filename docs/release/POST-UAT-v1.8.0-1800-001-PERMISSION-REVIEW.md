# v1.8.0 / 1800_001 Statement-Level Permission Review

Status: **execution-readiness review only**. Nothing in this document grants a permission or authorizes migration execution.

Target principal: `gh-fieldvisit-uat-migrate` in `db-fieldvisit-uat`. The existing `gh-fieldvisit-uat` principal is out of scope and must remain `db_datareader` only.

## Reviewed SQL unit

- `database/migrations/1800_001_organization_center_team_lifecycle/Up.sql`
- Required predecessor: exactly `1.7.0-008`
- Allowed execution sequence: `Up.sql` → `Verify.sql` → `STOP_FOR_REVIEW`
- The review assumes the migration principal retains its existing `db_datareader` membership for all user-table reads.

## Statement-level permission matrix

| `Up.sql` statement / lines | Required effective permission | Recommended grant for UAT | Reason |
|---|---|---|---|
| `BEGIN TRANSACTION`, `COMMIT`, `ROLLBACK`, variables, `THROW`, built-in functions | No additional database grant | None | Session control and built-in expressions do not justify a broader role. |
| `sys.sp_getapplock` with omitted `@DbPrincipal` (lines 7–15) | Membership in the default `public` database role | None | Every database user is already a member of `public`; do not add an explicit `EXECUTE` grant. The lock is transaction-owned and scoped to the target database. |
| Read `SchemaVersions`; read `Teams`; historical fingerprint reads of `VisitTrips`, `VisitTripSnapshots`, `VisitTripSnapshotStops` (lines 17–60, 78–142) | `SELECT` on the user tables | Retain existing `db_datareader`; no new read role | The role grants database-wide user-table/view `SELECT`. It is already present and was proven by the migration-identity read-only smoke test. |
| `OBJECT_ID`, `COL_LENGTH`, `sys.*` metadata used by preflight/verify | Metadata visibility derived from permissions on the securables; some system catalog rows are public | Covered by `db_datareader` plus the temporary DDL permission | No standalone `VIEW DEFINITION` is needed for this fixed script. The future Verify script reports effective permissions before execution. |
| `CREATE TABLE dbo.SchemaMigrationDataBaselines`, `dbo.Centers`, `dbo.TeamCenterAssignments` (lines 62–76, 174–204, 210–233) | `CREATE TABLE` in the database plus ability to create/alter objects in `dbo`; referenced-key permission for FKs | Temporary `db_ddladmin` | Microsoft documents `CREATE TABLE` + schema `ALTER` as the direct permission pair. `db_ddladmin` supplies database DDL capabilities, including `CREATE TABLE`, `ALTER ANY SCHEMA`, and database `REFERENCES`, without `db_owner`. |
| `INSERT dbo.SchemaMigrationDataBaselines ... SELECT ...` (lines 83–133) | `INSERT` on a table that does not exist until this same atomic batch creates it | Temporary `GRANT INSERT ON SCHEMA::dbo` | An object-level grant cannot be applied before the new table exists without splitting the reviewed atomic migration. Schema-scoped `INSERT` is therefore the narrowest reliable pre-grant; it must be revoked after the reviewed unit. |
| `ALTER TABLE dbo.Organizations ADD ... RowVersion`; `ALTER TABLE dbo.Teams ADD ... RowVersion` (lines 144–159) | `ALTER` on each table; `UPDATE` can be required because adding a non-null populated column updates existing rows | DDL via temporary `db_ddladmin`; temporary object-level `UPDATE` only on `dbo.Organizations` and `dbo.Teams` | This avoids `db_datawriter`. The permission exists only to let SQL Server populate the new `ROWVERSION` values; the script contains no business-data `UPDATE` statement. |
| `ALTER TABLE dbo.VisitTripSnapshots ADD` nullable snapshot columns (lines 272–276) | `ALTER` on `dbo.VisitTripSnapshots` | Temporary `db_ddladmin` | The change is additive and nullable; no backfill or `UPDATE` grant is needed for this table. |
| `ALTER TABLE ... ADD CHECK/FOREIGN KEY` (lines 150–165) | `ALTER` on the child table; `REFERENCES` on referenced keys | Temporary `db_ddladmin` | The fixed role covers the DDL and database `REFERENCES`. All constraints are created `WITH CHECK` where applicable and are verified as trusted. |
| Inline PK, UNIQUE, DEFAULT, CHECK, FK constraints in new `CREATE TABLE` statements | Same `CREATE TABLE`/schema DDL authority; referenced-key permission for FKs | Temporary `db_ddladmin` | These are part of creating the three reviewed tables, not general application DML. |
| `CREATE [UNIQUE] INDEX` on `Teams`, `Centers`, `TeamCenterAssignments` (lines 167–172, 206–208, 235–236) | `ALTER` on the target table or `db_ddladmin` membership | Temporary `db_ddladmin` | Microsoft lists either table `ALTER` or the fixed role. The same temporary role already covers the other DDL. |
| `CREATE TRIGGER ... ON dbo.TeamCenterAssignments` through `sp_executesql` (lines 238–270) | `ALTER` on the trigger table; ordinary execution of `sys.sp_executesql` | Temporary `db_ddladmin`; no extra `EXECUTE` grant | Microsoft requires table `ALTER` for a DML trigger. The trigger is fixed in the approved/hash-locked `Up.sql`. |
| `INSERT dbo.SchemaVersions` (lines 278–285) | `INSERT` on `dbo.SchemaVersions` | Covered by temporary `GRANT INSERT ON SCHEMA::dbo` | The schema grant is already unavoidable for the not-yet-created baseline table and implies `INSERT` on `SchemaVersions`; adding a redundant object grant would not reduce effective access. It is deliberately not `db_datawriter` and is revoked with the execution window. |

`Verify.sql` is read-only. Its user-table reads are covered by retained `db_datareader`; catalog visibility is covered by the temporary DDL authority and permissions on the reviewed objects.

## Option comparison

| Model | Grants | Advantages | Risks / operational cost |
|---|---|---|---|
| Granular DDL grants | `CREATE TABLE`; `ALTER ON SCHEMA::dbo`; object/table `ALTER` and `REFERENCES`; schema `INSERT`; object-level `UPDATE` | Looks explicit when read as a list | `ALTER ON SCHEMA::dbo` is already broad across the schema and carries documented ownership-chaining risk. It overlaps several object grants, is easy to under-grant for inline FKs/indexes/triggers, and is fragile across later migrations. It does not materially isolate this migration in a database whose application objects share `dbo`. |
| Temporary `db_ddladmin` + minimal DML | Existing `db_datareader`; temporary `db_ddladmin`; temporary `INSERT ON SCHEMA::dbo`; temporary `UPDATE` only on `Organizations` and `Teams` | Standard, predictable DDL behavior; easy to verify and revoke; avoids `db_owner`, `db_datawriter`, credentials, and secret-based authentication | `db_ddladmin` can run any database DDL and Microsoft warns that members can potentially elevate through privileged code. It is acceptable only for the short approved window with Environment approval, exact commit/hash locks, fixed workflow, write-free window, and immediate revoke. |

## Recommendation

Use **temporary `db_ddladmin` plus minimal DML** for the single `1800_001` UAT execution unit:

1. Keep existing `db_datareader`.
2. Immediately before the approved window, a DBA separately runs the reviewed Grant script.
3. Grant temporary `db_ddladmin` membership.
4. Grant temporary `INSERT ON SCHEMA::dbo`; this covers both the newly created baseline table and `dbo.SchemaVersions` without `db_datawriter`.
5. Grant temporary `UPDATE` only on `dbo.Organizations` and `dbo.Teams`.
6. Do not grant `db_owner`, `db_datawriter`, `db_securityadmin`, SQL credentials, a client secret, or Production access.
7. After the result and evidence are captured, a DBA runs the separately reviewed Revoke script. Future migrations require a fresh statement-level review and re-grant.

This is the lowest operational-risk model that remains maintainable for the current `dbo`-based schema. The executable workflow must never run the Grant or Revoke scripts.

## Microsoft permission references

- [CREATE TABLE permissions](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-table-transact-sql?view=sql-server-ver17)
- [ALTER TABLE permissions](https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-table-transact-sql?view=sql-server-ver17)
- [CREATE INDEX permissions](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql?view=sql-server-ver17)
- [CREATE TRIGGER permissions](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql?view=sql-server-ver17)
- [`sp_getapplock` permissions](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17)
- [Database-level roles](https://learn.microsoft.com/en-us/sql/relational-databases/security/authentication-access/database-level-roles?view=sql-server-ver17)
- [Schema permission cautions](https://learn.microsoft.com/en-us/sql/t-sql/statements/grant-schema-permissions-transact-sql?view=sql-server-ver17)

## Hard stops

- Any required permission is missing or any forbidden broad role is present.
- The target is not exactly `db-fieldvisit-uat` or the principal is not exactly `gh-fieldvisit-uat-migrate` with type `EXTERNAL_USER`.
- The exact predecessor, clean partial-schema checks, approved commit, or SQL hashes do not match.
- Required GitHub Environment approval or the Recovery Gate evidence is absent.
- Any request would change `gh-fieldvisit-uat`, a firewall, Production, Seed/Import, or application deployment.
