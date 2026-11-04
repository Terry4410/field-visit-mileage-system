# Path B — Solo-Owner UAT execution playbook (2026-11-04)

**Status: PREPARED, NOT EXECUTED.** This document converts the canonical 25-case tracker into one-person execution steps. Original IDs/meanings and original checksum-covered source documents are unchanged. It is not a substitute for a live test, evidence, SQL installation, approval or production sign-off.

## 0. STOP gate — do not release the UAT URL before all are independently verified

- [ ] Exact Path B database is proven to contain required 69 tables and precisely three SchemaVersions markers; constraints, triggers and schema fingerprint checked.
- [ ] Migration was completed by a separately authorized exact-target one-shot installation. Any unknown/partial state is HOLD, never replay.
- [ ] Application runtime identity has reviewed object-scoped ACL; broad schema/DDL roles are absent.
- [ ] Approved matching API and Frontend binaries are deployed to verified Path B endpoints, with health/ready/version and Browser→API→SQL successful.
- [ ] Three unique **Demo** Visitor/Leader/Admin test actors are working with appropriate Organization/Team and effective-date fixtures; each passes an authorized action and denied action.
- [ ] Google Routes live success and explicit failure/manual fallback can be exercised without exposing API keys.
- [ ] Backup/PITR restore decision path, emergency stop and evidence capture method are known.
- [ ] SQL public access remains disabled; no production or legacy UAT target is used.

If any item is not evidenced, record `NO-GO` and continue *offline* preparation/IT pre-UAT handover only. These are gates, not claims of completion.

## 1. One-person setup (after GO)

- Reserve a dedicated UAT session and use three **nonproduction** test accounts. Suggested display labels only: `pathb-visitor01`, `pathb-leader01`, `pathb-admin01`. Never place passwords, tokens, connection strings or personally identifying employee data in GitHub.
- Use a clearly disposable test Organization with Team A and Team B, two Visitor fixtures as needed for ownership checks, one Leader whose Team permission has effective dates, and an Admin who cannot approve own high-risk request. Provisioning is **not completed** by this document.
- For each case, note test date/time (Asia/Taipei), sanitized actor ID, synthetic data ID, expected outcome, observed outcome, evidence link, tester, result, defect link and retest result.
- Status vocabulary: `NOT RUN` (no real attempt), `PASS` (actual observed expected behavior with link), `FAIL` (attempted with contradicting result), `BLOCKED` (dependency prevents execution). Do not infer PASS from CI or synthetic tests.
- Screenshots must redact login secrets, API keys, private connection details and real employee/business data; store any private evidence in an access-restricted Owner/IT channel.
- Test in order: smoke / Visitor / Leader / Admin / Excel / complete lifecycle / iPhone / security; reuse disposable synthetic fixture IDs for traceability.

## 2. Canonical 25-case execution matrix

| ID | Actor | Owner action | Required observed outcome | Evidence to capture | Initial result |
| --- | --- | --- | --- | --- | --- |
| VIS01 | Visitor | 登入、導覽、建立/修改/保留草稿 | 草稿僅本人可存取；重登入狀態正確 | 登入頁、草稿 ID、本人清單 | NOT RUN |
| VIS02 | Visitor | 建立多站行程，使用 Google Routes 真實成功回傳 | 站點順序/路線與里程資料可確認 | 路線畫面、服務成功記錄（不含金鑰） | NOT RUN |
| VIS03 | Visitor | 觸發路線服務失敗/不可用，走明示人工處理 | 可明確選擇人工備援，非假裝 Google 成功 | 失敗提示與人工路線結果 | NOT RUN |
| VIS04 | Visitor→Leader | 主管退回後修改並重新送出 | 退回原因、版本/審計與狀態更新一致 | 退回與重送畫面、行程 ID | NOT RUN |
| VIS05 | Visitor | 使用核准據點與暫存地點 | 清楚區分地點權限及所有權 | 選單/地點結果 | NOT RUN |
| VIS06 | Visitor | 檢視自己的歷史；嘗試他人資源 | 只顯示自己的資料；越權被拒 | 歷史與拒絕畫面 | NOT RUN |
| LEAD01 | Leader | 開啟有效授權 Team 待處理清單 | 僅看到授權隊伍項目 | 隊伍清單與範圍 | NOT RUN |
| LEAD02 | Leader | 處理一筆已送出里程 | 里程計算與審查欄位一致 | 處理前後狀態與里程 | NOT RUN |
| LEAD03 | Leader | 做出最後里程決定並核准 | 有權限者成功，狀態及核准者留痕 | 核准結果 | NOT RUN |
| LEAD04 | Leader | 退回一筆並填寫原因 | 訪員可見原因，操作進歷史 | 退回原因/歷史 | NOT RUN |
| LEAD05 | Leader | 驗證生效日內與生效日外 Team 歷史 | 僅有效期間授權可見 | 有效期間測試結果 | NOT RUN |
| LEAD06 | Leader | 嘗試跨 Team 操作及並發修改同筆資料 | 越權拒絕；衝突不得靜默覆蓋 | 拒絕與衝突結果 | NOT RUN |
| ADMIN01 | Admin | 新增地點，驗證重複警示 | 地點建立與重複提醒符合規範 | 新增及警示 | NOT RUN |
| ADMIN02 | Admin | 合併地點，查看歷史參照 | 合併後既有引用/稽核歷史保留 | 合併前後記錄 | NOT RUN |
| ADMIN03 | Admin | 送出需管理核准的高風險變更 | 只有適當審核者能批准 | 申請/審核狀態 | NOT RUN |
| ADMIN04 | Admin | 嘗試核准自己提出的高風險申請 | 自我核准拒絕且留存證據 | 權限拒絕 | NOT RUN |
| XLS01 | Admin | 匯入有效 Excel 並預覽 | 預覽筆數、欄位與驗證結果正確 | 預覽摘要 | NOT RUN |
| XLS02 | Admin | 完成允許的匯入並追溯來源 | 成功/失敗筆數與匯入來源可追蹤 | 執行結果/追蹤 ID | NOT RUN |
| XLS03 | Admin | 匯入無效和部分無效 Excel | 錯誤可辨識；部分成功/整批拒絕符合契約 | 錯誤摘要及結果 | NOT RUN |
| XLS04 | Admin | 匯出指定查詢結果 | 資料與查詢/範圍一致且不外洩 | 匯出筆數/欄位 | NOT RUN |
| XLS05 | Visitor/Leader | 嘗試不被允許的匯入與匯出 | 權限拒絕，跨範圍資料不出現 | 拒絕提示 | NOT RUN |
| TRIP01 | Visitor→Leader | 完成建立、送審、核准/退回、重送完整流程 | 狀態、里程、歷史前後一致 | 同一行程 ID 的事件序列 | NOT RUN |
| TRIP02 | Visitor | 實際 iPhone 使用、輸入、導航與錯誤復原 | 手機關鍵操作可完成，錯誤狀態可復原 | 裝置/瀏覽器版本與畫面 | NOT RUN |
| SEC01 | 三角色 | 測試角色、Organization、Team 隔離 | 跨角色及跨組織一律依權限拒絕 | 允許/拒絕對照 | NOT RUN |
| SEC02 | 三角色 | 歷史、並發、稽核完整性 | 沒有靜默覆寫；歷史可追溯且不可越權讀取 | 測試資料 ID、衝突/歷史結果 | NOT RUN |

The matrix is a **preparation guide**, not a change to canonical acceptance criteria; the canonical source remains `BUSINESS-UAT-25-TRACKER.md`. A negative security test must actually be denied to PASS.

## 3. Result template (one entry for every attempted case)

```text
Case ID:
Execution time (Asia/Taipei):
UAT URL/environment binding (nonsecret):
Actor label / role / Organization / Team (sanitized):
Synthetic test-data ID:
Expected:
Actual:
Result: NOT RUN | PASS | FAIL | BLOCKED
Evidence link (restricted):
Defect issue link (if applicable):
Retest time / result:
Tester:
```

## 4. Stop / retest / exit criteria

- Stop the relevant workflow immediately if isolation breaks (Visitor sees others' data; Leader crosses Team; Admin can self-approve), if data integrity fails, if the wrong SQL target is involved, or if an action suggests a repeated/partial schema migration. Record FAIL and defect evidence, no improvised fixes in the live database.
- A failed mandatory test cannot be counted as success. A blocked test remains BLOCKED. Retest after an approved fixed binary is deployed and note the changed release binding.
- **Business UAT completed** requires all 25 canonical IDs independently executed and evidenced as PASS against one attested, correctly paired UAT release, with no open release-blocking defects. A separate explicit Owner sign-off is required. No automatic IT production approval.
- **Pre-UAT technical IT handover** can occur independently with approved source, schema files, accepted binaries, manifest/custody, SOP, known gaps and this unexecuted tracker. Label explicitly PRE-UAT / runtime NOT ACCEPTED.
- **Final IT handover** adds as-built SQL fingerprint/ACL, exact paired binary hashes, test outcome/sign-off, secret ownership transfer and versioned full-document checksum manifest. Enterprise Entra SSO remains receiving IT scope.

## 5. Solo-Owner priority and delegated execution boundary

1. Use the previously approved one-attempt App-MI read-only probe only from a verified authenticated existing container channel, after exact-script byte binding and staging are confirmed. The prior documented attempt count is zero; reconcile other sessions first. No improvisation or rerun on UNKNOWN.
2. Fresh catalog/marker state, appropriate distinct migration identity and viable secure original sealed installer transport must be verified before **separate** DDL Owner approval.
3. Once schema/ACL/paired deployment and three-role smoke pass, execute this 25-case matrix as a single business session.
4. Independently prepare PR #85 for pre-UAT source/schema handover in parallel. Do not require UAT PASS to hand over honestly labeled technical assets.

### Integrity and write boundary

This new supplementary file is outside original `checksums.sha256` and older supplemental file index; refresh a full versioned integrity manifest for final IT handover. This is **documentation-only**, no Azure/SQL/identity/config/code/deploy/CI/PR merge authorization.
