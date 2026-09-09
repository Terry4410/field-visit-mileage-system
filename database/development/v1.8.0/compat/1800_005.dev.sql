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
        THROW 53800, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-004')
        THROW 53801, N'尚未套用 prerequisite Migration 1.8.0-004。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-005')
        THROW 53802, N'Migration 1.8.0-005 已套用，不得重複執行。', 1;

    IF COL_LENGTH(N'dbo.Projects', N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.Projects', N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Projects', N'RowVersion') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTypes', N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTypes', N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTypes', N'RowVersion') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules', N'CreatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules', N'UpdatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules', N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules', N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules', N'RowVersion') IS NOT NULL
        THROW 53803, N'偵測到 1.8.0-005 部分欄位已存在；請由 IT Review。', 1;

    IF EXISTS(SELECT 1 FROM dbo.Projects WHERE EndDate IS NOT NULL AND StartDate IS NOT NULL AND EndDate < StartDate)
        THROW 53804, N'Projects 存在 EndDate 早於 StartDate；停止 Migration，不自動修正。', 1;

    IF EXISTS(SELECT 1 FROM dbo.MileageRateRules WHERE EffectiveTo IS NOT NULL AND EffectiveTo < EffectiveFrom)
        THROW 53805, N'MileageRateRules 存在無效日期區間；停止 Migration。', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.MileageRateRules
        WHERE VehicleType NOT IN(N'Motorcycle', N'Car')
    )
        THROW 53806, N'MileageRateRules 存在非 Motorcycle/Car 車種；停止 Migration，不自動轉碼。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MileageRateRules a
        JOIN dbo.MileageRateRules b
          ON ISNULL(b.OrganizationId, -1) = ISNULL(a.OrganizationId, -1)
         AND b.VehicleType = a.VehicleType
         AND b.MileageRateRuleId > a.MileageRateRuleId
         AND a.IsActive = 1 AND b.IsActive = 1
         AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
         AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
    )
        THROW 53807, N'MileageRateRules 存在有效費率期間重疊；停止 Migration，不自動改價或日期。', 1;

    /* Reuse predecessor checks only after validating their normalized definitions. */
    DECLARE @ProjectsDateDefinition NVARCHAR(MAX) =
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(definition,
            N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), CHAR(13), N''), CHAR(10), N''), CHAR(9), N''))
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.Projects')
          AND name = N'CK_Projects_DateRange'
          AND is_disabled = 0 AND is_not_trusted = 0
    );
    IF @ProjectsDateDefinition IS NULL
       OR @ProjectsDateDefinition <> N'enddateisnullorstartdateisnullorenddate>=startdate'
        THROW 53808, N'Development compatibility failed: CK_Projects_DateRange definition differs from intended date-range semantics.', 1;

    DECLARE @RateVehicleDefinition NVARCHAR(MAX) =
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(definition,
            N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), CHAR(13), N''), CHAR(10), N''), CHAR(9), N''))
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.MileageRateRules')
          AND name = N'CK_MileageRateRules_VehicleType'
          AND is_disabled = 0 AND is_not_trusted = 0
    );
    IF @RateVehicleDefinition IS NULL
       OR @RateVehicleDefinition NOT IN
    (
        N'vehicletype=n''motorcycle''orvehicletype=n''car''',
        N'vehicletype=n''car''orvehicletype=n''motorcycle''',
        N'vehicletype=''motorcycle''orvehicletype=''car''',
        N'vehicletype=''car''orvehicletype=''motorcycle''',
        N'vehicletypeinn''motorcycle'',n''car''',
        N'vehicletypein''motorcycle'',''car'''
    )
        THROW 53809, N'Development compatibility failed: CK_MileageRateRules_VehicleType definition differs from intended Motorcycle/Car semantics.', 1;

    DECLARE @RateDateDefinition NVARCHAR(MAX) =
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(definition,
            N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), CHAR(13), N''), CHAR(10), N''), CHAR(9), N''))
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.MileageRateRules')
          AND name = N'CK_MileageRateRules_DateRange'
          AND is_disabled = 0 AND is_not_trusted = 0
    );
    IF @RateDateDefinition IS NULL
       OR @RateDateDefinition <> N'effectivetoisnulloreffectiveto>=effectivefrom'
        THROW 53810, N'Development compatibility failed: CK_MileageRateRules_DateRange definition differs from intended date-range semantics.', 1;

    DECLARE @RateValueDefinition NVARCHAR(MAX) =
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(definition,
            N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), CHAR(13), N''), CHAR(10), N''), CHAR(9), N''))
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.MileageRateRules')
          AND name = N'CK_MileageRateRules_Rate'
          AND is_disabled = 0 AND is_not_trusted = 0
    );
    IF @RateValueDefinition IS NULL
       OR @RateValueDefinition <> N'rateperkm>=0'
        THROW 53811, N'Development compatibility failed: CK_MileageRateRules_Rate definition differs from intended non-negative-rate semantics.', 1;

    ALTER TABLE dbo.Projects ADD
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.Projects WITH CHECK ADD
            CONSTRAINT FK_Projects_InactivatedByUser FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);';
    CREATE INDEX IX_Projects_Search
        ON dbo.Projects(OrganizationId, IsActive, StartDate, EndDate, TeamId)
        INCLUDE(ProjectCode, ProjectName);

    ALTER TABLE dbo.VisitTypes ADD
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.VisitTypes WITH CHECK ADD
            CONSTRAINT FK_VisitTypes_InactivatedByUser FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);';
    CREATE INDEX IX_VisitTypes_Active_Sort
        ON dbo.VisitTypes(IsActive, SortOrder, VisitTypeId)
        INCLUDE(VisitTypeCode, VisitTypeName);

    ALTER TABLE dbo.MileageRateRules ADD
        CreatedByUserId INT NULL,
        UpdatedByUserId INT NULL,
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.MileageRateRules WITH CHECK ADD
            CONSTRAINT FK_MileageRateRules_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
            CONSTRAINT FK_MileageRateRules_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
            CONSTRAINT FK_MileageRateRules_InactivatedByUser FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);';

    CREATE UNIQUE INDEX UX_MileageRateRules_Scope_Vehicle_Start
        ON dbo.MileageRateRules(OrganizationId, VehicleType, EffectiveFrom)
        WHERE IsActive = 1;
    CREATE INDEX IX_MileageRateRules_AsOf
        ON dbo.MileageRateRules(OrganizationId, VehicleType, IsActive, EffectiveFrom, EffectiveTo)
        INCLUDE(RatePerKm, RuleName);

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_MileageRateRules_NoActiveOverlap
        ON dbo.MileageRateRules AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.MileageRateRules x
                ON ISNULL(x.OrganizationId,-1)=ISNULL(i.OrganizationId,-1)
               AND x.VehicleType=i.VehicleType
               AND x.MileageRateRuleId<>i.MileageRateRuleId
               AND i.IsActive=1 AND x.IsActive=1
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53820, N''同一 Organization/VehicleType 的有效費率期間不得重疊。'', 1;
        END;';

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-005',
        N'Project soft lifecycle, Visit Type ordering concurrency and Motorcycle/Car effective rate governance',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-005 project / visit type / rate lifecycle completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
