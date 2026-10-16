# UAT Schema Fidelity — Protected Workflow Proposal

Status: **DRAFT PR / NOT INSTALLED / NOT EXECUTED**

Base: `post-uat/v1.8.0` at `ce70eddc290e85375c64311b6d96eedc09942dba`.

This proposal is isolated from the Scheme A staging branch. It does not authorize a
protected update, workflow dispatch, UAT query, migration, restore, deployment or
feature activation.

## Reused R7 inventory

- Query: `UatSchemaFidelity.readonly.sql`
- Reviewed content: the R7 metadata inventory; the protected proposal adds a final
  newline, so its immutable SHA-256 is
  `e83552d4b1472e677f61a5e44973b80321d2874322f4a5954c070b8f3fd2c232`.
- Fixed target: `sql-fieldvisit-jpe-uat.database.windows.net / db-fieldvisit-uat`.
- Result scope: target marker; SchemaVersions aggregate; columns and SQL facets;
  indexes and PK/unique facets; FK mappings/actions/trust; CHECK definitions/trust;
  four historical-table counts and high-watermarks.
- Excluded: business row values, login/user identity, `AppliedBy`, connection strings,
  access tokens and raw SQL exception text.

The existing protected smoke run `38835570403` used
`UAT_ReadOnly_Preflight.sql` SHA-256
`44c20438c8125150d8cdbfae5e2dab40f23acea6386507b96bec31ff3cb10955`.
It did not execute this inventory, and its existing classifier is not compatible
with these seven result sets.

## Proposed protected path

The new `azure-sql-uat-schema-fidelity-readonly.yml` is intentionally separate from
the already-proven smoke workflow. It:

1. accepts no dispatch inputs;
2. rejects any repository/ref other than
   `Terry4410/field-visit-mileage-system` / `post-uat/v1.8.0`;
3. checks out the exact dispatch SHA and verifies it equals `GITHUB_SHA`;
4. pins both SQL and result validator with SHA-256 before OIDC login;
5. fixes resource group, server and database and cross-checks the protected
   environment variables;
6. uses the existing `uat` protected environment, OIDC and
   `ApplicationIntent ReadOnly` with bounded timeouts and abort-on-error;
7. suppresses raw SQL errors and rejects any result-set count, order, column,
   target, mode, SchemaVersion or historical-aggregate mismatch;
8. emits one normalized metadata JSON artifact plus aggregate counts in the step
   summary. It does not print metadata rows to the log.

The proposal-only CI is a different workflow with `contents: read` only. It has no
OIDC permission, Azure login, UAT environment or SQL execution capability.

## 010 migration and EF compatibility gate

`compare_uat010_schema_inventory.py` compares normalized UAT metadata with a
normalized reference created by applying the protected 001–010 SQL chain to a new
disposable SQL Server database. Comparison is exact for:

- columns: schema/table/ordinal/name, type, length, precision/scale, nullability,
  identity, computed, rowversion and default;
- indexes and primary/unique keys: type, uniqueness, enabled state, filter,
  key/include order and direction;
- foreign keys: columns, referenced object, actions, enabled and trusted state;
- CHECK constraints: name, normalized definition, enabled and trusted state.

The comparator independently enforces the 010 delta:
`CK_RouteCalculationAttempts_Reason` must be enabled and trusted and contain exactly
the reviewed reason surface, including `BackgroundMileageJob`.

EF compatibility must use an exported relational-model reference from the same
Protected source SHA. It must be reconciled against the same normalized 001–010
reference before a result may be called PASS. The current repository has SQL-script
migrations rather than an EF migration snapshot, so the actual EF export and the
disposable 001–010 normalized reference are required dependencies, not assumptions.
A catalog fingerprint alone cannot substitute for these comparisons.

## Required Owner approvals

1. Approve the exact protected PR diff, workflow path and pinned file hashes.
2. Approve merge/update of `post-uat/v1.8.0` at the reviewed head SHA.
3. Separately approve one dispatch of the new SELECT-only workflow from that exact
   protected SHA. Approval to merge is not approval to dispatch.
4. Review the resulting normalized artifact and the disposable 001–010 / EF
   comparison report before any migration or deployment decision.

No approval in this proposal includes UAT DDL/DML, migration, restore, deployment,
flag activation, Business UAT or PR merge.

## Remaining HOLD items

- Proposed workflow is unmerged and has never queried UAT.
- Normalized UAT inventory does not exist.
- Protected 001–010 disposable normalized reference and EF relational-model export
  are not yet attached; therefore actual 010/EF fidelity is **HOLD**, not PASS.
- Migration registry/additive contracts 011–020, restore validation, protected-based
  RC, real SQL-backed Browser E2E and Business UAT remain separate gates.

## Minimal protected-based RC delta

The minimal RC should be assembled only after fidelity approval. Its dependency
order is:

1. this protected inventory proposal and independently approved inventory evidence;
2. exact 001–010 / EF reconciliation with zero unexplained drift;
3. separately reviewed immutable 011, 012 and 014 plus additive contracts; 013 stays
   reserved;
4. historical fingerprints and preconditions on a disposable clone/restore;
5. one nominated integrator imports reviewed Work A changes without merging the
   large Recovery history;
6. Exact-SHA nondeploy CI, real same-database journeys and release decision;
7. only then request bounded migration/deployment/flag/UAT authorizations.

The RC must not merge the entire Recovery or PR #33 staging branch merely to obtain
individual files. Use a reviewed file/commit allow-list and record every dependency.
