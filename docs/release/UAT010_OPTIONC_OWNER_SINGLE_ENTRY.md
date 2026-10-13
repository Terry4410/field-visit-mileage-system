# UAT010 × Package B Option C — Owner 單一入口（持續更新）

> **目前：HOLD / NO-GO。** 本文件是閱讀索引，不是 UAT Migration、部署、正式合併或 Package B B3/B4 放行授權。
>
> **Owner 不必管理逐批 Markdown 報告。** 以 GitHub commit + exact-SHA Actions CI 為驗證證據；工作過程的 Batch 1/2/3 報告可保留作參考，不需逐份轉交 IT。最終僅交付一份 UAT 測試手冊、一份 IT 維護／部署／復原手冊、一份正式驗收紀錄。

## 1. 單一程式來源

- Repository: `Terry4410/field-visit-mileage-system`
- Candidate branch: `work/v180-uat010-packageb-optionc-integration-r1`
- CI workflow: `.github/workflows/uat010-optionc-baseline-nondeploy.yml`（**nondeploy only**）
- Baseline: `6a07a8fb0053a1032aabe76a65aed37f40dd3c4a`，外訪員／小組長共用單筆介面，小組長保留 Excel
- 已通過的 Batch 3 exact SHA: `b08da595a74285e0e6f27e61842f9013e9ec7a92`
- Batch 3 CI: https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38349446343
- **任何新 commit 均需以新 SHA 的 CI 重新驗證，不得沿用上述 PASS 當作新 SHA 放行。**

## 2. 目前已實作與驗證（非部署）

- 共享地點介面候選、Leader live HR/role/team/代理小組長權限檢查
- 地點背景 geocode 與核准／發布分離，Excel 高風險變更不直接覆寫已發布資料
- 管理員待審 UI 候選：差異、理由、退回／核准、自行核准阻擋，**前端開關 hard OFF**
- B3 服務／atomic executor 候選：B3 HTTP controller **未掛載**、Approve/Apply **DENY ALL**
- 正式地點 identity/address 直接覆寫防護：無版本化 swap 前禁止變更
- Package B canonical `011/012` SQL 草稿來源指紋檢查及獨立 SQL Server 2022 Up/Verify/unique Pending/DecisionKey/self-review 驗證
- 前後端建置與單元測試、既有 SQL fixture PASS；**不代表完整 E2E 或 Business UAT PASS**

## 3. 未完成：下一批工程工作與解除 HOLD 關卡

| 關卡 | 完成條件 | 狀態 |
| --- | --- | --- |
| C1 發布版本 | staged version → geocode → duplicate review → 原子 publish/swap；保留歷史 snapshot | BLOCKED |
| C2 申請授權 | Visitor 與 Leader 均須 live HR/role/team/ownership；禁止代送及跨組 | BLOCKED |
| C3 獨立核准 | Admin 不得自審；決策 key 防重送；失敗／unknown commit 不盲目重試 | BLOCKED |
| C4 DB 唯一性 | 統一 `ChangeRequests` / `ChangeRequestEvents`，011/012 單一路徑；013 保留 | PARTIAL |
| C5 隔離 SQL | 真實並發、取消、交易回滾、失敗注入、歷史與 geocode/publish E2E | BLOCKED |
| C6 Business UAT | 外訪員、小組長 Excel、管理員、權限負向測試；Owner 簽核 | BLOCKED |

## 4. 絕對禁止

- 未取得 Owner **exact-SHA** 明確授權前，不執行 UAT `011/012` Migration、UAT 前後端部署或任何 Production 操作。
- 不合併至 `post-uat/v1.8.0`、`main`；不修改 Package B source branch。
- 不啟用 `PackageB:B3:Enabled` 或 `OPTIONC_APPROVAL_UI_ENABLED`；不發布 Admin approval HTTP route。
- 不將測試草稿置入正式 migration 路徑；不將 `011` 用作平行的 `LocationChangeRequests` migration。
- 不以 Markdown 報告或模擬 PASS 代替 exact-SHA CI、真實 SQL 或 Business UAT。

## 5. Owner 應做什麼？

**現在：無需動作。** 工作團隊繼續在候選分支完成 C1–C6、重跑 CI，彙整單一放行報告。只有進入正式 UAT Migration／部署前，才由 Owner 審閱合併核准單；不要求逐步回覆「請繼續」。

## 6. IT 最終交付（屆時產出）

1. **一份 IT 技術交接文件**：架構、設定、角色、資料表、部署／回復、GitHub Actions、維護方式、版本對照。
2. **一份 Business User UAT 文件**：各角色 SOP、正反向案例、缺陷追蹤、測試結果。
3. **一份放行紀錄**：exact SHA、Migration schema、CI run、實際 UAT 測試證據、Owner 核准。

原始設計與治理資料仍可在 `docs/release/UAT010_PACKAGEB_OPTIONC_CANONICAL_NONDEPLOY_CONTRACT.md`、`docs/release/UAT010_PACKAGEB_OPTIONC_MIGRATION_REGISTRY_NONEXECUTABLE.md` 與 `docs/release/optionc-canonical-sql-draft/` 追溯。
