# Package B — independent IT/DBA/HR review readiness batch

**Issue #24 | WORK-only evidence preparation | NONEXECUTABLE | release decision: NO-GO**

## 1. Frozen review facts

| Item | Verified value | Boundary |
| --- | --- | --- |
| Protected source | `post-uat/v1.8.0` / `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2` | Unchanged; not promoted by this package. |
| Candidate under review | `work/v180-package-b-b2-b3-controlled` / `90e69473c77829ca531da4abd6f1a60a3ac296b5` | WORK-only; candidate evidence is not external sign-off. |
| Latest exact-SHA non-deploy CI | Run `38217728463` — SUCCESS | Two jobs: controlled build/test/evidence and disposable localhost SQL Server only. |
| B3 runtime state | Feature flag OFF; `ApproveAsync` / Apply DENY ALL | No real Approved/Applied API call, migration, deployment or Business UAT was run. |
| Formal B4 acceptance | `0/22` | A fixture or TestServer PASS is partial technical evidence, never a completed B4 case. |

Run `38217728463` independently passed the source guard, evidence-parser negative tests, source-hashed 14-gate ledger, read-only parity inventory, B4 matrix parser, baseline ancestry, backend/frontend build and tests, disposable SQL catalog/concurrency/audit tests, six dated-site SQL cases, and 20 JWT/HTTP-plus-disposable-SQL cases. It did **not** access Azure, UAT, Production, HR data or an approved 1800_011/012 schema.

## 2. One 14-gate external-evidence request — do not duplicate CI coverage

The machine-readable source-of-truth is
`PACKAGE-B3-IT-EVIDENCE-DELTA-MATRIX.json`; every gate is intentionally
`BLOCKED_UNSIGNED`. The following table is the reviewer-facing request, not a
replacement sign-off ledger.

| Gate | Responsible independent owner | Required external evidence / acceptance |
| --- | --- | --- |
| DDL-01 | IT/DBA | Reviewed, forward-only 1800_011 Up/Verify, predecessor version and immutable file SHA256. |
| DDL-02 | IT/DBA | Reviewed 1800_012 Up/Verify, allowed state/event transitions, rerun rule and forward recovery. |
| CAT-01 | DBA | Signed `sys.columns`/types/nullability/rowversion inventory and EF mapping. |
| CAT-02 | DBA | Signed enabled-and-trusted CHECKs, defaults, unique/filtered indexes, key order/filter/include columns. |
| CAT-03 | DBA | Signed FK direction/actions/trust, trigger, object dependency and permission inventory. |
| LOC-01 | DBA + Development | Complete real `Locations` catalog; 012 seven-field update compatibility with all FK/index/default/trigger dependencies. |
| HIST-01 | DBA + Business | True reference graph for stops, snapshot stops, notes/history, geocoding and exports; no cascade and no live-location contamination of snapshots. |
| VER-01 | DBA + Security | Expected immutable catalog fingerprint derived from reviewed DDL, separately retained before comparison; not a same-session self-computed live hash. |
| GRANT-01 | Security + DBA | Least-privilege, time-bounded 011/012 migration principal; pre/post Grant–Verify–Revoke audit. |
| DEP-01 | IT DevOps | Separate SHA/TREE-pinned UAT migration workflow with backup, postflight verification, failure stop and rollback/forward-recovery ownership. |
| VAL-B1-002 | HR + IT | Masked, read-only provenance roster for formal leaders/delegates, validity periods, HR status, Admin grants and revocations; historic/backfilled membership is explicitly non-authoritative. |
| B3-APPROVE | IT + Owner | Independently reviewed atomic executor and live authorization proof before any removal of DENY ALL. |
| B4-UAT | Business + Owner | Approved UAT target and signed 22-case result pack, each with sanitized evidence. |
| PRODUCTION | Owner + IT | Separate production change window, rollback and formal release authorization. |

**Schema catalog extraction requirements.** The DBA package must identify UAT
database/server, capture time and principal, and return versioned output for
`SchemaVersion`; `sys.tables`, `sys.columns`, `sys.types`; CHECK/defaults;
indexes/index columns; FKs/FK columns; triggers; expression dependencies; and
object/principal permissions. It must cover `ChangeRequests`,
`ChangeRequestEvents`, `Locations` and every historical-reference table named
above. The expected fingerprint is calculated from a canonical reviewed schema
manifest (object names, ordered columns, normalized definitions, trust and
enabled state) before it is compared to catalog output. Missing, extra,
untrusted or reordered items are a mismatch and keep HOLD.

## 3. VAL-B1-002 — HR/IT reviewer request (no real employee data accessed)

Provide a masked, read-only attestation containing only a reviewer alias and
stable pseudonymous IDs. For each sampled grant and denial, record effective
time, organization, formal Leader appointment or explicitly delegated grant,
source system record identifier, current HR employment status, active role,
team scope, location ownership and revocation evidence. The reviewer must
confirm all of the following:

1. Team membership, picker visibility, historical auto-assignment and a
   backfilled `TeamLeaderAssignment` **do not** create team-location authority.
2. Only a current formal appointment or valid explicit delegation can grant it;
   expired, leave, terminated, pre-hire, disabled, external/anomalous or
   cross-organization identities are denied.
3. The application must re-read effective HR, role, organization, team and
   ownership inside the transaction; a JWT claim alone is insufficient.
4. Evidence conflicts, missing sources or an inability to verify revocation
   result in `REJECT/HOLD`, never a presumed grant.

No roster, identity value, SQL credential or UAT query is collected by this
repository. HR/IT retain the original evidence and return a separately governed
attestation reference.

## 4. B3 atomicity review: source-supported facts and remaining gaps

| Review invariant | Candidate/source evidence | Independent gap that remains |
| --- | --- | --- |
| Source version | Candidate requires immutable expected source `RowVersion`; stale values are fail-closed in isolated checks. | Validate against actual reviewed 012 catalog and API/SQL runtime. |
| Single transaction | Disposable fixture checks serialized request decision/event behavior and rollback of failed event writes. | Review/approve/apply executor is intentionally unavailable; no live atomic apply exists. |
| Audit invariants | Request/event constraints and immutable-history probes reject malformed state/reason/actor/decision data. | DBA must attest actual trusted constraints and historical dependencies. |
| Idempotency/concurrency | Pending and DecisionKey duplicate/race fixtures are partial evidence. | Test actual enabled API with approved schema only after separate authorization. |
| UNKNOWN COMMIT | Candidate rehearsal requires a new-connection, read-only reconciliation by immutable request/correlation/decision/event facts; it forbids blind retry. | IT must approve timeout/connection-loss observability, reconciliation operator, timebox and final UAT evidence. |

There is no uncovered candidate defect suitable for silent code expansion in
this batch. The genuine blockers are external: reviewed 011/012 DDL/catalog,
VAL-B1-002, authorized atomic executor design/implementation, and approved
isolated/UAT runtime evidence.

## 5. B4 entry mapping — all formal cases remain NOT RUN

| Cases | Required role(s) | Prerequisite/data dependency | Required sign-off |
| --- | --- | --- | --- |
| B4-01–04 | DBA, IT | Exact 011/012 catalog, SchemaVersion, trusted constraints/indexes/FKs/triggers | DBA + IT |
| B4-05 | Two Visitors | Same owned Location, concurrency harness, audit counts | Business + IT |
| B4-06–07 | Admin, Visitor | DecisionKey and stale source/request RowVersions | IT + Business |
| B4-08 | Two same-team Visitors | One owns Location; peer does not | HR/IT + Business |
| B4-09 | Cross-org Visitor/Admin | Isolated organizations and foreign GUIDs | Security + IT |
| B4-10–12 | HR, Security, test identities | Lifecycle/role/external-projection revocation | HR + Security |
| B4-13 | Dual-role submitter + independent Admin | Submitted request and distinct reviewer | Owner + IT |
| B4-14 | Historical-only Leader | Unattested historic assignment and VAL-B1-002 denial | HR/IT + Owner |
| B4-15–17 | Visitor/Location/Security | Pending request, geocoding callback, historical references, spoofed team query | Business + Security |
| B4-18–19 | Admin, IT | Unsupported payloads and named duplicate vs unrelated SQL failures | IT + Security |
| B4-20 | Admin + HR | Role revocation during bulk sensitive write | HR + IT |
| B4-21–22 | DBA, IT | Disposable approved schema/trigger and malformed request/event rows | DBA + IT |

Case-level action and expected result remain in
`PACKAGE-B-FULL-UAT-22-CASE-TRACEABILITY-NONEXECUTABLE.md`. This mapping
assigns prerequisites and sign-off without pretending any case ran:
**formal status = 0/22 NOT RUN**.

## 6. Handoff decision

**Package B stays NO-GO.** B3 is OFF, Approve/Apply is DENY ALL, all fourteen
external gates are unsigned, Business UAT is HOLD and Production is HARD HOLD.
This document authorizes nothing: no protected-branch change, UAT migration,
database access/change, deployment, feature activation or formal test execution.
