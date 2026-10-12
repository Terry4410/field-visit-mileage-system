# PACKAGE B3 — IT Schema Review & Final UAT Execution Gate Register (DRAFT · NONEXECUTABLE)

> 狀態：**準備與審查用途，非執行核准**。Owner 已授權獨立 WORK 分支的程式、文件與可拋棄 SQL Server 2022 測試，**未授權** 1800_011/1800_012 正式 UAT Migration、權限 Grant、部署、B3 Approve/Apply、Business UAT 或 Production。
>
> 本文件由 IT／DBA／HR 權限 Owner 補證後，再提交 Owner 一次性合併核准。任何測試 PASS 都不能自行解除 HOLD。

## 一、基準與可稽核證據

| 來源 | 識別與解釋 |
|---|---|
| Repository | `Terry4410/field-visit-mileage-system` / Issue #24 |
| Protected source | `post-uat/v1.8.0` = `68b94138e2b79d9bff694d3bf8dcb4825d0dcbe2`，本階段不得改動 |
| WORK 安全分支 | `work/v180-package-b-b2-b3-controlled`（只准 WORK 實作、測試、文件） |
| 最近經驗證的 CHECK / Default 候選 | WORK commit `039e37a5b07e4c4f384ba56cc82f406b1aa85564`，GitHub Actions Run `38206127018`：Backend/Frontend CI SUCCESS、隔離 SQL **163/163 PASS**、JWT/HTTP+SQL **20/20 PASS** |
| CI Artifact | `package-b-b3-real-sql-disposable-only`（Run 38206127018, ID 11727746119）；`package-b-b3-b4-security-evidence`（ID 11727840596） |
| 說明 | SQL 僅在 random-name `B3SqlFixture_*` / localhost SQL Server 2022 執行，011→012 候選 DDL 在 Serializable 交易內一律 ROLLBACK；不是實際 Migration |
| 前置文件 | `PACKAGE-B3-1800_011-SCHEMA-IA-NONEXECUTABLE.md`、`PACKAGE-B3-1800_012-FORWARD-ONLY-ATOMIC-APPLY-IA-NONEXECUTABLE.md`、`PACKAGE-B-UAT-CONSOLIDATED-FINAL-EXECUTION-APPROVAL-DRAFT.md` |

**精確差異**：目前 repository 實際 `database/migrations` 的 v1.8 遷移最多到 `1800_010`，且目前沒有 `1800_011`／`1800_012` 正式 Up.sql / Verify.sql 與專屬 UAT migration workflow。測試建立的 `ChangeRequests` / `ChangeRequestEvents`、檢核與版本標記，絕不能充當經 IT 審核的正式 SQL Migration。

## 二、IT／DBA 必須補齊的獨立 Schema 證據

| ID | 驗證與待交證據 | 主要責任 | 本次狀態 |
|---|---|---|---|
| DDL-01 | 由 IT 提交可版本追蹤、前向且不破壞資料的 1800_011 **實體 Up/Verify**；列出 baseline `1.8.0-010` 的必要依賴／現場實際 SchemaVersion | DBA + IT | BLOCKED：只有 IA 草案 |
| DDL-02 | 由 IT 提交獨立審核的 1800_012 **實體 Up/Verify**，嚴格限定允許的 B3 011→012 狀態與稽核事件轉換，並明列可重跑政策及回復/補償計畫 | DBA + IT | BLOCKED：只有 IA 草案 |
| CAT-01 | 對 `ChangeRequests` 與 `ChangeRequestEvents` 固定完整 `sys.columns` 型別／長度／precision／scale／nullability／identity／rowversion，及 EF SQL Server 對應 | DBA | WORK 候選部分 PASS，未獲 IT 簽認 |
| CAT-02 | 固定實際 8 條 CHECK 內容及 trust/enable、Default、Unique/filtered index 的鍵順序、filter、included column 與 disabled 狀態 | DBA | WORK 候選部分 PASS，未獲 IT 簽認 |
| CAT-03 | 固定 FK source/target 每欄關聯、update/delete NO ACTION、信任狀態、trigger／permission／schema-qualified object dependency | DBA | WORK 候選部分 PASS，未獲 IT 簽認 |
| LOC-01 | 完整 `dbo.Locations` 實際 UAT 欄位、所有 FK/filtered index/default/trigger 與 012 七欄更新相容性；同時比對 EF 映射 | DBA + 開發 | BLOCKED：縮減/擴充 fixture 不是完整 UAT Schema |
| HIST-01 | 列出 `VisitTripStops`、`VisitTripSnapshotStops`、TeamLocationNotes、TeamLocationNoteHistory、GeocodingAttempts 等「有引用即保留」關係，逐項確認無級聯與歷史 Snapshot 不被即時 Location JOIN 汙染 | DBA + 業務 | 部分隔離測試 PASS；真實來源待驗 |
| VER-01 | IT 從受控版本與審核紀錄**獨立固定** Schema catalog checksum；檢查者不可只把同一次 live sys catalog 自我計算結果當預期值 | DBA + 資安 | BLOCKED：只有 WORK candidate / disposable dynamic SHA256 |
| GRANT-01 | 011/012 專用、時限、最低權限 UAT SQL migration Principal，執行前／後 Grant-Verify-Revoke 及稽核證據；不得沿用 1800_010 權限 | 資安 + DBA | BLOCKED |
| DEP-01 | 011/012 **獨立 UAT 工作流程**（SHA/TREE pin、preflight、備份、日誌、postflight、驗證、失敗回報），須與 application deploy 分離 | IT DevOps | BLOCKED |

**建議給 IT 的只讀資料表**：SchemaVersion 版本與時間、sys.tables/sys.columns/sys.types、sys.check_constraints、sys.default_constraints、sys.indexes/sys.index_columns、sys.foreign_keys/sys.foreign_key_columns、sys.triggers、sys.sql_expression_dependencies、資料庫 Principal/Object permissions。這只是索取資料的清單；**本檔沒有任何 SQL 指令、連線權杖或可執行遷移內容**。真實 UAT 讀取權限亦由 DBA 自行核定。

## 三、資安與真實業務關卡：非 Schema PASS 可代替

| Gate | IT/HR/Owner 必須出示的證據 | 現況 |
|---|---|---|
| `VAL-B1-002` | HR 在職／留停／離職有效日、實際內部身分、Admin 角色授權、Team 會員及跨組織拒絕、獨立審核者等具出處的 HR/IT 權限來源及撤銷紀錄 | BLOCKED |
| B3 Reject / Approve | 真實後端在同一交易內重讀 HR 狀態、JWT+角色+現況角色、Team+Membership、組織、Location 權屬；與提交者分離；來源 rowversion、request lock、DecisionKey unique | 僅 WORK 限制情境有測試；Approve/Apply 永久 DENY ALL 直到另行核准 |
| B3 Atomic Apply | source rowversion 守門、七欄最小寫入、request 狀態/時間戳、雙筆不可缺稽核事件、交易逾時與 UNKNOWN COMMIT 後新連線只讀重建證據、不自動重送 | 實際 executor **不允許啟用**；傳輸斷線真實模擬待補 |
| Snapshot Query/Export | 最新 Approved Snapshot 的名稱、地址、專案、核定里程及補助金額不得取自即時 Location；無 Snapshot 不可偷用 live 回補；需跨 org、API 權限與匯出完整測試 | Repository→XLSX 的 EF InMemory 限縮測試 PASS；完整 UAT 報表/HTTP 待驗 |
| B4 Business UAT | 正式 Case 01–22：資料、帳號、步驟、結果、重測、Owner/Business Sign-off，必須基於已核准部署的真實 UAT | **0/22，HOLD** |
| Production | UAT/B4 全 PASS、IT/HR/資安/Owner 核准、部署回退/監控及完整移交 | **HARD HOLD** |

## 四、可一次合併送 Owner 的「最終執行核准單」必填欄位

1. **擬執行範圍**：環境限定 UAT，列出 1800_011、1800_012、各自 SQL file SHA256、Git commit/tree、Workflow Run ID 與執行順序（**不得以本文件代替核准**）。
2. **Migration 門檻**：前置 schema 精確版本、備份/回復點、SQL Principal、timebox、禁止同時部署應用、Post-Verify 證據附件與例外處理者。
3. **安全審核**：IT/DBA 對欄位／約束／索引／FK／trigger／權限完整簽認；HR/資安對 VAL-B1-002 真實資料來源簽認，缺一保持 HOLD。
4. **分段簽核**：Migration 執行、Backend 部署、Frontend 部署、B3 Feature Flag 切換、正式 22 項 Business UAT、Production，每段有獨立 APPROVE / REJECT 狀態。Migration 的核准不自動授予後續任何一段。
5. **成功／失敗判準**：CI 確切 Run/SHA 全 PASS、real UAT catalog exact parity、schema/dml/audit invariants、未知交易結果不自動重送、回復流程及 Grant-Revoke 後証據。
6. **最終簽核紀錄**：Owner、IT 系統維護、DBA、HR 權限 Owner、資安、Business UAT Owner 的核准時間與具體附件；空白／不一致／逾期皆作 **REJECT/HOLD**。

## 五、目前提交給 Owner 的決策狀態

- **允許繼續**：WORK 分支候選 DDL IA、隔離 SQL 測試、非部署 CI、文件、Issue #24 紀錄、IT 審核準備。
- **不得執行**：變更 `post-uat/v1.8.0`、UAT 真實 011/012 Migration、Azure SQL 權限提升或 DML、任何部署／真實 B4 操作、B3 Approve/Apply／Flag 切換、Production。
- **需要新核准**：一旦 DDL/資安/來源證據全部齊備，請提出有 exact SHA／檔案雜湊／環境／回退方案的**一次性最終執行核准單**；在取得 Owner 授權前，系統一律 NO-GO。

狀態：**DRAFT — OWNER/IT/DBA/HR/SECURITY 尚未共同簽認；B3 OFF；Approve/Apply DENY ALL；B4 0/22；Business UAT HOLD；Production HARD HOLD。**

## 六、CI 機器可讀證據與自動拒絕界線

WORK-only 非部署 CI 另執行
`python3 scripts/ci/package_b_it_handoff.py`，產出
`.ci-artifacts/b3-it-handoff-ledger.json` 與對應 Markdown。
JSON 逐份記錄本清單、兩份 011／012 IA、候選
Name/Physical/CHECK+Default 檢核原始碼、Snapshot 匯出測試的
**來源檔 SHA256**，並以最新 Git HEAD 固定來源；每個
IT/DBA/HR/資安/Owner Gate 一律標註
`BLOCKED_UNSIGNED`，總結為
`IT_HANDOFF_PREP_PASS_RELEASE_HOLD` 與
`release_decision=NO_GO`。CI 的 PASS 只表示已成功蒐集
完整、未虛構的工作底稿與 HOLD 證據，不表示簽核完成。

`package_b_guard.py` 與 .NET Release Tripwire 已補上
**兩個版本 1800_011 與 1800_012** 的 Migration/Workflow
禁止清單；包含使用不相關 workflow 檔名隱藏 migration 版本
的情況。日後只有 Owner/IT 明確授權新的正式部署階段，
並獨立重新設計其防護、完成驗收，才能改變此封鎖。
本清單的完整工作底稿可供 IT 逐項填入附檔，但不接受
只將 CI 轉成綠燈即自動發給 GO。

## 七、IT 差異矩陣（來源檔固定、簽核仍然 HOLD）

機器可讀版：`PACKAGE-B3-IT-EVIDENCE-DELTA-MATRIX.json`。每個 Gate 具備
Owner（責任單位）、`reviewed_source_path`（**僅**既有 WORK 方案、測試或文件）、
`candidate_evidence_class`（WORK_TEST_ONLY／DOCUMENTED_ONLY），以及
`independent_external_evidence_required`（IT／DBA／HR 仍需自行補的正式證據）。

既有 `package_b_it_handoff.py` 在非部署 CI 中將這 14 個 Gate 的 JSON
資料與程式碼內**獨立固定的**來源路徑一對一核對，重新計算每份檔案的
SHA256。少一個 Gate、責任人錯置、引用不在 repository 的正式 SQL、
把 `BLOCKED_UNSIGNED` 改成 APPROVED、把測試假稱已具備正式簽章，
或將 `can_unlock_migration` 改為 true，全部 FAIL CLOSED。

注意：此矩陣由開發端紀錄「現階段的缺口」，**沒有** HR/DBA/Owner 的
簽署機制，也不收集任何 UAT 帳密、HR 個資或 SQL 金鑰。機器驗證 PASS
只證明所有必要差異仍被誠實標為 BLOCKED，不是可執行的 Migration
核准、B3 Approve/Apply 解除或正式 B4 驗收。所有簽核必須在獨立受控
流程完成，並另取 Owner 的具體授權。

## ApproveAsync source-contract safety hardening (WORK-only)

The nondeploy CI's prior string search could mistake a throw in a comment
or after an early return for a real DENY ALL. The WORK-only
`scripts/ci/package_b_approval_hold.py` now checks the **entire**
production `ApproveAsync(Guid,V180B3Review,CancellationToken)` method
against a source-pinned, exact allowlist: `ReadyAsync`, live actor/Admin
evaluation, and unconditional `B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED`
rejection. A different method signature, additional statement, conditional
denial, an early return, or extra DML fails the CI guard. Both
`package_b_guard.py` and the independent IT handoff ledger use the same
fail-closed contract. The C# release-tripwire test also asserts the
entire method source contract. Negative Python tests inject misleading
comments, early returns, mutation-before-throw and missing entrypoints.
This is a **static source tripwire**, not a proof of runtime permissions,
no migration, and cannot certify UAT or enable Approve/Apply.

## 九、Repository 設定覆寫檢查與真實環境差異（未完成 IT 簽認）

WORK CI 的 `scripts/ci/package_b_appsettings_hold.py` 只讀掃描
`backend/src/FieldVisit.Api/appsettings*.json`：基本
`appsettings.json` 必須明確維持
`PackageB:B3:Enabled=false` 與
`Auth:LiveSessionGuard:Enabled=false`；所有另外加入的
`appsettings.Development.json`／`appsettings.UAT.json`／
`appsettings.Production.json` 等變體不得設為 true、字串、
null 或不合法型態；JSON 解析失敗也會 FAIL CLOSED。

這只針對 repository 所含設定檔。ASP.NET Core 仍可被
**Azure App Service Environment Variables、Key Vault、
部署參數、外部 Configuration Provider** 覆寫；本次
CI **無法**證明真實 UAT 配置為 OFF。請 IT DevOps 於授權的
執行前置檢查內，獨立出示真實有效設定、變更歷程、
配置優先序及取證時間（證據不得包含 secret／token）。
缺乏該正式證據，Feature Flag Gate 維持 BLOCKED_UNSIGNED。

此增量不啟動任何 UAT 連線，不會修改部署、核准或 SQL DML，
亦不會把原本的 `B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED` 解除。
