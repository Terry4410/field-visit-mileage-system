# UAT migration governance — 1800_002 through 1800_007

This file freezes the reviewed migration source and execution boundaries for the
v1.8.0 UAT sequence. It does not authorize execution. Each stage is a separate
`workflow_dispatch` unit and must stop at `STOP_FOR_REVIEW`.

This work branch is not a registration or promotion action. GitHub requires a
manually dispatched workflow to be registered on the repository default branch;
the separate default-branch registration used for 1800_001 remains a downstream
Control Tower gate and is intentionally outside this change's authorization.

## Source reconciliation

| Stage | Reconciled source | Decision |
|---|---|---|
| 1800_002 | unchanged from `post-uat/v1.8.0` at `d384251d8db801c827641fad124c00c541e8a1b2` | Already identical to application-authoritative source. |
| 1800_003 | application SHA `35767fa4463fe17e4a459e08f23f6ea30a15c904` | Includes the final employment/site temporal and reverse coverage protections. |
| 1800_004 | application SHA `35767fa4463fe17e4a459e08f23f6ea30a15c904` | Includes cleared-note semantics, active-location assignment protection, snapshot-safe locking, and executable dynamic batches. |
| 1800_005 | application SHA `35767fa4463fe17e4a459e08f23f6ea30a15c904` | Implements the final VisitType and MileageRate exact-series authority contract. |
| 1800_006 | application SHA `35767fa4463fe17e4a459e08f23f6ea30a15c904` | Implements authoritative outbox idempotency, lease, finalization, and dormant ProjectManager recipient behavior. |
| 1800_007 | application SHA `35767fa4463fe17e4a459e08f23f6ea30a15c904` | Uses executable dynamic batches and the exact F-A foreign-key verification scope. |

There are no supporting files inside the 1800_003–007 migration directories
other than `Up.sql` and `Verify.sql`. No `Down.sql` exists and none is introduced.

## Immutable SQL locks

| Stage | Up.sql SHA-256 | Verify.sql SHA-256 |
|---|---|---|
| 1800_002 | `465fc7f6886fcd837401113b363ecc0256a2706b684c59bf0f9bebbf52c99b4f` | `f153ff787a160682e3e965cdc14c4e886adf3d9044d7194bf99a150a54d00358` |
| 1800_003 | `7298f48089a953795ca6cc18f5e599a09d21ba93df5b267f1213db746cf426b5` | `ac779a839918fc41c70eb6f8e7a005d397b87770d388c2b518811959e534f597` |
| 1800_004 | `f7ab917e9fd0c10f5e88a5cd6534f5f91c27bb372615ad733e7b7c1fc22d9cb6` | `250997d5ec4a3f185d35c664af48bc347f527ff2c4e3427542cab858b12f8d9f` |
| 1800_005 | `4a78f8d53f77111b1c445bf9669274cbc70824ca589b0d8322367e5257551d10` | `fe4a264fa98100dd07d228d5984f112095f1f5dd8b806fe43d5a89fc4231f8ae` |
| 1800_006 | `69f3b864e91131ef0ffbc95e515aeac29ad1b1c2b48d9eb8e66cdb737eabcd56` | `df93b3580eb70a03260859e76c5df2478bc34bae3c8b967f69c3ce2623bb14d8` |
| 1800_007 | `d89a061c15c95427a0cb1793b762a7fce306cd8019f276bb26664f617ffa364d` | `c27e43aa0311be1c8f2c7f8a424ca8fff80faaac7a311335d56f69b0a72e395e` |

The shared read-only historical fingerprint verifier is also locked at
`1d7f77dc200590c2ba3b989ed340442d835f8e02be9edbb57af68ae5e4e9cba0`.

## Least-privilege gates

Every stage retains only the reviewed migration baseline: `CONNECT`, membership
in `db_datareader` and `db_ddladmin`, `INSERT` on schema `dbo`, and the default
`public` ability used by `sys.sp_getapplock`. The workflow rejects `db_owner`,
`db_securityadmin`, `db_datawriter`, unexpected role memberships, and any
explicit permission outside the exact current-stage allowlist.

Temporary object-level `UPDATE` permissions must be granted immediately before
the applicable stage and revoked before the next stage:

| Stage | Exact temporary UPDATE permission | Reason |
|---|---|---|
| 1800_002 | `dbo.UserIdentityProfiles` | Explicit employment backfill UPDATE. |
| 1800_003 | none | New tables plus nullable columns only. |
| 1800_004 | `dbo.Locations` | Persisted computed columns materialize values for existing rows. |
| 1800_005 | `dbo.Projects`, `dbo.VisitTypes`, `dbo.MileageRateRules` | ROWVERSION materialization; MileageRateRules also has explicit canonicalization and derived-range UPDATEs. |
| 1800_006 | `dbo.Employments` | A NOT NULL defaulted column is applied to existing rows with `WITH VALUES`. |
| 1800_007 | `dbo.MileageCalculations` | `ManualFallbackUsed` is NOT NULL/defaulted and applied with `WITH VALUES`. |

The workflows do not grant or revoke permissions. A DBA must perform any
required permission change in a separate approved action. A mismatch blocks the
stage before `Up.sql` can run.

## Execution invariants

Each workflow enforces all of the following:

- dispatch from `post-uat/v1.8.0` only;
- exact per-stage confirmation token;
- `GITHUB_SHA == APPROVED_MIGRATION_COMMIT_SHA` before checkout;
- checkout of the exact approved SHA without persisted credentials;
- exact Up/Verify SHA-256 locks;
- fixed UAT subscription context, resource group, SQL server, and database;
- exact latest predecessor SchemaVersion and a clean target-stage shape;
- one `Up.sql`, one `Verify.sql`, and exactly one SchemaVersion advance;
- preserved VisitTrips and VisitTripSnapshots key fingerprints;
- terminal `STOP_FOR_REVIEW`, with no next-stage dispatch.

Run `python3 scripts/validate_uat_migration_workflows.py` for repository-only
validation. It never connects to Azure SQL and never dispatches a workflow.
