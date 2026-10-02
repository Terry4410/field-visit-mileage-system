SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-008')
    THROW 54500, N'Verify failed: 找不到 SchemaVersion 1.8.0-008。', 1;

IF OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries', N'TR') IS NULL
    THROW 54501, N'Verify failed: MileageRate protection trigger 不存在。', 1;

DECLARE @Definition NVARCHAR(MAX) =
    OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
DECLARE @Normalized NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(@Definition, N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''));

IF @Definition IS NULL
    THROW 54502, N'Verify failed: 無法讀取 MileageRate protection trigger definition。', 1;

IF CHARINDEX(N'sys.sp_getapplock', @Normalized) = 0
   OR CHARINDEX(N'fieldvisit.mileagerateseries', @Normalized) = 0
   OR CHARINDEX(N'@lockmode=n''exclusive''', @Normalized) = 0
   OR CHARINDEX(N'@lockowner=n''transaction''', @Normalized) = 0
   OR CHARINDEX(N'@locktimeout=10000', @Normalized) = 0
    THROW 54503, N'Verify failed: MileageRate exact-series transaction lock 不完整。', 1;

IF CHARINDEX(N'lead(', @Normalized) > 0
   OR CHARINDEX(N'updatedbo.mileageraterules', @Normalized) > 0
   OR CHARINDEX(N'database-derived', @Normalized) > 0
    THROW 54504, N'Verify failed: trigger 仍含自動衍生/改寫 EffectiveTo 邏輯。', 1;

IF CHARINDEX(N'r.effectiveto<r.effectivefrom', @Normalized) = 0
   OR CHARINDEX(N'activeexact-seriesoverlapdetected', @Normalized) = 0
    THROW 54505, N'Verify failed: trigger 缺少日期合法性或 active overlap protection。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.MileageRateRules
    WHERE EffectiveTo IS NOT NULL
      AND EffectiveTo < EffectiveFrom
)
    THROW 54506, N'Verify failed: MileageRateRules 存在失效日早於生效日。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.MileageRateRules a
    JOIN dbo.MileageRateRules b
      ON (a.OrganizationId = b.OrganizationId OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL))
     AND a.VehicleType = b.VehicleType
     AND a.MileageRateRuleId < b.MileageRateRuleId
     AND a.IsActive = 1
     AND b.IsActive = 1
     AND a.EffectiveFrom <= COALESCE(b.EffectiveTo, CONVERT(date, '99991231'))
     AND b.EffectiveFrom <= COALESCE(a.EffectiveTo, CONVERT(date, '99991231'))
)
    THROW 54507, N'Verify failed: active MileageRateRules 期間重疊。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.MileageRateRules
    WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
          (N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2, N'CAR' COLLATE Latin1_General_100_BIN2)
)
    THROW 54508, N'Verify failed: VehicleType 非 canonical MOTORCYCLE/CAR。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-008' AS MigrationVersion,
    COUNT_BIG(*) AS MileageRateRuleCount,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveMileageRateRuleCount
FROM dbo.MileageRateRules;
