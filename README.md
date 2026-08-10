# 外訪行程與里程管理系統｜Azure SQL Existing Schema UAT

本版本直接對應目前 Azure SQL `SchemaVersions = 1.5.0` 的既有資料表，不建立第二套 `Trips / Groups / SubsidyRates`。

## UAT 架構

```text
GitHub Pages (React + TypeScript)
        ↓ HTTPS
Azure App Service (ASP.NET Core .NET 8)
        ↓
Azure SQL db-fieldvisit-uat
```

## 正式移交目標

```text
企業 Web / DMZ
→ ASP.NET Core .NET 8+
→ Microsoft SQL Server 2019+
→ Microsoft Entra ID
```

## Existing Schema 對應

- `Teams`：小組
- `VisitTrips`：行程主檔
- `VisitTripStops`：拜訪站點
- `MileageCalculations`：自算 / 系統 / 核定里程與補助快照
- `MileageRateRules`：每公里補助與生效區間
- `ApprovalRecords`：核准 / 退回紀錄
- `VisitTripStatusHistory`：狀態歷史
- `Locations / LocationApprovalHistory`：地點與發布紀錄
- `Projects / ProjectLocations / VisitTypes`：專案與拜訪形式
- `Users / Roles / UserRoles`：身分與業務角色
- `AuditLogs`：稽核

## 安裝順序

1. Azure SQL 已完成 Schema 1.5.0。
2. 執行 `database/020_uat_demo_seed.sql` 建立 UAT 示範帳號 / 主檔（可選，但建議）。
3. App Service 設定 Connection String / Auth / CORS。
4. GitHub 設定 `AZURE_WEBAPP_PUBLISH_PROFILE` 與 `AZURE_WEBAPP_NAME`。
5. Push `main`，API workflow 會建置與部署。
6. 修改 `frontend/public/config.js` 指向 App Service `/api/v1`。
7. GitHub Pages Source 選 GitHub Actions。
8. 執行 `scripts/uat-smoke-test.ps1`。

詳見 `docs/DEPLOYMENT_ZH-TW.md`。

## UAT Demo Auth

UAT 使用 `EmployeeNo + 共用 Demo Password`。建議 Seed 後：

- `visitor01` 外訪員
- `visitor02` 外訪員
- `leader01` 小組長
- `admin01` 管理者
- `gov01` 督導

正式版由 IT 將 Demo JWT Provider 替換為 Microsoft Entra ID；`Users.EntraObjectId` 已保留。

## Secret 原則

SQL Password、JWT Key、Google API Key、Entra Secret、Publish Profile 不可 commit 至 GitHub。
