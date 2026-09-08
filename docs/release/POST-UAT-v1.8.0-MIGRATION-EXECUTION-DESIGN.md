# v1.8.0 Azure SQL UAT Migration Execution Design

Status: **design-only Freeze Correction**. This document and its workflow draft do not authorize or execute Azure SQL, create an identity, change a role, modify a firewall, seed data, deploy an application or merge `main`.

Workflow draft: `docs/workflows/azure-sql-uat-migration-1800-001.draft.yml`. It is intentionally outside `.github/workflows`, so GitHub Actions does not register or expose it through `workflow_dispatch`.

## Identity and environment separation

| Purpose | Identity / environment | Frozen access rule |
|---|---|---|
| Existing UAT read-only checks | `gh-fieldvisit-uat` / `uat` | Remains exactly Azure `Reader` plus database `db_datareader`; no elevation and never used for migration |
| Future UAT schema migration | `gh-fieldvisit-uat-migrate` / `uat-migration` | Separate OIDC trust and separately reviewed least-privilege Azure/database grants; no Production access |

The migration identity, its federated credential, contained database user and grants do not exist as a result of this package. IT/DBA must review the exact statements in `1800_001/Up.sql` and authorize only the minimum permissions required; `db_owner`, broad shared credentials, SQL passwords and client secrets are not part of this design.

## Approved source lock

The executable workflow must not treat the latest `post-uat/v1.8.0` branch head as approved. After the user approves one exact commit, a repository administrator records its lowercase 40-character SHA in the protected `uat-migration` Environment variable `APPROVED_MIGRATION_COMMIT_SHA`. This value is not a `workflow_dispatch` input and the dispatcher cannot override it.

The draft checks out that exact SHA and, before Azure OIDC login, requires both `GITHUB_SHA` and the checked-out `HEAD` to equal it. Therefore, if the branch advances after approval, dispatching the newer branch head fails before any Azure token is requested. A workflow or SQL change requires a new Review and a deliberate update of the protected approved-commit variable.

The SQL content locks reviewed with this design are:

| File | SHA-256 |
|---|---|
| `1800_001_organization_center_team_lifecycle/Up.sql` | `7a2f5409b1ed5aaa52686c8d4be8346dd60c25f00b42a52489314ad63b00214c` |
| `1800_001_organization_center_team_lifecycle/Verify.sql` | `4dfa7f571c1946e6a034bcb9acb9a3e7504cfd050bcfdfbb6426a091a12e1026` |

The expected hashes are fixed in the workflow. It calculates both files' SHA-256 values after checkout and fails before OIDC if either differs. The commit lock and both SQL hash locks must pass together; neither control is a substitute for the other.

## Action and dependency supply-chain controls

| Component | Reviewed release | Executable reference |
|---|---|---|
| `actions/checkout` | `v4.4.0` | `actions/checkout@11d5960a326750d5838078e36cf38b85af677262` |
| `azure/login` | `v2.3.1` | `azure/login@7184910d9eb2b1c5e48f7073824a90609bb9b6d6` |
| PowerShell `SqlServer` module | `22.4.5.1` | `Install-Module -RequiredVersion 22.4.5.1` plus loaded-version verification |

The final executable workflow must retain these full action commit SHAs rather than floating major-version tags. Checkout uses `persist-credentials: false`. The pinned `SqlServer` module is downloaded and verified before Azure OIDC login, so no new external dependency is downloaded after the privileged Azure token is issued.

Permissions are job-scoped. `branch-guard` explicitly has `contents: none` and `id-token: none`. Only `migrate-1800-001` has `contents: read` and `id-token: write`; there is no workflow-global OIDC permission.

## First execution boundary

The first future executable workflow has no migration selector. Its only SQL write path is:

1. Validate the dispatched ref and fixed `1800_001` execution unit.
2. Check out and verify the protected approved commit, then verify both fixed SQL SHA-256 values.
3. Install and verify the pinned `SqlServer` module before Azure OIDC login.
4. Validate `DB_NAME() = db-fieldvisit-uat` and exact predecessor `1.7.0-008`; refuse existing `1.8.0-001` or a partially prepared schema.
5. Run only `database/migrations/1800_001_organization_center_team_lifecycle/Up.sql`.
6. Run only the matching `Verify.sql`. This verifies `SchemaVersions`, trusted constraints/indexes and the bounded SHA-256 fingerprints of pre-existing Trip, Snapshot and Snapshot Stop history.
7. Confirm `1.8.0-001` and stop.

The workflow cannot select or execute `1800_002`–`1800_007`. Success does not authorize the next migration. The `1800_001` result, verification output and historical fingerprint evidence require Review before any later execution design is considered.

## Workflow control sequence

| Order | Control | Failure behavior |
|---:|---|---|
| 1 | `workflow_dispatch` confirmation must equal `1800_001` | Fail in branch guard before Environment/OIDC |
| 2 | `github.ref` must equal `refs/heads/post-uat/v1.8.0` | Immediate fail |
| 3 | `environment: uat-migration` | GitHub Required reviewer must approve before the job starts |
| 4 | Protected approved SHA format and `GITHUB_SHA` equality before checkout | Branch advancement or other dispatch mismatch fails before checkout/OIDC |
| 5 | Pinned checkout of `APPROVED_MIGRATION_COMMIT_SHA`; `persist-credentials: false`, then checked-out `HEAD` equality | Missing/invalid/mismatched source fails before OIDC |
| 6 | Fixed Up/Verify SHA-256 checks | Any SQL content mismatch fails before OIDC |
| 7 | Install and verify pinned `SqlServer 22.4.5.1` | Dependency failure occurs before OIDC |
| 8 | OIDC sign-in using pinned `azure/login` and `AZURE_MIGRATION_CLIENT_ID` | Immediate fail; no secret fallback |
| 9 | Subscription/RG/SQL Server/Database metadata match protected variables and frozen UAT names | Immediate fail; no target repair |
| 10 | Entra-token SQL connection and `DB_NAME()`/predecessor/partial-schema preflight | Immediate fail; no SQL write |
| 11 | `1800_001/Up.sql` | `SET XACT_ABORT ON`; transaction rollback and fail on any SQL error |
| 12 | SQL application lock | `Up.sql` obtains exclusive transaction-owned `FieldVisit.SchemaMigration`; lock failure stops the run |
| 13 | `1800_001/Verify.sql` including historical fingerprints | Any `THROW` stops the run |
| 14 | Post-condition check and Azure logout | Confirm only `1.8.0-001`; stop for Review |

GitHub Environment Required approval and deployment-branch protection are repository settings, not YAML properties. Both must be configured manually; the YAML branch guard remains an additional defense and runs before the protected Environment job.

## Protected configuration required later

Configure these as GitHub Environment variables on `uat-migration`, only after the design receives execution approval:

- `APPROVED_MIGRATION_COMMIT_SHA`: exact lowercase 40-character commit SHA explicitly approved for execution; never a branch name or dispatcher-supplied value.
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
- Repository administrators create `uat-migration`, add Required reviewers, restrict deployment branches/tags to `post-uat/v1.8.0`, and configure the protected variables above. If `Terry4410` is currently the only reviewer, **do not enable Prevent self-review**, because no independent approver would remain. Enable it only after at least one independent IT/DBA reviewer is assigned and the organization confirms that reviewer can approve the deployment.
- IT records an approved UAT restore/recovery point and a write-free maintenance window for the future Up + Verify interval.
- A later reviewed change must place the identical executable workflow under `.github/workflows` on `post-uat/v1.8.0`; its resulting commit is the candidate approved migration commit. A workflow-only PR may then copy the identical file to `main` for `workflow_dispatch` registration. Both changes require explicit approval; this package performs neither and never merges `main`.
- After final Review, an administrator sets `APPROVED_MIGRATION_COMMIT_SHA` to the exact approved commit. Immediately before a future dispatch, reviewers compare the registered workflow, pinned action SHAs and SQL file hashes with that commit and confirm that no `1800_002`–`1800_007`, Seed/Import, firewall or deployment step is present.

## Hard stop conditions

Stop without remediation if the branch or `GITHUB_SHA` differs from the protected approved commit; either SQL hash differs; an action is not at its reviewed commit; the identity, subscription, resource group, SQL Server or database differs; Environment approval is absent; predecessor/partial-schema state differs; SQL application lock cannot be obtained; network access would require firewall relaxation; permissions are missing or broader than approved; any SQL step fails; or the historical fingerprint changes.

No workflow step may create an identity, alter a role, change a firewall, run Seed/Import, deploy application code, access Production or continue to `1800_002`.
