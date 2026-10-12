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
