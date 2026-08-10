# Azure SQL Existing Schema 1.5.0 → .NET 8 API → GitHub 部署 SOP

## 目前基準

- DB：`db-fieldvisit-uat`
- Schema：`1.5.0`
- API App Service：目前已建立，部署後用 `/health` 驗證
- Repo：`Terry4410/field-visit-mileage-system`
- Frontend API URL 已預填目前 UAT App Service Host；若 App Service 改名，再修改 `frontend/public/config.js`。

## 1. 資料庫

不要再執行舊版 `001_schema.sql`。

先執行：

```sql
-- database/015_schema_v1_5_verify.sql
```

若要建立示範帳號與主檔，再執行：

```sql
-- database/020_uat_demo_seed.sql
```

如果希望一登入就有「已核准 / 待批次里程」資料，再執行：

```sql
-- database/030_uat_sample_transactions.sql
```

## 2. Azure App Service Connection String

App Service → Settings → Environment variables → Connection strings：

Name：

```text
DefaultConnection
```

Value：

```text
Server=tcp:<你的 SQL Server>.database.windows.net,1433;
Initial Catalog=db-fieldvisit-uat;
User ID=<SQL Admin>;
Password=<SQL Password>;
Encrypt=True;
TrustServerCertificate=False;
Connection Timeout=30;
```

Type：`SQLAzure`。

## 3. App Settings

```text
Auth__Issuer = FieldVisit.UAT
Auth__Audience = FieldVisit.UAT.Users
Auth__JwtKey = 至少 32 字元隨機值
Auth__DemoPassword = 123456
Cors__AllowedOrigins__0 = https://terry4410.github.io
Cors__AllowedOrigins__1 = http://localhost:5173
```

Save → Restart。

## 4. GitHub Actions Secret / Variable

Repository → Settings → Secrets and variables → Actions。

Secret：

```text
AZURE_WEBAPP_PUBLISH_PROFILE
```

Value：Azure App Service Download publish profile 的完整內容。

Variable：

```text
AZURE_WEBAPP_NAME
```

Value：Azure App Service Resource Name，**不是完整網址**。

## 5. API 部署

把本專案內容放在 repository 根目錄後，進：

GitHub → Actions → `Deploy API to Azure App Service` → Run workflow。

成功後開：

```text
https://<你的-app-service>/health
https://<你的-app-service>/health/db
https://<你的-app-service>/swagger
```

預期 `/health/db`：

```json
{"status":"ok","database":"db-fieldvisit-uat"}
```

## 6. Frontend

`frontend/public/config.js` 必須指向：

```text
https://<你的-app-service>/api/v1
```

GitHub → Settings → Pages → Source：`GitHub Actions`。

Push main 後 `Deploy Frontend to GitHub Pages` 會自動執行。

## 7. Demo Login

執行 `020_uat_demo_seed.sql` 後：

```text
visitor01 / 123456
visitor02 / 123456
leader01  / 123456
admin01   / 123456
gov01     / 123456
```

密碼不是存 DB，而是 App Service `Auth__DemoPassword`。

## 8. 核心多人 UAT

1. 手機 A `visitor01` 建立並送出行程。
2. 電腦 B `leader01` 進入小組長作業。
3. 執行「全部未計算」或日期區間批次里程。
4. 核准，系統寫入 `ApprovalRecords`、`MileageCalculations`、`VisitTripStatusHistory`、`AuditLogs`。
5. 手機 A 歷史查詢看到核定里程、費率、補助。

## 9. UAT Provider

目前：

```text
IRouteCalculationService → MockRouteCalculationService
IGeocodingService → MockGeocodingService
```

這兩個 Provider 會真的更新 Azure SQL，但里程 / 座標不是 Google 正式結果。
正式串接時只替換 Infrastructure Provider，不改行程與簽核 Use Case。
