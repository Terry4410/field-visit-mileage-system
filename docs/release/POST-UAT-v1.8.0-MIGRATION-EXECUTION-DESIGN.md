# v1.8.0 Azure SQL UAT Migration Execution Design

Status: **design-only Freeze Correction**. This document and its workflow draft do not authorize or execute Azure SQL, create an identity, change a role, modify a firewall, seed data, deploy an application or merge `main`.

Workflow draft: `docs/workflows/azure-sql-uat-migration-1800-001.draft.yml`. It is intentionally outside `.github/workflows`, so GitHub Actions does not register or expose it through `workflow_dispatch`.

## Identity and environment separation

| Purpose | Identity / environment | Frozen access rule |
|---|---|---|
| Existing UAT read-only checks | `gh-fieldvisit-uat` / `uat` | Remains exactly Azure `Reader` plus database `db_datareader`; no elevation and never used for migration |
| Future UAT schema migration | `gh-fieldvisit-uat-migrate` / `uat-migration` | Separate OIDC trust and separately reviewed least-privilege Azure/database grants; no Production access |

The migration identity, its federated credential, contained database user and grants do not exist as a result of this package. IT/DBA must review the exact statements in `1800_001/Up.sql` and authorize only the minimum permissions required; `db_owner`, broad shared credentials, SQL passwords and client secrets are not part of this design.

## First execution boundary

The first future executable workflow has no migration selector. Its only SQL write path is:

1. Validate the dispatched ref and fixed UAT target.
2. Validate `DB_NAME() = db-fieldvisit-uat` and exact predecessor `1.7.0-008`; refuse existing `1.8.0-001` or a partially prepared schema.
3. Run only `database/migrations/1800_001_organization_center_team_lifecycle/Up.sql`.
4. Run only the matching `Verify.sql`. This verifies `SchemaVersions`, trusted constraints/indexes and the bounded SHA-256 fingerprints of pre-existing Trip, Snapshot and Snapshot Stop history.
5. Confirm `1.8.0-001` and stop.

The workflow cannot select or execute `1800_002`–`1800_007`. Success does not authorize the next migration. The `1800_001` result, verification output and historical fingerprint evidence require Review before any later execution design is considered.

## Workflow control sequence

| Order | Control | Failure behavior |
|---:|---|---|
| 1 | `workflow_dispatch` confirmation must equal `1800_001` | Fail in branch guard before Environment/OIDC |
| 2 | `github.ref` must equal `refs/heads/post-uat/v1.8.0` | Immediate fail |
| 3 | `environment: uat-migration` | GitHub Required reviewer must approve before the job starts |
| 4 | OIDC sign-in using `AZURE_MIGRATION_CLIENT_ID` | Immediate fail; no secret fallback |
| 5 | Subscription/RG/SQL Server/Database metadata match protected variables and frozen UAT names | Immediate fail; no target repair |
| 6 | Entra-token SQL connection and `DB_NAME()`/predecessor/partial-schema preflight | Immediate fail; no SQL write |
| 7 | `1800_001/Up.sql` | `SET XACT_ABORT ON`; transaction rollback and fail on any SQL error |
| 8 | SQL application lock | `Up.sql` obtains exclusive transaction-owned `FieldVisit.SchemaMigration`; lock failure stops the run |
| 9 | `1800_001/Verify.sql` including historical fingerprints | Any `THROW` stops the run |
| 10 | Post-condition check and Azure logout | Confirm only `1.8.0-001`; stop for Review |

GitHub Environment Required approval and deployment-branch protection are repository settings, not YAML properties. Both must be configured manually; the YAML branch guard remains an additional defense and runs before the protected Environment job.

## Protected configuration required later

Configure these as GitHub Environment variables on `uat-migration`, only after the design receives execution approval:

- `AZURE_MIGRATION_CLIENT_ID`: application/client ID of `gh-fieldvisit-uat-migrate`.
- `AZURE_TENANT_ID`: approved UAT tenant.
- `AZURE_SUBSCRIPTION_ID`: approved UAT subscription.
- `AZURE_RESOURCE_GROUP`: exactly `rg-fieldvisit-uat`.
- `AZURE_SQL_SERVER_NAME`: exactly `sql-fieldvisit-jpe-uat`.
- `AZURE_SQL_DATABASE`: exactly `db-fieldvisit-uat`.

The draft uses OIDC and a short-lived Microsoft Entra Azure SQL access token. It contains no SQL username/password, client secret, API key or permanent firewall operation.

## Manual IT / GitHub gates before future registration

- IT creates `gh-fieldvisit-uat-migrate` and a federated credential restricted to repository `Terry4410/field-visit-mileage-system` and subject `environment:uat-migration`.
- Azure administrators grant only UAT metadata visibility required for target validation; no Production scope.
- DBA creates and grants the separate contained database principal only after statement-level least-privilege review of `1800_001`; do not modify `gh-fieldvisit-uat`.
- Repository administrators create `uat-migration`, add Required reviewers, prevent self-review where supported, restrict deployment branches/tags to `post-uat/v1.8.0`, and configure the protected variables above.
- IT records an approved UAT restore/recovery point and a write-free maintenance window for the future Up + Verify interval.
- A separate reviewed PR may copy the draft to `.github/workflows` for registration. Because `workflow_dispatch` registration is a default-branch concern, any `main` change remains a separate explicit approval; this package does not perform it.
- Immediately before a future dispatch, reviewers compare the registered workflow and SQL file hashes with the approved commit and confirm that no `1800_002`–`1800_007`, Seed/Import, firewall or deployment step is present.

## Hard stop conditions

Stop without remediation if the branch, identity, subscription, resource group, SQL Server or database differs; Environment approval is absent; predecessor/partial-schema state differs; SQL application lock cannot be obtained; network access would require firewall relaxation; permissions are missing or broader than approved; any SQL step fails; or the historical fingerprint changes.

No workflow step may create an identity, alter a role, change a firewall, run Seed/Import, deploy application code, access Production or continue to `1800_002`.
