# Epic A — Query & Low-risk UX

Status: implementation complete; the pushed commit must pass the GitHub Actions gate before Epic B starts. Sole business baseline: uploaded v1.8.0 Freeze, approved in this conversation.

Gate amended by user: Impact → Coding → commit to post-uat/v1.8.0 → GitHub Actions Build/Test → next Epic only on success. Local .NET is not required. No SQL UAT connection or migration execution in Epic A.

## Scope and impact

- QRY-001–004: calendar-month shortcuts, rolling 3/6/12 months (month-end clamped), explicit custom dates; automatic filters, 400ms keyword debounce, stale-response protection; any-field trip keyword uses approved Snapshot fields for approved history.
- QRY-005–006: bounded server-side pagination for admin people, teams, projects and correction results. Team member selection must stop fetching every people page. Corrections default to pending admin close, so correction results are never loaded without an explicit condition.
- QRY-007–008: existing team code/name/status and project code/name/team/status/date search. Team effective-from/to filters depend on the new lifecycle tables and remain in Epic B; never substitute CreatedAt for business effective dates.
- PROJ-001 / VISIT-001–002 and Epic A plan: separate project/visit-type navigation, server-owned up/down ordering with stale-order conflict detection, modal close controls and mobile scroll behavior.
- Existing transaction writes, approval/correction authorization and Snapshot creation are unchanged. Submitted/approved distance/rate rules, organization/employment/site models, location governance, email, Google and Supervisor retirement remain their owning Epics.

## Files and boundaries

Frontend: App, UnifiedQueryPage, AdminPage, TeamManagementPage; new query/date/pagination/modal helpers and focused tests. Backend: query contracts, V160 repository partial and interfaces/service, a scoped query/reorder controller; shared trip keyword predicate and focused regression tests. CI: add isolated browser UI regression against local preview using synthetic API fixtures, never live UAT credentials/data.

No Domain entity, AppDbContext mapping or database file change. Reorder uses existing VisitTypes.SortOrder with an atomic transaction and AuditLogs; no migration is necessary. Retain old endpoints for compatibility; new management lists use paged endpoints. Lookup lists for unchanged transaction forms retain their existing scope and API contract.

## Verification plan

Run complete existing frontend/backend suites plus new tests for month/year/leap boundaries, query normalization, role/org scope, old-vs-current Snapshot keywords, filter-before-paging and stable order. Browser regression covers auto query/debounce, stale responses, filter/page resets, separate menus, up/down requests, mobile modal close/scroll and paged management screens. Browser fixtures prove UI behavior, not live Azure SQL or Google/email behavior.

Epic B is the first schema-dependent Epic (1800_001 then 1800_002). After Epic A passes, stop at that dependency and request the approved Azure SQL UAT access configuration; do not apply migrations.

## Pre-commit local evidence

- Frontend Vitest: 12 files, 60 tests passed.
- Frontend TypeScript/Vite production build: passed (210 modules).
- Playwright Epic A suite: 3 tests discovered successfully. The Work image has no local Chromium executable, so execution is delegated to the workflow's explicit `playwright install --with-deps chromium` step.
- Backend: not run locally because the approved process uses GitHub Actions for the .NET 8 build/test gate.
- Database: no schema/entity/mapping/migration changes and no Azure SQL connection or execution.
