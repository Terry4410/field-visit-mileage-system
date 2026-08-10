# Existing Schema 1.5.0 Mapping

本 API 依 `results(1).xlsx` 的 18 張原始 Table，以及已完成的 Gap Migration 1.5.0 直接 Mapping。

| Business | Existing Table / Column |
|---|---|
| 外訪員 / Team | Users.TeamId → Teams |
| 行程日期 / 時間 | VisitTrips.VisitDate / StartTime / EndTime |
| 時間 Warning | VisitTrips.HasTimeOverlapWarning / TimeOverlapConfirmed |
| 行程站點 | VisitTripStops |
| 自算里程 | MileageCalculations.ClaimedDistanceKm |
| 系統里程 | MileageCalculations.SystemDistanceKm |
| 核定里程 | MileageCalculations.ApprovedDistanceKm |
| 費率快照 | MileageCalculations.RatePerKmSnapshot |
| 補助快照 | MileageCalculations.ApprovedAmount |
| 費率版本 | MileageRateRules.EffectiveFrom / EffectiveTo |
| 核准 / 退回 | ApprovalRecords |
| 狀態歷史 | VisitTripStatusHistory |
| 地點 | Locations |
| 地點發布歷史 | LocationApprovalHistory |
| 專案 | Projects / ProjectLocations |
| 拜訪形式 | VisitTypes |
| Audit | AuditLogs |

## 不建立

- `Groups`
- `Trips`
- `TripStops`
- `SubsidyRates`
- `TripApprovals`

這些是早期 Gate 1 邏輯名稱，現在以 Existing Schema 為正式 UAT Mapping。
