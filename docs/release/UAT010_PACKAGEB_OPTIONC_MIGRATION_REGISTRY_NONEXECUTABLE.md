# UAT010 × Package B — Canonical schema and migration collision registry

**Status: NONEXECUTABLE DESIGN; Owner selected Option C, NOT Migration approval.**

| Version | Original candidates | Option C decision | Deployment |
|---|---|---|---|
| 1.8.0-010 | Existing Visitor-only UAT schema | Reported UAT baseline; catalog preflight still required | Previously deployed; no action here |
| 1.8.0-011 | UAT010 local ZIP `1800_011_uat010_location_change_governance` **versus** Package B `database/candidates/1800_011_b3_change_requests_candidate` | **COLLISION.** Retire UAT010 standalone path; canonical `ChangeRequests` and `ChangeRequestEvents` only. Source Package B draft needs new Option C reviewed immutable SQL. | HOLD |
| 1.8.0-012 | Package B `1800_012_b3_atomic_apply_candidate` | Draft extension for Applied, Approved/Applied events and DecisionKey. Requires compatibility review with unified 011. | HOLD |
| 1.8.0-013 | Package B `1800_013_val_b1_002_leader_grant` | RESERVED. Do not reuse. | HOLD |

## Canonical runtime contract
- `dbo.ChangeRequests` is the **only** request table; no parallel `dbo.LocationChangeRequests`.
- `dbo.ChangeRequestEvents` append-only audit; filtered unique Pending per (OrganizationId, EntityKind, EntityId); filtered unique DecisionKey.
- Current Option C source includes a copy of Package B **disabled** B3 domain/EF/preflight/executor candidate. **No B3 controller or service DI registration**, `PackageB:B3:Enabled=false`, `OptionC:Approval:Enabled=false`, `ApproveAsync` throws `B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED`.
- The isolated baseline SQL Server fixture exercises existing model invariants only. It does **not** run Option C 011/012 Up/Verify or atomic apply concurrency.
- No SQL migration candidate is included as an executable workflow step. GitHub CI is nondeploy and rejects changes under `database/migrations/` or `database/candidates/`.

## Before creating the reviewed Option C 011/012 candidate
1. Owner/IT confirm exact schema contract and published-location version/swap policy.
2. Read-only Azure UAT preflight: actual latest SchemaVersion, tables, indexes, constraints, triggers, permissions and drift.
3. Compare UAT010 ZIP against Package B 011/012/013; calculate immutable SQL SHA-256 for approved candidate and rollback.
4. Rehearse Up/Verify/reject rerun/partial drift/unique race/atomic request-location rowversion/rollback on disposable SQL Server 2022.
5. Require separate explicit Owner UAT migration approval; then Backend and Frontend approvals, each exact-SHA gated. Production stays HOLD.

**DO NOT** run UAT010 standalone 011, overwrite Package B scripts, promote to `post-uat/v1.8.0`, or dispatch Azure migration from this branch.
