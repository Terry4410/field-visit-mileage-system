# 外訪行程與里程管理系統 v1.8.0 — Business UAT 使用指南

> **目前狀態 — HOLD**
>
> - Technical Readiness：**HOLD — Path B 安全及恢復 Gate 尚未完成**
> - Business User Access：**NOT RELEASED**
> - Business UAT：**NO-GO / NOT STARTED**
> - 本指南保留既有 25 項業務案例；不得以此文件表示目前已有可用環境或測試已通過。
> - 現有 63 表／26 筆歷史資料升級（Path A）與全新隔離 UAT（Path B）分開執行。

## 1. UAT 目的

Business UAT（User Acceptance Testing，使用者驗收測試）的目的，是由實際業務使用者確認系統流程是否符合工作需求。

本階段重點包括：

- 確認實際業務流程是否合理、可操作。
- 確認資料顯示是否正確且容易理解。
- 確認不同角色看到的功能與資料範圍是否符合授權。
- 確認建立、送出、審核、查詢、更正與匯出等流程是否符合實務。
- 本環境不是 Production，不應作為正式營運資料來源。
- 發現問題不代表 UAT 失敗；請將問題記錄、分類並追蹤處理。

## 2. UAT 環境

| 項目 | 內容 |
|---|---|
| Business UAT URL | **尚未核發；不得使用下列歷史網址** |
| Release | PR #83 Path B 候選；尚未部署 |
| Environment | 新隔離 UAT；尚未建立 |
| Backend / DB | 尚未建立或配置 |
| Technical readiness | **HOLD** |
| Effective runtime flags | **尚未從新環境取證** |
| Recovery rehearsal | **尚未完成** |
| 既有資料庫 Path A | 保留 63 張表及 26 筆歷史標記；不得在 Path B 使用或覆蓋 |

不要使用任何舊版 GitHub Pages UAT 網址。正式網址及測試開始通知只會在安全 Gate、部署和回復驗證通過後提供。

## 3. 測試帳號

只可使用新隔離 UAT 建立並核准的測試帳號。此環境尚未建立，因此目前沒有可用帳號或密碼。

| 角色 | UAT 測試帳號狀態 |
|---|---|
| Visitor / 外訪員 | 尚未建立 |
| Leader / 小組長 | 尚未建立 |
| Admin / 管理者 | 尚未建立 |

不要使用舊環境帳號或 Production 帳號。測試憑證會在部署與角色隔離驗證後，透過 Owner 核准的私密管道交付；不得寫入測試案例、截圖檔名、Issue、Email 主旨或 repository。

## 4. 四種角色

### Visitor / 外訪員

主要負責自己的外訪行程，可：

- 建立、儲存及送出外訪行程。
- 修改尚可修改的行程並重新送出。
- 查詢自己的歷史行程。
- 使用正式地點或臨時地點。
- 選擇專案、拜訪形式與行程小組。
- 輸入自算里程。
- 對已核准行程提出更正申請。

### Leader / 小組長

負責授權小組的行程管理，可：

- 查看授權小組的行程與 Dashboard。
- 檢視系統里程計算結果。
- 核准或退回指定行程。
- 管理授權範圍內的地點。
- 審核更正申請。

### Admin / 管理者

負責 UAT 環境內的管理功能，包括：

- 人員與權限。
- 小組與成員。
- 地點主檔。
- 專案管理。
- 拜訪形式。
- 補助費率。
- 全組織授權範圍的行程查詢。
- 更正管理與結案。

### Supervisor / 督導

以查詢與匯出為主：

- 唯讀查詢授權資料。
- Excel 匯出。
- PDF 匯出。
- 不應提供可修改業務資料的功能。

## 5. 建議 UAT 執行順序

建議依下列順序執行，方便追蹤同一筆測試資料的完整生命週期：

1. **A. Login / Role / Navigation**：先確認帳號、角色與選單。
2. **B. Visitor 建立行程**：建立 Draft、輸入地點與相關欄位並送出。
3. **C. Leader 計算里程與核准**：確認系統里程、審核、核定與退回。
4. **D. Query / Excel / PDF**：確認條件篩選與匯出內容。
5. **E. Return → Modify → Resubmit**：確認退回後可修改並重新送出。
6. **F. Approved Trip Correction**：確認已核准行程的更正流程。
7. **G. Admin Master Data controlled tests**：僅由指定 Admin 測試者執行受控主檔案例。
8. **H. Supervisor read-only**：確認督導僅能查詢與匯出。
9. **I. Mobile / iPhone**：確認手機版主要操作可正常使用。

## 5.1 P2B 里程決策與正式送出規則

- Visitor 可儲存 Draft，但**正式送出至少需要 2 stops**。
- Path B UAT 必須使用伺服器端 Google Routes provider；API Key 只由 UAT Secret 設定提供，禁止放入前端、原始碼、日誌或測試證據。Google Routes 成功時須保存系統距離及可追蹤的路線嘗試識別。
- Google Routes 失敗時，必須留下可追蹤失敗結果並明確轉人工備援；不得靜默填入系統距離或預選人工決策。
- Leader 核准需在畫面上明確選擇「公司里程決策依據（必選）」。
- ManualFallback payload 為 `DistanceDecisionSource="ManualFallback"`、`RouteCalculationAttemptId=null`。
- 真實 Maps 成功與失敗備援均為凍結的 UAT 驗收案例（VIS02／VIS03）；Mock、單元測試或隔離 SQL 測試不能替代。若 Google Key／服務授權尚未完成，Path B 保持 HOLD。
- 更正若沒有改變核定里程，沿用原 Approved Snapshot 的距離決策證據；若核定里程改變，Admin close 形成新的公司距離決策。
- Business User 不需也不應操作 SQL、migration 或 Run 17 residual cleanup。

## 6. UAT 資料使用原則

- 僅使用 **UAT 資料**，不使用 Production data。
- 除非 test case 明確要求，不修改既有基準 master data。
- 新增測試資料需使用容易辨識的 UAT prefix。
- 建議命名格式：`UAT-YYYYMMDD-Tester-xxx`。
- 不隨意停用既有 Project、Visit Type 或 Rate。
- 管理者如需測試新增主檔，應建立專用 UAT 測試資料。
- 測試完成後，依案例要求決定保留或停用測試資料。
- Business User 不執行 SQL。
- Business User 不需要操作 GitHub 或 Azure。
- 不將 UAT 測試資料誤認為正式 Production 主檔。

## 7. 本次驗收範圍

### Visitor / 外訪員

- 建立草稿。
- 新增正式地點。
- 使用臨時地點。
- 選擇專案 / 拜訪形式。
- 選擇行程小組。
- 輸入自算里程。
- 正式送出。
- 修改 / 重送。
- 歷史查詢。
- 更正申請。

### Leader / 小組長

- Dashboard。
- 行程審核。
- 系統里程背景工作。
- 核定里程。
- 核准。
- 退回。
- 地點管理。
- 更正審核。

### Admin / 管理者

- Dashboard。
- 人員與權限。
- 小組與成員。
- 地點主檔。
- 專案管理。
- 拜訪形式。
- 補助費率。
- 行程查詢。
- 更正管理。

### Supervisor / 督導

- Dashboard。
- 行程查詢。
- Excel。
- PDF。
- read-only validation。

### Cross-role / 跨角色

- Visitor → Leader → Query。
- Return → Resubmit。
- Approved correction → Leader → Admin。
- Snapshot / history preservation。

## 8. 非本次 Business UAT 驗收項目

以下項目不屬於本次 Business UAT 驗收範圍：

- Production deployment。
- Production Entra ID。
- Production security / penetration test。
- Production monitoring / SIEM。
- Production backup / restore。
- Production Google Routes usage and Production credentials.

Path B Business UAT still requires a controlled UAT Google Routes key and real success/failure verification for VIS02 and VIS03. UAT key, quota/billing and key restrictions must be approved before enabling the provider. This section does not waive or replace those two cases.
- Production data migration。
- Production master-data import。

目前 repository 的預設 Route Provider 仍為 **Mock**。Path B UAT 部署時必須透過新環境的受控設定切換到 Google Routes，並以私密 UAT Key 執行 VIS02 成功與 VIS03 失敗轉人工備援。上方兩項為必要 acceptance criteria；此段說明預設值，並不表示真實 Maps 測試已通過。

## 9. v1.8.0 Master Data 說明

參考文件：

`docs/release/POST-UAT-v1.8.0-UAT-MASTER-DATA-READINESS.md`

Path B 安全放行後，Business UAT 才可使用新隔離環境提供的受控資料開始測試。

另有一份 master-data workbook，供未來由 Business 確認並核准下列資料：

- Centers。
- Team-Center。
- Deployment Sites。
- Team-Site。
- Employment-Site。
- Mileage Rates。

該 workbook 的資料內容、關係、日期與費率必須由 Business / HR data owner 確認。開始 Business UAT **不代表**已授權任何 master-data import，也不代表可以自行推導、補填或匯入缺少的資料。

任何後續 import 都必須另外完成 Business sign-off、結構與關聯驗證、preview / reject review，以及獨立的執行授權。

## 10. 問題回報方式

發現問題時，請至少記錄下列資訊：

- Test Case ID。
- Tester。
- Role / Account。
- Date / Time。
- Browser / Device。
- Steps。
- Expected Result。
- Actual Result。
- Screenshot。
- Trip No / record identifier（如適用）。

### Severity 定義

| Severity | 定義 |
|---|---|
| BLOCKER | 核心業務流程無法繼續，且目前沒有可接受的替代方式。 |
| MAJOR | 核心功能結果不正確，但可能存在暫時 workaround。 |
| MINOR | 非核心的顯示、文字、操作便利性或其他 usability 問題。 |
| QUESTION | 業務規則或預期行為需要進一步釐清，尚不能直接判定為 defect。 |

若案例結果為 FAIL 或 BLOCKED，請建立或填寫對應 Issue ID，避免問題只存在於口頭或聊天紀錄。

## 11. UAT 完成條件

Business UAT 達到可進入 sign-off 的條件如下：

- 所有 mandatory P0 cases 都已執行。
- 沒有 unresolved BLOCKER。
- 所有 MAJOR issues 都有明確 disposition。
- Business rule questions 已解決，或由 Business 明確認可為 known items。
- 測試證據、Issue ID 與必要 record identifier 已完成記錄。
- Business Owner 完成 Business UAT Sign-off 文件。

在完成 Path B 安全 Gate、部署、有效旗標取證、成對復原演練並核發新網址／帳號前：

**TECHNICAL READINESS = HOLD**

**BUSINESS USER ACCESS = NOT RELEASED**

**BUSINESS UAT = NO-GO / NOT STARTED**
