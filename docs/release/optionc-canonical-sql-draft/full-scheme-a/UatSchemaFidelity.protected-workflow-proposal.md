# UAT Schema Fidelity — Protected Workflow Change Proposal

Status: **STAGING REVIEW PROPOSAL ONLY — NOT INSTALLED OR EXECUTED**

This proposal is intentionally outside `.github/workflows`. GitHub Actions will not discover or dispatch it. The current staging worktree cannot run the inventory through the existing protected path.

## Exact query under review

- Source: `docs/release/optionc-canonical-sql-draft/full-scheme-a/UatSchemaFidelity.readonly.sql`
- SHA-256: `c00a269a310222550626544a053ed343a3b55a6c71aad1f128668b6b4d2aabc0`
- Target fixed by query and proposed runner: `sql-fieldvisit-jpe-uat.database.windows.net / db-fieldvisit-uat`
- Evidence returned: SchemaVersions aggregate; table/column/type/length/precision/nullability/identity/computed/rowversion/default metadata; indexes; FK mappings/trust; CHECK definitions/trust; and row counts/high-watermarks for the four historical tables.
- No business row values, login names, applied-by identities, or DDL/DML are queried. Historical data is limited to aggregate counts and max IDs.

## Why the existing protected run is not compatible

Run `38835570403` checked out `post-uat/v1.8.0` at `ce70eddc290e85375c64311b6d96eedc09942dba`, SHA-verified the different preflight source `UAT_ReadOnly_Preflight.sql` at SHA-256 `44c20438c8125150d8cdbfae5e2dab40f23acea6386507b96bec31ff3cb10955`, then invoked the preflight evidence classifier. It did not check out or execute the inventory above. Its classified result shape cannot safely accept the inventory's several schema-metadata result sets. No compatible already-approved Protected route exists.

## Minimal Protected change proposed for independent review

1. Add the reviewed inventory file at the exact path above to the Protected source. Pin the hash as an immutable workflow constant; reject any mismatch before OIDC or SQL connection.
2. Add a separate `workflow_dispatch` read-only evidence job on the existing `uat` protected environment. Keep `contents: read`, `id-token: write`, concurrency serialization, the existing protected-branch guard, fixed resource group/server/database guards, and the existing Azure OIDC identity. Do not accept server, database, query path, or hash as dispatch inputs.
3. Check out only `post-uat/v1.8.0`; assert the checked out commit equals `GITHUB_SHA`; verify the SQL SHA-256 equals `c00a269a310222550626544a053ed343a3b55a6c71aad1f128668b6b4d2aabc0` before OIDC login. Keep Azure SQL `ApplicationIntent=ReadOnly`, bounded connection/query timeouts, abort-on-error, and sanitized SQL exceptions.
4. Use a separate schema-inventory result validator rather than the existing preflight classifier. Require the exact ordered result-set labels/columns, correct fixed database/server identity, one supported SchemaVersions aggregate, and all four historical aggregate rows. Reject unexpected columns, result sets, missing values, or nonzero SQL errors. Emit only normalized metadata JSON and a short summary; never dump token, connection string, row data, or exception details.
5. Keep the existing preflight workflow unchanged until the proposal is independently reviewed and explicitly installed on Protected. Do not dispatch this proposed query from Staging.

## Comparison and release gate

Compare the returned catalog metadata against the real Protected 001–010 migration output and EF model, normalized by schema/table/ordinal/name. Require exact SQL type, max length, precision, scale, nullability, identity/computed/default and rowversion parity; compare PK/FK columns/actions, CHECK definitions/disabled/trusted state, and index type/keys/includes/order/filter/uniqueness/disabled state. Separately validate SchemaVersions and all 010 backfill prerequisites against existing data using count/duplicate/orphan aggregate checks. Whole-catalog fingerprints alone cannot close any of these comparisons.

Until the proposed Protected run is independently approved and yields these result sets, 010 schema fidelity and UAT compatibility remain **HOLD**.
