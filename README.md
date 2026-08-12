# 外訪行程與里程管理系統 v1.6.0 FINAL Release Candidate

> 狀態：**Release Candidate / Codespaces Verified**
> Baseline：`main@4bc7ab6706485d490ce7be0a4fb86d2c92e8381a`
> Application：v1.6.0｜DB Schema：1.6.0｜Route/Geocoding：Mock (UAT)
> 正式目標：React + TypeScript → ASP.NET Core .NET 8+ → SQL Server 2019+ / Azure SQL → Microsoft Entra ID

本版本以 `docs/requirements/v1.6.0-requirement-freeze.md` 為唯一需求基準，完成 v1.6.0 UAT Architecture Consolidation。Google Routes、Google Production Geocoding、Production Entra ID 與 DMZ/Intranet 正式部署不在 v1.6.0 範圍。

## v1.6.0 完成範圍

- Leader 多小組 `UserTeamScopes`、Admin 角色/Team Scope 維護。
- Approved 歷史資料以 Snapshot 為唯一查詢/報表來源；既有 Approved 資料於 Migration Backfill。
- Approved 更正：Visitor 申請 → Leader 審核 → 財務性更正由 Admin 結案 → Snapshot V2+。
- 1 個地點可送出/核准但里程、費率、補助為 N/A；2 個以上才進里程流程。
- Unified Query：日期、小組、外訪員、地點、專案、拜訪形式、狀態；Server-side pagination。
- 同一 Query Definition 產生畫面、Server-side Excel（三工作表）與 PDF。
- LocationCode、地點/專案 Excel Template → Preview → Validate → Error Excel → Confirm → Result。
- 補助費率由生效日自動銜接前後版本；同日重複版本禁止。
- Mileage / Geocoding Background Jobs；Leader Geocoding 支援全部未處理、日期區間、勾選。
- Mobile-first 觸控與字級基準。
- Backend Role / Organization / Team Scope / Ownership / Reference ID 驗證。
- `/health/live`、`/health/ready`、CI Build/Test/Deploy Gate。

## 套用前必要條件

1. Repository 必須是 `Terry4410/field-visit-mileage-system`。
2. `git rev-parse HEAD` 必須等於：

   `4bc7ab6706485d490ce7be0a4fb86d2c92e8381a`

3. tracked files 必須沒有未 commit 變更。
4. **不要先執行 SQL**；先套用程式並 Build/Test。

## 建議流程

請依 `docs/release/DEPLOY_v1.6.0.md` 執行：

`Apply RC → Static Verify → Frontend Build/Test → Backend Build/Test → Azure SQL Up.sql → Verify.sql → Commit/Push → GitHub Actions → UAT Smoke Test`

## 狀態用語

- 本 ZIP：**Release Candidate / Codespaces Verified**。
- Codespaces `npm test/build` + `dotnet build/test` 成功：**Build Verified**。
- Azure SQL `Verify.sql` 全 PASS / ErrorCount=0：**DB Verified**。
- GitHub Actions + 實際瀏覽器 Smoke Test 成功：**UAT Deployed**。

本環境無 .NET SDK、無 Azure SQL 連線、無 Azure App Service Runtime，因此不得把本 RC 稱為 Build Verified / DB Verified / UAT Deployed。
