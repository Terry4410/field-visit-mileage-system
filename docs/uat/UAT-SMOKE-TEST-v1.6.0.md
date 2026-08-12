# v1.6.0 UAT Smoke Test Checklist

## 1. Login / role / mobile

- [ ] visitor01 can login; mobile controls are readable and major buttons are easy to tap.
- [ ] Multi-role user can switch role; menu/data changes to active role.
- [ ] Logout/login refreshes new role/team assignments.

## 2. Visitor

- [ ] Create a one-stop trip, leave mileage blank, submit successfully; status becomes PendingApproval; mileage/rate/subsidy show N/A.
- [ ] Create a 2+ stop trip; mileage is required before submit.
- [ ] Create overlapping time; warning appears and submit requires confirmation.
- [ ] Existing location + non-project works.
- [ ] Temporary location + non-project works.
- [ ] List project + temporary location works.
- [ ] VisitType can be selected even when no project; VisitPurpose remains optional per stop.
- [ ] Draft can be deleted and disappears from normal query.

## 3. Leader multi-team / mileage / locations

- [ ] Grant leader01 two teams in Admin; logout/login leader01; both teams appear in query/review data.
- [ ] Background mileage modes work: AllPending, DateRange, Selected.
- [ ] One-stop trip is directly approvable with N/A mileage/subsidy.
- [ ] 2+ stop trip Mock mileage completes and goes PendingApproval.
- [ ] Leader can edit approved mileage before approval, then approve/return.
- [ ] Location background modes work: all pending/unuploaded, date range, selected.
- [ ] Location Excel import preview catches errors; error Excel downloads; clean file confirms.

## 4. Snapshot / correction

- [ ] Newly Approved trip creates Snapshot V1 and SnapshotStops.
- [ ] Change master Project/VisitType/Location/Team display name; Approved history/export still shows the frozen Snapshot value.
- [ ] Visitor requests non-financial correction; Leader approves; Snapshot V2 is created and original V1 remains.
- [ ] Visitor requests financial correction (approved km or date causing rate change); Leader approves → PendingAdminClose; Admin closes → V2+.
- [ ] Approved row itself is not directly edited.

## 5. Unified Query / export

For Visitor, Leader, Admin and Supervisor as applicable:

- [ ] Default period is current month.
- [ ] Date/team/visitor/location/project/visit type/status filters return correct rows.
- [ ] Pagination 20/50/100 works.
- [ ] Excel downloads **all** matching rows, not only the current page.
- [ ] PDF downloads the same matching rows/filters.
- [ ] Excel has `查詢條件`, `行程彙總`, `拜訪地點明細` sheets.
- [ ] Approved history/export uses latest Snapshot version.
- [ ] PDF Traditional Chinese text is readable, table does not overflow, totals/page number render.

## 6. Admin master data

- [ ] User roles and multiple TeamScopes save; only one primary team can be selected.
- [ ] Location create/edit/deactivate works; LocationCode is stable.
- [ ] Project create/edit/deactivate and Project Excel import work.
- [ ] VisitType create/edit/deactivate works; label is 顯示順序 and default = max + 10.
- [ ] Rate new effective date automatically shortens the previous active version; historical insertion creates non-overlapping ranges.

## 7. Supervisor / security

- [ ] Supervisor has read/export only; cannot mutate master/trip/mileage/correction.
- [ ] Leader cannot access an unassigned team by changing IDs manually.
- [ ] Admin cannot mutate another Organization by changing IDs manually.
- [ ] Visitor cannot retrieve another visitor's trip/correction.

## 8. Operations

- [ ] `/health/live` returns healthy.
- [ ] `/health/ready` returns healthy with DB available; DB failure returns 503 without exposing DB name.
- [ ] Frontend and API GitHub Actions are green.
