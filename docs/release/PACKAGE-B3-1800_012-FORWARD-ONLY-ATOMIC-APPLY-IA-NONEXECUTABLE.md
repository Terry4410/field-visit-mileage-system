# Package B3 — Forward-only 1800_012 approval/apply schema impact analysis
## NONEXECUTABLE DESIGN — NO SQL MIGRATION AUTHORIZATION

**Issue #24 | WORK candidate | Prepared under Owner's conditional UAT
preparation authority. This file contains NO executable DDL.**

### 1. Why a separate revision is mandatory

The 1800_011 candidate explicitly constrains `ChangeRequests.Status` to
`Pending` / `Rejected`, and `ChangeRequestEvents.EventType` to
`Submitted` / `Rejected`. Its review-state and event-decision CHECKs are
also rejection-only. Updating the C# code to write `Applied` or
`Approved` without a **separate** reviewed, forward-only schema revision
would violate trusted SQL constraints. Do **not** rewrite or quietly replace
1800_011, disable constraints, bypass readiness or run ad hoc ALTER commands.

**Design choice requiring independent signoff:** the approval and publication
are one indivisible transaction. The request has terminal `Applied`
(no persisted intermediate `Approved` status); the append-only event log
records `Approved` then `Applied` in the same commit. No asynchronous
post-approval publication, partial commit, or externally visible Approved-only
request state is permitted.

### 2. Exact schema delta for a proposed 1800_012 (specification only)

| Object | Proposed change | Mandatory invariant |
| --- | --- | --- |
| `SchemaVersions` | Append version `1.8.0-012` after exact latest 011 | Never alter or erase earlier version history |
| `CK_B3_ChangeRequests_KnownStatus` | Replace with trusted enabled CHECK allowing Pending, Rejected, Applied | No Returned/Cancelled/Approved request states |
| `CK_B3_ChangeRequests_ReviewState` | Replace with Pending, Rejected, Applied mutually exclusive shapes | Applied requires independent reviewer, nonblank reason, ReviewedAt and AppliedAt; Rejected has no AppliedAt |
| `CK_B3_ChangeRequestEvents_EventType` | Replace with Submitted, Rejected, Approved, Applied | Unknown event types denied |
| `CK_B3_ChangeRequestEvents_DecisionState` | Approved has nonnull actor and DecisionKey; Applied has actor and **null DecisionKey** | Only one unique decision key per decision; Approved/Applied pair correlated by request and correlation ID |
| `UX_B3_ChangeRequestEvents_DecisionKey` | Retain unique filtered index (non-null DecisionKey) | One-time decision; replay cannot create another decision |
| `UX_B3_ChangeRequests_Org_Entity_Pending` | Retain exact filtered unique Pending index | At most one Pending per organization/entity |
| `ChangeRequests.RowVersion` and source `Locations.RowVersion` | Retain physical ROWVERSION | Compare both freshly locked versions inside transaction |
| `ChangeRequestEvents` history | Append-only, NO ACTION FKs, no DML triggers | Both decision and apply events commit with location change or neither commits |

The precise CHECK definitions, constraint replacement order, SQL Server
three-valued NULL semantics, index fingerprints, data compatibility and
version marker must be independently reviewed before creating any Up/Verify
script. `CHECK UNKNOWN` must not accidentally admit NULL reviewer/reason
or malformed audit records.

### 3. Atomic transaction contract

1. Require **new** schema readiness: latest exactly `1.8.0-012`, all
   011 base catalog fingerprints plus reviewed 012 CHECK fingerprints.
   The current 011-only readiness MUST fail closed on 012 until a separate
   code change and independent security review are approved.
2. Begin serializable transaction, lock request by public ID/org with
   `UPDLOCK,HOLDLOCK`; independently resolve live HR status and effective
   Admin authority from authoritative DB (not claims or UI).
3. Lock source Location by ID/org with `UPDLOCK,HOLDLOCK`; recheck current
   requester team membership, source ownership, effective org/team, active
   approved Customer classification, and geocoding/duplicate/history rules.
4. Re-read Pending request and both SQL RowVersions, compare canonical
   BeforeJson with locked current source, validate exactly seven whitelisted
   proposed fields, compare DecisionKey and reason to locked decision input,
   ensure requester != reviewer, and reject DecisionKey replay.
5. Execute a single guarded source UPDATE with the original RowVersion and
   scope predicates; **require exactly one affected row**. No unconditional
   update and no publication by geocoding callback.
6. Update the Pending request to Applied with reviewed/applied timestamps,
   reason and reviewer, guarded by its original RowVersion; **require exactly
   one affected row**.
7. Append `Approved` and `Applied` immutable events in the **same**
   transaction with one correlation ID; the Approved event alone carries
   DecisionKey, while Applied links the same request/correlation.
8. Commit only after every UPDATE and audit INSERT succeeds. Any failure
   rolls back location, request and events together. No SQL error may be
   converted into success. Translate only known named duplicate indexes to
   409; unexpected SQL errors fail closed and trigger incident review.
9. After commit, read-only verification confirms exact expected location,
   terminal request, two immutable events and no duplicate DecisionKey.
   Verify historical trip/snapshot records remain unchanged.

### 4. Concurrency and replay test obligations

- Same Pending request, two reviewers: exactly one complete Applied
  transition, one correlated Approved/Applied pair, loser conflict.
- Same DecisionKey on different requests: second decision denied; no
  location/request/audit mutation.
- Stale source or request RowVersion: no updates, no events.
- Mid-transaction failure at source UPDATE, request UPDATE, Approved event
  insert, Applied event insert or commit: rollback all side effects.
- Cross-org and peer-team request IDs, self-review, revoked Admin,
  terminated/leave/expired requester or reviewer, unattested Leader:
  authorization denied with no side effects.
- Pending geocoding callback or duplicate-location candidate: cannot
  publish, auto-approve or bypass independent review.
- Disabled/untrusted CHECK, FK, unique index, altered filter, enabled trigger,
  unexpected schema version, partial 012 installation: readiness denied.
- Repeat requests after successful Applied: retain immutable history and
  require fresh source RowVersion for any new proposal.

### 5. UAT and production gates

| Gate | Current status |
| --- | --- |
| Owner conditional preparation authorization | GRANTED — preparation only |
| 1800_011 reviewed executable script | NOT AUTHORIZED |
| Actual UAT latest 010/011 catalog evidence | NOT VERIFIED |
| Proposed 1800_012 executable DDL | NOT WRITTEN / NOT AUTHORIZED |
| 012 SQL Server isolated upgrade and rollback testing | NOT EXECUTED |
| Atomic apply executor | NOT IMPLEMENTED / DENY ALL |
| Independent HR/IT VAL-B1-002 provenance | BLOCKED |
| Full 22-case UAT acceptance | 0/22 |
| B3 activation | OFF |
| Business UAT | HOLD |
| Production | HARD HOLD |

This IA is a dependency and test specification only. No migration is
performed by creating this document. The final consolidated Owner execution
approval packet must include exact immutable SQL script checksums, live UAT
database identity, schema predecessor evidence, IT recovery ownership,
independent security review, deployment artifacts and the formal B4 matrix.


### 6. Transaction failure-injection and rollback evidence contract (WORK only)

This is a NONEXECUTABLE acceptance contract. No UAT write or deployment
is authorized.

Capture baseline snapshots of the target Location (seven fields and
RowVersion), Pending request (review fields and RowVersion), request events,
and historical VisitTripStop/VisitTripSnapshotStop references in a disposable
SQL Server 2022 fixture. After each forced rollback, compare every captured
field and event identity with the baseline. SQL identity gaps are not
rollback evidence.

| Injected failure | Required observation |
| --- | --- |
| Before source UPDATE | No source, request or event mutation |
| Source UPDATE returns zero or multiple rows | Conflict; rollback all changes |
| After source UPDATE, before request UPDATE | Source rolled back; request remains Pending |
| Request UPDATE returns zero or multiple rows | Source and request rolled back; no events |
| After request UPDATE, before Approved INSERT | Both UPDATEs rolled back |
| Approved INSERT FK, CHECK or uniqueness failure | No source, request or audit mutation |
| After Approved INSERT, before Applied INSERT | Both UPDATEs and Approved event rolled back |
| Applied INSERT FK or CHECK failure | Both UPDATEs and both events rolled back |
| Commit exception or dropped connection | Outcome UNKNOWN; reconcile using immutable correlation and DecisionKey before retry, never return success blindly |
| Two concurrent reviewers | At most one complete Applied transition; losing reviewer conflicts |
| Duplicate DecisionKey across requests | Exactly one decision accepted; loser has no mutations |
| Live permission or schema drift after preview | Locked revalidation denies without writes |

Executor invariants: use one SQL connection and one Serializable transaction
for both guarded UPDATEs and both audit INSERTs. Lock request first, then
location; bind all untrusted values as SQL parameters; demand exactly one
affected row for each UPDATE. Do not convert concurrency, deadlock, timeout
or ambiguous commit errors into success. Never blindly retry a transaction
whose commit outcome is unknown. Approved and Applied share one nonempty
correlation ID and ordered UTC timestamps; only Approved carries DecisionKey.

Each injection requires a fixture/test ID, SQL Server version, exact WORK
SHA, test-run URL and captured before/after assertions. Pure C# preflight,
CI success and documentation do not count as isolated SQL runtime PASS or
formal B4 UAT. Keep B3 OFF, ApproveAsync DENY ALL, 012 nonexecutable and
Package B NO-GO pending independent approval.
