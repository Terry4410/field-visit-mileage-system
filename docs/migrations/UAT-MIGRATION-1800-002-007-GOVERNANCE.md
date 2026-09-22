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

## Mandatory recovery governance

This policy is mandatory for every stage 1800_002 through 1800_007. It is a
precondition to authorization, not permission to execute a migration.

The following machine-verifiable markers are normative:

```text
RECOVERY_MODEL=RESTORE_OR_REVIEWED_FORWARD_FIX
PRE_STAGE_RESTORE_POINT=REQUIRED
RECOVERY_OWNER=REQUIRED
WRITE_QUIESCENCE=REQUIRED
AUTO_RERUN=FORBIDDEN
AD_HOC_ROLLBACK_SQL=FORBIDDEN
IN_PLACE_SQL_REWRITE_AFTER_EXECUTION=FORBIDDEN
POST_STAGE_STOP_FOR_REVIEW=REQUIRED
```

### Pre-stage recovery gate

Before any stage is authorized, all of the following must be satisfied:

- verified pre-stage Azure SQL restore/PITR capability and a usable pre-stage
  recovery point exist;
- a Recovery Owner is identified;
- application writes are quiesced for the approved migration window;
- the exact predecessor SchemaVersion is verified;
- the historical fingerprint baseline is captured and verified; and
- no partial target-stage state exists.

No stage may execute if this recovery gate is incomplete.

### Failure, rerun, and immutable-source rules

If any stage fails, STOP immediately. Do not automatically rerun. Do not
manually rerun without a new Control Tower authorization. Do not edit the failed
`Up.sql` in place. Do not bypass predecessor or clean-state checks.

There is no approved `Down.sql` for 1800_002 through 1800_007. No improvised
rollback SQL, temporary hand-written reverse SQL, destructive schema reversal,
or manual cleanup intended to simulate `Down.sql` is permitted. Every recovery
action requires explicit Control Tower review.

### Recovery decision matrix

#### Case A — transaction failed or rolled back; clean exact predecessor remains

- STOP and perform read-only verification.
- Confirm the exact predecessor SchemaVersion.
- Confirm that no partial objects, columns, or data state exists.
- Confirm that historical fingerprints are unchanged.
- Diagnose the failure.
- Control Tower may explicitly authorize a retry of the same immutable stage.
- Never retry automatically.

#### Case B — stage committed and additive state is understood; Verify fails

This case applies only when historical fingerprints remain intact. STOP, freeze
writes, and do not use rollback SQL. The recovery path is a new separately
reviewed forward-fix migration with a new migration artifact/version,
independent review, immutable SQL hashes, a protected execution mechanism, QA
and governance review, and explicit Control Tower authorization. Do not edit or
reuse the already executed stage SQL in place.

#### Case C — fingerprint mismatch, destructive or ambiguous change, or unproven partial state

STOP, freeze writes, do not retry, and do not forward-fix immediately. Perform a
restore/PITR assessment and use the verified pre-stage recovery point only when
authorized. After restore, rerun read-only preflight and prove the exact clean
predecessor before any new migration authorization.

### Mandatory post-stage stop

After every successful stage, the required sequence is:

`Up → Verify → SchemaVersion confirmation → historical fingerprint verification → STOP_FOR_REVIEW`

The next stage is never automatically authorized.

## Immutable SQL locks

| Stage | Up.sql SHA-256 | Verify.sql SHA-256 |
|---|---|---|
| 1800_002 | `82c9e06743af010fdde97d77b7fdb3455ce51b1f1bf0c71c61174b812d2a4f87` | `f153ff787a160682e3e965cdc14c4e886adf3d9044d7194bf99a150a54d00358` |
| 1800_003 | `e8dea828f29dc64c99b59a042be75580c08c4dfbc9f681223d30950dad3f4aab` | `ac779a839918fc41c70eb6f8e7a005d397b87770d388c2b518811959e534f597` |
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
| 1800_004 | none | Existing-table changes are DDL-only; persisted computed columns do not require an explicit object `UPDATE` grant. |
| 1800_005 | `dbo.MileageRateRules` only | Explicit VehicleType canonicalization and EffectiveTo derivation UPDATEs. Projects and VisitTypes changes are DDL-only. |
| 1800_006 | `dbo.Employments` | A NOT NULL defaulted column is applied to existing rows with `WITH VALUES`. |
| 1800_007 | `dbo.MileageCalculations` | `ManualFallbackUsed` is NOT NULL/defaulted and applied with `WITH VALUES`. |

The workflows do not grant or revoke permissions. A DBA must perform any
required permission change in a separate approved action. A mismatch blocks the
stage before `Up.sql` can run.

For 1800_004, the reviewed temporary permission model is exactly membership in
`db_ddladmin` plus `INSERT ON SCHEMA::dbo`, with no object-level `UPDATE`
permission. Its permission gate requires effective `ALTER` on `dbo.Locations`
and `dbo.DeploymentSiteLocationAssignments`, effective `REFERENCES` on
`dbo.Users`, `dbo.Locations`, and `dbo.Teams`, and effective `UPDATE` on
`dbo.Locations` to remain zero. Stage 001/002 object-level `UPDATE` residue must
also be absent.

For 1800_005, the reviewed temporary permission model is exactly membership in
`db_ddladmin`, `INSERT ON SCHEMA::dbo`, and
`UPDATE ON OBJECT::dbo.MileageRateRules`. No `UPDATE` permission is permitted on
`dbo.Projects` or `dbo.VisitTypes`; their new columns are DDL materialization,
not application-data rewrites. The permission gate requires effective `ALTER`
on `dbo.Projects`, `dbo.VisitTypes`, and `dbo.MileageRateRules`, and effective
`REFERENCES` on `dbo.Users`. Current-stage forbidden UPDATE surfaces and all
prior-stage UPDATE surfaces must remain zero. The Grant and Revoke scripts are
independent approved actions outside the migration workflow, and Revoke must be
usable after either migration success or failure. The immutable Stage 005 SQL
locks, recovery governance, single-stage execution boundary, no-automatic-rerun
rule, and terminal `STOP_FOR_REVIEW` remain unchanged.

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
