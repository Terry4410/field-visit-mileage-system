# Package B — Business UAT execution pack (Owner candidate / NOT YET AUTHORIZED)

Issue #24 | Controlled WORK-only preparation | formal execution status: **0/22 NOT RUN**.

This Owner-facing operating pack follows the intended sequence: technical
completion → Owner UAT approval → controlled deployment → Business UAT →
business-defect correction → internal-installation handoff. The later IT
handoff is not an automatic prerequisite to all business testing; real UAT
still needs a named target, approved data boundary and the gates listed below.

## Immutable entry record

Record before any UAT action: exact candidate SHA/tree; backend/frontend
artifact digests; UAT server/database identity; deployment run IDs; latest
SchemaVersion; B3/LiveSessionGuard runtime values; test-data batch ID; UTC
time; tester and reviewer aliases. Never retain credentials or real employee
data in this pack.

## Test accounts and controlled data

| Alias | Required live fact | Controlled data |
| --- | --- | --- |
| `UAT-VIS-A` | active Visitor in O1/T1 | owns approved active `UAT-B3-L1` Customer Location |
| `UAT-VIS-B` | active Visitor in O1/T1 | does not own `UAT-B3-L1` |
| `UAT-VIS-C` | active Visitor in O2 | no O1 visibility or write scope |
| `UAT-ADM-A` | active independent Admin in O1 | distinct requester/reviewer identity |
| `UAT-ADM-B` | active Admin in O2 | no O1 scope |
| `UAT-LEAD-H` | historical/backfilled Leader only | must fail until VAL-B1-002 is attested |
| `UAT-REV-01` | controlled revocation identity | temporary HR/role/team test only |

Use synthetic addresses and a unique `UAT-B3-<batch>-*` prefix. Establish an
approved cleanup/recovery SOP before any mutation; do not use existing
business records, official sites or real employee data.

## 22-case execution matrix

Every result needs sanitized HTTP/UI evidence, correlation ID, before/after
counts and RowVersions where relevant, audit sequence, tester, independent
reviewer and PASS/FAIL/BLOCKED status.

| Cases | Setup / procedure | Expected result | Required signoff |
| --- | --- | --- | --- |
| B4-01–04 | DBA read-only catalog evidence; do not create drift in shared UAT | Missing/version/index/column/CHECK drift is unavailable; zero DDL/DML | DBA + IT |
| B4-05 | Two `UAT-VIS-A` sessions submit same high-risk change | one Pending/Submitted pair; other request 409 | Business + IT |
| B4-06 | `UAT-ADM-A`, only after B3 activation approval | same DecisionKey replays once only; 409/no extra event | IT + Business |
| B4-07 | stale Location or request RowVersion | 409; source and audit unchanged | IT + Business |
| B4-08 | `UAT-VIS-B` tries A-owned Location | 403/404; zero request/event | Business + IT |
| B4-09 | `UAT-VIS-C`/`UAT-ADM-B` target O1 GUID | no disclosure or write | Security + IT |
| B4-10–11 | `UAT-REV-01` role/HR/team revoked mid-session | next sensitive action denied; zero write; controlled restore | HR + IT |
| B4-12 | projected external Admin identity | 403; zero privileged write | Security |
| B4-13 | dual-role requester tries own review | 403; independent reviewer mandatory | Owner + IT |
| B4-14 | `UAT-LEAD-H` historical grant only | denied; remains BLOCKED without VAL-B1-002 | HR + IT + Owner |
| B4-15–17 | geocode race, referenced delete, spoofed team scope | no auto-publish/history loss/foreign disclosure | Business + IT |
| B4-18–20 | payload tamper, SQL failure mapping, Admin revocation | untrusted/unknown condition fails closed | IT + Security |
| B4-21–22 | DBA isolated validation only | trigger/malformed audit state rejected; audit preserved | DBA + IT |

## B3 boundary

Current checked-in state remains `PackageB:B3:Enabled=false`; `ApproveAsync`
and Apply are DENY ALL. B4-06, B4-13 and the positive Approved/Applied
journey are therefore **BLOCKED** until a separate Owner activation decision
after candidate 1800_011/012 review, deployment and atomic-executor evidence.
No UAT script may fabricate a successful approval.

## Owner UAT execution approval sheet

| Decision item | Owner decision | Mandatory attachment |
| --- | --- | --- |
| Pin source and exact CI | PENDING | SHA/tree, jobs and artifact digests |
| Execute candidate 1800_011 in named UAT | PENDING | DBA preflight, file SHA256, backup/recovery window |
| Execute candidate 1800_012 in named UAT | PENDING | separate preflight, 011 Verify, forward-recovery approval |
| Deploy backend/frontend with B3 OFF | PENDING | immutable artifacts, health/smoke, rollback plan |
| Provision B4 accounts/data | PENDING | HR test-data SOP and Owner data boundary |
| Enable B3 / Approve-Apply | NOT APPROVED | atomic executor, independent security review |
| Start formal Business UAT | PENDING | named environment, deployment PASS, case owners |
| Production | HARD HOLD | separate release authorization |

**Current recommendation:** candidate preparation can continue; real UAT
remains HOLD until the Owner approves the marked actions and exact UAT
environment evidence is attached.
