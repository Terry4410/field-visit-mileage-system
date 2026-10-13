# Visitor-only UAT 010 readiness (WORK candidate)

## Scope and provenance

This candidate is based directly on protected `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2`, on branch `work/v180-visitor-only-uat010`. It is intentionally separate from Package B `work/v180-package-b-b2-b3-controlled` and does not change either protected or Package B branches.

The candidate uses the existing managed-location API and Location/AuditLog/RowVersion columns present before 1.8.0-011. It does not require ChangeRequests, Atomic Apply, VAL-B1-002 grant columns, or migrations 1800_011/012/013.

## Visitor authorization contract

| Operation | Required server evidence | Expected result |
|---|---|---|
| List | authenticated Visitor; organization/team scope | only scoped records returned |
| Create | active account; current Visitor role; HR status `Active`; effective team assignment; Customer type | server assigns organization and actor; `Pending`, inactive draft |
| Update | same checks plus `CreatedByUserId`, `Pending`, inactive, same team, Customer type, current RowVersion | own draft update only; stale version is 409 |
| Publish/activate/official/global/transfer | not in Visitor contract | denied; separate Admin workflow required |

Client-supplied owner, organization, approval status, activation and role claims are not authorization inputs. UI hiding is supplementary only.

## UAT approval draft (not executed)

1. Owner separately approves backend deployment of the exact Visitor-only SHA after dedicated CI SUCCESS and confirms UAT catalog remains `1.8.0-010`.
2. Owner separately approves frontend deployment of the matching built artifact.
3. IT provisions synthetic Visitor A/B/C accounts and records cleanup ownership; no real employee data is used.
4. Owner authorizes only the synthetic functional cases below. B3 remains OFF and public Approve/Apply remains DENY ALL.

Rollback is the prior verified UAT artifacts (backend `68b94138`, frontend `007b79db`) through the existing deployment workflow. No migration rollback is part of this candidate because no migration is executed.

## Synthetic Visitor SOP (formal UAT not run)

- Visitor A creates a Customer draft in A's effective team; capture HTTP response, status fields, actor, and AuditLog evidence.
- Visitor A reloads and edits the same Pending/inactive draft with the returned RowVersion; capture success and new RowVersion.
- Visitor B attempts to edit A's draft; expect 403/404 and no AuditLog mutation.
- Visitor C from another organization attempts list/write; expect denial and no mutation.
- Replay an old RowVersion; expect 409 and require reload.
- Send forged owner, organization, team transfer, active, Approved, official/global and publish fields; expect denial or forced safe draft state.
- Review audit and clean up only synthetic records under the Owner-approved test account.

All cases are `NOT RUN` until separate Owner deployment and Business UAT authorization.

## Package B dependency follow-up

| Candidate | Prerequisite / sequence | Visitor-only impact |
|---|---|---|
| 1800_011 | 010 baseline; creates ChangeRequests | excluded; not required |
| 1800_012 | 011 applied and verified; atomic apply tables | excluded; B3 remains OFF/DENY ALL |
| 1800_013 | 012 plus trusted VAL-B1-002 provenance columns | excluded; Leader path remains separate and unapproved |

The required sequence for any future Package B execution is `010 → 011 → 012 → 013`, with independent preflight/catalog evidence, backup/restore proof, and separate Owner approvals. This branch does not alter those candidates.

## Evidence status

- Protected source baseline: verified.
- Exact UAT catalog 010: previously reported, but not re-read by this batch.
- Disposable SQL fixture: dedicated CI executes the existing SQL Server 2022 invariant fixture; it is not a substitute for the actual UAT catalog.
- Exact 010 catalog compatibility: `UNKNOWN/HOLD` until read-only UAT catalog evidence confirms the deployed schema.
- B3 runtime flag: `UNKNOWN`; no activation attempted.
- Formal B4: `0/22`, `NOT RUN`.

## Predeployment security correction (separate branch, nondeploy)

- Branch: `work/v180-visitor-only-uat010-security-fix`, forked from Visitor-only SHA `6e802dc59bf62029e8d5acbc4f7619c726d02d69`.
- Visitor draft UPDATE requires a nonempty, valid and matching RowVersion. Missing, invalid and stale values fail with `ROWVERSION_CONFLICT` (HTTP 409 through the existing API exception handler). Admin and Leader update behavior is unchanged.
- Current Visitor authorization requires active account, time-effective Visitor role, Active HR status, time-effective team assignment, active team and matching Organization. Legacy UserRoles and UserTeamScopes do not restore expired/revoked permissions.
- Visitor-only list/search recheck current access and scope to effective teams in the same Organization. Organization-scoped global records with null TeamId remain readable, but cannot be edited without the separate write authorization.
- The existing manual Backend and Frontend deployment workflows require `approved_sha`, restrict manual dispatch to the correction branch, explicitly checkout `github.sha`, and stamp the built artifacts with `source-sha.txt`. Main-branch automatic push behavior is unchanged. A deployment workflow is NOT executed by this correction.
- Dedicated CI on the correction branch must pass, including disposable SQL fixture and migration immutability, before any Owner deployment decision. Verify the actual run's head SHA, event, artifact provenance, rollback artifact, and current UAT schema again before deployment.
- Known previous UAT Backend SHA: `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2`. Frontend SHA: `007b79dba12a1668b66164ac9f8440bbb8ac8e90`.
- Explicit Owner authorization is still required for each Backend and Frontend deployment and for synthetic UAT data provisioning. No SQL DDL/DML, migration, Package B B3 activation, or production deployment is included.

## Controlled deployment and rollback preflight — 2026-10-13 (NOT DEPLOYED)

### Exact-SHA manual dispatch contract
- Only a manual dispatch of the workflow on `work/v180-visitor-only-uat010-security-fix` is eligible. The first gate validates that exact branch, a lowercase 40-hex `approved_sha`, and an allowed `operation`.
- `operation=deploy_candidate` requires `approved_sha` to equal the workflow run's triggering `github.sha`. The workflow checks out that SHA and verifies the checkout before building or deploying.
- `operation=rollback` is strictly pinned per workflow: Backend `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2`, Frontend `007b79dba12a1668b66164ac9f8440bbb8ac8e90`. Other rollback SHAs are denied. The workflow checks out the explicitly approved old source and records that source in `source-sha.txt`.
- `main` continues to have the legacy workflow definition; it does not have these inputs. Do NOT assume GitHub's manual Run workflow UI exposes branch-specific inputs. If the UI cannot submit the exact inputs to the correction-branch workflow, STOP. Never dispatch the old main workflow as a substitute. Confirm the dispatched run's `head_branch`, `head_sha`, workflow path, inputs, and first gate before any Azure/Pages deploy job is allowed.
- A future code commit changes `github.sha`; the Owner must reapprove the exact final SHA. No prior SHA authorization is transferable.
- Main-branch push triggers are not modified by this candidate. Do not merge these workflow changes into `main` without separate impact analysis and explicit authorization because it can trigger production-facing deployment workflows.

### Nondeploy rollback proof
- The dedicated verification workflow rebuilds the prior Backend SHA into a publish directory with the same CJK font and a source SHA stamp, and rebuilds the prior Frontend SHA into `dist/` with its source SHA stamp.
- Both rebuilt directories are stored as 30-day GitHub Actions artifacts. This verifies that historical source can be rebuilt; it does **not** prove byte-for-byte identity with the original deployment or a successful Azure/Pages restore.
- Before actual deployment: record and verify UAT app-name, publish-profile target, GitHub Pages environment, effective settings/feature flags (B3 must be OFF), SQL DB identity and Schema 010, current healthy URLs, existing deploy runs, and rollback artifact digests. Never copy secret values into reports.
- Proposed sequence requiring explicit Owner approval: Backend exact SHA deploy -> API readiness and smoke -> Frontend same exact SHA deploy -> UAT browser smoke -> synthetic Visitor-only tests (separately approved).
- If candidate fails: freeze writes, assess impact, obtain explicit Owner rollback authorization, restore Frontend prior SHA if Frontend changed, restore Backend prior SHA if Backend changed, then verify health and DB 010. No migration rollback is expected; no DDL/DML authorized.
- This document is an execution plan, not a deployment approval or a claim of tested Azure rollback.

### Pending release blockers
1. Verify that GitHub manual dispatch can supply both inputs to the branch-specific workflow without falling back to the old default-branch definition.
2. Confirm Azure target settings and B3 runtime OFF without disclosing secrets.
3. Verify the new nondeploy CI and prior-source rebuild artifacts at the exact final correction SHA.
4. Explicit Owner approval for Backend and Frontend UAT deployment. Package B remains HOLD.
