# Package A — report export inventory and access review

Baseline: `post-uat/v1.8.0` at `b1a507c90c577fb2c0cdd6a98ef33481d3a5f790`.

This inventory describes checked-in endpoints and queries, not permissions inferred from UI visibility. Client query parameters narrow a report; they are not authorization evidence.

## Existing download entry points

| Report / file | API endpoint | UI entry point | Data/query source | Current server gate and row scope | Column / history notes |
| --- | --- | --- | --- | --- | --- |
| Trip and mileage query | `GET /api/v1/query/trips/export.xlsx` (PDF sibling also exists) | `UnifiedQueryPage` | `V160FinalService.ExportAsync` → `V160FinalRepository.QueryTripsAsync(exportAll: true)` → `ReportDocumentService` | Any authenticated role reaches the service; supervisor additionally needs current `ExportExcel` capability. Admin is organization-scoped, supervisor is organization/read-scope scoped, leader is claim-team scoped, visitor is own-user scoped; unknown role returns no rows. | Uses current assignments for leader row scope but approved row values come from immutable trip snapshots. Current/historical leader membership intersection is not defined; see HOLD below. |
| Personnel summary | `GET /api/v1/admin/query-exports/personnel.xlsx` | People and Access / report center | `V170PeopleAdminService.QueryAsync` → `V170PeopleAdminRepository.QueryAsync` | `admin` role; repository filters to token organization. Client team/status/keyword filters narrow rows. | Employee number, name, email, employment status, hire/termination, primary team/site/center. No HR role or separate column grants exist in current code. |
| Roles and login | `.../roles.xlsx` | Roles and Login / report center | Same people query | `admin`; token organization; optional current team/status filters | Role names, actual login state and team assignments are sensitive access-control data. No column-level grant exists. |
| Teams | `.../teams.xlsx` | Team settings / report center | `IV160FinalRepository.SearchTeamsAsync` | `admin`; repository requires organization and filters it. | Team code/name/active plus current effective member count (derived). |
| Team members | `.../members.xlsx` | Team members / report center | People query | `admin`; organization-scoped; a positive `teamId` is required and query applies current membership. | Role, employment state and deployment-site fields are included. Historical membership is not exported here. |
| Location master | `.../locations.xlsx` | Location management | `V160FinalService.SearchManagedLocationsAsync` | `admin` controller gate; service applies organization and optional team filter. | Address, tax ID and master note are present. No field-level grant exists. |
| Project master | `.../projects.xlsx` | Project management / report center | `IV160FinalRepository.SearchProjectsAsync` | `admin`; repository requires organization; optional team/date/status filters narrow. | Team ID is an identifier; location count is derived. |
| Visit types | `.../visit-types.xlsx` | Visit type administration / report center | `MasterService.VisitTypesAsync` | `admin` controller gate, but source method is a global master query without organization input. | Cross-organization ownership model is not explicit. Export scope is HOLD until owner confirms global master data is intended. |
| Employment centers | `.../centers.xlsx` | Official Site Maintenance / report center | Organization-bound `V180MasterDataAdminService.ListAsync("centers")` | `admin`; source query filters by current admin organization. Keyword/status filter is applied after authorization. | Effective dates and status are included. |
| Deployment sites | `.../deployment-sites.xlsx` | Official Site Maintenance / report center | Organization-bound centers and deployment-site lists | `admin`; source lists are organization-filtered. | Center name and current Location Code are joined/derived; effective dates included. |
| Locations and official-site histories | `.../locations-official.xlsx` | Location management | Scoped managed-location query plus five organization-bound master-history lists | `admin`; managed locations are organization-filtered; official master lists are organization-filtered. | Six logical tables: location master, centers, sites, site/location history, team/center and team/site effective assignments. Must preserve table boundaries and effective-dated records. |
| Full personnel history | `.../personnel-full.xlsx` | Report download center | People query plus organization-bound history lists | `admin`; organization filter for people/history; client-selected summary columns are server-whitelisted. | Contains status, role, team, deployment, data-scope and capability history, including source IDs and reference keys. This is highly sensitive HR/access evidence. No HR-specific grant exists. |
| People/access bulk current | `GET /api/v1/admin/people/bulk/current.xlsx` | People bulk panel | `V170PeopleBulkWorkbookService.ExportCurrentAsync` | Admin-only controller and service; organization-scoped current rows. | Includes capabilities and identity/access details; operational import/export format, not a business report. Preserve unchanged pending explicit compatibility requirements. |
| Import templates / import errors | `/api/v1/imports/{type}/template`, `/api/v1/imports/{id}/errors.xlsx`, admin bulk template/error endpoints | Import panels (admin and leader location import) | `WorkbookImportService`, `V170PeopleAdminController`, `V180MasterDataAdminController` | Templates contain no business rows. Error reports are tied to an authorized import batch/requester. | Import templates are not report exports; error reports retain current schema for upload/rework workflows. Include in endpoint inventory; no bilingual change in this delta. |

No separate approval, subsidy, or mileage-rate Excel download endpoint was found. Trip/mileage results are included in the trip query export. This is based on code search for all `.xlsx` file actions and UI download calls at the baseline SHA.

## Report-by-report role × row scope × column scope matrix

This matrix distinguishes the existing authorized projection from the new export-time checks. “Columns” names only columns currently returned by the endpoint; it does not assert a separate HR field grant where none exists.

| Endpoint / report | Visitor | Leader | Admin | HR | Row scope | Column scope / status |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /query/trips/export.xlsx` and `.pdf` | Allowed, own `VisitTrips.UserId` | Current team only, but historical rule unresolved; this branch blocks past/unbounded leader downloads | Current organization, only when live profile still matches token organization | Not a distinct role in current app | Query runs server side; query filters narrow it. Empty scope stays empty. Current team membership is reloaded before export. | Same trip projection for roles: date/time, visitor, team, projects, visit types, route/notes, mileage/rate/subsidy/status/correction fields. Per-role column grants not established. Historical leader export HOLD. |
| `personnel.xlsx` | Denied | Denied | Allowed only with current active account, current HR eligibility, live `admin` role, and matching non-null organization | No distinct `hr` role | People service filters to the active admin organization; client employee/team/status filters only narrow. | Same endpoint-defined summary fields for Admin. No independently defined HR/Admin field grants; HR-role access HOLD. |
| `roles.xlsx` | Denied | Denied | Same live Admin checks and organization query | No distinct `hr` role | Active admin organization; optional team/status filters narrow. | Role list, effective login state and team assignments. Access-control fields have no per-column grants. HR access HOLD. |
| `teams.xlsx` | Denied | Denied | Same live Admin checks; repository organization filter | No distinct `hr` role | Teams in the current organization; member count is current effective membership | Team code/name/active + derived effective member count. |
| `members.xlsx` | Denied | Denied | Same live Admin checks; positive requested team is further constrained by organization | No distinct `hr` role | Current active memberships in requested organization/team | Employee number, name, roles, employment state, primary/other teams and primary site. No column grant beyond Admin module access. |
| `locations.xlsx` | Denied | Denied | Same live Admin checks and current organization location query | No distinct `hr` role | Current organization; optional team filter narrows rows | Address, tax ID and master note are included; no per-field grant exists. |
| `projects.xlsx` | Denied | Denied | Same live Admin checks; repository organization filter | No distinct `hr` role | Current organization; optional team/date/status filters narrow | Project fields and derived active location count. |
| `visit-types.xlsx` | Denied | Denied | Server now fails closed with `REPORT_EXPORT_VISIT_TYPES_SCOPE_HOLD` | No distinct `hr` role | HOLD: current source has no organization input/key | No records returned until global-versus-organization ownership is confirmed. |
| `centers.xlsx` | Denied | Denied | Same live Admin checks; organization-bound master query | No distinct `hr` role | Current organization centers; keyword/status narrow | Center key/name/effective dates and derived effective status. |
| `deployment-sites.xlsx` | Denied | Denied | Same live Admin checks; organization-bound center/site query | No distinct `hr` role | Current organization sites; keyword/status/center narrow | Site and center fields, effective dates and derived current location code/status. |
| `locations-official.xlsx` | Denied | Denied | Same live Admin checks; location and official history queries are organization-bound | No distinct `hr` role | Current organization location rows plus organization-bound history lists | Six logical data tables with their current English/Chinese labels. Bilingual conversion HOLD because separate table schemas cannot safely be flattened without a migration plan; old output remains intact. |
| `personnel-full.xlsx` | Denied | Denied | Same live Admin checks and organization-bound people/history queries | No distinct `hr` role | Current organization people plus histories for those returned employees | Highly sensitive status/role/team/site/data-scope/capability histories. Bilingual conversion and HR/Admin field grants HOLD; existing multi-sheet output remains intact. |
| `people/bulk/current.xlsx` | Denied | Denied | Admin-only organization-bound operational import workbook | No distinct `hr` role | Organization-scoped current people/access state | Not a read-only report; unchanged import/export contract. Not converted. |

## Delta implemented in this branch

- All current query-export endpoints under `V180QueryExportController` recheck active account, current HR eligibility, the currently selected role, live role assignment and unchanged organization before the report service runs. Missing, revoked, suspended or mismatched authorization fails closed. Multi-role callers must select one active role.
- Trip Excel/PDF exports reload the live profile and pass a single validated active role into the existing repository scope, so a Visitor cannot gain another employee's rows from `visitorId`, and revoked Leader team assignments no longer rely on the old token snapshot. The existing supervisor `ExportExcel`/`ExportPdf` capability check remains.
- Leader trip export with missing `StartDate` or any date before today is blocked with `REPORT_EXPORT_HISTORICAL_TEAM_POLICY_HOLD` until an owner-approved historical membership rule exists.
- Admin scope is denied when organization is missing or changed from the token. Existing organization-filtered repositories remain the row filters; client organization/team parameters are not trusted to establish scope.
- V180 single-table reports (personnel summary, roles/login, teams, members, locations, projects, centers and deployment sites) now default to three sheets: `業務查詢資料`, `原始資料`, `欄位對照表`. Each output is generated from one server-authorized row projection. A verified column catalog is mandatory; an unmapped column fails closed. The sheets share the exact same data rows, use string cells, frozen header rows, AutoFilter and explicit source/derived descriptions. `?legacy=true` retains the pre-change layout.
- Trip report audit records no longer serialize query filters/keywords. V180 query-export workbooks log only a fixed report code and row count, never report rows or filter payloads.
- Existing multi-sheet history exports keep their old workbooks and data boundaries; conversion remains HOLD pending a validated three-sheet table model and downstream compatibility check.

## Remaining release blockers / Owner decisions

1. Define HR as a live role/capability and approve exact HR/Admin/Visitor/Leader column grants for personnel, role/login, full history, location tax ID/address/note, and trip notes/subsidy fields. Current system exposes no HR-specific role or field grant.
2. Decide the historical Leader trip rule (membership on trip date, current team membership, or another effective-date policy). Past/unbounded Leader exports remain blocked until decided.
3. Confirm whether visit types are system-global or organization-owned. Their Excel export remains blocked until ownership is represented and scoped.
4. Approve a bilingual format for `locations-official.xlsx` and `personnel-full.xlsx` that preserves logical table boundaries and document existing downstream integrations; the current multi-sheet workbooks remain unchanged.
5. Confirm whether the people/access bulk-current workbook is considered a report deliverable. It is an operational import/export contract and remains unchanged.
6. Approve a bilingual layout and role-specific column grants for the trip/mileage workbook. Its existing three-sheet report format is preserved for downstream compatibility; no bilingual conversion is included until its specialized projection and column policy are approved.

## Verification limits

The development container has no .NET SDK, so local C# test execution is unavailable. CI must run against the exact pushed SHA. Disposable SQL Server is not configured in this lane; no UAT database, SQL migration, permission change, deployment, or feature flag was touched. Unit coverage was added for live-role, current-HR eligibility, organization mismatch, single-role selection, supervisor capability, workbook structure, compatibility mode, formula-like text, leading zeros, filter/freeze behavior, missing field mappings, and sensitive-free audit payload design. HTTP pipeline and SQL-backed cross-role tests remain a required exact-SHA CI/review gate before acceptance.

## Acceptance matrix

| Requirement | Result | Evidence / remaining gate |
| --- | --- | --- |
| Existing Excel endpoint/UI/data-source inventory | PASS | Endpoint inventory above; imports separated from business reports. |
| Live active/HR/role/org/team scope checks for trip XLSX/PDF and V180 exports | PASS, unit-tested | Revalidated at request and file-generation completion; changed organization/team/data scope fails closed. HTTP pipeline and SQL-backed role revocation tests remain HOLD. |
| Visitor own-row query scope | PASS, implementation-preserved | `QueryTripsAsync` applies authenticated user ID in repository; direct API integration test remains HOLD. |
| Leader historical authorization | HOLD | Past or unbounded Leader trip export blocked. Owner must approve effective-date membership rule. |
| Admin organization scope | PASS, implementation-preserved | Admin query repositories require current organization; live profile must match token. Team-specific Admin functional-grant model is not separately represented; Owner decision/review remains HOLD where narrower grants apply. |
| HR role and column-level grants | HOLD | No HR role/capability or field grant exists. Exact Owner role × column policy needed before granting these fields to HR or other roles. |
| Bilingual three-sheet output | PASS for mapped single-table V180 reports | Synthetic personnel/team workbooks generated by exact-SHA CI; source-column mappings fail closed. |
| Trip bilingual output | HOLD | Existing specialized three-sheet format preserved for compatibility; bilingual projection awaits approved column policy. |
| Multi-table bilingual output | HOLD | `locations-official.xlsx` and `personnel-full.xlsx` retain legacy layouts pending safe logical-table bilingual mapping. |
| Visit-type export scope | HOLD / fail closed | Export returns explicit scope error until global-versus-organization ownership is confirmed. |
| `legacy=true` compatibility option | PASS, unit-tested | Single-table V180 exports can retain the original single-sheet layout; multi-sheet files and trip export remain unchanged. Downstream consumer test evidence is unavailable. |
| Workbook filter/freeze/formula-injection behavior | PASS, unit-tested | AutoFilter, frozen headers, strings preserve leading zeros and formula-looking input as text. Dates/numbers also remain text as in existing exports. |
| Actual HTTP/API negative tests | HOLD | Not runnable locally; add/execute in CI or a disposable integration environment before acceptance. |
| SQL-backed authorization and historical edge cases | HOLD | No disposable SQL configured. No UAT SQL was used. |
| Exact-SHA CI and sample outputs | PENDING | Workflow uploads test results, source SHA, and synthetic workbooks after branch push. |
| Issue #41 machine handoff | HUB_PUBLISH_PENDING | Live handoff schema has no Package A lane; do not submit under another lane. |
| Deploy / merge / UAT GO | NOT AUTHORIZED | Draft PR only; this lane makes no protected-branch, database, or deployment changes. |

## Effective report × role × scope matrix (baseline)

| Report class | Visitor | Leader | Admin | HR | Column access |
| --- | --- | --- | --- | --- | --- |
| Trip/mileage query | Allowed by current endpoint; rows restricted to own user ID | Allowed; rows restricted to `CurrentUserDto.TeamIds` | Allowed; rows restricted to token organization | No distinct HR role found | Same trip columns for each role; role-specific column policy is absent. Sensitive returned/notes, visitor and subsidy fields therefore need Owner policy review. |
| Personnel, role/login, full HR history, people bulk | Denied by role middleware/service | Denied | Allowed by role check; organization-scoped | No `hr` role or HR-specific capability found | Current role check is the only column gate; cannot establish HR-only field authorization. |
| Team/member, location, project, visit type and official master | Denied by role middleware | Denied | Allowed; organization-scoped except visit types' global master source | No separate HR grant | Location tax ID/address/note and roles/access fields have no column-level grants. |
| Import templates | No data rows; route varies by import UX | Location template available to leaders | Available as configured | Not separately defined | No business-record disclosure. |
| Import error reports | Denied except authorized batch flows | Location batch errors in leader flow | Authorized batch flows | No separate HR policy | Only that batch's submitted rows/errors; verify current requester/batch ownership in its service. |

This branch revalidates active status, current HR eligibility, live roles, organization, team/data scopes, and supervisor export capability before generation and again before returning a workbook. Role scope is narrowed to one verified role. Visitor rows continue through the repository's own-user filter; leader historical trip downloads fail closed pending policy. Single-table V180 workbooks are scoped identically across all three sheets. The trip workbook keeps its existing specialized three-sheet layout pending a bilingual projection and per-role column policy. Multi-table V180 reports retain their legacy sheets; their per-sheet policy equivalence remains an acceptance gate.

## Evidence-backed HOLD decisions required

1. **HR role and sensitive columns:** Does HR become a distinct server role/capability, and which exact columns may HR, Admin and other roles export? No `hr` role or column-grant model exists at baseline. Do not grant HR access by treating `admin` as HR or vice versa.
2. **Leader historical rows:** For a trip dated in a former team, may a Leader export it after leaving that team? Is access based on membership on trip date, current membership, either, or an explicitly granted history period? The code currently uses token `TeamIds` and approved snapshots for row fields, without an approved historical authorization policy.
3. **Global visit types:** Are visit types shared system-wide or owned by each organization? The current `VisitTypesAsync` source query has no organization filter or organization key.
4. **Legacy consumers:** Existing files use report-specific sheet names and headers (including multi-sheet personnel/history and official-site reports). Consumers must either adopt the documented three-sheet workbook or request `?legacy=true` while the compatibility option remains available and verified.
