# IT Handover

## 正式技術基準

- ASP.NET Core .NET 8+
- Microsoft SQL Server 2019+
- Microsoft Entra ID
- Web/API DMZ；Database Intranet

## 可替換 Integration Point

| ID | UAT | PROD |
|---|---|---|
| Auth | Demo JWT / EmployeeNo | Microsoft Entra ID |
| DB | Azure SQL | Intranet SQL Server 2019+ |
| Route | MockRouteCalculationService | Google Routes Provider |
| Geocoding | MockGeocodingService | Google Geocoding Provider |
| Hosting | Azure App Service | IIS / Container / 公司平台 |
| Secret | App Settings | Key Vault / 公司 Secret 平台 |

## 重要原則

- Controller 不直接寫 EF 查詢或核心業務規則。
- Application 不直接依賴 Google SDK / SQL Connection。
- Infrastructure 直接 Mapping 既有 Schema 1.5.0，不自行建立第二套 Table。
- `VisitTrips.RowVersion / Locations.RowVersion` 作為 optimistic concurrency。
- `MileageCalculations` 是三種里程與補助快照的來源。
- `MileageRateRules` 使用 EffectiveFrom / EffectiveTo 選擇行程日期有效費率。
- API Token / SQL 密碼 / Google Key 不得寫入 Audit 或 Git。
