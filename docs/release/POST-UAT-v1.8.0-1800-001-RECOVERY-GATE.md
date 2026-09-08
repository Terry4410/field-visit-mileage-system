# v1.8.0 / 1800_001 Azure SQL UAT Recovery Gate

Status: **design and evidence checklist only**. This gate does not restore, copy, pause, scale, seed, migrate, change a firewall, or modify any Azure resource.

Target: Azure SQL Database `db-fieldvisit-uat` on `sql-fieldvisit-jpe-uat` in `rg-fieldvisit-uat`.

## Current conclusion

Azure SQL Database provides automated backups and Point-in-Time Restore (PITR) across service tiers, but the **specific UAT database's short-term retention and earliest restore point have not been verified in this preparation session**. The local preparation environment has no Azure CLI/authenticated Azure session, and no permission was expanded or token reused to obtain that metadata.

Therefore, `1800_001` is **NO-GO** until IT records the read-only evidence below and confirms that the proposed restore timestamp is inside the available restore window. A successful historical fingerprint check does not replace recoverability.

Microsoft documents that Azure SQL Database PITR creates a **new database** and cannot overwrite the current database in place. Restore requires separate Azure RBAC such as Contributor or SQL Server Contributor; the migration identity's Resource Group Reader role must not be expanded for recovery.

## Read-only Azure metadata evidence

Run these commands manually from an IT-controlled, already-authenticated Azure CLI session. They are inspection commands only; do not run any `set`, `update`, `restore`, `copy`, `pause`, or `scale` command.

```bash
az account show \
  --query '{subscriptionId:id,subscriptionName:name,state:state,tenantId:tenantId}' \
  --output json

az sql db show \
  --subscription '<approved-subscription-id>' \
  --resource-group 'rg-fieldvisit-uat' \
  --server 'sql-fieldvisit-jpe-uat' \
  --name 'db-fieldvisit-uat' \
  --query '{id:id,name:name,status:status,location:location,edition:edition,currentServiceObjectiveName:currentServiceObjectiveName,earliestRestoreDate:earliestRestoreDate,creationDate:creationDate}' \
  --output json

az sql db str-policy show \
  --subscription '<approved-subscription-id>' \
  --resource-group 'rg-fieldvisit-uat' \
  --server 'sql-fieldvisit-jpe-uat' \
  --name 'db-fieldvisit-uat' \
  --query '{retentionDays:retentionDays,diffBackupIntervalInHours:diffBackupIntervalInHours}' \
  --output json
```

Record the raw JSON as a Review attachment. If `earliestRestoreDate` is absent, null, later than the planned recovery point, or the short-term retention query is denied, stop and ask the Azure owner to verify the same fields in Azure Portal. Do not elevate `gh-fieldvisit-uat-migrate` merely to inspect or restore backups.

## Mandatory pre-migration evidence record

All values must be captured immediately before the future Environment approval and again just before `Up.sql`:

| Evidence | Required value / rule |
|---|---|
| Review-approved source | Exact `post-uat/v1.8.0` 40-character commit SHA; must equal protected `APPROVED_MIGRATION_COMMIT_SHA` and dispatched `GITHUB_SHA` |
| SQL locks | Exact reviewed SHA-256 for `1800_001/Up.sql` and `Verify.sql` |
| Recovery timestamp | `T0_UTC`, recorded in ISO 8601 with milliseconds and `Z`; use a trusted UTC clock and retain the workflow start time |
| Azure target | Exact Subscription, `rg-fieldvisit-uat`, `sql-fieldvisit-jpe-uat`, and `db-fieldvisit-uat` resource IDs/names |
| Database status | `Online`/available state and recorded service tier/objective; no scale operation during the window |
| PITR | Actual `earliestRestoreDate`, short-term `retentionDays`, and confirmation that `T0_UTC` is within the recoverable window |
| Schema state | `DB_NAME() = db-fieldvisit-uat`; latest `SchemaVersions` entry is exactly `1.7.0-008`; `1.8.0-001` absent |
| Partial schema | All three new tables and all new columns from `1800_001` absent |
| Historical state | Pre-migration counts and a durable copy of the `Up.sql`/`Verify.sql` fingerprint evidence for bounded Trip, Snapshot, and Snapshot Stop rows |
| Application state | Named Business owner confirms a write-free window; background jobs and API writes are quiesced; no deployment is running |
| Recovery owner | Named Azure operator who already holds approved restore permission; migration identity remains Reader at Azure scope |

Recommended read-only SQL evidence (run only through a separately reviewed read-only check or during the future migration workflow preflight):

```sql
SELECT DB_NAME() AS DatabaseName, SYSUTCDATETIME() AS ObservedAtUtc;

SELECT VersionNumber, Description, AppliedAt, AppliedBy
FROM dbo.SchemaVersions
ORDER BY AppliedAt DESC, VersionNumber DESC;
```

## Database copy decision

A separate database copy is **optional, not a default prerequisite**, when all of these are true:

- PITR metadata is confirmed and covers `T0_UTC`.
- IT accepts the documented restore time/RPO for UAT.
- The write-free window is enforceable.
- The additive migration and historical fingerprints remain exactly as reviewed.

Consider a copy only if PITR evidence is inconclusive, recovery-time testing is required, or Business requires a side-by-side immutable comparison. A copy creates a billable database and needs broader Azure permissions; it therefore requires separate cost, naming, retention, cleanup, access, and approval decisions. This package must not create it.

## Write-free maintenance window

1. Business and IT announce the exact UTC start/end and responsible owners.
2. Stop new application writes through an already-approved application/operational maintenance control; drain in-flight writes and stop background writers. Do not use a firewall change or ad hoc database-role change as the maintenance mechanism.
3. Confirm no deployment, Seed/Import, data correction, or other migration is active.
4. Capture the Azure metadata, `T0_UTC`, target resource IDs, schema predecessor, and partial-schema evidence.
5. Separately verify the temporary migration grants and protected GitHub Environment approval configuration.
6. Only after a future explicit execution authorization, dispatch the exact approved commit and run only `1800_001 Up.sql → Verify.sql → STOP_FOR_REVIEW`.
7. Keep writes disabled until Verify and historical fingerprints pass and the reviewer accepts the evidence.
8. Revoke the temporary DDL/DML permissions through the separately reviewed DBA step. Re-enable writes only after the review decision.

If a write occurs after `T0_UTC`, stop. A full PITR replacement could discard that legitimate write, so IT and Business must reconcile it before choosing recovery.

## Verify-failure decision matrix

| Observed state | Default decision | Reason / required action |
|---|---|---|
| `Up.sql` throws and its transaction rolls back; predecessor remains exactly `1.7.0-008`; no partial object exists | Do not restore. Revoke temporary rights, preserve logs, diagnose, and submit a newly reviewed retry/fix | `SET XACT_ABORT ON` and the explicit transaction should leave no committed `1800_001` state. Prove that before retrying. |
| `Up.sql` committed, `Verify.sql` fails only on an additive DDL condition, and all historical fingerprints/counts match | Prefer a reviewed forward-fix | Restoring the whole database may be more disruptive than an additive corrective migration. Never edit the applied script or rerun `Up.sql`; create a separately reviewed corrective version. |
| `Up.sql` committed and any historical Trip/Snapshot fingerprint or bounded count differs | Freeze writes and prefer PITR/new-database recovery assessment | The migration safety invariant failed. Preserve evidence; do not make an ad hoc data repair. Azure owner selects a point after reviewing any post-`T0_UTC` writes. |
| Schema state is partial/ambiguous, constraints are untrusted, or the cause cannot be bounded | Freeze writes; Recovery owner chooses PITR versus a reviewed forward-fix | No automatic rollback or second execution. Compare restored copy/current database if separately approved. |
| PITR/retention evidence is absent or `T0_UTC` is outside the recoverable window | No migration execution | Recovery cannot be demonstrated. A separately approved database copy or corrected backup policy decision is required before rescheduling. |
| Restore is selected | Restore to a new database, validate it, then perform a separately approved replacement/cutover | Azure SQL Database PITR does not overwrite the source database. Restore/cutover is a distinct incident procedure, not part of the migration workflow. |

## Hard stops

- Required reviewer evidence, write-free owner, Recovery owner, or PITR evidence is missing.
- `earliestRestoreDate`/retention does not cover the recorded `T0_UTC`.
- Target identity/resource/branch/approved SHA/hash differs from the reviewed values.
- Schema predecessor is not exactly `1.7.0-008`, or partial `1800_001` state exists.
- Any migration, grant, restore, copy, firewall, scale, Seed/Import, deployment, or Production action is proposed under this preparation gate.

## Microsoft recovery references

- [Restore a database from Azure SQL Database backups](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql)
- [Azure CLI `az sql db` and short-term retention policy commands](https://learn.microsoft.com/en-us/cli/azure/sql/db?view=azure-cli-latest)
