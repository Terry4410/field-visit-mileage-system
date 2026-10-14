# Option C UAT Execution Authorization Package

## Status

- Recovery branch: `work/v180-optionc-final-gates-recovery-r1`
- Engineering candidate SHA before this document: `c75cd4b3c7fd2fea012634ced246edc28c7ab21e`
- Last exact-SHA CI: `38511402016 — SUCCESS`
- Scope: preparation and read-only preflight only
- UAT/Production migration, deployment and flag activation: NOT EXECUTED

This package is an approval package, not an execution authorization.

## Target identification gate

A preflight may run only after Owner/IT supplies all of the following:

- Exact UAT SQL server FQDN
- Exact UAT database name
- Approved GitHub Environment and read-only identity
- Target allow-list record or change ticket
- Backup timestamp, retention, restore owner and recovery point
- Business/UAT deployment window and rollback contact

Environment name alone is insufficient. The current repository workflow `.github/workflows/azure-sql-readonly-uat-smoke.yml` is read-only but is branch-guarded to `post-uat/v1.8.0`; no Recovery-branch bypass is authorized.

Current result: **UAT target preflight NOT RUN — target not specified in this authorization message.**

## Read-only schema preflight

Run only with `ApplicationIntent=ReadOnly`, no transaction writes, and capture output:

1. Confirm server/database identity and reject master/model/msdb/tempdb.
2. Confirm connectivity identity has no DDL/DML permission.
3. Confirm latest SchemaVersions and exact dependency order:
   `1.8.0-010 -> 1.8.0-011 -> 1.8.0-012 -> 1.8.0-014`.
4. Confirm canonical tables, columns, trusted CHECK constraints, unique indexes and foreign keys.
5. Confirm `013` remains absent/unmodified.
6. Confirm additive audit candidate is not partially applied, or is fully applied with both event types and trusted constraints.
7. Confirm Stage and published-version catalog compatibility.
8. Confirm no duplicate pending Request, DecisionKey or conflicting Location authority.
9. Confirm stable LocationId and historical Trip/Snapshot references.
10. Record schema fingerprints, row counts, audit counts, operator, UTC timestamp and target identity.

Any mismatch is a STOP condition. Do not repair in UAT during preflight.

## Additive migration dependency and recovery

Disposable candidate source:

`docs/release/optionc-canonical-sql-draft/audit/StageEvidenceAuditEvents.disposable.sql`

Required future execution order:

1. Read-only target and backup gate.
2. Canonical 011.
3. Canonical 012.
4. Canonical 014 and verified backfill.
5. Additive Stage evidence audit candidate.
6. Application deployment and read-only health checks.
7. Separately approved feature activation.

The additive candidate preserves existing events and extends only the EventType and DecisionState constraints. It is not migration 013.

Rollback policy:

- Before execution: stop and restore the verified pre-change backup.
- After execution with no application activity: stop, preserve evidence, and restore the verified backup.
- After any application activity: do not run a destructive reverse migration or drop constraints containing evidence rows; disable flags, preserve audit, reconcile requests, and restore only under separately approved recovery procedure.
- Unknown COMMIT: fresh non-pooled read-only reconciliation; never blind retry.

## Deployment and flag sequence

Future controlled sequence, subject to separate approval:

1. Backup and read-only schema preflight PASS.
2. Apply approved database changes in exact order.
3. Verify schema fingerprints and audit invariants.
4. Deploy backend.
5. Run backend health and authorization read-only checks.
6. Deploy frontend.
7. Keep all flags OFF during smoke validation.
8. Enable backend candidate approval only after Owner approval.
9. Enable frontend approval only after backend verification.
10. Run controlled smoke cases.
11. Freeze evidence and proceed to Business UAT.

Disable sequence:

1. Frontend approval flag OFF.
2. Backend Option C and candidate flags OFF.
3. Keep audit, Request, Stage and published history intact.
4. Reconcile uncertain decisions.
5. Restore only with explicit recovery approval.

Current flags remain OFF:
`OptionC:Approval:Enabled=false`, `OptionC:Candidate:Enabled=false`, `PackageB:B3:Enabled=false`, frontend approval=false.

## 22-case Business UAT execution plan

Business UAT is to be performed only after authorized UAT deployment; it is not a prerequisite to reviewing deployment authorization.

| Case | Role | Scenario | Expected result | Evidence |
|---|---|---|---|---|
| OC-01 | Visitor | Submit high-risk address change | Pending, no publication | Request/Stage |
| OC-02 | Leader | Submit team-owned change | Pending, no publication | Request/Stage |
| OC-03 | Visitor | Terminated requester submits | Denied | HR status |
| OC-04 | Leader | Leave-status requester submits | Denied | HR status |
| OC-05 | Admin | Review queue scope | Same org/team only | Queue |
| OC-06 | Admin | Self-approval | Denied | Audit unchanged |
| OC-07 | Admin | Cross-organization review | Denied | Scope |
| OC-08 | Admin | Cross-team review | Denied | Scope |
| OC-09 | Admin | Approve ready request | Applied atomically | Request/Stage/Audit |
| OC-10 | Admin | Reject pending request | Rejected/Abandoned | Request/Stage/Audit |
| OC-11 | Admin | Duplicate DecisionKey | Replay denied | Unique event |
| OC-12 | Admin | Concurrent decisions | One winner | Serializable lock |
| OC-13 | Admin | Requester role revoked | Decision denied | Live role |
| OC-14 | Admin | Reviewer role revoked | Decision denied | Live role |
| OC-15 | Admin | Team reassignment | Denied | Live scope |
| OC-16 | Admin | Missing HR evidence | Fail closed | Auth evidence |
| OC-17 | Admin | Missing geocode evidence | Publish denied | Stage |
| OC-18 | Admin | Missing duplicate evidence | Publish denied | Stage |
| OC-19 | Admin | Unknown COMMIT | Reconcile only | Fresh read |
| OC-20 | Visitor | Own status query | Correct state | Status API |
| OC-21 | Visitor/Leader | Low-risk MasterNote edit | Direct save works | Location |
| OC-22 | Admin | Location/history preservation | Version 1 and history intact | SQL evidence |

Business users must record actor, target, timestamp, expected result, actual result, evidence link and sign-off for every case. No automated rehearsal is Business UAT PASS.

## Owner final execution approval sheet

The following must be checked before a separate execution approval is issued:

- [ ] Exact UAT target supplied and allow-listed
- [ ] Read-only schema/version/dependency preflight PASS
- [ ] Backup and restore evidence reviewed
- [ ] Additive migration checksum and rollback owner confirmed
- [ ] Deployment order and flag sequence approved
- [ ] Recovery contact and unknown-commit procedure confirmed
- [ ] Backend/frontend candidate artifacts identified by exact SHA
- [ ] Business UAT 22-case schedule and users assigned
- [ ] Explicit Owner authorization to execute UAT migration
- [ ] Explicit Owner authorization to deploy
- [ ] Explicit Owner authorization to activate flags

Until every applicable gate is checked, status is **HOLD / NO-GO**.

## Current recommendation

Engineering candidate: PASS in isolated SQL/HTTP.

UAT execution authorization review: **READY TO REVIEW AFTER TARGET IDENTIFICATION**.

Actual UAT execution: NOT AUTHORIZED by this package.

Business UAT: NOT RUN and no Business UAT GO declared.
