# v1.8.0 Azure SQL UAT Migration Execution Design

Status: **1800_001 execution-readiness preparation; not execution authorization**. This document, security scripts and executable workflow definition do not authorize or execute Azure SQL, change a role, modify a firewall, seed data, deploy an application or merge `main`.

Reviewed draft: `docs/workflows/azure-sql-uat-migration-1800-001.draft.yml`.

Executable candidate: `.github/workflows/azure-sql-uat-migration-1800-001.yml`. Adding the file to `post-uat/v1.8.0` prepares a candidate commit but does not dispatch it. GitHub registration on `main`, Environment configuration, permission grants, Recovery Gate and an explicit execution approval remain separate gates.

## Identity and environment separation

| Purpose | Identity / environment | Frozen access rule |
|---|---|---|
| Existing UAT read-only checks | `gh-fieldvisit-uat` / `uat` | Remains exactly Azure `Reader` plus database `db_datareader`; no elevation and never used for migration |
| Future UAT schema migration | `gh-fieldvisit-uat-migrate` / `uat-migration` | Separate OIDC trust; Azure UAT Resource Group Reader; Azure SQL currently `db_datareader`; temporary `1800_001` grants require separate DBA action and Review; no Production access |

The migration identity, federated credential, Environment and contained database user were created outside this repository and the read-only identity path has passed its smoke test. This package does not create or modify them. The statement-level review is recorded in `POST-UAT-v1.8.0-1800-001-PERMISSION-REVIEW.md`; the recommended time-bounded model is existing `db_datareader` plus temporary `db_ddladmin`, schema-scoped `INSERT`, and object-level `UPDATE` only on the two tables whose new `ROWVERSION` values must be materialized. The schema `INSERT` also covers the `SchemaVersions` audit row; no redundant object grant is required. `db_owner`, `db_datawriter`, shared credentials, SQL passwords and client secrets are not part of this design.

The Grant, Verify and Revoke scripts are DBA-operated preparation artifacts under `database/migrations/security/uat/`. The executable migration workflow must never call them.

## GitHub state reconciliation (2026-09-08 UTC)

| Item | Observed state |
|---|---|
| `post-uat/v1.8.0` before this readiness package | `b18efc665f17ef1320dc612da675ad49a0014192` |
| Migration identity read-only workflow | `.github/workflows/azure-sql-migration-identity-readonly-uat-smoke.yml` exists on `post-uat/v1.8.0` and `main` |
| Registration PR | [#12](https://github.com/Terry4410/field-visit-mileage-system/pull/12) is merged; merge commit `e385fea8c51c525ac1d191adb521b4b1a548973b` |
| Latest smoke run | [Run 34186028923, attempt 2](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/34186028923) completed successfully from `post-uat/v1.8.0` at `b18efc665f17ef1320dc612da675ad49a0014192` |
| `uat-migration` deployment branch rule | Custom branch policy exists for exactly `post-uat/v1.8.0` |
| `uat-migration` Required reviewer | GitHub Environment API currently reports only a branch-policy protection rule and no Required reviewer rule; migration execution remains blocked until a reviewer rule is configured and rechecked |

The earlier attempt of the same smoke run failed only at the read-only SQL query step due to the reported post-login timeout; attempt 2 succeeded. No firewall or privilege change is justified by that transient result.

## Approved source lock

The executable workflow must not treat the latest `post-uat/v1.8.0` branch head as approved. After the user approves one exact commit, a repository administrator records its lowercase 40-character SHA in the protected `uat-migration` Environment variable `APPROVED_MIGRATION_COMMIT_SHA`. This value is not a `workflow_dispatch` input and the dispatcher cannot override it.

The draft checks out that exact SHA and, before Azure OIDC login, requires both `GITHUB_SHA` and the checked-out `HEAD` to equal it. Therefore, if the branch advances after approval, dispatching the newer branch head fails before any Azure token is requested. A workflow or SQL change requires a new Review and a deliberate update of the protected approved-commit variable.

The SQL content locks reviewed with this design remain:

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

The executable candidate retains these full action commit SHAs rather than floating major-version tags. Checkout uses `persist-credentials: false`. The pinned `SqlServer` module is downloaded and verified before Azure OIDC login, so no new external dependency is downloaded after the privileged Azure token is issued.

Permissions are job-scoped. `branch-guard` explicitly has `contents: none` and `id-token: none`. Only `migrate-1800-001` has `contents: read` and `id-token: write`; there is no workflow-global OIDC permission.

## First execution boundary

The first future executable workflow has no migration selector. Its only SQL write path is:

1. Validate the dispatched ref and fixed `1800_001` execution unit.
2. Check out and verify the protected approved commit, then verify both fixed SQL SHA-256 values.
3. Install and verify the pinned `SqlServer` module before Azure OIDC login.
4. Validate exact Azure Tenant/Subscription/RG/SQL Server/database resource IDs, `DB_NAME() = db-fieldvisit-uat`, exact database principal/temporary permission state, and exact predecessor `1.7.0-008`; refuse existing `1.8.0-001` or any partially prepared table/column state.
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
| 9 | Tenant/Subscription/RG/SQL Server/Database resource IDs match protected variables and frozen UAT names | Immediate fail; no target repair |
| 10 | Entra-token SQL connection and exact principal/role/effective-permission, `DB_NAME()`/predecessor/partial-schema preflight | Immediate fail; no SQL write |
| 11 | `1800_001/Up.sql` | `SET XACT_ABORT ON`; transaction rollback and fail on any SQL error |
| 12 | SQL application lock | `Up.sql` obtains exclusive transaction-owned `FieldVisit.SchemaMigration`; lock failure stops the run |
| 13 | `1800_001/Verify.sql` including historical fingerprints | Any `THROW` stops the run |
| 14 | Post-condition check and Azure logout | Confirm only `1.8.0-001`; stop for Review |

GitHub Environment Required approval and deployment-branch protection are repository settings, not YAML properties. Both must be configured manually; the YAML branch guard remains an additional defense and runs before the protected Environment job.

## Protected configuration required later

Confirm these GitHub Environment variables on `uat-migration`, only after the candidate commit receives execution approval:

- `APPROVED_MIGRATION_COMMIT_SHA`: exact lowercase 40-character **new executable candidate commit** explicitly approved for execution; never a branch name, dispatcher-supplied value, or the obsolete design-only commit `0322b0e9ec3d31169bf7efe510fc4db5b3e8452a`.
- `AZURE_MIGRATION_CLIENT_ID`: application/client ID of `gh-fieldvisit-uat-migrate`.
- `AZURE_TENANT_ID`: approved UAT tenant.
- `AZURE_SUBSCRIPTION_ID`: approved UAT subscription.
- `AZURE_RESOURCE_GROUP`: exactly `rg-fieldvisit-uat`.
- `AZURE_SQL_SERVER_NAME`: exactly `sql-fieldvisit-jpe-uat`.
- `AZURE_SQL_DATABASE`: exactly `db-fieldvisit-uat`.

The draft uses OIDC and a short-lived Microsoft Entra Azure SQL access token. It contains no SQL username/password, client secret, API key or permanent firewall operation.

## Recovery Gate

`POST-UAT-v1.8.0-1800-001-RECOVERY-GATE.md` is mandatory. The specific UAT earliest restore point and short-term retention are not confirmed by this repository preparation. IT must capture read-only Azure metadata, a pre-migration UTC timestamp, exact schema state, a named Recovery owner and a write-free window before execution can be authorized. Database copy remains optional and requires separate cost/permission approval.

Azure SQL Database PITR creates a new database rather than overwriting the existing one. A committed migration with matching historical fingerprints normally favors a separately reviewed forward-fix; a historical fingerprint mismatch or unbounded/ambiguous state triggers write freeze and PITR assessment. The executable workflow performs no restore or copy.

## Manual IT / GitHub gates before future execution

- Confirm the existing `gh-fieldvisit-uat-migrate` federated credential remains restricted to repository `Terry4410/field-visit-mileage-system` and subject `environment:uat-migration`.
- Keep the Azure identity at UAT Resource Group Reader; no Production scope and no restore/copy authority for the migration identity.
- A DBA separately reviews and, only after authorization, runs `Grant-gh-fieldvisit-uat-migrate-1800_001.sql`, followed by its Verify script. Do not modify `gh-fieldvisit-uat`. Do not run these scripts from the migration workflow.
- Repository administrators create `uat-migration`, add Required reviewers, restrict deployment branches/tags to `post-uat/v1.8.0`, and configure the protected variables above. If `Terry4410` is currently the only reviewer, **do not enable Prevent self-review**, because no independent approver would remain. Enable it only after at least one independent IT/DBA reviewer is assigned and the organization confirms that reviewer can approve the deployment.
- IT completes the Recovery Gate, records an approved UAT restore/recovery point and enforces a write-free maintenance window for the future Up + Verify interval.
- Review the executable workflow commit on `post-uat/v1.8.0`. A workflow-only PR may copy the identical file to `main` for `workflow_dispatch` registration; this package never merges `main`.
- After final Review, an administrator sets `APPROVED_MIGRATION_COMMIT_SHA` to the exact approved commit. Immediately before a future dispatch, reviewers compare the registered workflow, pinned action SHAs and SQL file hashes with that commit and confirm that no `1800_002`–`1800_007`, Seed/Import, firewall or deployment step is present.
- After the future `Up → Verify → STOP_FOR_REVIEW` evidence is captured, a DBA separately runs the reviewed Revoke script. Revocation intentionally blocks later migrations until a new statement-level permission review and grant.

## Hard stop conditions

Stop without remediation if the branch or `GITHUB_SHA` differs from the protected approved commit; either SQL hash differs; an action is not at its reviewed commit; the identity, subscription, resource group, SQL Server or database differs; Environment approval is absent; predecessor/partial-schema state differs; SQL application lock cannot be obtained; network access would require firewall relaxation; permissions are missing or broader than approved; any SQL step fails; or the historical fingerprint changes.

No workflow step may create an identity, alter a role, run the Grant/Revoke scripts, change a firewall, restore/copy a database, run Seed/Import, deploy application code, access Production or continue to `1800_002`.
