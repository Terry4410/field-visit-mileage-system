# UAT010 × Package B Option C — Owner 單一入口（持續更新）

## 2026-10-14 — Excel published Location Update candidate (Recovery-only)

- Verified starting baseline: `01e9403afa7e01dce67f62a556f800d3dc0e1b6e`, exact-SHA CI `38587007376` SUCCESS. **This is not a claim that the new Excel Update increment passed CI.**
- New candidate-only `POST /api/v1/optionc-candidate/imports/{id}/submit-updates-for-review` consumes actual Excel/CSV preview **published Update** rows with preview-bound RowVersion. Existing Excel **Create** review flow is preserved.
- Reuses the **same** Serializable canonical submission, server-owned risk classification, private Stage, Submitted audit and independent Leader / HighRisk Admin publication. The ImportBatchItem and canonical audit link are committed together; audit-backed replay recovers RequestPublicIds. Legacy Confirm stays fail-closed.
- Mixed Create/Update batches **explicitly fail closed**. A Leader-owned Location may be updated by its authorized owner; a non-owner Admin cannot silently impersonate an owner. Cross-owner Excel bulk Update is not implemented.
- New UI action is separately gated by `OPTIONC_EXCEL_UPDATE_UI_ENABLED=false`. The additive `1.8.0-EXCEL-CANONICAL-STATUS` constraint is required **before** candidate Excel submission; draft SQL only, no UAT/Production migration. Migration 013 untouched.
- Remaining tests: full Update multi-row/partial-failure matrix, stale preview/revocation, Trip/mileage/report/export route audit, and final exact-SHA SQL E2E. **HOLD / NO-GO**; no UAT target preflight, deploy, flags or Business UAT.
- Detailed contract: `docs/release/SCHEME_A_EXCEL_PUBLISHED_UPDATE_NONDEPLOY.md`.


> **目前：HOLD / NO-GO。** 本文件是閱讀索引，不是 UAT Migration、部署、正式合併或 Package B B3/B4 放行授權。
>
> **Owner 不必管理逐批 Markdown 報告。** 以 GitHub commit + exact-SHA Actions CI 為驗證證據；工作過程的 Batch 1/2/3 報告可保留作參考，不需逐份轉交 IT。最終僅交付一份 UAT 測試手冊、一份 IT 維護／部署／復原手冊、一份正式驗收紀錄。

## 2026-10-14 — Scheme A CreateLocation／LowRisk 原子發布增量（Recovery only）

- 第一批 Create/LowRisk 契約與 SQL rehearsal 的 exact-SHA `b15158b3923b1e843230e1e7d8178f4a5ee0622b`／CI `38523928698` **SUCCESS**。
- 第二批 isolated-only atomic writer、獨立 Leader/HighRisk Admin 核准、版本化帳本與 Trip/Snapshot 歷史不變性，exact-SHA `184eafd268602861d3b1eee2b01edc9e6eaa5fa2`／CI `38524764115` **SUCCESS**。
- 第三批新增 candidate-only `publish-reviewed` HTTP route 與同一 Serializable transaction 的 live HR／角色／組織／小組／任命／代理資格重驗。CreateLocation High 需獨立 Leader + Admin；UpdatePublishedLocation Low 由獨立 Leader 最終發布；來源 RowVersion、BeforeJson、Stage evidence hash、DecisionKey 與 audit event 均受檢核。
- Disposable SQL E2E 增加 HTTP/DI 正向 Create/LowRisk 發布及 Leader 撤權、Admin 不得繞過 LowRisk Leader、BeforeJson 偽造等負向測試。**本批最新 exact-SHA CI 尚須以 Actions 結論確認；未經 PASS 不得宣告完成。**
- 本批 HTTP 測試的 canonical request 與 stage 仍由隔離 SQL fixture 建立；**完整 Create/LowRisk HTTP 提交、地點保留、證據服務與 UI 串接未完成**。Excel／legacy direct mutation／background publication 的 canonical routing 未完成。
- **整體 Scheme A = HOLD／NO-GO**；未授權／未執行 UAT target preflight、migration、deployment、flag activation、Business UAT、promotion／merge；migration 013 保留。

詳細契約：`docs/release/SCHEME_A_CREATE_LOWRISK_CONTRACT.md`。

## 2026-10-14 — Scheme A 第四批：LowRisk Visitor → Leader 非部署流程

- Recovery 分支增量：Server-owned published-location risk classification；LowRisk pending request + Stage (`NotRequired` geocode, pending duplicate evidence)；Leader live HR/appointment first review；同一獨立 Leader 的 duplicate evidence；候選 HTTP `publish-reviewed` 原子發布。
- Leader Reject 於同一 Serializable transaction 將 Stage 改為 Abandoned；若為 CreateLocation 同步將 reservation 改為 Abandoned，避免被拒絕申請留下可發布 Stage。
- 隔離 SQL Server 2022 HTTP/DI 測試：Visitor 真正送審 → Leader 初審 → evidence → LowRisk 發布；拒絕 premature evidence／Admin 替代／Leader 撤權；Leader 拒絕時 Stage 原子 Abandoned；歷史 Trip/Snapshot 不變。
- LowRisk Unknown Commit 觀測改由原獨立且即時有效的小組長查詢；Admin 不得冒充 LowRisk 決策者；觀測僅回傳證據狀態，絕不自動重試 COMMIT。
- **本批 exact-SHA CI 需獨立確認，先前 PASS 不可套用。**
- **尚未完成**：CreateLocation 真正 HTTP reservation/submit/evidence、Pending Location 全查詢排除、Leader/Visitor 正式 UI 串接、Excel/legacy/background bypass 清理。UAT migration、deployment、flag activation、Business UAT、promotion 全部未授權／未執行；**HOLD / NO-GO**。

## 2026-10-14 — 第五批：高風險最終駁回與 Merge 相容性（Recovery only）

- 前一批修正 `cc6342c5cdfaaf734c5ec72b07f052e5feea56b2` 的 exact-SHA CI `38527277591` 已 **SUCCESS**。
- 新增候選拒絕流程安全控制：LowRisk 不得經 Admin 通用 reject API 繞過獨立 Leader；一般 HighRisk Create/Update 需有同一申請的 LeaderApproved 決定明細與 Audit Event，且於同一 Serializable transaction 重驗 Leader HR/角色/小組任命及獨立 Admin 資格。
- CreateLocation 高風險最終駁回：同交易將 `LocationPublicationStages` 與 `LocationCreateReservations` 標為 `Abandoned`，不可保留可發布的 pending reservation。
- MergeFields 仍保留原獨立 Admin 駁回契約；以 `LocationMergeDetails` 辨識，不可藉 Merge 名義繞過正常 Create/Update 核准。加入低風險 Admin bypass 拒絕及高風險 Leader-before-Admin 拒絕 HTTP 測試。
- **本批 Exact-SHA CI 尚待完成，不宣告工程 PASS。** CreateLocation 真實 HTTP 送審、Pending Location 全查詢隔離、Excel／legacy／background canonical routing 仍為阻礙；UAT/Production 全面 **HOLD／NO-GO**。

## 2026-10-14 — 第六批：低風險 Legacy MasterNote 直接寫入封鎖（Recovery only）

- Read-only path audit found `V170LocationRepository.UpdateMaintenanceAsync` still permitted published `MasterNote` direct update, bypassing Scheme A LowRisk independent Leader review and versioned publication.
- Recovery-only security correction: published identity/address HighRisk remains blocked; published note-only LowRisk now also fails closed with `LOCATION_LOW_RISK_CHANGE_REQUIRES_OPTIONC_LEADER_REVIEW` before any Location mutation. Non-published draft maintenance and separate team-specific note contract are unchanged.
- Added policy and source-order tests to prevent silent reintroduction of the direct published MasterNote bypass.
- Previous CI `38527806267` **FAIL** because a newly extended HTTP rejection test omitted isolated Leader Review schema; test setup corrected by installing the disposable Leader schema and live-appointed reviewer.
- **Newest exact-SHA CI still pending; do not mark PASS.** CreateLocation HTTP workflow, Excel/legacy/background full routing, UI, UAT target preflight and Business UAT remain **HOLD / NO-GO**.

## 1. 單一程式來源

- Repository: `Terry4410/field-visit-mileage-system`
- Candidate branch: `work/v180-optionc-final-gates-recovery-r1`
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

## 2B. Leader first-level review增量（Recovery `2ce6108f9e091c32ea3a6099a9040196bb0a1e6c`；CI `38518256406` SUCCESS）

- Visitor 的地點變更由 server-owned LeaderReview service 建立 `LeaderFirstLevel` 詳情；Leader reviewer 必須 live HR、有效 leader role、UserTeamScope/Assignment 及正式/代理任命均有效，且同組織/小組、不得自審。
- 高風險案件在 Admin atomic publication 的同一 Serializable transaction 內要求已核准的獨立 Leader review；沒有 review、撤權、RowVersion 不一致或 DecisionKey replay 均 fail closed。
- additive `ChangeRequestLeaderReviews` 僅為 disposable candidate，依 011 → 012 → 014 → leader-review 順序；migration 013 未修改，所有 flags 仍 OFF。

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

## 2E. 最新交易回滾負向測試增量（2026-10-13）

- 已加入隔離 SQL Server 實際交易故障注入案例：先將版本 ledger 暫時切換，再刻意使用不存在的 `ChangeRequestId` 寫入 `ChangeRequestEvents` 觸發 FK 失敗，驗證整筆 transaction rollback、先前 Current 版本及 Pending Request 保留。
- 此為 **ledger + failed audit 的真實 SQL rollback 測試**，**不是**完整包含 `dbo.Locations`、stage、live admin authorization 與正式核准狀態的原子發布測試。切勿推論完整 Atomic Swap 已完成。
- 新 SHA 的完整 nondeploy CI 尚待 GitHub Actions 實際結論；未驗證前 C1/C3/C6 持續 HOLD。

## 2F. 回滾測試修正：避免唯一索引假陽性（2026-10-13）

- 已修正前一版故障注入測試：原本同一 `ChangeRequestId` 直接插入第三版可能先觸發已核准申請唯一索引，無法證實 Audit FK 失敗回滾。
- 新測試在同一 Serializable transaction 實際修改 ledger 的 current 標記、stage 狀態、source Location 狀態和 Request 決議欄位，**再**使用不存在的 request FK 插入 audit，預期 SQL 失敗後全部 rollback。
- 交易結束後驗證 ledger Current/版本筆數、Stage Ready、Location Approved、Request Pending、Audit 0 筆。這是正確的跨表回滾隔離測試，仍非可用的核准／發布 API。
- 新 exact-SHA CI 結果尚待確認，未通過前維持 C1/C3/C6 HOLD。

## 2G. 第七批：隔離 Atomic Publication Writer 候選（2026-10-13）

- 新增 `V180OptionCAtomicPublicationWriter`：**internal、未註冊、無 HTTP route、只允許 `OptionCIsolated_*` 一次性 SQL 資料庫**；由呼叫者提供 Serializable transaction，不自行提交或盲目重試。
- 單一 SQL batch 遵循 Request → Location → Stage → Ledger 鎖序，驗證 Pending、高風險、組織／小組／建立者、來源 RowVersion、staged JSON 與 geocode/duplicate 版本證據、admin 與 requester 不同、DecisionKey 唯一。
- 同一交易內切換 ledger Current，更新同一 `LocationId` 的正式欄位，插入新版、標記 Stage Published、Request Applied，產生 Approved/Applied 兩筆 canonical audit。
- 新增隔離 SQL Server success-path 測試與 replay-denial 測試；**須以 exact-SHA CI 實際結果為準**，目前不可推論 PASS。
- **未完成的安全關卡**：外部即時 HR/角色/Leader 任命驗證仍須由未來 orchestrator 在鎖定交易中取得，而非接受 client boolean；immutable stage mutation 和 ledger 回填、歷史 Snapshot invariants、實際 geocoding callback 與 duplicate reviewer 的授權整合、unknown commit 查證及 Admin E2E 尚未完成。此 Writer 不是可上線的 B3 API，C1/C3/C6 仍 HOLD。

## 2H. Atomic Writer 負向授權測試（2026-10-13）

- 新增獨立隔離 SQL 測試：申請人即時權限失效、管理員即時權限失效、審核者與申請者同一人，均必須拒絕，不得提前修改 Request／Stage。
- 此批測試只驗證 writer 的拒絕分支；傳入的 live-auth 旗標並非真正的 DB HR／role 查詢，不能用以放行正式 Admin Approval。真正的權限 orchestrator、unknown commit reconciliation、完整 E2E 仍為 BLOCKED。
- 須以最新 exact-SHA 的 GitHub Actions CI 結論核實，先前成功的 SHA 不能作為本批放行依據。

## 2I. Atomic Writer 鎖內資料一致性加強（2026-10-13）

- 於 request-first／source／stage 鎖定後增加發布前資料驗證：Stage 的 `ProposedSha256` 必須完全等於 canonical `ChangeRequests.ProposedJson` 的 SHA-256，且正式發布必需有非空白 `LocationName` 與 `Address`。防止缺失身分資料或跨版本 stage 被寫到 `Locations`。
- 僅修改 unregistered、isolated-only Writer；不新增 API、不打開 B3 功能、沒有 UAT Migration／部署。
- 注意：仍缺少 transaction-bound live HR/role/team orchestrator 及 authoritative geocode reviewer 整合，B3 與 Business UAT 繼續 HOLD。新 exact-SHA CI 結論須另行確認。

## 2J. 隔離即時授權 Orchestrator 候選（2026-10-13）

- 新增內部未註冊 `V180OptionCAtomicPublicationOrchestrator`，僅允許 `OptionCIsolated_*` 資料庫；透過既有 `V180B3LiveAuthorizationRead.EvaluateActorAsync` 驗證審核管理員 live HR / role，並以 `IsSubmitterTeamStillValidAsync` 重新驗證原申請者之 HR、角色、小組及任命。
- 所有判定在同一個 Serializable EF/SQL transaction 內執行，先鎖 canonical request，再呼叫隔離原子發布 Writer。審核不得由外部任意輸入 `liveRequesterAuthorized`／`liveIndependentAdminAuthorized`。
- **未達正式安全驗收**：仍需確認實際 SQL fixture、正確的 SQL 鎖序、並行撤權、unknown-COMMIT 新連線查證、完整 migration backfill、Location Snapshot 與 end-to-end API。未註冊到 DI／controller；B3 DENY ALL；UAT／Production HOLD。
- Exact-SHA CI 尚待實際結論；不可將新增檔案等同成功驗證。

## 2K. Atomic Writer 跨組織 SQL 負向案例（2026-10-13）

- 隔離 SQL fixture 新增使用錯誤 OrganizationId 核准同一 canonical Request 的拒絕案例；交易 rollback 後核對 Request Pending、Stage ReadyForPublish、PublishedVersion 保持 version 1。
- 測試於實際 disposable SQL Server 執行；新 exact-SHA CI 未回報成功前不得宣稱 PASS。
- 仍未完成並行撤權、Unknown Commit 新連線驗證與完整 Admin/Visitor/Leader E2E；所有 UAT Migration／部署／B3 feature flag 均 HOLD。

## 2L. Unknown Commit 唯讀查證候選（2026-10-13）

- 新增 `V180OptionCUnknownCommitReconciler`：僅隔離 SQL，可於 Commit acknowledgement 遺失時以 **全新、不共用 pool** 的 SQL connection 唯讀檢查 canonical Request、DecisionKey Approved Event、Applied Event、Current Version、Stage Published。
- Orchestrator 捕捉可能的 unknown Commit 時回傳帶有證據分類的例外：`COMMITTED_EVIDENCE_REQUIRES_REVIEW`、`NO_COMMIT_EVIDENCE_REQUIRES_REVIEW` 或 `INDETERMINATE`；**三者均不得自行重試或向使用者宣稱發布成功**。即使原請求已取消，也以獨立 CancellationToken 查證。
- 此為未註冊 nondeploy 候選，尚缺實際 Commit 故障注入及證據完整性（包含跨版本、來源欄位和 correlation）驗證，不可作為 UAT 發布放行依據。
- 仍未實作正式 Migration、Admin API/E2E、並行撤權交易測試，B3、UAT、Production 維持 HOLD。

## 2M. Exact-SHA CI 確認與 WORK 交接紀錄（2026-10-13）

- 已確認 **遠端候選 SHA `c9d2149f1b0bed52ec326af5fbec6a9039ae65c2` 的 nondeploy GitHub Actions Run `38355122734` = SUCCESS**。該 Run 驗證的是這一個 SHA，而不是後續新 commit。
- WORK 曾於本機產生僅變更本文件的 commit `82920c46609f53c8cad8ce9f7d18b61bd2089818`（parent 為 `c9d2149...`），因 HTTPS 認證無法 push；其本機 commit / tree SHA 不能冒充為遠端同步結果。原始 bundle `UAT010-OptionC-82920c4.bundle` 留存於原 WORK 環境。
- Control Tower 使用具備 GitHub 寫入權限的連線，於本候選分支直接補入本節作為等效狀態紀錄，**這不是匯入原 bundle，也不是原 `82920c4` commit**。
- 截至 WORK 回報：既有 Run 涵蓋 Build、Backend tests、Frontend tests/build、disposable SQL、011/012 isolated rehearsal；不能據此聲稱 Admin API、正式 Atomic Swap/E2E 已完成。
- **NO-GO**：C1、C3、C6 仍 BLOCKED；正式 Migration delta=0；011/012 尚未授權 UAT 執行；保留 `013`；B3 OFF、B4 HOLD、UAT/Production 未部署；本文件更新產生的新遠端 SHA 必須另外以 exact-SHA CI 驗證。

## 2N. `8878aa5` exact-SHA CI 結果（2026-10-13）

- 遠端候選 HEAD：`8878aa5298dd950fc7602cd7130ce95f1b357e45`；parent：`c9d2149f1b0bed52ec326af5fbec6a9039ae65c2`。
- Exact-SHA nondeploy CI：[Run 38356043677](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38356043677) = **SUCCESS**；`provenance-and-build` 單一工作及其所有 mandatory steps 均 SUCCESS。
- 驗證內容包含：exact-source/nondeploy guard、011/012 static provenance、Frontend npm/test/build、Backend Release build/tests、disposable SQL runtime fixture、011/012 isolated Up/Verify/replay rehearsal。
- Run 無 artifacts，且 workflow 明確維持 Release NO-GO；此結果不代表正式 Atomic Publication、Admin API/E2E、UAT Migration 或 Business UAT 已完成。

## 2O. Option C blocker-resolution batch（2026-10-13）

- 新增 `V180OptionCPublicationCatalogRules`：Atomic Publication orchestrator 在任何 transaction/DML 前，必須以唯讀 system catalog 驗證 ChangeRequests、ChangeRequestEvents、Stage、Published Version 的必要欄位、唯一索引與 trusted CHECK constraints；僅資料庫名稱符合 `OptionCIsolated_*` 不再視為 schema 證據。
- 新增 `1800_014` **NONEXECUTABLE DRAFT**：Stage／Published Version ledger、穩定 `LocationId`、歷史版本保留、Current／ChangeRequest 唯一性、Proposal／Address／Duplicate evidence hash binding、Preflight／Verify／Recovery。`013` 保留，正式 `database/migrations` 未變更。
- 新增 014 provenance/安全邊界測試；本機 Python CI static tests：4 tests PASS。
- GitHub connector 以原始 parent `d3a47b1ee2681f54a237bb2d5bc1d9eb0fd741ba` 與相同 Tree `5a10c9a458556e8b8fefd391c0b65a71e00d1a60` 重建並發布；遠端提交為 `a2cf11b0e8791cc38c422a0ca6ccc976d29eb513`（原本本機提交 `006a4764e2f34face6cf47f8433d067ee3acfa5b` 的來源內容與 Tree 相同，SHA 因 Git metadata 不同）。
- Exact-SHA nondeploy CI [Run 38360812533](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38360812533) = **SUCCESS**；`provenance-and-build` job 及其 4 項 Python static、Frontend npm/test/build、Backend build/tests、disposable SQL、011/012 isolated rehearsal、NO-GO boundary steps 均 SUCCESS；Run 無 artifacts。
- 本批尚未完成正式 UAT schema backfill、公開 API、Admin E2E 或 Business UAT；CI 證明 build/test 與隔離 rehearsal，不等於正式 Atomic Swap／Admin approval／Business UAT。C1/C3/C5/C6 仍不可標示完成。

## 2P. Option C P0 runtime completion batch（2026-10-13）

- 新增 `1800_014/Backfill.sql` 非執行草稿：只在 `OptionCIsolated_*`、最新 `1.8.0-014` 的 disposable DB 執行；只將 `Approved+Active` Location 以明確欄位順序回填 version 1，保留穩定 `LocationId` 與 source RowVersion。
- Backfill 在任何寫入前拒絕缺少名稱／地址／建立者／RowVersion、組織／小組／使用者參照不存在、已部分回填或競態重跑；Pending/Inactive drafts 不進入 published ledger。
- 新增隔離 SQL runtime test：Preflight → 014 Up → Verify、ambiguous-source rollback、deterministic backfill、rerun denial、current/version uniqueness、catalog guard、FK rollback 與 LocationId preservation。
- 強化 `V180OptionCPublicationCatalogRules`：檢查 index unique/filter 定義、014 必要 FK trusted 狀態，以及必要 CHECK constraints trusted 狀態。
- Exact-SHA candidate `63f9b4aef4987837637c3ac6aa3e776a9bf1849f` 的 [Run 38362702475](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38362702475) = **SUCCESS**：Frontend 22 files / 99 tests PASS；Backend 701/701 PASS；014 runtime test `OptionC014_runtime_preflight_up_verify_backfill_and_catalog_guard_are_fail_closed` PASS；disposable SQL fixture 5/5 PASS；canonical 011/012 rehearsal 1/1 PASS；所有 nondeploy/NO-GO guards PASS。
- 014 runtime 已實際驗證 Preflight → Up → Verify、ambiguous source fail-closed、deterministic version-1 backfill、Pending/Inactive 排除、rerun denial、stable LocationId、current/version uniqueness、catalog index/FK/CHECK trusted contract 與跨表 rollback。CI 無 artifacts。
- C5 的 014 runtime 子關卡 **PASS**；但完整 C5 仍為 **PARTIAL / HOLD**，因完整公開 API/E2E、unknown-commit fault injection、並行 live authorization 尚未完成。C1/C3/C6 仍不可標示完成。

## 2Q. C1/C3 nondeploy integration correction（2026-10-13）

- **已修正的實際缺口**：`V180OptionCUnknownCommitReconciler` 原先把成功發布誤限定為 published version number = 1；014 deterministic backfill 後首次發布會是 version 2，因此可能錯誤回報 `INDETERMINATE`。現在改以 fresh、non-pooled connection 的 request/current-row count、published stage 及 canonical audit evidence 判定，不依賴固定版本號。
- **Exact-SHA 證據**：來源修正提交 `cb36e6b03afa6174fd76f75f991f660071defeda`；nondeploy GitHub Actions [Run 38363920542](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38363920542) = **SUCCESS**。Backend `701/701` PASS；014 runtime publication writer、rollback、replay 與授權負向測試 PASS；disposable SQL fixture `5/5` PASS；011/012 rehearsal PASS；Frontend npm/test/build steps SUCCESS；所有 source/NO-GO guards SUCCESS。
- **Unknown Commit 證據**：成功出版後以 fresh connection 查證 version 2 情境，回傳 `COMMITTED_EVIDENCE_REQUIRES_REVIEW`；不盲目重試，也不向呼叫者宣稱已完成發布。
- **C1**：**PARTIAL / HOLD**。隔離 executable publication writer、014 runtime、rollback、stable LocationId、version/replay evidence PASS；但 public API/DI 仍 disabled，完整 Visitor → Admin → Publication E2E 及 Trip/Snapshot history proof 尚未完成。
- **C3**：**PARTIAL / HOLD**。隔離 harness 覆蓋 requester/reviewer separation、自審、跨組織／跨範圍拒絕；live HR/revocation transaction proof 及 public Admin approval route 仍 disabled/unverified。
- **C5**：**PARTIAL / HOLD**。目前 isolated SQL/runtime subtests PASS；真實 concurrent approval、fault-injected commit acknowledgement 與完整 API/E2E 仍 HOLD。
- **C6 Business UAT**：**BLOCKED / 0/22**。未執行 UAT migration/deployment、B3/B4、013 或 Production 操作。

## 2R. C1/C3/C5 executable integration harness（2026-10-13）

- 新增未註冊 `V180OptionCAdminApprovalService` 與 SQL-backed isolated approval-evidence provider。服務先在同一 `Serializable` transaction 內鎖定 canonical `ChangeRequests`，重新讀取申請人資格、獨立 Admin 資格、組織及小組界線，再呼叫既有 Atomic Publication Writer；不再由呼叫端傳入 live authorization boolean。
- 新增 disposable SQL E2E：Visitor canonical Submitted request → persistent Stage → matching geocode／duplicate evidence → independent Admin approval → Location／Stage／Ledger／Request／Approved+Applied audit atomic publication；核對 `LocationId` 不變、Current version 2、version 1 保留，以及 `VisitTripStop`／`VisitTripSnapshotStop` 參照數不變。
- 同一 harness 覆蓋自審、reviewer 撤權、requester 撤權、跨組織／小組拒絕、DecisionKey replay、audit FK fault rollback；public UAT/Production route、DI registration 與 B3 flag 仍 disabled。
- Exact-SHA candidate `5729cf58e7aa681f7e81ec2245040f74d159b9ec` 的 [Run 38366502191](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38366502191) = **SUCCESS**；Backend `702/702` PASS，新增 `Visitor_to_admin_to_publication_preserves_history_and_fail_closed_denials` PASS，014 runtime、Stage/Writer、011/012 rehearsal、disposable SQL `5/5` 及 Frontend npm/test/build steps 均 SUCCESS，所有 nondeploy/NO-GO guards SUCCESS。
- 本批完整隔離 E2E 已通過，但不是 UAT/Production 證據：public route/DI 仍 disabled，live HR/role/team adapter 尚未接到部署服務，真實並行 approval 與 commit-ack fault injection 仍待補。故 C1/C3/C5 整體仍 **PARTIAL / HOLD**，不得宣稱 Business UAT PASS。
- 後續精確 SHA `b4140115a8f422012fd996d36d5a7de7ef0622ec` 的 [Run 38369098412](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38369098412) = **SUCCESS**；Backend `702/702`、approval E2E、014 runtime、Stage/Writer、011/012 disposable rehearsal、Frontend npm/test/build 與 NO-GO guards 均 SUCCESS。為避免未通過的 fixture 造成假 PASS，本次未將 Reject 正向 SQL E2E 列為完成；Reject 候選服務仍保留但其正向 persistent-state proof 為 **HOLD**。

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


## Recovery branch status (2026-10-14)

- Recovery branch: `work/v180-optionc-final-gates-recovery-r1`; original integration branch `work/v180-uat010-packageb-optionc-integration-r1` was not modified. No UAT/Production migration or deployment, protected-branch change, 013 change, B3/B4 enablement, merge, or promotion was performed.
- Recovery CI is recovery-branch-only and exact-SHA pinned. Final engineering source SHA before this document update: `a6e1c8c43460a682ec031dfc55339ee4b9a82b54`. GitHub Actions Run [38485388564](https://github.com/Terry4410/field-visit-mileage-system/actions/runs/38485388564) = **SUCCESS** on disposable SQL Server 2022.
- **P0 Reject: PASS in isolated SQL E2E.** Root cause was SQL Server `NOCOUNT ON` causing `ExecuteNonQueryAsync()` to return `-1` although one canonical row changed. The service now uses guarded scalar `@@ROWCOUNT`, locks the request/DecisionKey, persists `Rejected` plus terminal Stage `Abandoned` and canonical audit in one caller-owned Serializable transaction. Tests prove reviewer identity, no Location/version publication, unchanged Trip/Snapshot references, replay/DecisionKey reuse denial, audit-failure full rollback, and HR/role/org/team fail-closed behavior.
- **P1 concurrent decisions: PASS in isolated SQL E2E.** Two independent SQL connections/transactions cover Approve-vs-Approve, Approve-vs-Reject, Reject-vs-Reject, DecisionKey reuse, reviewer/requester HR and role revocation, and organization/team mismatch. Each race commits exactly one terminal decision, with one canonical terminal audit and no duplicate publication; the loser is denied safely.
- **P2 Unknown Commit: PASS in isolated SQL E2E.** A real encrypted SQL application packet containing the commit request is forwarded through a TCP fault-injection proxy; the client acknowledgement is then lost by socket closure. A fresh non-pooled connection reconciles `COMMITTED_EVIDENCE_REQUIRES_REVIEW`; Request/DecisionKey/reviewer, terminal Stage, LocationId, current version 2 plus historical version 1, canonical Approved/Applied audit, and Trip/Snapshot references are verified with no duplicate publication. Confirmed rollback and indeterminate classifications are also tested; no blind retry occurs.
- Existing Visitor → Admin → Publication E2E remains passing. Final backend test run: **708 passed, 0 failed**; frontend unit tests and production build passed; canonical 011/012/014 disposable SQL rehearsal passed; explicit release NO-GO guard passed.
- **Readiness remains HOLD / NO-GO.** The approval service and SQL evidence provider are isolated non-registered candidates; public Admin API/DI and authoritative deployed HR/role/team adapter remain disabled/unverified. Business UAT remains blocked (0/22). No Business UAT GO is declared.


## Batch 5C evidence update — 2026-10-14

Engineering verification is complete through Recovery CI. Merge lost-ack evidence is disposable-only and does not authorize UAT/Production execution. UAT target preflight and Business UAT remain NOT RUN.


## Final Engineering Closure — 2026-10-14

Current authoritative Recovery SHA: `eb31fda053d8564a0d8aaa0dff2966966c41cb91`; exact-SHA CI `38507636467` SUCCESS. Engineering PASS: 733/733 backend, frontend 23/23 and build, HTTP/DI + disposable SQL, merge atomicity/history/lost-ack reconciliation, and canonical 011/012/014 rehearsal. UAT target schema preflight NOT RUN; Business UAT NOT RUN; no GO declaration. Flags and execution policies remain OFF.


## Final evidence-service closure — 2026-10-14

Current authoritative Recovery branch: `work/v180-optionc-final-gates-recovery-r1`; SHA `05ebca0748da49ae88a4403b5365ab3e1a1427c4`; exact-SHA CI `38509844660` SUCCESS. Backend 734/734, frontend 23/23 plus production build, disposable SQL/HTTP evidence-flow E2E and canonical rehearsal passed. Geocode success/failure and duplicate-review transitions are live-authorized, RowVersion-bound and fail closed on stale or revoked actors; background publication remains disabled. Canonical immutable evidence audit-event schema is still blocked as described above. UAT target preflight NOT RUN; Business UAT NOT RUN; no GO declaration.


## Canonical audit contract closure — 2026-10-14

Authoritative Recovery branch: `work/v180-optionc-final-gates-recovery-r1`; SHA `2084e6d470264826ac359fa91ce1c633bc30e475`; exact-SHA CI `38511099449` SUCCESS. Backend 735/735, frontend 23/23 plus production build, real HTTP/DI evidence E2E, canonical audit event E2E, collision rollback, stale/revocation denial and canonical 011/012/014 rehearsal passed. The additive audit candidate is disposable-only and does not modify 013 or activate any feature. UAT target read-only preflight is NOT RUN; Business UAT is NOT RUN by design and is not a prerequisite for reviewing a future UAT execution authorization. No UAT/Production migration, deployment or GO declaration has occurred.


## UAT execution preflight authorization — 2026-10-14

Owner authorized preparation and read-only preflight only. No UAT target was supplied in the authorization message, so target connectivity and schema preflight remain NOT RUN. The consolidated package is `docs/release/OPTIONC_UAT_EXECUTION_AUTHORIZATION_PACKAGE.md`. Recovery engineering remains authoritative at SHA `c75cd4b3c7fd2fea012634ced246edc28c7ab21e`, exact-SHA CI `38511402016` SUCCESS. No migration, deployment, flag activation, promotion, merge or Business UAT GO is authorized by this entry.


## UAT release-readiness finalization — 2026-10-14

The executable migration register ends at 1800_010; candidate 011/012/014 plus
LocationMergeDetails and StageEvidenceAuditEvents remain nondeploy review artifacts.
The exact order is target/backup gate -> 011 -> Verify -> 012 -> Verify ->
014/backfill -> Verify -> additive details -> additive audit -> backend/frontend deployment.
Reserved 1800_013 is untouched. A read-only SQL preflight candidate and SHA-256 artifact
script were added; the existing post-uat/v1.8.0 branch guard is unchanged.
Exact UAT target, allow-list, Entra read-only identity and backup/restore evidence are
not available from authorized repository configuration, so preflight is NOT RUN and status
remains HOLD. Business UAT remains NOT RUN.

## Authoritative Recovery evidence — 2026-10-14

- Recovery branch: work/v180-optionc-final-gates-recovery-r1
- Authoritative preparation SHA: 3012835e728d0e3882a061d920cd93916811c8f9
- Exact-SHA Recovery CI: 38513216745 — SUCCESS
- Hash/preflight contract, backend/frontend regression, disposable SQL Server 2022 Option C E2E, and canonical 011/012/014 rehearsal completed on this SHA.
- UAT target identification and read-only UAT preflight: NOT RUN; no exact authorized target or secrets were available.
- Backup/restore evidence: NOT VERIFIED.
- Flags remain OFF; no UAT/Production SQL write, migration, deployment, promotion, merge, or Business UAT GO occurred.
- Decision: HOLD pending the IT action checklist and separate Owner execution authorization.


## 2026-10-14 authoritative Recovery evidence

- HEAD: `2ce6108f9e091c32ea3a6099a9040196bb0a1e6c`
- Exact-SHA CI: `38518256406 — SUCCESS`
- Backend Release build/tests, frontend tests/build, Option C SQL E2E, canonical SQL rehearsal（含 Leader review additive table/constraint catalog assertions）均 PASS。
- UAT/Production migration、deployment、promotion、feature activation、Business UAT：未執行，維持 HOLD。


## Final Leader review engineering closure — Recovery 81413aa394a19b08e3e697a5a85bba2992e422eb

- Exact-SHA CI: `38521449544 — SUCCESS`。
- Backend `737 tests PASS; frontend `24 test files and build PASS。
- Real disposable SQL/HTTP/DI E2E PASS: Visitor submit → independent live-appointed Leader approval → application-owned geocode evidence → duplicate evidence → independent Admin approval → atomic publication, Version 2 current / Version 1 historical, canonical audit and historical references preserved。
- Additive audit compatibility PASS: Submitted / Rejected / Approved / Applied / GeocodeEvidenceRecorded / DuplicateReviewEvidenceRecorded / LeaderApproved / LeaderRejected all remain permitted; rerun returns controlled STOP `51083)。
- No UAT/Production SQL writes, migration, deployment, promotion, feature activation or migration 013 modification。
- Remaining HOLD: CreateLocation/LowRisk controlled application path, Excel/legacy/background canonical routing, Leader rejection/concurrency/revocation positive HTTP matrix expansion, UAT target preflight and Business UAT。


## Scheme A closure checkpoint — 2026-10-14

- Recovery HEAD: `8ac7d9b5b1005019ab668e19ec994445dec4ca55` (DI composition correction; exact CI pending).
- Candidate Option C services are now explicitly registered in the API host. Each service still fails closed unless `OptionC:Candidate:Enabled=true`, the SQL target is loopback isolated `OptionCIsolated_*`, and catalog proof succeeds. Deployed flags remain OFF.
- Existing real disposable HTTP/DI/SQL Leader flow and negative security/concurrency/revocation tests remain retained; no UAT/Production SQL write, migration, deployment, promotion, merge, or Business UAT GO occurred.
- Scheme A blocker remains precise: canonical CreateLocation/LowRisk application publication is not implemented because the verified atomic writer only supports existing high-risk published Location updates/merges. Excel/legacy/background writers therefore remain fail-closed for published changes; implementing a new writer without an additive schema and identity/reference contract would be unsafe.
- UAT target preflight and Business UAT remain NOT RUN.


## Scheme A authoritative nondeploy checkpoint — 2026-10-14

- Verified Recovery implementation SHA: `5ca1060423e9ef326b82b6cd6c1025250a0d578a`.
- Exact-SHA CI: `38534680973 — SUCCESS`; backend **753 PASS**, additional
  SQL Admin E2E **1 PASS**, SQL 011/012/014 rehearsal **2 PASS**,
  frontend **24 test files PASS** and production build PASS.
- Real disposable SQL Server 2022 HTTP/DI CreateLocation E2E now passes:
  Visitor submission → server-owned identity and inactive Pending reservation →
  private Stage → independent live-appointed Leader review →
  application-owned geocode and duplicate evidence → independent Admin approval →
  atomic V1 publication. A second request is independently Leader-rejected
  and its Stage/reservation are Abandoned without published history.
- Negative evidence: self-review denied; cross-org submission denied;
  duplicate Pending create denied; premature publication denied; canonical
  Submitted/LeaderApproved/GeocodeEvidenceRecorded/
  DuplicateReviewEvidenceRecorded/Approved/Applied events and historical
  Trip/Snapshot reference preservation verified.
- The next candidate extends the same test to prove actual HTTP Picker
  exclusion before publication and inclusion after it; **do not count that
  extension PASS until its own Exact-SHA CI succeeds**.
- Excel Location Create remains fail-closed in Preview and Confirm;
  legacy published mutation/direct-note routes are guarded. Full Excel routing,
  broad Legacy/Background audit, all UI roles and all operational read-path
  proofs remain incomplete.
- No UAT/Production SQL writes, migration, deployment, promotion, merge,
  flag activation, UAT-target preflight or Business UAT was performed.
  **Scheme A HOLD / NO-GO; NOT READY for UAT execution authorization review.**
  This current checkpoint supersedes older 2026-10-14 engineering status
  paragraphs where the CreateLocation HTTP flow is described as absent.

### Security / Business UAT case expansion (preparation only)

| Case | Role | Test | Engineering evidence / UAT result |
|---|---|---|---|
| SA-CR-01 | Visitor | CreateLocation HTTP pending request and private Stage | Isolated PASS / UAT NOT RUN |
| SA-CR-02 | Visitor | Same identity submitted twice | Isolated PASS (duplicate denied) / UAT NOT RUN |
| SA-CR-03 | Cross-org | Submit without team/org scope | Isolated PASS (denied) / UAT NOT RUN |
| SA-CR-04 | Visitor | Self-review of CreateLocation | Isolated PASS (denied) / UAT NOT RUN |
| SA-CR-05 | Leader/Admin | Independent first and final review, evidence and V1 publication | Isolated PASS / UAT NOT RUN |
| SA-CR-06 | Leader | Reject CreateLocation, abandon Stage/reservation | Isolated PASS / UAT NOT RUN |
| SA-CR-07 | Visitor | Pending invisible in Picker/favorite/trip/mileage/reports | Picker candidate CI PENDING; others HOLD / UAT NOT RUN |
| SA-CR-08 | Visitor/Admin | Live HR/team revocation, replay and concurrent duplicate requests | Existing partial tests; expanded Create-specific matrix HOLD |
| SA-EX-01 | Excel | No direct unreviewed Location Create | Fail-closed PASS / canonical import not implemented |
| SA-LG-01 | Legacy | No published direct note/identity write | Fail-closed candidate / complete matrix HOLD |
| SA-BG-01 | Background | No unpublished direct publication | Preliminary guard / complete matrix HOLD |


## Background geocoding reservation boundary — 2026-10-14

Legacy `BackgroundJobService.ProcessGeocodingAsync` originally selected all
Pending, inactive Locations and could update the server-owned RowVersion of an
unpublished Scheme A CreateLocation reservation. The Recovery candidate now
requires catalog plus additive version proof and excludes **every** LocationId
referenced by `LocationCreateReservations` from its eligible geocoding drafts.
Abandoned reservations are also excluded; valid unreserved legacy drafts still
run. On older databases with no additive contract, it avoids referencing the
missing table. Partial candidate schema fails closed.

An additional disposable SQL / EF query regression checks reserved exclusion
and legal legacy draft eligibility. This change is a nondeploy safety control,
not Excel routing, UI readiness or Business UAT clearance. Latest verified
Picker + CreateLocation full HTTP E2E: SHA
`57d3faee175cd178a3481228159069efc596cf7c`, exact-SHA CI
`38535811600 — SUCCESS`. New Background test remains unverified until
its own Exact-SHA CI completes. HOLD / NO-GO.

## Legacy draft reserved-identity guard — 2026-10-14

Source inspection found that `V160FinalRepository`'s legacy
`UpdateManagedLocationAsync`, `DeactivateManagedLocationAsync`,
`GetManagedLocationDeleteImpactAsync` and `DeleteManagedLocationAsync`
could otherwise treat the same Scheme A CreateLocation reserved row as an
unpublished ordinary draft. An additive-schema-aware
`V180OptionCCreateReservationGuard` now fails closed before such operations
on **any** LocationId ever used by a CreateLocation reservation (including
Abandoned) while permitting unreserved legacy drafts. The same optional-schema
catalog proof is now shared with background geocoding; no missing-table SQL
query is compiled on older databases. Disposable SQL tests assert reserved
denial and unreserved draft compatibility.

Verified prior Background fence exact SHA
`d89ee8cefbc50984a72275e9b6e55a571ddcd088`,
CI `38536671419 — SUCCESS` (753 backend + 1 Admin E2E +
2 SQL rehearsal; frontend 24 files). This new legacy-guard change must
obtain its own Exact-SHA CI before a PASS claim. UAT/Production HOLD.


## Authoritative Scheme A recovery checkpoint — 2026-10-14 / Verified 0dfb975

**Recovery only**: `work/v180-optionc-final-gates-recovery-r1`

- Exact engineering SHA: `0dfb9757650d5276e9ee7c078c89ec227b60f8a5`
- GitHub Actions Exact-SHA: `38537429814 — SUCCESS`
- Backend main suite: **753 PASS**. Additional isolated Admin E2E: **1 PASS**.
  Canonical 011/012/014 disposable SQL rehearsal: **2 PASS**.
  Frontend: **24 test files PASS**, production build **PASS (no deploy)**.
- Real disposable SQL/DI/HTTP CreateLocation transaction exercised:
  Visitor submit with server-owned stable LocationId + Pending reservation /
  private Stage; independent live-appointed Leader review; Admin geocode /
  duplicate evidence; independent Admin final atomic V1 publication.
  The same test includes duplicate pending denial, self-review and cross-org
  denial, premature publication denial, independent Leader rejection to
  Abandoned Stage / reservation, canonical event audit and historical
  Trip/Snapshot reference preservation.
- Picker/search HTTP now tested: Pending reserved identity is not selectable;
  after approved publication it becomes visible; adding pending favorite is
  denied. CI `38535811600` confirmed this test after correction of its
  old JWT test fixture.
- **Bypass guards**: Excel Location Create Preview/Confirm is fail-closed;
  legacy published direct updates/notes fail-closed; background geocoding
  excludes all canonical reserved LocationIds; legacy draft update,
  deactivation, delete impact and permanent delete call the same
  additive-schema-aware reservation guard, including abandoned reservations.
  Ordinary unreserved legacy Pending drafts remain eligible.
- **Scope of evidence**: reservation guard is covered by isolated SQL/EF
  negative tests plus successful API build. A complete direct HTTP test for
  every legacy mutation endpoint, concurrent Create-vs-legacy request,
  all background job modes, and all operational trip/mileage/report queries
  is **not** yet claimed.
- **Remaining HOLD blockers**: comprehensive Create-specific concurrent
  submission / replay / revocation / rollback HTTP matrix; comprehensive
  Pending inclusion/exclusion audit in every trip, mileage, export and report;
  Excel canonical import workflow; legacy/background canonical routing (beyond
  present fail-closed safety); Visitor/Leader/Admin approval UI; exact UAT target
  identification, read-only target preflight, backup/restore evidence, DBA
  migration/dependency plan sign-off, authorized UAT deployment and Business UAT.
- **Authorization boundary**: no UAT/Production SQL writes or migrations, no
  UAT target preflight, no deployment, flag activation, promotion, merge, or
  Business UAT GO; migration 013 untouched. This is nondeploy engineering
  evidence, **not** UAT execution authorization or Business UAT success.
- **Owner decision**: **SCHEME A HOLD / NO-GO**. Older status fragments in
  these evolving release documents are historical and superseded by this
  Exact-SHA checkpoint.

## Excel reserved-Pending update path — 2026-10-14

Source review identified a pending CreateLocation RowVersion bypass in the
legacy `WorkbookImportService`: Excel Update could target an inactive
Pending LocationCode already bound to `LocationCreateReservations`.
The Recovery-only patch invokes the same additive-schema-aware reservation
guard during **Preview**, during whole-batch **Confirm preflight** and
immediately before applying each Location Update. Preview displays
`LOCATION_CANONICAL_CREATE_RESERVATION_REQUIRES_REVIEW` for affected
rows; Confirm fails before batch writes or before a raced row mutation.
The existing fail-closed Excel Create policy and published Update denial
remain unchanged; unreserved legacy drafts remain eligible. The guard
does not replace full canonical Excel review routing; concurrency and real
Excel HTTP E2E are still HOLD until demonstrated.
Prior verified CI: `38537883997 — SUCCESS` (SHA
`9c32eca3c876755d3f8f660ec8f81f1ad6bdf359`).
This new Excel code is not PASS until its own Exact-SHA CI completes.
No UAT/Production changes or migrations.


## Recovery-only frontend candidate delta — 2026-10-14

The visitor/leader scoped location page gained an **OFF-by-default**
isolated CreateLocation submission / My Requests UI branch. The existing
legacy draft path is unchanged while the flag is OFF. The Leader review
page now uses the authenticated configured API client (rather than
bare unauthenticated fetch), with explicit self-review and terminal
state controls and one-time decision keys. Client guards do **not**
replace server live HR/Leader assignment and independent reviewer checks.
`OPTIONC_APPROVAL_UI_ENABLED`,
`OPTIONC_LEADER_REVIEW_UI_ENABLED` and the new
`OPTIONC_CREATE_LOCATION_UI_ENABLED` all remain **false**.
This is not a UAT/Production enablement or Business UAT approval.


## Admin candidate Stage / final publication routing — 2026-10-14

The isolated Admin review UI now distinguishes server-returned
`OperationCode=CreateLocation` from `UpdatePublishedLocation`.
CreateLocation uses `publish-reviewed`, never the generic `approve`
route. A new independent Admin-only Stage GET supplies a current
version-bound evidence contract. UI requires Stage `ReadyForPublish`
before enabling Create publication; live HR, Leader-first evidence and
Admin authorization remain server-enforced. Geocode and duplicate evidence
are explicit separate decisions with unique evidence keys and replay-safe
error messaging. This does NOT authorize or turn on any frontend flags.
All Scheme A UI features are OFF by default; Business UAT remains HOLD.


## Scheme A verified nondeploy checkpoint — 2026-10-14

**Authoritative verified engineering source:** `work/v180-optionc-final-gates-recovery-r1`
at `21e12c8342b86bea99eccb1cb1d4dee78b152b23`.
**Exact-SHA Recovery CI:** `38553015599 — SUCCESS`.
Backend main suite **753 PASS**, separate Admin isolated E2E **1 PASS**,
canonical 011/012/014 disposable SQL rehearsal **2 PASS**;
frontend **24 test files PASS** and nondeploy production build PASS.
This is isolated engineering evidence, **NOT** UAT readiness authorization.

Verified isolated CreateLocation path: Visitor HTTP submission → server-owned
LocationId → inactive Pending reservation and private Stage → independent,
live-appointed Leader first-level review → independent authorized Admin Stage
read and geocode/duplicate evidence → final reviewed atomic publication →
Version 1 current, canonical events and historical Trip/Snapshot references.
Denied: self-review, cross-organization, premature Admin Stage/evidence,
premature publication, unreviewed Picker/favorite access, duplicate Pending
submission and simultaneous same-identity HTTP creates (exactly one commits).
Rejected request abandons Stage/reservation without publishing; a new request
can resubmit the same identity with a **different LocationId** while retaining
its rejected historical evidence.

Safety controls: Excel Location Create fail-closed; Excel Pending Update
Preview, Confirm preflight and apply-time block canonical reserved LocationIds;
legacy published note/identity writes fail-closed; legacy draft
update/deactivate/delete blocked for reserved CreateLocation IDs; background
geocoding ignores all reserved IDs. Ordinary *unreserved* legacy drafts remain
eligible. This is NOT completed canonical Excel/Legacy/Background routing.

Candidate frontend includes gated Create/my-requests, authenticated Leader
first-level review and Admin evidence/reviewed publication UI. The flags
`OPTIONC_APPROVAL_UI_ENABLED`,
`OPTIONC_LEADER_REVIEW_UI_ENABLED`,
`OPTIONC_CREATE_LOCATION_UI_ENABLED` are all **false**; candidate backend
is also isolated-only and disabled by default. No Browser Business UAT run.

**Unresolved mandatory HOLD blockers**: Excel canonical import routing;
exhaustive Legacy/Background route-level HTTP fail-closed matrix; all
trip/mileage/report/export Pending visibility and reference enforcement;
Create-specific role/team/HR revocation, concurrent cancel/review and
commit-ack replay/rollback expansions; browser role-scope Business UAT; full
security and migration signoff, target identification/read-only preflight
and backup/rollback proof. Keep Scheme A **HOLD / NO-GO**.

No UAT/Production migration or SQL write, no UAT target preflight, no deploy,
feature activation, promotion/merge or Business UAT GO was performed in this
Recovery batch. Migration 013 remained untouched. Earlier checkpoints in this
document are retained for history but superseded by this verified SHA.

## Scheme A legacy HTTP Create fail-closed delta — 2026-10-14

The old `POST /api/v1/managed-locations` path made inactive Pending
Customer Locations without canonical ChangeRequest, Reservation or
independent Leader/Admin review. On database schemas with the verified
`1.8.0-CREATE-LOWRISK` additive contract it is now **fail-closed**
(`LOCATION_LEGACY_CREATE_REQUIRES_CANONICAL_REVIEW`), rather than
claiming an unreviewed Pending row is a canonical submission.
No-change legacy database schemas without the additive contract keep
the prior draft path; non-Customer location categories are out of this
Customer Create scope. An isolated real HTTP/SQL negative test confirms
that legacy Create is rejected without a committed Location or Request.
This does NOT implement the full Legacy canonical routing or user-facing
business UAT. Flag remains OFF. Last verified before this patch:
`9d43b89cefae0042b37f6b709e10e8d861ec6933`,
Exact-SHA CI `38553823661 — SUCCESS`; the new delta must pass its
own Exact-SHA CI before acceptance. UAT/Production not touched.


## Scheme A controlled engineering checkpoint — 2026-10-14 / Legacy HTTP and Create revocation

**Last exact engineering SHA:** `8e0c30ab78bb4fcffd9d2a9df1b946be2b4df866`
on the **only** authorized Recovery branch
`work/v180-optionc-final-gates-recovery-r1`.
**Exact-SHA GitHub Actions Run:** `38560339144 — SUCCESS`.
Nondeploy CI log: backend **753 PASS**, separate Option C Admin SQL
E2E **1 PASS**, canonical 011/012/014 disposable rehearsal **2 PASS**,
frontend **24 test files PASS**, frontend build PASS, explicit NO-GO gate PASS.

### New verified safety delta

- Under the *verified additive* `1.8.0-CREATE-LOWRISK` contract,
  `POST /api/v1/managed-locations` can no longer directly create an
  unreviewed Customer Location. Expected error:
  `LOCATION_LEGACY_CREATE_REQUIRES_CANONICAL_REVIEW` (422).
  Old databases **without** this additive contract retain the existing
  legacy draft behavior; this is not an assertion of UAT/Production
  readiness or current deployment.
- A valid org/team-scoped HTTP actor is used for the Legacy create denial,
  and an org/team-scoped Admin for Update / delete-impact / permanent
  delete denial against a canonical reserved LocationId.
  SQL assertions confirm no unauthorized Location or Request insertion,
  no source RowVersion drift and no reservation mutation.
- CreateLocation HTTP test revalidates *live* Leader appointment,
  requester HR status and Admin role following successful independent
  Leader approval. Revoked actors cannot obtain a privileged Stage
  read; Stage remains Pending and no GeocodeEvidenceRecorded is emitted.
- Leader-originated CreateLocation is allowed to enter Pending, but
  a sole appointed Leader cannot self-review and Admin cannot bypass
  the missing independent Leader review, publish or reject through
  the generic Admin endpoint. No published Version is created.
- Canonical Create/LowRisk/HighRisk/MergeFields coverage and historical
  preservation from earlier verified E2E remain regression-tested.
  Pending Picker exclusion, Excel Preview/Confirm reservation guard,
  Background geocoder reservation exclusion and old draft write guards
  remain on the Recovery branch.
- New negative cases use disposable `OptionCIsolated_*` SQL Server
  databases, not UAT or Production. Status = engineering validation,
  **not** authorized Business UAT.

### Remaining HOLD — do not infer GO

1. Excel true canonical Request+review batch routing (current Customer
   Create must fail closed).
2. Exhaustive Legacy/Background HTTP job-route audit including update
   races and all location categories.
3. Pending-location exclusion throughout Trip, mileage calculation,
   exports, report queries and reconciliation (Picker alone insufficient).
4. Create-specific lost-commit replay/rollback and all concurrent
   reviewer/revocation permutations.
5. Browser-tested Visitor/Leader/Admin UI and cross-team role
   matrix; frontend candidate approval flags still OFF.
6. DBA-reviewed UAT schema/dependency/migration sequence, exact UAT
   target, backup/restore and separate Owner execution authorization.

**Restrictions unchanged:** migration 013 untouched; no UAT/Production
SQL writes, migration, deployment, promotion, feature enablement,
target preflight or Business UAT GO. **SCHEME A HOLD / NO-GO.**
Earlier checkpoint paragraphs remain historical and are superseded
by this verified Exact-SHA checkpoint.


## Legacy maintenance read privacy & Excel template delta — 2026-10-14

- Identified a read-path gap: `GET /api/v1/managed-locations` and
  `/managed-locations/search` could expose server-owned Pending
  `CreateLocation` Reservations to ordinary same-team maintenance users
  before approval. The optional-contract guard now filters all
  Create reservations that are not Published with an Approved Location.
  Pending and Abandoned reservation identities remain hidden; successfully
  Published identities return through authorized legacy maintenance searches.
  When the additive reservation contract is absent, the historical
  legacy draft query remains unchanged.
- Added real disposable SQL HTTP/DI negative coverage for both management
  list/search before publication, successful published inclusion, and
  rejected/Abandoned exclusion. The normal operational Picker and favorite
  checks remain in the same CreateLocation E2E.
- Corrected the Excel Locations template instructions: unreviewed Excel
  Create is blocked in Preview/Confirm; published or reserved Pending
  Update must use canonical review. The sample may show a blocked Create
  case, but the template no longer describes it as a valid import.
- No feature flags enabled. This is an engineering candidate requiring
  its own Exact-SHA CI before PASS; UAT/Production Migration, SQL writes,
  deployment, promotion, target preflight and Business UAT remain forbidden.


## Legacy Master API fail-closed & worklist privacy delta — 2026-10-14

Source audit found legacy `GET /api/v1/locations/pending`,
`PUT /api/v1/locations/{id}` and `POST /api/v1/locations/{id}/promote`
outside the modern managed-locations API. The legacy pending worklist
now uses the same optional-schema-aware query exclusion as other private
CreateLocation reservation lists; tracking reads for legacy Master
writes are refused for any reserved Create LocationId (including
Abandoned). The old Master PUT additionally rejects **all** Approved
locations before touching RowVersion or mutable fields, requiring a
canonical published-location change request instead.

Added real disposable SQL/HTTP regression for pending worklist privacy,
reserved Master PUT/promote denial, no source RowVersion change, and
published Master PUT rejection. The historical nontracking Location
lookup used for Trip/Snapshot reporting remains unchanged. Candidate
requires its own Exact-SHA SUCCESS; no UAT or Production execution.


## Authoritative new read/write bypass checkpoint — 2026-10-14

**Verified code SHA:** `27b084d016eda41fbd2dda5555aa99da392c7377`,
**Exact-SHA CI:** `38564555552 — SUCCESS`.
Recovery branch only: `work/v180-optionc-final-gates-recovery-r1`.

New protections established and SQL/HTTP tested:

- `GET /api/v1/managed-locations` and
  `/managed-locations/search`: no private Pending/Abandoned
  canonical CreateLocation reservation rows; successful Published rows
  reappear after independent review.
- `GET /api/v1/locations/pending`: excludes canonical private
  reservation rows. Inactive ordinary non-reserved drafts remain
  eligible. The dedicated SQL fixture adds minimal old Trip columns
  only within `OptionCIsolated_*` to exercise this legacy route.
- `PUT /api/v1/locations/{id}` and
  `POST /api/v1/locations/{id}/promote`: canonical reserved
  LocationIds fail closed. Already Approved non-reserved locations
  also fail closed via `EnsureUnpublishedDraft` before source mutation.
  RowVersion stays unchanged in the negative HTTP cases.
- Excel Locations template now states that Create is blocked until
  canonical Excel routing and that published/reserved updates cannot
  bypass review.
- Isolated CreateLocation HTTP/SQL already proves private Pending
  Picker exclusion and approved publication inclusion; normal Trip
  save requires Active + Approved and Trip Context requires the same.
  Complete runtime report, mileage, import and all Trip request
  combinations are **not yet** claimed.

Additional positive compatibility test in next SHA confirms unreserved
legacy Pending drafts continue to appear to authorized users in
maintenance and Pending worklists, while canonical reserved items do
not. Do not count this new positive assertion as PASS until its own
Exact-SHA CI succeeds.

**HOLD blockers:** Excel canonical batch submission; exhaustive
Legacy/Background API and concurrency matrix; complete Trip/mileage/
report/export Pending reference E2E; Create lost-commit replay and
revocation permutations; browser role UAT; UAT schema + DBA migration
dependency review and explicit Owner deployment authorization.
All flags OFF, no main/post-uat writes, no migration 013 changes,
no UAT/Production SQL writes or deployments, no UAT-target preflight,
no Promotion/Merge, no Business UAT GO. **SCHEME A HOLD / NO-GO.**


## Scheme A authoritative Excel canonical Create review checkpoint — 2026-10-14

**Verified exact source** `708cdc5c81ab0d143724fcb42cf1e6b8a3911ccf`
on `work/v180-optionc-final-gates-recovery-r1`.
**Exact-SHA CI** `38586022534 — SUCCESS`; backend **754 PASS**,
additional isolated Admin SQL E2E **1 PASS**, canonical 011/012/014
disposable SQL rehearsal **2 PASS**, frontend **25 test files PASS**
and build PASS. Recovery release gate remains explicit **NO-GO**.

### Completed in disposable OptionCIsolated SQL only

- A separate candidate-only HTTP command
  `POST /api/v1/optionc-candidate/imports/{importBatchId}/submit-creates-for-review`
  consumes validated legacy Excel/CSV preview Create rows; it does not
  invoke unreviewed legacy Confirm or enable any UAT endpoint.
- Each row commits server-owned LocationId, **inactive Pending** Location,
  canonical HighRisk CreateLocation Request, Reservation, private Stage,
  Submitted event and Excel ImportBatchItem status in one **Serializable**
  transaction. Event details preserve ImportBatchId and ImportBatchItemId.
- Batch preflight refuses cross-owner/cross-org, mixed Update,
  unexpected errors, missing team or physical address. Subsequent live
  HR/role/team authorization is performed by the canonical Create flow.
- Two-row successful batch has exactly two independent Requests/Stages
  and Submit events; subsequent retry recovers the same Request IDs
  using canonical audit without making new identities.
- A second-row pending duplicate fails closed after a valid first row.
  The first row stays traceable as PartiallySubmittedForReview; failed
  second row creates no Request/Reservation/Stage. Retrying does not
  duplicate the first row.
- A real `.csv` HTTP upload is normalized to `.xlsx`, previewed, and
  submitted into the candidate workflow. Preview never treats a private
  pending reservation as NoChange; Create rows without team/address are
  blocked before submission. Abandoned historical identities do not
  falsely count as active unchanged Locations.
- Gated frontend AdminImportPanel displays the canonical reviewed-create
  action, counts and Request IDs. Flag
  `OPTIONC_EXCEL_CREATE_UI_ENABLED=false`, along with all other
  Option C approval/Create/Leader UI flags. Candidate backend flag remains
  OFF by default.

### Mandatory migration/authorization boundary

The canonical Excel flow requires the **additive status CHECK constraint
contract** `1.8.0-EXCEL-CANONICAL-STATUS` (not deployed). The current
`docs/release/optionc-canonical-sql-draft/excel-import/ExcelImportStatusContract.disposable.sql` is a NONEXECUTABLE
UAT/Production draft explicitly restricted to disposable
`OptionCIsolated_*` databases. The backend fails closed if the marker
and enabled ImportBatches/ImportBatchItems status constraints are missing.
This prerequisite must be included in a future DBA-reviewed migration
dependency plan with exact target/schema proof, backups, rollback and
separate Owner authorization. Never enable frontend/backend flags first,
and never replace status constraints directly on UAT/Production.

### Remaining HOLD

- Excel **published Location Update** and broader mixed-operation
  canonical batch routing remain unimplemented; such rows continue to
  fail closed. Excel Create is reviewed submission only, not publication.
- Complete route-level Excel/Legacy/Background write-path matrix,
  all live Trip/mileage/report/export pending exclusion and historical
  read invariants, remaining Create concurrency/unknown-commit
  permutations and Browser role-based Business UAT remain incomplete.
- No UAT/Production SQL writes, migrations, target preflight, deploy,
  feature activation, promotion/merge, or Business UAT GO was performed.
  Migration 013 untouched. **Scheme A HOLD / NO-GO.**
