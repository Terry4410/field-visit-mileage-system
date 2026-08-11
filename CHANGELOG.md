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

