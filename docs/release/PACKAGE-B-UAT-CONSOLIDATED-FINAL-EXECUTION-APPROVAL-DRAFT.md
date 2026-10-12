# Package B — Consolidated UAT execution approval packet (DRAFT / NOT APPROVED)

**Issue #24 | Owner conditional preparation authority 2026-10-11 | WORK-only artifact**

**DECISION: NO-GO / DO NOT EXECUTE.** This is a proposed single Owner review
packet, **not** permission to run SQL, deploy, promote, enable B3 or lift HOLDs.
The Owner authorized preparation and disposable tests only. Production is
**HARD HOLD** regardless of a future UAT decision.

## 1. Identity and immutable baseline

| Field | Current value / required verification |
| --- | --- |
| Repository | `Terry4410/field-visit-mileage-system` |
| Controlled source branch | `work/v180-package-b-b2-b3-controlled` |
| Protected source branch | `post-uat/v1.8.0` |
| Protected exact SHA | `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2` — revalidate at approval time |
| Candidate SHA / TREE | **PENDING** — pin the exact final approved candidate, never use a moving branch |
| Build artifacts and SHA-256 | **PENDING** — record backend/frontend artifact digests |
| Latest required isolated CI | **PENDING** — collect run ID, jobs, conclusions and SQL TRX SHA-256 |
| Target SQL Server and database | **UNVERIFIED** — IT to supply exact UAT identity by read-only evidence |
| Live latest SchemaVersions | **UNVERIFIED** — 011 predecessor requires exactly `1.8.0-010` |
| B3 feature flag | **OFF** |
| B3 Approve/Apply | **DENY ALL** |
| Formal B4 acceptance | **0/22** |
| VAL-B1-002 manager-grant provenance | **BLOCKED / independent HR+IT attestation required** |
| Business UAT | **HOLD** |
| Production | **HARD HOLD** |

Never substitute disposable SQL Server 2022 fixture success for a deployed
UAT SQL Server schema, HTTP authorization, independent HR data or B4 signoff.

## 2. Dependency gates — in strict order

1. **Source freeze**: Owner and IT review exact candidate SHA/tree and full
   diff against protected SHA. Independent security review confirms B2/B3
   operation/role/ownership policy, historical/audit invariants and fail-closed
   behavior. CI and disposable SQL suite green at that exact SHA.
2. **UAT identity / preflight**: IT read-only check server, database, login,
   environment, latest schema `1.8.0-010`, 011 object absence, all expected
   columns, constraints, indexes, triggers, FK dependencies and drift.
   Any partial 011 artifact, schema mismatch or missing permission = STOP.
3. **SQL IA**: Independently review proposed `1800_011` DDL and a distinct
   forward-only approval/apply schema revision. Do **not** rewrite 011 to
   allow `Applied`. The current 011 candidate allows only Pending/Rejected
   and Submitted/Rejected. Atomic approval/apply is **not** authorized.
4. **Recovery review**: IT certifies UAT backup/restore ownership, restore
   point, estimated locks/downtime, maintenance window, failure checkpoints,
   forward-recovery for non-reversible DDL and application rollback order.
   Do not promise transactional DDL rollback or data reversal without proof.
5. **Independent HR/IT attestation**: Certify manager-grant provenance,
   effective dates and authority for VAL-B1-002. Historical/backfilled
   TeamLeaderAssignments alone are **not** acceptable evidence.
6. **Explicit final Owner execution approval**: Specify each approved action,
   exact SQL version and SHA, environment, executor, window and stop criteria.
   Conditional preparation authorization does not satisfy this gate.
7. **If approved, IT-controlled execution only**: Perform read-only preflight
   again immediately before each separately approved schema action, execute
   only the reviewed immutable script, capture DB before/after evidence and
   stop on first failure. No speculative automatic schema repair.
8. **If approved, controlled UAT app deployment**: Verify schema compatibility
   and B3 flag OFF, deploy approved backend then frontend artifacts, perform
   health/smoke checks, validate exact artifact SHA and environment override
   values. Revert app artifacts on health failure; escalate DB recovery to IT.
9. **Formal 22-case B4 acceptance**: Authorized real actors and independent
   HR/IT evidence; capture per-case request/response, SQL after-state,
   audit/history, actor role/effective date, correlation ID, evidence owner
   and signoff. No test that mutates UAT without an approved test-data plan.
10. **Separate B3 activation decision**: Requires approved forward-only
    schema, atomic apply executor, full independent security evidence and
    explicit Owner authorization. Remains OFF and DENY ALL otherwise.

## 3. SQL impact and recovery worksheet

| Review item | Evidence required | Current state |
| --- | --- | --- |
| 011 predecessor and exact catalog | Read-only SQL output, DB identity, timestamp | PENDING |
| 011 candidate schema | Reviewed SQL text, checksum, approved owner, constraint semantics | DRAFT ONLY |
| Forward-only 012+ | Independent revision/IA for Applied state and audit, no 011 rewrite | NOT APPROVED |
| Table/index locks and runtime | SQL Server estimate and maintenance window | UNKNOWN |
| FK/unique/CHECK/triggers | Six trusted NO ACTION FKs; required unique indexes and eight CHECKs | DISPOSABLE EVIDENCE ONLY |
| Existing data impact | Counts, compatibility and zero implicit business-data repair | UNVERIFIED |
| Backup/restore | IT backup identifier, RPO/RTO, restore operator and drill evidence | PENDING |
| Failure recovery | Script-specific failure checkpoints; forward-fix vs restore decision | PENDING |
| Application compatibility | Exact backend/frontend build and flag OFF smoke | PENDING |

**Stop immediately** on incorrect server/database, schema predecessor drift,
missing trusted constraints, unapproved DDL checksum, SQL error, unexpected
data changes, audit mutation, elevated privilege, B3 unexpectedly enabled,
health-check failure or HR authorization anomaly.

## 4. Deployment worksheet (no deployment authorized)

| Step | Required evidence | Result |
| --- | --- | --- |
| Validate exact approved source SHA/tree | Owner/IT diff signoff | PENDING |
| Verify CI and SQL fixture artifacts | Run IDs, job PASS, TRX digest | PENDING |
| Pin backend artifact and config | Artifact SHA, UAT secret references (never values), flag OFF | PENDING |
| Pin frontend artifact | Artifact SHA, API endpoint/environment config | PENDING |
| Pre-deploy health and schema readiness | UAT-only evidence | PENDING |
| Backend deploy and smoke | Run ID, health/API checks, no unintended B3 writes | NOT AUTHORIZED |
| Frontend deploy and smoke | Run ID, route/access/UX checks | NOT AUTHORIZED |
| Rollback decision | Prior artifact IDs, operator, time and DB compatibility | PENDING |

## 5. Formal B4 acceptance register

The authoritative 22-case traceability is
`scripts/ci/package_b_b4_coverage.py` and
`docs/release/PACKAGE-B-B3-B4-REMAINING-RUNTIME-UAT-GATES-NONEXECUTABLE.md`.
For each B4-SQL-01 through B4-SQL-22, the executor must attach:
test ID, actor and HR/role/team effective-date evidence, exact request,
HTTP status/body, correlation ID, before/after database snapshot,
immutable audit proof, negative authorization result, independent reviewer,
execution timestamp, environment/build SHA and explicit PASS/FAIL signature.

**Current acceptance 0/22**. A partial disposable SQL mapping is never a
formal case PASS. Required cases include concurrency, DecisionKey replay,
stale RowVersion, peer-team/cross-org IDOR, revoked HR/Admin/Leader grants,
geocoding races, safe-delete history, unknown payloads, trigger/catalog drift
and malformed audit state. Formal UAT cases that need enabled B3 or approval
must not be attempted until the separate activation gate has passed.

## 6. Proposed Owner approval form — intentionally blank

| Proposed action | Owner decision | Independent prerequisite |
| --- | --- | --- |
| Execute reviewed 1800_011 in named UAT DB | **NOT APPROVED** | SQL IA, preflight, recovery, IT executor |
| Execute any forward-only approval/apply schema revision | **NOT APPROVED** | Separate DDL IA and security signoff |
| Deploy exact backend UAT artifact | **NOT APPROVED** | Schema, CI, artifact, rollback checks |
| Deploy exact frontend UAT artifact | **NOT APPROVED** | Backend health, artifact, smoke |
| Run formal UAT test-data mutation / 22-case B4 | **NOT APPROVED** | Actor/HR attestation and test-data plan |
| Enable B3 / allow Approve-Apply | **NOT APPROVED** | Atomic executor and independent security approval |
| Promote WORK source | **NOT APPROVED** | Separate promotion approval |
| Production release | **HARD HOLD** | Not in this packet |

**GO criteria**: every mandatory gate has an independently reviewable
artifact, no critical risk unresolved, explicit action-by-action Owner
approval, and verified target identity. Otherwise **NO-GO**.

Owner final decision: **PENDING**. Approved SHA/tree: **PENDING**.
UAT DB identity: **PENDING**. IT executor: **PENDING**.
Independent HR/IT attestation: **PENDING**. Window: **PENDING**.

## 7. Handoff rules

- Never paste tokens, connection strings or live credentials into evidence.
- Keep source SHA, workflow run ID, SQL TRX digest and exact schema
  fingerprints together; rerun gates if any changes.
- Keep evidence separated: isolated SQL / static CI / UAT HTTP / HR
  attestation / formal business signoff.
- If a gate fails, record the failing invariant, do not silently skip it,
  and request one consolidated Owner decision for external blockers.

## 8. Additional 012 WORK-only proof and remaining independent fingerprint sign-off

SQL 2022 fixture now compares full catalog metadata snapshots within a
rollback-only 011→012 rehearsal. Tests deliberately alter CHECK semantics,
DecisionKey filtered-index predicates, predecessor history and enabled
trigger behavior while preserving earlier partial-check shape. This protects
test evidence against a class of token-preserving schema drift, but **cannot
serve as a release fingerprint**, because the baseline is captured
inside a disposable fixture rather than pinned to an independently signed
DDL artifact. Separate IT-approved, immutable 012 fingerprint and actual
Locations DDL parity are **BLOCKED / REQUIRED**. The exact green WORK SHA,
SQL TRX SHA-256 and current named case list must accompany any later
security review. Formal B4 stays 0/22 and all release decisions remain HOLD.



## 9. Further isolated Locations integration — only a reduced stub

WORK test source includes a three-table, real-named `dbo.Locations` /
`dbo.ChangeRequests` / `dbo.ChangeRequestEvents` disposable rehearsal
with four rollback/replay/scope scenarios and exact seven-field UPDATE
parameters. A reduced Locations **test stub** is not physical schema
equivalence and simulated in-test authorization booleans are not live HR/IT
proof. Formal executor, unknown COMMIT result resolution and all UAT approvals
remain expressly blocked. Do not reuse this helper in production code.



## 10. Unknown COMMIT and physical Locations parity — WORK-only evidence

A separate reconnecting, read-only observation probe now checks the
B3Probe transaction for terminal Request + seven Location values/versions,
independent reviewer, exactly matched Approved/Applied audit and one
DecisionKey, without any retry. An exact rollback may be observed as
NO_COMMIT evidence; partial audit, drift, cross-request replay and absent
record are INDETERMINATE. This does not handle a real transport-disconnected
COMMIT nor authorize user-facing success or resubmission. A separate
disposable EF model test demonstrates the reduced Locations stub lacks
geocoding/duplicate/history columns: full table parity is still REQUIRED.
Keep a real COMMIT-ack-loss, concurrent post-commit drift and full
Locations migration/canonical catalog review on the independent security
worklist; formal B4 remains 0/22.
