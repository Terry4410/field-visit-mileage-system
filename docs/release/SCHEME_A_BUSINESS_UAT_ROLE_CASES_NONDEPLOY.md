# Scheme A — Role-based Browser Business UAT cases (DRAFT / NOT RUN)

**HOLD / NO-GO.** Prepared for future Owner-authorized UAT only.
Do not run against UAT/Production, migrate, deploy, enable candidate flags,
or claim Business UAT PASS. All tests require an explicitly approved target,
effective HR roles/teams and known canonical migration dependencies.

| Case | Actor / workflow | Required expected result |
|---|---|---|
| VIS-01 | Visitor Create Customer Location | Pending private identity; request/Stage/Reservation/audit; not selectable |
| VIS-02 | Visitor Create duplicate | Duplicate blocked, no orphan request/identity |
| VIS-03 | Visitor own published LowRisk note change | Saved/Applied directly through live write authorization; canonical version/event/audit advance atomically; no fabricated review |
| VIS-04 | Visitor published HighRisk address change | Pending; source remains current; server risk High |
| VIS-05 | Visitor request status | Own Pending/Rejected/Applied visible; other team's denied |
| VIS-06 | Visitor unauthorized team/HR leave | Submission and retry denied server-side |
| LEAD-01 | Independent live Leader queue | Only effective scoped pending requests visible |
| LEAD-02 | Leader self-review | Denied, including Leader-originated Create |
| LEAD-03 | Leader managed-team published LowRisk note change | Saved/Applied directly with current appointment/delegation; read scope alone denied |
| LEAD-04 | Leader approve HighRisk | Remains pending Admin/evidence; no early publish |
| LEAD-05 | Leader reject Create | Request rejected; Stage/Reservation abandoned atomically |
| LEAD-06 | Leader role/delegation revoked | Previously issued review cannot publish |
| ADMIN-01 | HighRisk independent Admin approval | Requires Leader and evidence; atomic publication |
| ADMIN-02 | Admin reject before Leader | Denied; no approval bypass |
| ADMIN-03 | Admin reject after Leader | Terminal rejected; Stage/Reservation abandoned |
| ADMIN-04 | Admin self-approval or scope revoked | Denied, no partial writes |
| XLS-01 | Excel Create-only preview + reviewed submit | Per-row Request link; no direct Location publication |
| XLS-02 | Excel Update-only published preview + submit | Preview RowVersion, canonical Low/High risk and Request link |
| XLS-03 | Excel mixed Create/Update | Explicitly denied; no direct Confirm fallback |
| XLS-04 | Excel replay/lost ACK | Existing Request IDs recovered, no duplicate audit |
| XLS-05 | Excel stale row, partial failure | Stale denied; committed rows traceable; retry safe |
| TRIP-01 | Picker/Trip/One-stop/mileage | Pending/Abandoned hidden; Approved in scope usable |
| TRIP-02 | Historical Snapshot/report/export | Original LocationId and historical displayed values retained |
| SEC-01 | Cross-org/team and HR termination | No selection, review, publication or data leakage |
| SEC-02 | Concurrent approve/reject/replay | One terminal outcome, canonical events, rollback safety |

## Prerequisites and execution order

1. Owner approves exact UAT target identification and read-only preflight.
2. Owner separately approves additive migration plan and execution window:
   canonical 011/012/014, Stage/Evidence, LeaderReview, CreateLowRisk and
   **1.8.0-EXCEL-CANONICAL-STATUS** in verified compatible order; never edit
   reserved migration 013.
3. Verify all constraints, FK, status transitions, audit union, re-run
   behavior, rollback, schema markers and feature flags OFF.
4. Separate Owner authorization for Backend/Frontend UAT deployment and
   controlled candidate flag enablement.
5. Run Browser Business UAT by Visitor, independent Leader, independent
   Admin and Excel importer; collect screenshots, RequestPublicIds, SQL
   audit, historical Snapshot/Trip evidence and final Owner sign-off.
6. Until all are satisfied, remain **NO-GO**.

These are test cases, not execution results. Test count is a planning
inventory, not an assertion that the 25 cases have passed.

## 25-case / legacy 22-case reconciliation

The Scheme A inventory intentionally remains **25 cases** (`VIS 6 + LEAD 6 +
ADMIN 4 + XLS 5 + TRIP 2 + SEC 2`). The older `OC-01..OC-22` matrix is an
Option C security trace and is not a replacement count. Mapping is many-to-many:

| Scheme A cases | Legacy OC trace | Reconciled outcome |
|---|---|---|
| VIS-01/02 | OC-01/11/12 | Create remains reviewed and duplicate/replay safe |
| VIS-03, LEAD-03 | OC-21 | Frozen policy: authorized LowRisk note saves directly with live write scope and atomic version/audit; no Leader/Admin decision |
| VIS-04, LEAD-01/02/04 | OC-01/02/05/06 | HighRisk remains pending through independent Leader then Admin |
| VIS-05/06, LEAD-06 | OC-03/04/13/15/16/20 | Own status and live revocation/scope remain fail closed |
| LEAD-05, ADMIN-01..04 | OC-06..10/14/17/18 | Reject/approve/evidence and independent Admin controls |
| XLS-01..05 | OC-11/12/19 plus Excel-specific coverage | Per-row linkage, replay, stale/partial handling; no legacy Confirm bypass |
| TRIP-01/02 | OC-22 | Operational isolation plus immutable historical references |
| SEC-01/02 | OC-07/08/11/12/13/14/15/16/19 | Cross-scope, concurrency, replay, Unknown Commit and rollback |

No case was removed to force a legacy count match. `OC-21` and the earlier
VIS-03/LEAD-03 expectations are superseded by the frozen direct-LowRisk rule.
