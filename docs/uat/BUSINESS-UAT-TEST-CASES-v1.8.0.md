# 外訪行程與里程管理系統 v1.8.0 — Business UAT 測試案例

> **狀態**
>
> - Technical Readiness：**PASS**
> - Business UAT：**READY TO BEGIN / NOT YET SIGNED OFF**
> - 所有案例執行前，請確認使用的是 UAT 環境與指定 UAT 帳號。

## 使用方式

1. 依 Case ID 執行案例。
2. `Actual Result` 請記錄實際觀察結果。
3. `Result` 僅使用：**PASS / FAIL / BLOCKED / NOT RUN**。
4. FAIL 或 BLOCKED 必須填入 `Issue ID`。
5. 涉及新增資料時，請使用可辨識的 UAT prefix，例如 `UAT-YYYYMMDD-Tester-xxx`。
6. 不要為了完成案例而修改既有基準主檔；若缺少安全測試資料，應記錄 BLOCKED。

## Mandatory P0 Cases

| Case ID | Priority | Role | Scenario | Steps | Expected Result | Actual Result | Result | Issue ID |
|---|---|---|---|---|---|---|---|---|
| LOGIN-01 | P0 | Visitor / 外訪員 | Visitor login and navigation | 1. 使用 `pilotv01` 登入。<br>2. 確認登入後首頁。<br>3. 檢查側邊 / 手機可用選單。 | 可存取「今日行程」與「歷史紀錄」，且不出現未授權角色功能。 |  | NOT RUN |  |
| LOGIN-02 | P0 | Leader / 小組長 | Leader login and navigation | 1. 使用 `pilotl01` 登入。<br>2. 逐一檢查角色選單。 | 可存取「小組總覽 / 行程審核 / 行程查詢 / 地點管理」。 |  | NOT RUN |  |
| LOGIN-03 | P0 | Admin / 管理者 | Admin login and navigation | 1. 使用 `pilota01` 登入。<br>2. 逐一檢查管理者選單。 | 應正好可看到並開啟：管理儀表板、人員與權限、小組與成員、地點主檔、專案管理、拜訪形式、補助費率、行程查詢、更正管理。 |  | NOT RUN |  |
| LOGIN-04 | P0 | Supervisor / 督導 | Supervisor login | 1. 使用 `pilots02` 登入。<br>2. 檢查可用選單。<br>3. 確認沒有可修改主檔或業務資料的操作。 | 可使用「查詢總覽 / 行程查詢」，且不提供 edit / master-data action。 |  | NOT RUN |  |
| VIS-01 | P0 | Visitor / 外訪員 | Visitor saves Draft | 1. 建立新行程。<br>2. 輸入可辨識的 UAT 備註。<br>3. 儲存為 Draft。<br>4. 到歷史紀錄查詢。 | Draft 儲存成功，且可在歷史紀錄中找到。 |  | NOT RUN |  |
| VIS-02 | P0 | Visitor / 外訪員 | 2-stop trip using existing locations | 1. 建立 2-stop 行程。<br>2. 使用既有正式地點。<br>3. 視情況選擇 Project / Visit Type。<br>4. 輸入 claimed mileage。<br>5. 送出。<br>6. 記錄 Trip No。 | 行程送出成功，狀態與資料正確，Trip No 可記錄並後續追蹤。 |  | NOT RUN |  |
| VIS-03 | P0 | Visitor / 外訪員 | Temporary business location | 1. 建立測試行程。<br>2. 加入臨時業務地點。<br>3. 儲存或送出並重新檢視。 | 臨時地點可加入行程，且不會被無聲轉成 Production master data。 |  | NOT RUN |  |
| VIS-04 | P0 | Visitor / 外訪員 | 1-stop trip | 1. 建立只有 1 stop 的測試行程。<br>2. 依目前規則完成可填欄位。<br>3. 儲存 / 送出。 | 1-stop 行程可依目前 business rule 處理；里程 / 費率 / 補助在不適用時顯示 N/A 或符合既定規則。 |  | NOT RUN |  |
| VIS-05 | P0 | Visitor / 外訪員 | Draft edit / delete | 1. 僅使用本次新建 UAT Draft。<br>2. 修改欄位並儲存。<br>3. 再建立一筆可安全刪除的 UAT Draft。<br>4. 刪除指定 Draft。 | 修改成功；刪除只影響指定的 UAT Draft，不影響其他紀錄。 |  | NOT RUN |  |
| LEAD-01 | P0 | Leader / 小組長 | Leader sees Visitor submitted trip | 1. Visitor 先完成 VIS-02。<br>2. Leader 使用授權帳號登入。<br>3. 於審核 / 清單中尋找該 Trip No。 | 可看到正確 Visitor、Team 與 Trip 資料。 |  | NOT RUN |  |
| LEAD-02 | P0 | Leader / 小組長 | System mileage processing for 2+ stop trip | 1. 使用指定 2+ stop UAT Trip。<br>2. 依目前畫面觸發 / 等候系統里程處理。<br>3. 查看處理結果。 | 指定 UAT Trip 的背景工作建立並完成成功；不要求 Google 路線距離準確度。 |  | NOT RUN |  |
| LEAD-03 | P0 | Leader / 小組長 | Leader approves designated trip | 1. 開啟指定待審 Trip。<br>2. 檢視里程、費率、補助。<br>3. 完成核准。 | 狀態變為 Approved；核定里程 / rate / subsidy 依系統規則顯示。 |  | NOT RUN |  |
| LEAD-04 | P0 | Leader / 小組長 | Leader returns designated trip | 1. 使用另一筆指定 UAT Trip。<br>2. 輸入退回原因。<br>3. 退回。 | Visitor 可看到 Returned 狀態與退回原因。 |  | NOT RUN |  |
| VIS-06 | P0 | Visitor / 外訪員 | Visitor edits Returned trip and resubmits | 1. 開啟 LEAD-04 退回的 Trip。<br>2. 修改指定內容。<br>3. 重新送出。 | Returned Trip 可修改並再次送出，且狀態正確更新。 |  | NOT RUN |  |
| QUERY-01 | P0 | Leader / 小組長 | Leader query | 1. 開啟行程查詢。<br>2. 依序測試 date、visitor、keyword、project、visit type、status（如適用）。<br>3. 檢查組合條件。 | 各篩選條件與組合篩選正確套用，結果符合 Leader 授權範圍。 |  | NOT RUN |  |
| QUERY-02 | P0 | Admin / 管理者 | Admin query | 1. Admin 開啟行程查詢。<br>2. 使用可辨識的查詢條件。<br>3. 檢查跨小組結果。 | 可查看組織層級授權資料，且結果符合條件。 |  | NOT RUN |  |
| QUERY-03 | P0 | Supervisor / 督導 | Supervisor query | 1. Supervisor 開啟行程查詢。<br>2. 使用至少兩種篩選條件。<br>3. 檢查結果與可操作控制項。 | 授權範圍內的唯讀查詢正常，沒有資料修改功能。 |  | NOT RUN |  |
| QUERY-04 | P0 | Leader / Admin / Supervisor | Excel export | 1. 套用可辨識的查詢條件。<br>2. 執行 Excel 匯出。<br>3. 開啟下載檔並與查詢結果比對。 | Excel 代表目前完整 applied query 的結果，而不是只有目前畫面 page。 |  | NOT RUN |  |
| QUERY-05 | P0 | Leader / Admin / Supervisor | PDF export | 1. 套用可辨識的查詢條件。<br>2. 執行 PDF 匯出。<br>3. 檢查內容與中文字型。 | PDF 代表目前完整 applied query，且繁體中文可正常閱讀。 |  | NOT RUN |  |
| CORR-01 | P0 | Visitor / 外訪員 | Open Approved trip correction request | 1. 找到指定 Approved Trip。<br>2. 建立更正申請。<br>3. 記錄更正原因與必要識別碼。 | 可建立更正申請；原 Approved record 保留作為歷史參考。 |  | NOT RUN |  |
| CORR-02 | P0 | Leader / 小組長 | Leader reviews correction | 1. 開啟 CORR-01 更正案件。<br>2. 依目前規則判斷是否涉及財務影響。<br>3. 執行 Leader 審核。 | 非財務更正可依目前規則完成；財務更正於適用時繼續送 Admin。 |  | NOT RUN |  |
| CORR-03 | P0 | Admin / 管理者 | Admin closes financial correction | 1. 使用指定需 Admin 處理的財務更正。<br>2. 檢查原資料。<br>3. 完成結案。 | 產生新的 Snapshot version；原 Snapshot 仍保留。 |  | NOT RUN |  |
| CORR-04 | P0 | Admin / Leader / Supervisor | Query corrected trip | 1. 查詢 CORR-03 的 Trip。<br>2. 檢查最新 Snapshot。<br>3. 檢查歷史版本。 | 最新 Snapshot 可見，且歷史沒有被覆寫。 |  | NOT RUN |  |
| SUP-01 | P0 | Supervisor / 督導 | Supervisor read-only enforcement | 1. 查詢授權資料。<br>2. 測試 Excel / PDF 匯出。<br>3. 檢查是否存在 Admin / Leader mutation controls。 | 查詢與匯出可用；不得出現管理或修改業務資料的控制項。 |  | NOT RUN |  |
| MOBILE-01 | P0 | Visitor / 外訪員 | Visitor iPhone / mobile layout | 1. 使用約手機寬度的瀏覽器或 iPhone。<br>2. 使用 Visitor 登入。<br>3. 檢查手機導覽。<br>4. 開啟主要表單與 modal。 | Mobile navigation 可見；「首頁 / 紀錄」可使用；表單與 modal 不依賴 desktop-only 操作。 |  | NOT RUN |  |
| E2E-01 | P0 | Cross-role | End-to-end business journey | 1. Visitor 建立 2-stop Trip。<br>2. Submit。<br>3. Leader 執行 system mileage。<br>4. Leader Approve。<br>5. Supervisor / Admin Query。<br>6. Export。<br>7. Visitor correction。<br>8. Leader review。<br>9. 若為 financial correction，由 Admin close。<br>10. Query latest Snapshot。<br>11. 記錄 Trip No、Correction ID（如適用）、Snapshot Version。 | 同一筆 business record 可由建立一路追蹤至核准、查詢、匯出、更正與最新 Snapshot；歷史脈絡可辨識。 |  | NOT RUN |  |

## Controlled P1 Admin Cases

> **僅指定 Admin UAT tester 可執行。**
>
> 只使用本次新建且帶 UAT prefix 的 records。不要為了完成案例而修改既有 baseline pilot accounts 或既有基準 rate / master data。

| Case ID | Priority | Role | Scenario | Steps | Expected Result | Actual Result | Result | Issue ID |
|---|---|---|---|---|---|---|---|---|
| ADM-01 | P1 | Admin / 管理者 | Create / edit / deactivate UAT Project | 1. 建立新的 UAT-prefixed Project。<br>2. 修改允許欄位。<br>3. 依規則停用該測試 Project。 | 新增、修改、停用只影響指定 UAT Project，狀態與畫面一致。 |  | NOT RUN |  |
| ADM-02 | P1 | Admin / 管理者 | Create / edit / reorder / deactivate UAT Visit Type | 1. 建立新的 UAT-prefixed Visit Type。<br>2. 修改內容。<br>3. 調整順序。<br>4. 停用該測試 Visit Type。 | 指定 UAT Visit Type 可依規則新增、修改、排序與停用，不影響既有 baseline。 |  | NOT RUN |  |
| ADM-03 | P1 | Admin / 管理者 | Create / edit designated UAT Location | 1. 建立新的 UAT-prefixed Location。<br>2. 修改可編輯欄位。<br>3. 重新查詢。 | 指定 UAT Location 建立與修改成功，資料可正確查詢。 |  | NOT RUN |  |
| ADM-04 | P1 | Admin / 管理者 | Validate User / Role / Team Scope screen | 1. 開啟人員與權限。<br>2. 檢查指定 UAT 使用者的 role / team scope 顯示。<br>3. 除非另有明確核准，不變更 baseline pilot accounts。 | 畫面可正確呈現 User / Role / Team Scope；不因測試而修改 baseline pilot accounts。 |  | NOT RUN |  |
| ADM-05 | P1 | Admin / 管理者 | Mileage rate change impact warning | 1. 開啟補助費率功能。<br>2. 使用專用、安全的 UAT rate dataset 才執行變更測試。<br>3. 檢查 impact warning / 生效規則。 | 不得僅為通過測試而改既有 baseline rate。若沒有專用安全 UAT rate dataset，可記為 BLOCKED，原因：「Dedicated rate test data required.」 |  | NOT RUN |  |

## Evidence 要求

每一個 mandatory case 執行後，Business User 應至少記錄：

- Result：PASS / FAIL / BLOCKED / NOT RUN。
- FAIL / BLOCKED 時的 Issue ID。
- 必要時附 Screenshot。
- 涉及行程時記錄 Trip No。
- 涉及更正時記錄 Correction ID。
- 涉及 Snapshot 時記錄 Snapshot Version。

Business UAT 尚未執行完成前，不應將本文件標示為 signed off。
