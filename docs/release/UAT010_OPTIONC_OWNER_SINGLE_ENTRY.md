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
- 第五批工程測試通過 SHA：`4fe153c97a73aa2d24bf898bc209e7dc74ceb5aa`；CI Run `38351366538` SUCCESS（後續文件更新須另驗證）。
- **任何新 commit 均需以新 SHA 的 CI 重新驗證，不得沿用上述 PASS 當作新 SHA 放行。**

## 2. 目前已實作與驗證（非部署）

- 共享地點介面候選、Leader live HR/role/team/代理小組長權限檢查
- 地點背景 geocode 與核准／發布分離，Excel 高風險變更不直接覆寫已發布資料
- 管理員待審 UI 候選：差異、理由、退回／核准、自行核准阻擋，**前端開關 hard OFF**
- B3 服務／atomic executor 候選：B3 HTTP controller **未掛載**、Approve/Apply **DENY ALL**
- 正式地點 identity/address 直接覆寫防護：無版本化 swap 前禁止變更
- Package B canonical `011/012` SQL 草稿來源指紋檢查及獨立 SQL Server 2022 Up/Verify/unique Pending/DecisionKey/self-review 驗證
- 前後端建置與單元測試、既有 SQL fixture PASS；**不代表完整 E2E 或 Business UAT PASS**

## 2A. 第四批增量：共用申請資格與真實併發（2026-10-13）

- **共用送審**：Visitor 與 Leader 均可在未啟用的 B3 候選服務中，為本人同組的 Approved/Active Customer Location 建立單一 `ChangeRequests` 申請；不允許替其他人、其他小組或其他組織送審。
- **Leader 即時資格**：HR 登入狀態、有效角色雙來源、有效小組／UserTeamScope／UserTeamAssignment、正式或有效代理小組長任命全部須成立。僅有 JWT claim 不足以取得權限。
- **管理員審核前重新檢查申請者**：當前 HR、Visitor／Leader 角色、小組及任命均重新驗證；撤權後的 Pending 申請不能因舊快照直接放行。
- **真實 SQL 併發**：兩條獨立 SQL 連線競爭同一 Pending slot 或 DecisionKey，唯一索引只能允許其中一筆；故意讓事件 FK 寫入失敗後必須完全 Rollback。
- **驗證證據**：[SQL 併發與回滾 Run 38350261384](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38350261384) = SUCCESS；[送審與審核者資格 Run 38350447545](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38350447545) = SUCCESS；最新提交含 UserTeamScope 強化的 [Run 38350477618](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38350477618) 須以實際結論為準。
- **仍未完成**：正式地點版本化發布／原子 swap、統一核准寫入服務與 API 啟用、完整 Visitor/Leader/Admin E2E；**不得以這批 CI PASS 宣稱 Business UAT PASS**。

## 2B. 第五批增量：版本化發布安全閘門（2026-10-13）

- **新增非部署安全閘門**：`V180OptionCVersionedPublicationGate`。僅接收未來在同一 Serializable transaction 中從資料庫重新鎖定的證據；不是 HTTP API、SQL writer、授權憑證或可用的發布功能。
- **嚴格拒絕**：來源／申請 RowVersion 過期、stage 與 request／組織／小組／地點不符、自己審核、HR／角色撤權、DecisionKey 重複、重複地點覆核未完成、歷史快照不完整，以及地址變更但 geocode 未成功／對應舊地址。
- **已新增正反向單元測試**；[Run 38351366538](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38351366538) = **SUCCESS**（exact SHA `4fe153c97a73aa2d24bf898bc209e7dc74ceb5aa`），包含 .NET、前端與隔離 SQL Server 既有測試。
- **設計契約**：[版本化發布安全規格](./UAT010_OPTIONC_VERSIONED_PUBLICATION_NONDEPLOY.md)。既有已發布地點禁止直接覆寫，B3 `ApproveAsync` 仍 DENY ALL。
- **尚未完成**：持久化 stage/version schema、正式原子 swap writer、地點歷史 FK／snapshot 完整稽核、geocode 與 duplicate evidence 的實際交易整合、Business E2E。C1 仍 **BLOCKED**。

## 2C. 最新增量：持久化 Stage 及證據版本綁定（2026-10-13）

- 本分支在原交接 SHA `2fddad733a88f50e567c66536d74a9bf007e3237` 之後已有 3 個快轉 commit，至 `c7c36e75cf86a6e336b36254f7d136be2d990088`：新增 disposable SQL Server `LocationPublicationStages`、未註冊的 `V180OptionCStageDraftWriter` 與隔離測試。僅為 nondeploy 持久化 Stage，不是正式版本切換。
- 新增 Stage 表 CHECK：成功 geocode 的地址雜湊必須等於 Stage `ProposedJson.Address`；已清除的 duplicate evidence 版本雜湊必須等於 `ProposedSha256`。這避免舊地址 geocoder callback 與其他版本的 duplicate clearance 被誤用。
- 延伸隔離 SQL 測試：舊地址／舊版本證據被拒絕；正確證據才允許 `ReadyForPublish`；原 `Locations` 仍保持 `Approved/Active`。
- **此增量的 exact-SHA CI 結論尚須以 GitHub Actions 實際執行為準；不以原有 PASS 代替。**
- **仍未完成**：真正 immutable published-version history、atomic swap、同一交易內綁定 live authorization 與 request/Location/stage locks、API/E2E 及 UAT 部署。`1800_013` 仍保留；此 Stage SQL 僅允許名稱 `OptionCIsolated_*` 的一次性資料庫，不可當作 UAT Migration 執行。

## 2D. 第六批：Published-version ledger 隔離實作（2026-10-13）

- 增加 `LocationPublishedVersions` 到**一次性隔離 SQL 草稿**；`LocationId` 穩定，版本號以 `(LocationId,VersionNumber)` 唯一、同一地點同時最多一筆 `IsCurrent=1`，每次核准 `ChangeRequestId` 不可重複消耗。
- 保留原 JSON、SHA-256、發布者及來源 RowVersion，FK 採 `NO ACTION`，避免刪除 cascade 損壞歷史參照。新增 SQL 測試：雙 Current 拒絕、單次版本前進、舊版本保留及正式地點 Approved 不受單純 ledger 操作影響。
- **非部署限制**：尚無正式 backfill、SQL ledger immutability 權限政策與跨表完整 Atomic Swap；此測試中的版本前進只操作 ledger，不能誤稱完整發布。真正 C1 仍 **BLOCKED**；`013` 保留。
- 本批新 commit 的 exact-SHA CI 必須取得實際 SUCCESS 才可標示測試 PASS；不得沿用上一版結果。

## 3. 未完成：下一批工程工作與解除 HOLD 關卡

| 關卡 | 完成條件 | 狀態 |
| --- | --- | --- |
| C1 發布版本 | staged version → geocode → duplicate review → 原子 publish/swap；保留歷史 snapshot | BLOCKED |
| C2 申請授權 | Visitor 與 Leader 均須 live HR/role/team/ownership；禁止代送及跨組 | PARTIAL：候選服務／單元測試，尚無可用 API |
| C3 獨立核准 | Admin 不得自審；決策 key 防重送；失敗／unknown commit 不盲目重試 | BLOCKED |
| C4 DB 唯一性 | 統一 `ChangeRequests` / `ChangeRequestEvents`，011/012 單一路徑；013 保留 | PARTIAL |
| C5 隔離 SQL | 真實並發、取消、交易回滾、失敗注入、歷史與 geocode/publish E2E | PARTIAL：真實 Pending／DecisionKey 競爭及回滾 PASS；其他 HOLD |
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
