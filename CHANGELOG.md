# Changelog

## UAT Existing-Schema v1.0

- 改為直接 Mapping Azure SQL Schema 1.5.0。
- 使用 `VisitTrips / VisitTripStops / MileageCalculations / MileageRateRules / ApprovalRecords`。
- 支援時間重疊 Warning 與確認後送出。
- 支援外訪員草稿、送出、退回修改、重新送出與歷史查詢。
- 支援小組長全部未計算 / 日期區間批次里程。
- 支援單筆 / 批次核准與退回。
- 核准時保存 Rate / Approved Amount 快照。
- 支援 Pending Location 日期區間、地址 / Plus Code、批次發布。
- 支援管理者費率生效區間維護。
- UAT Route / Geocoding 採 replaceable Mock Provider。
- GitHub Actions：API → Azure App Service；Frontend → GitHub Pages。

## UAT v1.5.5 - Admin Master Data CRUD

- 管理者可新增／修改／刪除（安全停用）補助費率。
- 管理者可新增／修改／刪除（安全停用）專案主檔。
- 管理者可新增／修改／刪除（安全停用）拜訪形式主檔。
- 外訪員／小組長只讀取啟用且有效的專案與拜訪形式；管理者可檢視停用主檔。
- 刪除採 soft deactivate，保留歷史行程、核准與費率快照的一致性。

## UAT v1.5.6 - Draft Delete & Excel Query Export

- 外訪員歷史紀錄中的 Draft 草稿新增刪除功能。
- 草稿刪除採 Cancelled 軟刪除並保留 Audit / Status History，不影響稽核追蹤。
- 已刪除草稿不再出現在外訪員歷史、管理／督導報表及小組長待上傳地點清單。
- 所有既有下載功能由 CSV 統一升級為真正的 Excel .xlsx。
- 小組長「地點管理」查詢新增「下載查詢結果」。
- 修正貨幣畫面可能顯示 $$2.50 的重複貨幣符號。

## UAT v1.5.7 - Simplified Visitor Stop Editor

- 新增拜訪地點不再要求使用者先判斷「地點清單／專案地點／臨時地點」三種排他來源。
- 改為「專案（選填）」與「地點取得方式」兩個獨立概念。
- 地點取得方式固定為「從既有地點選擇」或「臨時新增地點」。
- 固定清單型專案預設顯示專案地點，但仍可切換臨時新增地點。
- 自行維護型專案預設臨時新增，但仍可切換既有正式地點。
- 既有地點搜尋結果改為先選取，再按「加入行程」，不再點搜尋結果立即加入。
- Modal 底部固定顯示「取消／加入行程」，避免手機捲動後找不到操作按鈕。
- 行程目的維持每個拜訪地點各自維護且為選填。
- 不變更 Azure SQL Schema 與既有 API Contract。
## UAT v1.6.0 - Phase 1 Foundation

- 建立 v1.6.0 Requirement Freeze 與開發基線。
- 新增 UserTeamScopes / Snapshot / Correction / Background Job 的 Domain 與 DB Migration Foundation。
- 手機版導入最低 44px 觸控高度、16px 表單字級與固定底部操作等 Mobile-first 基線。
- 管理者 Dashboard 移除補助費率管理與 UAT 架構大卡片。
- 拜訪形式「排序」改名「顯示順序」，新增時自動使用目前最大值 + 10。
- 管理者現行報表查詢補上「下載查詢結果」。
- Google Routes 仍維持 Mock Provider，正式串接保留 v1.7.0。

## UAT v1.6.0 Phase 2 - Multi-Team Scope & Approved Snapshot

- CurrentUser 正式加入 TeamScopes，JWT 同步攜帶多小組授權。
- 小組長 Review Queue、批次里程、Master Data 與 Trip Data Scope 改為支援多個授權小組。
- Admin / Supervisor 單筆 Trip 查詢新增 Organization Data Scope 驗證。
- 外訪員送出規則調整：至少 1 個地點即可送出；只有 2 個以上地點才要求自算里程並進行系統里程／補助流程。
- 單一地點行程直接進 PendingApproval，核准後里程／費率／補助保留為 N/A。
- Approved 時自動建立 VisitTripSnapshot / VisitTripSnapshotStops v1 歷史快照。
- API 在建立 Stop 時重新驗證 Location / Project / VisitType 的啟用狀態與資料範圍，不信任前端傳入的 Reference ID。
- 外訪員新行程的自算里程不再預設 40 km，避免 UAT 產生假資料。

