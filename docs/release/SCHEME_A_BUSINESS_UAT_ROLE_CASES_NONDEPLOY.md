# Scheme A — Role-based Browser Business UAT cases (DRAFT / NOT RUN)

**HOLD / NO-GO.** Prepared for future Owner-authorized UAT only.
Do not run against UAT/Production, migrate, deploy, enable candidate flags,
or claim Business UAT PASS. All tests require an explicitly approved target,
effective HR roles/teams and known canonical migration dependencies.

| Case | Actor / workflow | Required expected result |
|---|---|---|
| VIS-01 | Visitor Create Customer Location | Pending private identity; request/Stage/Reservation/audit; not selectable |
| VIS-02 | Visitor Create duplicate | Duplicate blocked, no orphan request/identity |
| VIS-03 | Visitor published LowRisk note change | Pending; source remains current; server risk Low |
| VIS-04 | Visitor published HighRisk address change | Pending; source remains current; server risk High |
| VIS-05 | Visitor request status | Own Pending/Rejected/Applied visible; other team's denied |
| VIS-06 | Visitor unauthorized team/HR leave | Submission and retry denied server-side |
| LEAD-01 | Independent live Leader queue | Only effective scoped pending requests visible |
| LEAD-02 | Leader self-review | Denied, including Leader-originated Create |
| LEAD-03 | Leader approve LowRisk | Duplicate evidence and controlled apply required |
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
