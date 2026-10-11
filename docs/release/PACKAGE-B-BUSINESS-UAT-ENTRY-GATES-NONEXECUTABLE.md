# Package B — Business User UAT entry decision (NONEXECUTABLE)

**Issue #24 | WORK-only candidate | Owner-controlled GO/NO-GO | 2026-10-11**

## Scope decision required before UAT

There are **two different possible UAT targets**. Do not conflate them:

1. **Existing deployed v1.8.0 business workflows, excluding Package B3**:
   can be handed to Business Users only after an independent UAT deployment
   and existing 18-case RC/Business smoke verification on the exact deployed SHA,
   with B3 OFF and Package B feature routes unavailable.
2. **Full Package B location submission/review/approval/apply workflows**:
   **NOT READY**. B3 approval/apply is intentionally DENY ALL; 1800_011 is
   NONEXECUTABLE; manager grant provenance VAL-B1-002 is BLOCKED;
   all 22 B4 formal acceptance cases are 0/22. No Business UAT of those
   workflows can be called complete by enabling flags or using test-only SQL.

## Current technical evidence (NOT a GO decision)

- Last verified complete WORK run `38123279041`, SHA
  `9bbbf9ddbc6d190750eadd22819add172318da7b`:
  backend 804/804, frontend 92/92, disposable B3 SQL 51/51,
  dated-site SQL 6/6, JWT HTTP disposable SQL 20/20 PASS.
- CreateDraft atomic transaction rollback fault-injection: isolated PASS.
- Subsequent WORK changes add 1800_009/010 static CHECK inventory and
  a nonempty-stop rollback regression. **Require a fresh full green run on
  the exact final candidate SHA**; older green runs cannot validate it.
- Static schema inventory explicitly reports `schema_parity=NOT_VERIFIED`.
  EF EnsureCreated disposable databases are not migration-chain parity.
- Protected source remains `post-uat/v1.8.0` at
  `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2`.

## Owner / IT entry gate checklist

| Gate | Required evidence before Business User login | Current disposition |
| --- | --- | --- |
| UAT scope | Owner signs off existing-workflows-only vs full Package B | **UNDECIDED** |
| Source | Final SHA + protected branch review, controlled promotion authorization | **HOLD** |
| CI | Full green backend/frontend/SQL and signed evidence manifest on exact SHA | **PENDING latest** |
| SQL schema | Read-only deployed UAT catalog + SchemaVersions, CHECK/FK/index/trigger trust, migration-chain parity review | **NOT VERIFIED** |
| Migration 011 | Owner-approved change request, tested backup/rollback, IT execution window, post-check | **NOT AUTHORIZED** |
| B3 permissions | VAL-B1-002 manager-grant provenance independently attested; visitor ownership/IDOR negatives | **BLOCKED** |
| Approval/apply | Independent Admin review, atomic apply, immutable history/audit, negative E2E | **DENY ALL / INCOMPLETE** |
| B4 formal | 22/22 documented acceptance results on approved target environment | **0/22** |
| Flags | Exact deployed environment readback; B3 OFF until separate authorization | **OFF BY DESIGN** |
| Runtime | Backend + frontend health, login, representative visitor/leader/admin workflows, API and UI screenshots | **NOT VERIFIED FOR CANDIDATE** |
| Security | Real HR status/session revocation, tenant/ownership, access after role change, secrets/logs check | **PARTIAL / HOLD** |
| Business | Named testers, test accounts, sample data, UAT scripts, issue triage, exit criteria | **NOT SIGNED** |
| Production | Separate security, migration and Owner authorization | **HARD HOLD** |

## Workstream sequencing — no bypass

1. Close latest WORK CI, retain immutable artifacts, fix red tests on WORK only.
2. Complete disposable SQL deadlock/timeout/retry and migration-vs-EF gap
   evidence; document remaining parity differences, never label static
   analysis as actual parity.
3. Resolve scope: if Owner selects existing-workflows-only, exclude all
   B3 routes and test B3 disabled end-to-end. Prepare controlled promotion
   and UAT deployment requests; **do not execute without separate approval**.
4. For full B3 UAT, first deliver independently reviewed approval/apply
   implementation, manager grant provenance, 011 migration and runtime
   validation. Only then run all 22 formal B4 scenarios.
5. After exact UAT SHA is deployed with approval, run the full RC smoke,
   role-by-role Business UAT scripts, data reconciliation, and issue register.
6. Owner and IT jointly record GO/NO-GO; **no implied GO from CI green**.

## Formal disposition

**Business User UAT for full Package B: NO-GO.**
**Existing v1.8.0 workflows: CONDITIONAL; not a release approval.**
B3 OFF, LiveSessionGuard default OFF, Approve/Apply DENY ALL,
B4 0/22, Business UAT HOLD, Production HARD HOLD. This file performs
no migration, deployment, database mutation, promotion or activation.
