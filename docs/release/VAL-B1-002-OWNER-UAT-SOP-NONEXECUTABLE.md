# VAL-B1-002 Owner Business UAT SOP — NOT RUN

Prerequisites: separately approved UAT deployment of the exact WORK SHA, authorized 1800_013 candidate execution, synthetic accounts, and B3 OFF. This document does not authorize any of them.

1. Admin signs in with an active HR status and current Admin role, opens **小組與成員 → 小組長任命與代理**, and creates an `Active / FormalAppointment / AdminMaintained` record with evidence reference for synthetic Leader A and Team A.
2. Leader A opens 地點管理. The server returns only Team A from `leader-authorization`; create a Pending, inactive Customer Location. Capture audit evidence.
3. Leader A updates a peer-created Pending Customer draft in Team A using the current RowVersion. Confirm low-risk save and audit event.
4. Leader A attempts Team B, cross-organization, Official/global, active/published, owner/team/type change, and stale RowVersion operations. Each must be denied or held for B3; no direct high-risk mutation.
5. Admin creates a bounded delegation from Leader A's trusted appointment to synthetic Leader B. Leader B performs the Team A low-risk path; a delegation outside the appointment period/scope must fail.
6. Admin revokes the appointment or delegation. On Leader A/B's next sensitive write, server-side revalidation denies access without relying on the old JWT/UI state.
7. Use a historical/backfilled Leader record with no `AdminMaintained` evidence. Confirm it remains denied.
8. Repeat Visitor own-draft create/update and peer-draft denial.

Expected invariant: B3 remains OFF, public `ApproveAsync` DENY ALL, no formal B4 scenario is marked complete, and all UAT/Production actions remain separately controlled.
