# Package B — Full Business UAT: 22-case traceability and handoff (NONEXECUTABLE)

**Owner scope: A — FULL PACKAGE B** | Issue #24 | WORK-only | 2026-10-11

This document is an executable *manual test specification* only after separate
approval and deployment; it is not an approval, SQL migration, production
instruction, or evidence of a passed test. All 22 formal cases remain PENDING.

## Scope, actors and data requirements

Actors: (1) Visitor A with a current verified HR identity, live Visitor role,
current team membership and an owned approved Customer Location; (2) Visitor B
in the same team, who does NOT own A's Location; (3) Visitor C in another
organization; (4) independent Admin A with valid current role; (5) Admin B
in another organization; (6) Leader with historical/backfilled but **not
attested** manager grant; (7) disabled/Leave/Terminated user. Use distinct
nonproduction accounts, deterministic fixture identifiers and approved
synthetic location addresses. Record user role/org/team and UAT database
identity before execution. Do not copy real employee personal data into
logs, screenshots, or test exports.

**Evidence per case:** exact deployed commit SHA, frontend/backend version,
UTC timestamp, test account alias, feature flags, request correlation ID,
HTTP status and sanitized response, UI screenshot where applicable,
SQL before/after row counts and RowVersion, audit/event sequence, defect ID,
tester and independent reviewer signoff. A 403/409/503 response without
proving zero unauthorized writes is not sufficient.

| ID | Business user action / setup | Expected observable result | Gate |
| --- | --- | --- | --- |
| B4-01 | Start with missing/partial 011 schema, missing or untrusted FK | B3 unavailable, no request/event/location write or auto-DDL | IT schema |
| B4-02 | Present older/later latest SchemaVersion or stale 011 row | Denied; exact latest predecessor and approved 011 required | IT schema |
| B4-03 | Disable or alter Pending/DecisionKey uniqueness | B3 unavailable, no race bypass | IT schema |
| B4-04 | Drift SQL column size/nullability/CHECK | B3 unavailable; catalog discrepancy recorded | IT schema |
| B4-05 | Two Visitors submit concurrent high-risk edits to one Location | Exactly one Pending + Submitted event; other 409 | Visitor |
| B4-06 | Replay identical DecisionKey on same/other request | Only one decision; duplicate 409, no extra event | Admin |
| B4-07 | Edit Location or request after screen load, submit stale RowVersion | 409, no stale publish or audit side effects | Visitor/Admin |
| B4-08 | Visitor B changes Visitor A's Location in same team | 403, no request or event | Visitor |
| B4-09 | Visitor/Admin from different organization query/write GUID | 403/404; no data disclosure or writes | Security |
| B4-10 | Revoke role or set Leave/Terminated/disabled during session | Subsequent sensitive action denied; zero write | HR |
| B4-11 | Revoke team scope/assignment/membership or disable team | Visitor action denied; zero write | HR/Team |
| B4-12 | External identity with projected Admin role | Admin action denied; zero write | Security |
| B4-13 | Submitter with dual roles reviews own request | 403; independent reviewer mandatory | Admin |
| B4-14 | Leader has historical, unattested grant only | No team-wide edit/review authority | Owner B1 |
| B4-15 | Geocoding finishes/fails while request Pending | Pending remains; no auto-approve or auto-publish | Location |
| B4-16 | Attempt permanent delete with historical references | Denied; history and snapshots preserved | Location |
| B4-17 | Spoof teamId on shared Location/history read | No foreign notes or audit disclosure | Security |
| B4-18 | Tamper EntityKind, OperationCode, RiskCode, ProposedJson | Fail closed; cannot approve/apply unsupported payload | Admin |
| B4-19 | Duplicate SQL key vs deadlock/unrelated SQL failure | Named duplicate 409 only; unrelated errors safe failure | IT |
| B4-20 | Revoke Admin role then attempt HR/Role/Team/Project/Site bulk writes | All denied; dependent audit/history unchanged | Admin |
| B4-21 | Install enabled trigger on request/event tables in disposable fixture | Catalog readiness denied; no implicit side effect | IT schema |
| B4-22 | Malformed Rejected/Pending/Submitted event and review metadata | Trusted CHECK denies; existing audit preserved | IT schema |

## Mandatory positive business journey (separate from negative B4 count)

1. Visitor A opens location management and submits a permitted low-risk
   own-record edit. Expected direct-save with historical audit, no Pending.
2. Visitor A submits a high-risk change with clear before/after diff,
   reason and immutable expected source RowVersion. Expected Pending and
   Submitted audit; original published Location remains unchanged.
3. Admin A views own-organization Pending queue, inspects full proposed
   changes, source history, ownership, geocoding and duplicate indicators.
4. Admin A rejects with reason. Expected Rejected event and audit; published
   Location unchanged. Visitor sees reason and may submit a new request.
5. Visitor A submits a new high-risk request. Independent Admin A approves
   and atomically applies the *validated* proposal; expected one Approved
   decision and one Applied transition with source RowVersion protection,
   immutable before/after audit and historical attribution. No success if
   any dependent SQL operation fails.
6. Leader access is tested **only after** independently attested
   VAL-B1-002 grants. A seeded historical assignment is never sufficient.
7. Verify mobile and desktop UI, filters, Excel export, error handling,
   retry behavior and audit reconciliation across roles.

**Current implementation gap:** Step 5 is deliberately unavailable:
`ApproveAsync` always denies; the formal 011 migration is draft-only.
Do not mock a successful approval in a Business UAT environment.
The complete UAT cannot be opened until these are independently
implemented, security-reviewed, authorized, deployed and validated.

## Entry/exit and change-control

Entry requires exact deployed source/DB evidence, all feature and role
permissions verified, migration owner authorization, B4 22/22 formal
negative acceptance, positive journey, real account/session revocation,
UI/Excel checks and Owner + IT signoff. No partial case counts count
as formal completion. Record each case PASS/FAIL/BLOCKED/NOT RUN with
artifact URLs and issue numbers. Exit requires zero unresolved critical
authorization, integrity or cross-tenant defects; medium/low exceptions
must be explicitly accepted by Owner.

**Disposition: FULL PACKAGE B BUSINESS UAT = NO-GO.**
B3 OFF, approval/apply DENY ALL, 1800_011 NONEXECUTABLE,
VAL-B1-002 BLOCKED, B4 0/22, Business UAT HOLD,
Production HARD HOLD. Protected branch and Azure UAT/Prod unchanged.
