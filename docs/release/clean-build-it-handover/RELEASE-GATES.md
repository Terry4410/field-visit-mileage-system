# v1.8.0 Clean Build 與 IT Handover 最小 Release Gates

版本：2026-10-19。Canonical Hub：Issue #41。這份文件是本次新資料策略的權威 gate matrix；舊文件留作歷史證據，不再依其 READY 標籤執行。

## 1. 結論、來源與授權

**CLEAN_BUILD_HOLD_WITH_EXACT_MISSING_EVIDENCE**。四 Gate 規格可送獨立唯讀審查；目前不宣告 Business UAT GO、Clean Build PASS、IT Handover PASS 或 Production GO。

| 固定輸入 | 接受的證據／邊界 |
|---|---|
| Protected | `4e02f37c0379f7f8003bb4ec3e8c27f82e31e3dc`；Tree `780003abb1750b36806832bf1f90745c2cd1af7c` |
| Frozen A+C | `138cd8352ab180254ed83bce4a77acb3fdfc948f`；CI `39145990902` SUCCESS；Worker real SQL 13/13，接受、不重開工程 |
| C1 | `39233612142` SUCCESS／Attempt 1，四階段 PASS；它證明當次受限 UAT 前置檢查，不證明 deployed API／Frontend 或全部 schema fidelity |
| PR #47 | Draft／Accepted NONDEPLOY／未合併；`8a6949bd3555f99bb42958f13ff0335513d24780`；CI `39245943529`、`39245948829`、`39245948584` SUCCESS；artifact `12183122146` SHA-256 `3703083a5701232272397bdd24cb89de7aa42cf1f0d536515c1b76fde5680d91` |
| Owner 資料決策 | 現有 db-fieldvisit-uat 全為測試／模擬資料；不作 Production 交易或主檔權威來源。UAT 結束後可提出另外授權的退役，現在沒有刪除權限 |
| 驗證範圍 | GitHub／原始碼唯讀與隔離文件準備；未做 Azure、shared SQL、HTTP ready probe、wake、匯入、移轉、合併、部署或設定變更 |

實際來源清單及 SHA-256 見 `source-ledger.json`，以 sourceSha + path 指定來源；A+C 的增量文件不能被誤讀為 Protected 已有功能。接受既有 A+C、PR #38／44／45／46、C1 與 #47 證據，不重新驗收其工程。Demo 僅限受控 UAT；Entra 移交 IT 處理，不在此重做。

## 2. 四 Gate 與唯一分類規則

A＝Owner-led Business UAT 開始前必備。B＝IT 接受移交前必備。C＝Production GO 前才必備的實際執行／正式環境證據。D＝由本次測試資料策略取代的歷史資料前提。B/C 不會倒掛成一般 Business UAT 的歷史追溯前提；任何 A 尚未滿足仍是 NO-GO。各列列出首次必須滿足的時點；之後持續適用。

| ID | Gate／控制 | 類別 | 最小通過證據 | 本次狀態 |
|---|---|:---:|---|---|
| G1.1 | UAT runtime/source/schema 對齊 | A | 指定 release source、API/Frontend binary digest、實際 deployment ID、設定摘要、必要 schema/trigger/permission 契約一對一相符 | HOLD；未認證目前 runtime |
| G1.2 | 核准的本版功能與旗標 | A | A+C 源碼接受不能代替 deployed 功能；指定功能、後端與前端旗標、資料庫相依及安全 OFF 路徑經獨立審查 | HOLD；沒有 promotion/activation 授權 |
| G1.3 | 安全帳號與授權隔離 | A | 每角色、Org、Team、owner 的受控模擬帳號；HR/role 撤銷 fail-closed；Demo 限 UAT、帳密不公開 | HOLD 待指定帳號／runtime 核對 |
| G1.4 | 當次測試安全／復原 | A | 明確測試邊界、資料隔離、sanitized evidence、寫入前可用復原路徑與停止條件；若要先變更 schema/runtime，必須另外有可用 rollback | HOLD；不能用「可丟棄」跳過 |
| G1.5 | 外部 provider 安全邊界 | A | 清楚列明 mock/controlled fixture/live；live Google 才需現有金鑰/費用/合約/retention/attribution 審核；不把 mock 當 live accuracy | HOLD 待本輪 scope/設定核對 |
| G2.1 | 最新 Business UAT 計畫與開始授權 | A | 第 6 節必測表、可追蹤版本、預期結果、指定測試者與獨立 runtime review；新單次／時窗授權 | PREPARED；NOT AUTHORIZED |
| G2.2 | Business User 驗收與簽核 | B | 必測通過、無未解 blocker/重大安全缺陷、其他重大問題有具名 disposition、實際手機／流程證據、Owner 簽核 | NOT EXECUTED，不預勾 |
| G3.1 | 可獨立重現的 clean schema | B | 空 DB→核准 baseline→核准 target；完整 constraints/indexes/triggers/history 契約、版本／hash、API SQL model 實測與復原證據 | CLEAN_BUILD_HOLD，缺可信 baseline |
| G3.2 | 主檔與 non-writing validation readiness | B | 業務核准輸入規格、穩定 keys、順序、reject、日期、去重、replay 規則；offline preview 無 DB write | 計畫完成，完整 dry-run 工具／核准資料未到位 |
| G3.3 | 正式主檔實際匯入 | C | 正式 inputs/digests、環境／操作身分、zero-error preview、執行授權與結果 reconciliation | NOT AUTHORIZED |
| G4.1 | 可操作的 IT 套件與接收人驗證 | B | 第 9 節全部必要材料可取得；IT 可自行驗證 build/import/deploy/recover，不依賴 Owner 個人技術記憶 | PREPARED SPEC，未接受移交 |
| G4.2 | 備份／復原／退役 scope 計畫 | B | DB、PITR、Blob 留存、證據、保存責任與實際復原驗證計畫；無自動刪除 | PREPARED；現況留存未查證 |
| G4.3 | 正式 Entra、正式 principal／MI、正式監控與復原 | C | 公司租戶／身分驗證、SQL least privilege、企業 secret custody、功能開啟與故障演練、正式 RPO/RTO 與 rollback | DEFERRED IT；未執行 |
| G4.4 | Production release／部署／GO | C | B 全部通過且正式 target/source/artifacts/schema/import/security/provider/recovery 逐項簽核；新授權 | NOT AUTHORIZED |
| D.1 | 將既有 UAT Trip/Snapshot/交易搬進 Production | D | Owner 已決定不遷移；Production 不讀 UAT rows 作 authoritative data | SUPERSEDED |
| D.2 | 為保留 UAT 測試 rows 而重建原始歷史 1.5 的 release 前提 | D | 明載原始 1.5 provenance 缺失；改要求 G3 的可審核新 clean baseline，不虛構原始來源 | SUPERSEDED；缺失事實保留 |
| D.3 | 把目前 UAT 主檔當正式匯入來源 | D | 正式 HR/Business inputs 另行核准；不得自動沿用測試人員、費率或位置資料 | SUPERSEDED |

G4 的「退役可執行」還需第 8 節的完整順序與獨立刪除授權；此文件不替未來實際退役設定自動時程。UAT 執行中仍須維護測試交易／快照／audit 的正確性，不能因 D 類就忽略資料完整性。

## 3. 現況、歷史證據與過時指引

| 輸入 | 實際讀到的內容 | 本版解讀 |
|---|---|---|
| BUSINESS-UAT-GUIDE／SIGNOFF v1.8.0 | `2fe86208...`、`1.8.0-007`、P2B、Mock、READY/RELEASED；舊兩 stops／人工里程單一選項與 Supervisor 描述 | 歷史套件，不可作新本版開測宣告或目前帳號清單 |
| OWNER-PRE-UAT-IT-HANDOFF v1.8.0 | f158121...／f6a0d60...；曾標 008 PREPARED；已有「Google 成功＋人工 fallback」及 HR 派駐預設需求 | 業務需求可追溯，部署/schema marker 是當時狀態，不是今日狀態 |
| DEPLOYMENT_ZH-TW | 假設現有 schema 1.5、示範 seed、SQL Admin connection 字串範例與 demo 設定 | 不得用於 clean Production 建置或現況 readiness；不照抄示範值／高權限身分 |
| A+C existing-uat-observations.json | 2026-10-16 記錄：Pages source stamp `0899f399...`；API 僅歷史 source `db235938...`；無 verified API binary archive／matched rollback pair | 是 dated observation。舊文件所列已接受 Visitor／A+C 工程缺口不再重開；runtime/archives HOLD 沒有被 C1 解決 |
| C1 accepted run | 當次固定 UAT target、010 前置、四 stage PASS、OIDC/privacy/readiness 接受 | 只重用既有證據；不能推論 API／Frontend 已部署 A+C 或 011–020 已套用 |
| 現有 deploy workflows | api-azure／frontend-pages 是 main 路徑 push 或 dispatch；API 使用既有 publish-profile；不是 clean-build 管線 | 本分支不匹配其 push target，不修改。不得宣稱目前 deployment 已採 OIDC／MI；GitHub OIDC、App SQL MI、user Entra 是不同邊界 |
| GitHub 本次讀取 | 讀到最近 100 runs，未見 API/Pages deployment；connector 不接受 workflow collection／filename-runs URL | 這個有界查閱不能證明「最新部署」或 Azure active binary；本次不改用 Azure 或觸碰 DB 的 probe |
| Recovery gate 文件 | 2026-09-08 的 PITR/7 days/12 hours metadata | 明確歷史值；本次沒有確認目前 PITR／Blob retention 或可恢復性 |

舊 ready claims 與舊手動執行步驟由本 matrix 的當次證據規則取代，原檔不覆寫。禁止把目前 Protected git SHA、/health label、UI version、source-stamp 單獨當作 matched runtime binary/schema 證據。

## 4. Clean Build 可行性與精確缺件

### 4.1 已讀取的來源與阻礙

| 來源 | 現況與 clean-build 意義 |
|---|---|
| `database/015_schema_v1_5_verify.sql` | 只有既有表／欄位查詢，且查詢 AppliedBy；不是 original CREATE baseline，不在本次執行 |
| Gate1 design archive | 其中 SQL_Schema_Draft.sql 明列 DESIGN DRAFT，UUID keys 等與後來契約不同；不能冒充核准 1.5 |
| 1600_001／002 | 001 直接 FK/查詢既有 Users、Teams、VisitTrips 等；002 對 Locations、rate、歷史 Snapshot 做增量/backfill。不是從零建核心表 |
| 1700_001–008 | 依賴前一代核心／版本，包含 identity/people、bulk、legacy supervisor、temporary-location cleanup、history action 等；不是空 DB baseline |
| 1800_001–010 | 001 要求 `1.7.0-008`、既有 Organizations/Teams/Users/Trips/Snapshots；其後依 predecessor 前進。008 取代 rate trigger，009/010 reason CHECK 變更不能只看最新版本標記 |
| A+C 011／012／014–020 | 9 個候選 step，013 reserved/不執行；011 request/events、012 atomic approval、014 Stage/PublishedVersion，015 audit、016 LeaderReview、017 Reservation、018 LowRisk、019 Merge、020 Import status；仍需既有 core/import/history/trigger 契約 |
| EF relational model | Protected 與 A+C 映射不同；model/EnsureCreated 不含全部 SQL trigger/default/歷史遷移契約；沒有可取代原始來源的核准 full migration snapshot |
| 既有 isolated fixtures | 可作契約測試輸入，不是 original baseline、UAT fidelity 或獨立 clean build。接受既有 PASS，不更名為本 Gate PASS |

**缺少的最小材料**：可審核、可定位來源且涵蓋 core 的完整 schema creation DDL／schema-only reference；逐物件 target manifest（PK/UK/FK、型別/nullable/default/computed/rowversion、filtered/unique indexes、CHECK/trust、triggers、audit/versions、必要 modules/dependencies）；與 A+C 的完整契約 reconciliation；核准新 baseline/version ledger 的語意。這些沒有齊備，所以本次不執行 clean-build rehearsal，也不製作假 PASS。

具體核對例：`TR_MileageRateRules_ProtectSeries` 的 rate 日期治理、Team/Employment/Site 的 period/primary 約束，以及 Trip/Snapshot、ChangeRequestEvents/PublishedVersions 等歷史／audit 契約，均不能只靠 EF 欄位或 version marker 證明。受檢 Up SQL 中可見的 trigger 名稱與原始檔可由 source ledger 定位；這不是已驗證的 UAT metadata。

### 4.2 最小取得／重建計畫（未授權執行）

工程建議：優先找可驗證的既有核准 schema-only DDL／DAC package／schema-only backup export 及 producer/source/hash。若沒有，就建立**新的、如實標示重建來源的 target baseline**，不再追求虛構原始 1.5。由 ChatGPT 準備技術材料，獨立審查者檢驗；Owner 只核准清楚的 release scope，不自行補 SQL／判斷架構。

1. 固定目標 application source 與契約（目前 frozen A+C 是參考，不代表已 promotion），列出必要 SQL objects/dependencies 及 minimum system/config records。
2. 既有來源仍不足時，另行提出一次 schema-only UAT acquisition 授權；先解決 metadata visibility、restricted evidence storage、module/trigger/constraint 完整性與 source binding。可重用 #47 的分析及分類器，但不自動合併／執行 #47，也不加入 R3。此 acquisition 不需帶出 UAT business rows、historical counts/keys 或 credentials。
3. 任何觀測 schema 都標記 ObservedCurrentUatSchema，獨立與 1600/1700/1800、A+C、EF 契約核對；區分 manual drift、missing objects、target additions。觀測本身／單一 fingerprint 不證明正確，也不證明 1.5 provenance。
4. 產生新的 schema-only build candidate 與逐物件來源/差異/hash ledger；移除 demo transactions 及 UAT account/config data。只有具名、業務核准的最小 system/reference records 可例外列白名單。保留其他 runtime secrets/identity/environment 配置在 schema 外。
5. 獨立審查 baseline、defaults/constraints/indexes/triggers/modules/audit/version 與 migration ordering。若從可信歷史 baseline replay，記錄真正執行順序；若新建 target baseline，明載涵蓋的舊契約／target compatibility，**不得偽造「曾逐次執行」的歷史 migration records 或 original 1.5 來源**。本次不決定或改寫 ledger SQL。
6. 只有可信 candidate 核准後，下一次 NONDEPLOY 才從明確 empty SQL Server 2022 DB 做最小真實 rehearsal，驗證下表；任何缺件 STOP。再經獨立 audit 及另外的 UAT/Production 執行決策。

### 4.3 未來 empty-build rehearsal 接受條件

| 檢查 | 必須證明的內容（本次均 NOT EXECUTED） |
|---|---|
| Start | 新 DB，無 user objects/業務 rows；記錄 exact source、每一 SQL hash、provider/version |
| Build | 核准 DDL／前置／次序／predecessor 與 marker；013 仍 reserved；不能 EnsureCreated 或 synthetic fixture 代替 |
| Schema | tables/columns/default/identity/computed/rowversion、PK/UK/FK、CHECK enabled/trusted、filtered/unique indexes、triggers、modules/dependencies、audit/歷史版本契約一致 |
| API compatibility | exact target API 的 SQL-backed schema/model smoke；缺物件或權限不足 STOP；測試用 temporary transactions 必須在核准隔離 fixture 內，最後乾淨 DB 無合成業務 rows |
| Records | allowlist 以外 Trip/Stop/Snapshot/Location/Employment 等 business data 為零；例外 minimum reference/config records 的來源／數量／用途單獨審核，不憑空造員工/費率 |
| Rerun/recovery | 已完整建置時明確 no-op 或 STOP（由核准契約決定）；partial/failure 停止，保留 sanitized evidence；不得盲目重播或 DROP 既有 DB；復原只在另核准 disposable scope |
| Result | hashes、完整差異、real SQL evidence、final empty-business state、獨立審查。沒有證據一律 HOLD |

### 4.4 A+C promotion blockers（不是重開已接受工程）

- candidate 與 canonical source 的 byte equivalence 是 isolated 契約證據，不是 UAT/Production 執行授權。
- 014–020 的 `OptionCIsolated_*` guards 及 NONEXECUTABLE/DISPOSABLE 註記保留；不改 DB 名稱或刪 guard 使現有腳本硬過測試。
- 正式 forward-only migration package 尚須核准 exact targets、完整 predecessor/partial/already-applied 判定、real schema prerequisites、applock/交易/錯誤 STOP、Up→Verify 與同時寫入邊界。
- 014 backfill 的來源 projection/version1/Approved+Active 規則：clean empty DB 應有可審查的 zero-row 行為；未來核准 master-data import 後的 initial published version 不能由匯入器繞過 A+C publication/audit。不是把 UAT 歷史 rows 搬入正式環境。
- 015–020 並非只換數字：確認 015 events、016 LeaderReview、017 Reservation 隔離、018 direct publish、019 merge/history、020 legacy import status 與 actual API/EF 契約。候選中 marker time/order 與 partial rerun 行為需整體核准，不可只看 MAX 版本。
- 013 reserved 不補造。正式 DDL／permission／backfill／rollback/forward-fix／artifact pins 需獨立 review。Promotion、migration、旗標、部署均未授權。

## 5. Business UAT GO 的最小條件

G1.1–G1.5 與 G2.1 全 PASS，並由獨立審查確認：

- [ ] 本輪 exact source、API 與 Frontend build/deploy artifact/digest、deployment ID 及非秘密設定摘要形成 matched release；不能把 git HEAD 當 runtime。
- [ ] 本輪承諾的 A+C 功能確實存在於所部署版本；所需 schema/trigger/constraints/permissions 可對應其 exact contract。若還在 010／缺 011–020，就不能宣稱完整 A+C runtime ready。
- [ ] 暫時 Demo 僅受控 UAT，角色／Org／Team／owner 測試身分已核對；既有員工 HR/role 撤銷拒絕路徑有測試方式。密碼不在 repo／logs／issue。
- [ ] 本輪測試 matrix、mock/live provider 邊界、通知 allowlist／費率／日期與用例 fixture 經核准；不對真實收件人發通知，不啟用未核准外部費用。
- [ ] UAT 權限、schema、資料、版本、rollback／write boundary 任一未知即 HOLD；所需部署/migration/帳號或設定準備需另外授權，不由此文件推定。
- [ ] 一份明確的 Owner 開測授權與 sanitized evidence 存放方式。缺任何一項不開始。

不要求開測前證明 original 1.5 lineage 或搬移測試歷史資料；但不能以此跳過實際 schema/source、安全、資料完整性及復原。若 clean-build 能力本身尚未證明，可仍安排受控 UAT 的前提是所有 A 類已通過；不預先宣告其可正式移交／上線。

## 6. 當版本最小 Business UAT 驗收表

E-AC＝接受的 A+C exact-SHA CI/independent audit；E-W＝已接受 Worker real SQL 13/13；E-C1＝accepted C1；E-P＝Protected 既有測試原始碼（存在不等於本輪 runtime 已通過）。不擴大既有 automated evidence 的證明範圍。

| ID／必測路徑 | 當版本預期／關鍵負向測試 | 既有自動證據可重用 | 仍需本輪 Business／runtime 證據 |
|---|---|---|---|
| U01 Visitor trip | 建立/儲存/編輯/submit；依本輪核准 one-stop eligibility/完整 stop/site basis 驗證，不沿用舊一律兩 stops 指引；invalid/stale submit STOP | E-P trip/context tests；E-AC 相依 regression | 手機完成一筆實際流程，錯誤提示與狀態正確 |
| U02 Primary Site | 人事主檔主要派駐據點預設 start/end；使用者可分別修改；送出後 snapshot 保存當次選擇，不被後續主檔改寫 | E-P V180TripContext/DeploymentSite tests | 預設、搜尋 picker、手改 start/end、日期切換 |
| U03 距離來源 | Google 成功採 API suggestion，失敗才人工 fallback，Leader 最後核定；沒有第三個「系統里程」業務選項；無 fallback 不可默默核准 | E-AC Google orchestration/canonicalization、E-W SQL transaction/late/重複安全 | 明確標 mock/controlled/live，成功與失敗 UI、來源/決策/audit 正確。live accuracy 需另核准設定 |
| U04 Leader | 授權範圍 review/核准/退回/重送與必要更正；拒絕非本組/非授權狀態/stale evidence | E-AC + E-P approval rules | 實際 Leader 使用、核定值、狀態及查詢一致 |
| U05 隔離與撤銷 | Org/Team/ownership 路由、直接 API、查詢、picker、export 均不得越界；離職/留停/role/team/ownership 撤銷後 fail-closed | E-AC independent scope/Visitor/Leader negatives；E-W live revocation | 各角色模擬帳號以正負配對測；UI 隱藏不代替 server denial |
| U06 Location Create | Visitor/Leader/Admin 的核准 Create/LowRisk/HighRisk 路徑；pending reservation 不可被 legacy API、picker、import 或外組看見／改寫 | E-AC P1 Reservation 接受證據 | 必要功能入口、建立、錯誤／pending/read scope，不能自動發佈 |
| U07 Option C review | 高風險 request→獨立 Leader→所需 evidence→獨立 Admin→atomic publication；self approval/跨組/HR撤銷/過期decision STOP；重送不重複 apply | E-AC canonical SQL/HTTP/review，E-W 重複/失效 transaction cases | 業務可理解的差異檢視／核准／退回，正確 Current/Previous |
| U08 歷史與更正 | 測試過程 Trip/Stop/Snapshot 參照、version/before-after/audit 一致；後續主檔/merge 不改寫已核准 snapshot；correction 保留舊決策 | E-AC history/merge + E-P correction tests | 同一組測試資料完成更正及追蹤。D 類不免除測試中的 integrity |
| U09 Duplicate/Merge | suspect 標示非自動刪合併；Admin 明確 decision/欄位選擇；現行/未來 deployment-site 等禁止條件生效；歷史仍可追溯 | E-AC MergeFields/legacy constraints + E-P notes/history | 實際 side-by-side、阻擋提示及 survivor 查詢 |
| U10 Excel/角色欄位 | HR、人事/角色、Team、Location、Project、VisitType、Trip/補助等現有端點逐一對照授權；Visitor/Leader/Admin/HR 不共用 effective scope/columns；直接 download API 同樣拒絕 | E-P V180QueryExport/people rules；A+C 不是 Package A 全部匯出修正的接受證明 | 有權/無權/越組/越owner/撤銷配對、篩選內容及禁止欄位；未證明的端點列 BLOCKED，不臆測 PASS |
| U11 主檔/日期 | 人事/Team/多 Site 與唯一 Primary、Project/VisitType、車種/人工費率日期；重疊/無效FK/跨Org拒絕，不自行填費率 | E-P master-data/personnel/team validation tests | HR/Admin 實際維護、有效日期、Trip 顯示與安全刪除/停用 |
| U12 iPhone | Visitor/Leader 必要頁面、picker、數值輸入、送出/核准/退回、更正與 Excel download；無遮蔽按鈕/欄位 | E-AC isolated browser 證據可定位，不宣稱 iPhone 真人完成 | 實體 iPhone Safari/常用瀏覽器、實際角色與同一 case data；截圖去識別 |

UAT 記錄：release/source/artifact/環境、Case ID、角色代稱、裝置/時間、預期/實際、PASS/FAIL/BLOCKED、sanitized evidence ref、issue/severity、重測綁定；不把帳密/員工資料/keys/原始 provider payload 放公開 Issue。簽核前必測全部有 disposition、零未解 blocker/重大安全或完整性缺陷；非阻斷已知項目具名接受。Owner/Business 測試者簽核自己的業務結果；不能用 CI badge 代簽。

## 7. 主檔匯入：現有能力、缺口、未來順序

### 7.1 已確認的範本／實作差異

- 固定 XLSX preparation template 第 5 row 才是 headers；含 Read Me、Centers、Team-Center、Deployment Sites、Team-Site、Employment-Site、Mileage Rates。含 OrganizationCode 及 RatePerKm 等業務欄位。它是準備表，不是可直接上傳到所有 importer 的通用檔。
- `V180MasterDataBulkWorkbookService` 實際要求 EmploymentStatus、Centers、TeamCenters、DeploymentSites、TeamSites、EmploymentSites 六張精確 sheets，組織來自目前 Admin scope；**沒有 Mileage Rates sheet**。名稱、headers 與 preparation workbook 不同，禁止直接混用。
- 該服務 Preview 會新增 ImportBatches/Items/AuditLogs 並 SaveChanges；Confirm 有 transaction/app-lock／revalidation 與 Confirmed replay 路徑。`V170PeopleBulkWorkbookService` 及 generic Location/Project WorkbookImportService 的 Preview 也寫 staging/audit。這些不是 non-writing preview，現在不可呼叫。
- `V180PersonnelBulkService` 的 preview 是來源檔解析及 scoped lookup，未在該 preview method SaveChanges；其 Personnel 主要維護已存在 UserCode/link，不可宣稱是 empty-system HR 全量建檔 importer。`V180TeamMembershipBulkService` 需 EmployeeNo、existing Team 與 v17/v18 membership 一致；不能空白推定 scope。
- 動態範本的 example rows 不是正式資料；不得把 example employees、地點、專案或費率匯入乾淨環境。A+C location/Excel request publication 必須遵守 accepted authorization；不能走 legacy import 繞過。

### 7.2 控制順序（設計，沒有執行）

| 順序 | 類別／資料 | 穩定 keys 與依賴 | 可重用能力／仍需核准 |
|---:|---|---|---|
| 0 | system reference/config | roles/status/enums/schema ledger/必要 config；逐項 allowlist、來源與用途 | 由 code/canonical contracts 審核。Demo users、sample transactions、live secrets 不在 baseline |
| 1 | Organization、Team、Center 及 Team-Center | OrganizationCode + scoped TeamCode/CenterCode，含有效日期 | Business 核准清冊；existing workbook 不含完整 Org/Team foundation import，mapping/建置路徑需補證據 |
| 2 | HR Person／Employment／受控身分 | 組織內 EmployeeNo、核准 UserCode、實際 identity binding；離職/留停/role 分責 | HR 原始核准資料。existing update/bulk 不保證 clean population；禁止姓名/email 模糊配對或假員編；Entra identity 由 IT 驗證 |
| 3 | Team membership／角色授權 | EmployeeNo + TeamCode + EffectiveFrom；role/team/Org scoped | existing preview/confirm 規則可重用，但禁止 Name match；先確立有效 primary/team periods，沒有權限預設拒絕 |
| 4 | authoritative Locations | 組織/Team scope + 核准 LocationCode；地址/TaxId/PlusCode 是業務內容，不作 fuzzy identity key | 已核准 source；需受控 staging/review/publication 與 duplicate report；不帶 UAT transactions/歷史 counts |
| 5 | Deployment Sites／Team-Site／Employment-Site | Center→Site/Location→Team-Site→EmployeeNo/Site；effective coverage、至多一 primary | preparation與API模板需明確 adapter/schema mapping；只在前置全部 valid 後規劃寫入 |
| 6 | Projects／Visit Types／Mileage Rates | scoped ProjectCode、明確 VisitType business key、Org+vehicle+effective start 的 rate version | generic Projects importer 只是部分能力；VisitType/rate import 路徑／外部鍵 mapping 未證明；Finance/Business 核准實際費率，日期不重疊 |
| 7 | genuinely operational transactions/history | Trip/Snapshot/Correction/Audit/history | 正式環境不從現有 UAT 匯入；只能之後另核准的真實營運來源。新發生的正常 publication/audit 由正式流程產生，不偽造舊歷史 |

### 7.3 最小 non-writing preview 與執行前契約

先作 offline pure-file validation，使用經另外核准、帶時間/來源/hash 的 reference key snapshot 或 disposable reference fixture；**零 shared DB mutation，零 staging/audit writes**。本次只設計，不寫新 general importer、不接收真實 HR inputs。線上需要查 reference keys 時另行授權，不偷偷使用寫入型 preview。

Preview 必須輸出受限 reject report：sheet/row/business-key代碼、error code、duplicate/FK/effective-date/overlap/cross-Org 原因，原始 PII 只留受限 destination。檢查 required fields、text leading zeros、same-file 重複、既有 key match 0/1/>1、日期區間/coverage、primary 重疊、numeric/nonnegative rate/vehicle domain、referenced active published Location、allowed status、stale rowversion；0 或多筆 match 均拒絕，不補值。

執行前固定 approved input digest、template version、target/schema/source、operator/role/Org、reference snapshot、preview digest/expiry、import ordering、no-change/update/create 分類及 revalidation/concurrency/transaction 邊界。確認時重新查授權／keys，不把成功 preview 當永久授權。拒絕／partial apply 停止且對帳，不盲目重匯；以 batch/decision key 或 natural-key+source digest 定義適用的 idempotency，明確 no-change vs conflict。不是每個現有 importer 都已證明跨步 all-or-nothing 或 clean-install idempotency；缺少的 adapter/validator/replay proof 列 G3.2 HOLD，未在本次偷做新功能。

目前只用受控模擬資料做 UAT；未來資料由 HR/Business/Finance 提供並簽核、ChatGPT 準備驗證報告、IT 接收並核對實際執行。真實資料不放公共 repo/Actions artifact/Hub；本次 artifact 僅文件與 hashes。

## 8. UAT 測試資料退役 SOP（不執行）

**目前禁止 delete／TRUNCATE／DROP／reset／restore；不得修改 `stfieldvisitbackup2608`、`fieldvisit-backup`、`$logs`；沒有任何自動 backup deletion。** Owner 的「都是測試資料」是 release scope 決策，不是現在的刪除授權。

1. 完成第 6 節 Business UAT，關閉 blocker；Owner 簽核結果，不以 CI 取代。
2. 保存簽核、sanitized evidence、source/artifact/issue refs、限制與測試結果到已核准受限位置；分清應保留 acceptance evidence 與可退役測試 rows，不把 PII/keys 存公開 Hub。保存期間與刪除責任由業務/IT 核准，不自創法定天數。
3. IT 完成第 9 節接受驗證，確認 production/clean schema 與 UAT 資源不共用未盤點的 dependencies；Owner-to-IT 責任轉移有日期/接收人。
4. **唯讀盤點** exact Azure resource IDs、DB backups、PITR earliest point/retention、Blob 容器/versions/snapshots/soft delete/lifecycle/immutability、實際 backup scope/排程/成本與存取；本次未查證，舊 7 days 不是現況。實際盤點亦需符合將來授權；不改 policy/retention、不列印 backup payload/credentials。
5. 驗證刪除 scope 無真實營運資料、合規/hold/其他系統或正式環境相依；包含 backup 可能的範圍不明則排除並 HOLD。Owner assertion 不是免除未來 exact-scope 檢查。
6. 另獲 **exact-target/exact-operation** 退役授權：resource IDs、排除項、保留 evidence/backup、必要可恢復窗口、執行人與時間、費用、STOP、核對方式；不沿用本 WORK 或舊 C1。
7. 優先評估「經審查的測試環境 replacement/retirement」以免逐表刪除破壞 FK/audit；只有已驗證 clean replacement/cutover 與相依隔離後才執行。這是建議方向，不是現時 provisioning／DROP 指令。無合格替代與 recovery，就保留原狀。
8. 最後驗證授權 target 的退役結果、無越界、IT 可操作 replacement（若有）、必要簽核證據及核准 backups 仍存在；append sanitized completion，不覆寫原歷史。任何異常 STOP，不擅自 restore/補刪。

Backup 與驗收文件不是「測試 rows 可丟棄」的自動連帶刪除範圍。既有 backup 儲存、retention、billing、logs 現在全部不動。

## 9. 最小自足 IT Handover 規格與接收驗證

以下是交付要求與驗證 SOP，不代表現時值已驗證。IT 接收前由 ChatGPT 整理成可下載、版本固定的材料；接收者不能只得到 Owner 的聊天紀錄或 CI badge。

| 交付項 | IT 必須拿到／自行核對 | 接受條件／目前缺口 |
|---|---|---|
| Release manifest | repo/branch/commit/parents/tree、source archive hash、API+Frontend binaries/artifact IDs/digests、build tools/lockfiles/fonts、CI/deploy IDs、非秘密設定版本 | 從 source 重現並與核准 artifacts 比對；知道可重建不等於 active package provenance。當前 active matched pair HOLD |
| Infra/env inventory | subscription/RG/resource IDs 受限保管；UAT固定SQL server/db、api-fieldvisit-uat、Pages；runtime/provider/version/region/SKU/network、成本/auto-pause、owner與dependencies | 生產不能照抄 UAT URI/名稱；Azure現況和 Production 未盤點/未 provisioning。storage三項列排除、不改動 |
| Clean schema runbook | 第 4 節核准 schema-only candidate、完整來源/object/hash manifest、empty-start前提、版本／前置／Up→Verify 順序、013 reserved、partial/re-run/forward-fix | IT 在隔離環境自行 empty build、API model契約、最後零 business rows；目前無可信 baseline，HOLD |
| Canonical migrations | 已核准版本與 included contracts、schema marker語意、applied checksum/時間/結果、DDL與data step分離；不得自動搬入 disposable candidates | 任何 partial/predecessor/hash不符 STOP，無自動 production migrate；候選 promotion 尚待核准 |
| Master-data import | 第 7 節 inputs/template/adapter/keys/日期/reject/revalidation/idempotency/對帳/復原材料 | IT 可對 synthetic fixtures 做 non-writing dry-run及核准isolated confirm；再驗證正式inputs，無範例資料殘留；完整toolchain HOLD |
| SQL least privilege | application principal 的 object/operation permission matrix；schema migration/read-only diagnostics/recovery 身分分離、effective DENY/GRANT證據、network/TLS | App不依賴 SQL Admin/db_owner；不因失敗擴權。現有approved UAT診斷OIDC成功不證明App principal權限／Production可用 |
| Identity separation | GitHub Actions OIDC的repo/ref/environment/federated trust；App Service managed identity或核准appSQL身分；終端使用者Entra tenant/audience/scope/binding | 三者獨立審核。現有 deploy workflow使用publish-profile不能說已轉MI；不傳任何token/secret/federation subject到公開文件 |
| Production Entra | Auth mode、token validation、tenant/audience/scope、精確tid/oid→核准員工綁定、external identities、停權／撤銷、多角色授權；企業擁有registration與secret/key custody | 原碼有Entra login路徑不代表租戶實測完成。不得靠姓名/fuzzy email 自動授權；現有 email-binding行為若與正式政策不符由IT另行審查。Demo正式禁用；本次不重做auth |
| HR/RBAC/scope | HR facts/status periods與role/team/Org/ownership責任、時間邊界、帳號建立/離職/留停、export columns、審核獨立性、audit | 撤銷與跨scope拒絕配對case及IT自驗；不只看UI或單一role flag |
| Feature activation | server/client旗標、相依schema/permission/provider、default OFF、activation/rollback順序、批准人與artifact | 預設安全OFF，schema與source齊備才可另授權開；沒有默認GO或部署即啟用 |
| Backup/recovery | 當前DB/PITR及Blob retention/ownership、restorable point、RPO/RTO、restore-to-target隔離／校驗／cutover、matched API/Frontend/config rollback pair | IT在另授權隔離環境實際驗證build/import/deployment/recovery；plan不是PASS，舊retention不是現在證據；不drop schema當rollback |
| Monitoring/ops | health/live與DB-touch ready區分、provider fallback、background jobs/revocation/outbox/allowlist、錯誤分類/告警/儲存與PII保護、incident owner/STOP/escalation | 只輸出sanitized狀態、source/hash/correlation；保護identities/SQL errors/provider payload；無IT前不私自擴權或修Azure |
| Troubleshooting | SQL timeout/40613→停、辨識serverless狀態、沿用accepted readiness邊界；錯source/schema/permission→隔離而非重試；privacy錯→HOLD；不從不同failure推同root cause | 冷啟動/重試/費用/修配置要另授權；不用Owner寫SQL/提供secret或安裝工具 |
| UAT/known limits | 具名Business signoff、版本case矩陣、缺陷disposition、mock/live限制、未證明項目、source/evidence locator | 全部必測有結果且當版對應，不能沿用007/P2B READY文案；G2未簽核 |
| Retirement/responsibility | 第 8 節資料分類、evidence保留、backup排除、exacttarget授權、resource ownership、runbooks/存取移交與時點 | 接收人具名確認可操作；Owner接受業務範圍，IT接受技術營運；沒有接收人／權限／實測就HOLD |

### IT 接受簽核（目前全部未勾選）

- [ ] 接收人/團隊、接收日期、公司擁有的repo/cloud/domain/storage/identity與必要合法存取已確認，沒有把Owner個人帳密當交付物。
- [ ] 可取得 matched exact source/binary/config/schema，能執行核准 build、synthetic import/reject、部署及隔離復原驗證並自行讀取結果。
- [ ] clean-build/re-run/partial failure、SQL permissions、Entra/HR/RBAC、feature activation、外部費用/通知與監控責任已驗證或明列接收條件；Production未完成項目仍不GO。
- [ ] Business UAT signoff、known limits、未解項負責人/期限及證據保存清楚。
- [ ] UAT retirement不得先於Business簽核與IT接受；backups/retention/合法保存不隨測試資料一起自動刪除。
- [ ] IT acceptance record：受控材料版本/hash、實測run/evidence、接收人/Owner批准、責任生效時間。未能自驗不可簽 IT HANDOVER PASS。

## 10. 最小後續順序與 Owner 合併決策草案

先獨立唯讀審查這份 scope/gate/缺件，再由 Control Tower 準備一份帶明確材料與停止條件的單一後續授權。不得自動啟動下一個 WORK。

**DRAFT — NOT AUTHORIZED。這份 draft 目前不授予任何操作。**

> 接受目前 UAT rows 是可於日後受控退役的測試資料；正式資料來自另外核准輸入。接受 original 1.5 provenance 缺失如實記錄，改以獨立可重現的新 clean baseline 作 IT Handover 前 G3 gate。這不豁免 G1 runtime/source/schema/security/recovery 或 G2 Business signoff。
>
> 在獨立審查接受本矩陣、固定後續 exact source/artifacts/target與restricted evidence方案後，才可提交一次整合的 NONDEPLOY baseline取得／重建與import-adapter/dry-run補證據工作授權。ChatGPT須先填好 source/object契約、必要取得方法、排除資料、測試範圍與費用邊界；Owner不用寫SQL或挑架構。若需要schema-only UAT取得、Azure management reads、wake或費用，必須在同一份未來授權內明列 exact方法/target/SQL hashes/呼叫次數/資料類別/金額或時窗上限與STOP；未明列就不得執行。
>
> 本草案不授予 Protected/main merge、A+C promotion、011–020 migration、部署、旗標、真實資料匯入、UAT開測、Entra/permissions/storage變更、資料退役或Production GO。這些各有exact-target條件，待材料可審查時在一份明確的後續 release決策中列准/不准，不用多次零碎技術確認；不得把一次同意當永久執行許可。

## 11. 獨立 READ-ONLY Delta AUDIT 清單

- 核對本分支從指定Protected SHA/tree建立；讀exact head/parent/tree/diff、Draft PR及CI/artifact digest；只應有此package與NONDEPLOY文檔provenance workflow。
- 重算 source-ledger 每個sourceSha/path的bytes hash；Protected、A+C、#47、原worktrees/stashes/history保持不變。
- 核對A/B/C/D時點；D只移除UAT-test-row搬遷/原始1.5追溯前提，不免除G1/完整性/安全/復原；沒有把CleanBuild PASS或Production要求冒稱已完成。
- 逐讀1600/1700/1800與A+C候選guard/013/模型；確認真缺baseline時沒有EnsureCreated、合成fixture或marker冒充empty build。
- 確認過時部署/source/schema/READY文案已標歷史；accepted工程不因舊doc的obsolete HOLD重新開案。C1只作前置證據。
- 核對Business UAT矩陣的當版語意、人工必測/既有自動證據界線、匯出role/columns、iPhone；無runtime GO。
- 核對XLSX與API sheet差異、Preview staging writes、update-only/缺adapter/缺rate路徑；沒有呼叫API／真實匯入或模糊配對。
- 核對退役順序、IT自驗/接收、backup/PITR/Blob未知、三個storage排除、獨立刪除授權；未提供會自動執行的刪除/restore指令。
- 檢查新workflow僅contents:read、無Azure/OIDC/environment/dispatch/deploy/SQL；artifact只有文件/hashes，不含catalog/HR/backup payload或secrets。
- 最後決策：可接受本NONDEPLOY Gate package；但 Clean Build 維持 HOLD_WITH_EXACT_MISSING_EVIDENCE，UAT/IT/Production未被授權或認證。
