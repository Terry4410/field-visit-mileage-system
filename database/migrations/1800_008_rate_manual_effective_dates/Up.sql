SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'FieldVisit.SchemaMigration',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;

    IF @LockResult < 0
        THROW 54400, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
        THROW 54401, N'尚未套用 prerequisite Migration 1.8.0-007。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-008')
        THROW 54402, N'Migration 1.8.0-008 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries', N'TR') IS NULL
        THROW 54403, N'找不到既有 MileageRate series protection trigger；停止 Migration。', 1;

    DECLARE @OldTriggerDefinition NVARCHAR(MAX) =
        OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
    DECLARE @OldTriggerNormalized NVARCHAR(MAX) =
        LOWER(REPLACE(REPLACE(REPLACE(REPLACE(@OldTriggerDefinition, N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''));

    IF @OldTriggerDefinition IS NULL
       OR CHARINDEX(N'lead(r.effectivefrom)', @OldTriggerNormalized) = 0
       OR CHARINDEX(N'throw53839', @OldTriggerNormalized) = 0
       OR CHARINDEX(N'fieldvisit.mileagerateseries', @OldTriggerNormalized) = 0
        THROW 54404, N'既有 MileageRate trigger 與 1.8.0-005 baseline 不一致；請由 IT Review。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MileageRateRules
        WHERE EffectiveTo IS NOT NULL
          AND EffectiveTo < EffectiveFrom
    )
        THROW 54405, N'既有 MileageRateRules 存在失效日早於生效日；停止 Migration。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MileageRateRules
        WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
              (N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2, N'CAR' COLLATE Latin1_General_100_BIN2)
    )
        THROW 54406, N'既有 MileageRateRules 含非 canonical VehicleType；停止 Migration。', 1;

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
        THROW 54407, N'既有 active MileageRateRules 期間重疊；停止 Migration。', 1;

    EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_MileageRateRules_ProtectSeries
ON dbo.MileageRateRules
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS
    (
        SELECT 1
        FROM deleted d
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM inserted i
            WHERE i.MileageRateRuleId = d.MileageRateRuleId
        )
    )
        THROW 54430, N''MileageRateRules physical DELETE is prohibited; use soft lifecycle.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted
        WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
              (N''MOTORCYCLE'' COLLATE Latin1_General_100_BIN2, N''CAR'' COLLATE Latin1_General_100_BIN2)
    )
        THROW 54431, N''VehicleType must be canonical MOTORCYCLE or CAR.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted
        WHERE EffectiveTo IS NOT NULL
          AND EffectiveTo < EffectiveFrom
    )
        THROW 54432, N''MileageRate EffectiveTo cannot be earlier than EffectiveFrom.'', 1;

    DECLARE @Affected TABLE
    (
        OrganizationId INT NULL,
        VehicleType NVARCHAR(50) NOT NULL,
        ResourceName NVARCHAR(255) NOT NULL
    );

    INSERT @Affected(OrganizationId, VehicleType, ResourceName)
    SELECT DISTINCT
        s.OrganizationId,
        s.VehicleType,
        N''FieldVisit.MileageRateSeries|ORG=''
        + COALESCE(CONVERT(nvarchar(20), s.OrganizationId), N''GLOBAL'')
        + N''|VEHICLE='' + s.VehicleType
    FROM
    (
        SELECT OrganizationId, VehicleType FROM inserted
        UNION
        SELECT OrganizationId, VehicleType FROM deleted
    ) s;

    DECLARE @Resource NVARCHAR(255), @SeriesLockResult INT;
    DECLARE series_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT ResourceName
        FROM @Affected
        ORDER BY ResourceName COLLATE Latin1_General_100_BIN2 ASC;

    OPEN series_cursor;
    FETCH NEXT FROM series_cursor INTO @Resource;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC @SeriesLockResult = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = N''Exclusive'',
            @LockOwner = N''Transaction'',
            @LockTimeout = 10000;

        IF @SeriesLockResult < 0
        BEGIN
            CLOSE series_cursor;
            DEALLOCATE series_cursor;
            THROW 54433, N''Unable to acquire MileageRate exact-series transaction lock.'', 1;
        END;

        FETCH NEXT FROM series_cursor INTO @Resource;
    END;

    CLOSE series_cursor;
    DEALLOCATE series_cursor;

    DECLARE @CurrentRows BIGINT;
    SELECT @CurrentRows = COUNT_BIG(*)
    FROM dbo.MileageRateRules r WITH (UPDLOCK, HOLDLOCK)
    JOIN @Affected a
      ON (r.OrganizationId = a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))
     AND r.VehicleType = a.VehicleType;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MileageRateRules r WITH (UPDLOCK, HOLDLOCK)
        JOIN @Affected a
          ON (r.OrganizationId = a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))
         AND r.VehicleType = a.VehicleType
        WHERE r.VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
              (N''MOTORCYCLE'' COLLATE Latin1_General_100_BIN2, N''CAR'' COLLATE Latin1_General_100_BIN2)
    )
        THROW 54434, N''MileageRate canonical VehicleType final invariant failed.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MileageRateRules r WITH (UPDLOCK, HOLDLOCK)
        JOIN @Affected a
          ON (r.OrganizationId = a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))
         AND r.VehicleType = a.VehicleType
        WHERE r.EffectiveTo IS NOT NULL
          AND r.EffectiveTo < r.EffectiveFrom
    )
        THROW 54435, N''MileageRate date-range final invariant failed.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MileageRateRules a WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.MileageRateRules b WITH (UPDLOCK, HOLDLOCK)
          ON (a.OrganizationId = b.OrganizationId OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL))
         AND a.VehicleType = b.VehicleType
         AND a.MileageRateRuleId < b.MileageRateRuleId
         AND a.IsActive = 1
         AND b.IsActive = 1
         AND a.EffectiveFrom <= COALESCE(b.EffectiveTo, CONVERT(date, ''99991231''))
         AND b.EffectiveFrom <= COALESCE(a.EffectiveTo, CONVERT(date, ''99991231''))
        JOIN @Affected x
          ON (a.OrganizationId = x.OrganizationId OR (a.OrganizationId IS NULL AND x.OrganizationId IS NULL))
         AND a.VehicleType = x.VehicleType
    )
        THROW 54436, N''MileageRate active exact-series overlap detected.'', 1;
END;';

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-008',
        N'Owner Pre-UAT: administrator-owned MileageRate effective date ranges without automatic neighboring-date rewrite',
        SYSUTCDATETIME(),
        N'v1.8.0 Owner Pre-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-008 MileageRate manual effective-date governance completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
