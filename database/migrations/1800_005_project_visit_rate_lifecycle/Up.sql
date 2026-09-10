SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @MigrationLockResult INT;
    EXEC @MigrationLockResult = sys.sp_getapplock
        @Resource = N'FieldVisit.SchemaMigration',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;
    IF @MigrationLockResult < 0
        THROW 53800, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-004')
        THROW 53801, N'尚未套用 prerequisite Migration 1.8.0-004。', 1;
    IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-005')
        THROW 53802, N'Migration 1.8.0-005 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.Projects',N'U') IS NULL
       OR OBJECT_ID(N'dbo.VisitTypes',N'U') IS NULL
       OR OBJECT_ID(N'dbo.MileageRateRules',N'U') IS NULL
        THROW 53803, N'1.8.0-005 prerequisite master tables are missing.', 1;

    IF COL_LENGTH(N'dbo.Projects',N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.Projects',N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Projects',N'RowVersion') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTypes',N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTypes',N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTypes',N'RowVersion') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules',N'CreatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules',N'UpdatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules',N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules',N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageRateRules',N'RowVersion') IS NOT NULL
        THROW 53804, N'偵測到 1.8.0-005 partial schema；停止 Migration。', 1;

    IF EXISTS (SELECT 1 FROM dbo.Projects WHERE EndDate IS NOT NULL AND StartDate IS NOT NULL AND EndDate < StartDate)
        THROW 53805, N'Projects 存在 EndDate 早於 StartDate；停止 Migration。', 1;
    IF EXISTS (
        SELECT 1 FROM dbo.VisitTypes
        GROUP BY VisitTypeCode HAVING COUNT_BIG(*) > 1
    )
        THROW 53806, N'VisitTypes 存在重複 GLOBAL VisitTypeCode；停止 Migration。', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.MileageRateRules
        WHERE UPPER(LTRIM(RTRIM(VehicleType))) COLLATE Latin1_General_100_BIN2
              NOT IN (N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2, N'CAR' COLLATE Latin1_General_100_BIN2)
           OR VehicleType IS NULL
    )
        THROW 53807, N'MileageRateRules 包含未知 VehicleType；僅允許 MOTORCYCLE/CAR 的已知大小寫表示。', 1;

    IF OBJECT_ID(N'dbo.CK_MileageRateRules_VehicleType',N'C') IS NOT NULL
        ALTER TABLE dbo.MileageRateRules DROP CONSTRAINT CK_MileageRateRules_VehicleType;

    UPDATE dbo.MileageRateRules
       SET VehicleType = CASE
            WHEN UPPER(LTRIM(RTRIM(VehicleType))) COLLATE Latin1_General_100_BIN2 = N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2
                THEN N'MOTORCYCLE'
            WHEN UPPER(LTRIM(RTRIM(VehicleType))) COLLATE Latin1_General_100_BIN2 = N'CAR' COLLATE Latin1_General_100_BIN2
                THEN N'CAR'
            ELSE VehicleType
       END;

    IF EXISTS (
        SELECT 1
        FROM dbo.MileageRateRules
        WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
              (N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2, N'CAR' COLLATE Latin1_General_100_BIN2)
    )
        THROW 53808, N'VehicleType canonicalization failed.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.MileageRateRules a
        JOIN dbo.MileageRateRules b
          ON (
               a.OrganizationId=b.OrganizationId
               OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL)
             )
         AND a.VehicleType=b.VehicleType
         AND a.EffectiveFrom=b.EffectiveFrom
         AND a.MileageRateRuleId<b.MileageRateRuleId
         AND a.IsActive=1 AND b.IsActive=1
    )
        THROW 53809, N'MileageRateRules 同一 exact series 存在重複 active EffectiveFrom；停止 Migration。', 1;

    ALTER TABLE dbo.Projects ADD
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;
    EXEC sys.sp_executesql N'
      ALTER TABLE dbo.Projects WITH CHECK ADD
        CONSTRAINT FK_Projects_InactivatedByUser
        FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);';
    IF OBJECT_ID(N'dbo.CK_Projects_Dates',N'C') IS NULL
      ALTER TABLE dbo.Projects WITH CHECK ADD
        CONSTRAINT CK_Projects_Dates CHECK(EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate);
    CREATE INDEX IX_Projects_Search
      ON dbo.Projects(OrganizationId,IsActive,StartDate,EndDate,TeamId)
      INCLUDE(ProjectCode,ProjectName);

    ALTER TABLE dbo.VisitTypes ADD
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;
    EXEC sys.sp_executesql N'
      ALTER TABLE dbo.VisitTypes WITH CHECK ADD
        CONSTRAINT FK_VisitTypes_InactivatedByUser
        FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);';
    CREATE UNIQUE INDEX UX_VisitTypes_VisitTypeCode
      ON dbo.VisitTypes(VisitTypeCode);
    CREATE INDEX IX_VisitTypes_Active_Sort
      ON dbo.VisitTypes(IsActive,SortOrder,VisitTypeName,VisitTypeId)
      INCLUDE(VisitTypeCode);

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

    ALTER TABLE dbo.MileageRateRules WITH CHECK ADD
        CONSTRAINT CK_MileageRateRules_VehicleType
        CHECK(VehicleType COLLATE Latin1_General_100_BIN2 IN
              (N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2,N'CAR' COLLATE Latin1_General_100_BIN2));
    IF OBJECT_ID(N'dbo.CK_MileageRateRules_Dates',N'C') IS NULL
        ALTER TABLE dbo.MileageRateRules WITH CHECK ADD
            CONSTRAINT CK_MileageRateRules_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom);
    IF OBJECT_ID(N'dbo.CK_MileageRateRules_NonNegativeRate',N'C') IS NULL
        ALTER TABLE dbo.MileageRateRules WITH CHECK ADD
            CONSTRAINT CK_MileageRateRules_NonNegativeRate CHECK(RatePerKm >= 0);

    ;WITH ordered AS (
      SELECT MileageRateRuleId,
             LEAD(EffectiveFrom) OVER(PARTITION BY OrganizationId,VehicleType ORDER BY EffectiveFrom,MileageRateRuleId) AS NextFrom
      FROM dbo.MileageRateRules
      WHERE IsActive=1
    )
    UPDATE r
       SET EffectiveTo = CASE WHEN o.NextFrom IS NULL THEN NULL ELSE DATEADD(day,-1,o.NextFrom) END
    FROM dbo.MileageRateRules r
    JOIN ordered o ON o.MileageRateRuleId=r.MileageRateRuleId;

    CREATE UNIQUE INDEX UX_MileageRateRules_Scope_Vehicle_Start
      ON dbo.MileageRateRules(OrganizationId,VehicleType,EffectiveFrom)
      WHERE IsActive=1;
    CREATE INDEX IX_MileageRateRules_AsOf
      ON dbo.MileageRateRules(OrganizationId,VehicleType,IsActive,EffectiveFrom,EffectiveTo)
      INCLUDE(RatePerKm,RuleName);

    EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_MileageRateRules_ProtectSeries
ON dbo.MileageRateRules
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;
  IF TRIGGER_NESTLEVEL(OBJECT_ID(N''dbo.TR_MileageRateRules_ProtectSeries''), ''AFTER'', ''DML'') > 1 RETURN;

  IF EXISTS(
      SELECT 1 FROM deleted d
      WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.MileageRateRuleId=d.MileageRateRuleId)
  )
      THROW 53830, N''MileageRateRules physical DELETE is prohibited; use soft lifecycle.'', 1;

  IF EXISTS(
      SELECT 1 FROM inserted
      WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
            (N''MOTORCYCLE'' COLLATE Latin1_General_100_BIN2,N''CAR'' COLLATE Latin1_General_100_BIN2)
  )
      THROW 53831, N''VehicleType must be canonical MOTORCYCLE or CAR.'', 1;

  IF EXISTS(
      SELECT 1
      FROM inserted i
      LEFT JOIN deleted d ON d.MileageRateRuleId=i.MileageRateRuleId
      WHERE i.IsActive=0
        AND (
          (d.MileageRateRuleId IS NULL AND i.EffectiveTo IS NOT NULL)
          OR
          (d.MileageRateRuleId IS NOT NULL AND
             (i.EffectiveTo<>d.EffectiveTo
              OR (i.EffectiveTo IS NULL AND d.EffectiveTo IS NOT NULL)
              OR (i.EffectiveTo IS NOT NULL AND d.EffectiveTo IS NULL)))
        )
  )
      THROW 53832, N''Inactive MileageRate EffectiveTo is historical system evidence and cannot be caller-authored.'', 1;

  DECLARE @Affected TABLE(
      OrganizationId INT NULL,
      VehicleType NVARCHAR(50) NOT NULL,
      ResourceName NVARCHAR(255) NOT NULL
  );
  INSERT @Affected(OrganizationId,VehicleType,ResourceName)
  SELECT s.OrganizationId,s.VehicleType,
         N''FieldVisit.MileageRateSeries|ORG=''
         + COALESCE(CONVERT(nvarchar(20),s.OrganizationId),N''GLOBAL'')
         + N''|VEHICLE='' + s.VehicleType
  FROM (
      SELECT OrganizationId,VehicleType FROM inserted
      UNION
      SELECT OrganizationId,VehicleType FROM deleted
  ) s;

  DECLARE @Resource NVARCHAR(255), @LockResult INT;
  DECLARE series_cursor CURSOR LOCAL FAST_FORWARD FOR
      SELECT ResourceName FROM @Affected ORDER BY ResourceName ASC;
  OPEN series_cursor;
  FETCH NEXT FROM series_cursor INTO @Resource;
  WHILE @@FETCH_STATUS=0
  BEGIN
      EXEC @LockResult=sys.sp_getapplock
          @Resource=@Resource,
          @LockMode=N''Exclusive'',
          @LockOwner=N''Transaction'',
          @LockTimeout=10000;
      IF @LockResult<0
      BEGIN
          CLOSE series_cursor;
          DEALLOCATE series_cursor;
          THROW 53833, N''Unable to acquire MileageRate exact-series transaction lock.'', 1;
      END;
      FETCH NEXT FROM series_cursor INTO @Resource;
  END;
  CLOSE series_cursor;
  DEALLOCATE series_cursor;

  DECLARE @CurrentRows BIGINT;
  SELECT @CurrentRows=COUNT_BIG(*)
  FROM dbo.MileageRateRules r WITH (UPDLOCK,HOLDLOCK)
  JOIN @Affected a
    ON (r.OrganizationId=a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))
   AND r.VehicleType=a.VehicleType;

  ;WITH ordered AS (
      SELECT r.MileageRateRuleId,
             LEAD(r.EffectiveFrom) OVER(
                 PARTITION BY r.OrganizationId,r.VehicleType
                 ORDER BY r.EffectiveFrom,r.MileageRateRuleId) AS NextFrom
      FROM dbo.MileageRateRules r WITH (UPDLOCK,HOLDLOCK)
      JOIN @Affected a
        ON (r.OrganizationId=a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))
       AND r.VehicleType=a.VehicleType
      WHERE r.IsActive=1
  )
  UPDATE r
     SET EffectiveTo=CASE WHEN o.NextFrom IS NULL THEN NULL ELSE DATEADD(day,-1,o.NextFrom) END
  FROM dbo.MileageRateRules r
  JOIN ordered o ON o.MileageRateRuleId=r.MileageRateRuleId
  WHERE
      (r.EffectiveTo<>CASE WHEN o.NextFrom IS NULL THEN NULL ELSE DATEADD(day,-1,o.NextFrom) END)
      OR (r.EffectiveTo IS NULL AND o.NextFrom IS NOT NULL)
      OR (r.EffectiveTo IS NOT NULL AND o.NextFrom IS NULL);

  IF EXISTS(
      SELECT 1
      FROM dbo.MileageRateRules a WITH (UPDLOCK,HOLDLOCK)
      JOIN dbo.MileageRateRules b WITH (UPDLOCK,HOLDLOCK)
        ON (a.OrganizationId=b.OrganizationId OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL))
       AND a.VehicleType=b.VehicleType
       AND a.MileageRateRuleId<b.MileageRateRuleId
       AND a.IsActive=1 AND b.IsActive=1
       AND a.EffectiveFrom<=COALESCE(b.EffectiveTo,CONVERT(date,''99991231''))
       AND b.EffectiveFrom<=COALESCE(a.EffectiveTo,CONVERT(date,''99991231''))
      JOIN @Affected x
        ON (a.OrganizationId=x.OrganizationId OR (a.OrganizationId IS NULL AND x.OrganizationId IS NULL))
       AND a.VehicleType=x.VehicleType
  )
      THROW 53834, N''MileageRate active exact-series overlap detected.'', 1;

  IF EXISTS(
      SELECT 1
      FROM (
          SELECT r.MileageRateRuleId,r.OrganizationId,r.VehicleType,r.EffectiveTo,
                 LEAD(r.EffectiveFrom) OVER(
                     PARTITION BY r.OrganizationId,r.VehicleType
                     ORDER BY r.EffectiveFrom,r.MileageRateRuleId) AS NextFrom
          FROM dbo.MileageRateRules r WITH (UPDLOCK,HOLDLOCK)
          JOIN @Affected a
            ON (r.OrganizationId=a.OrganizationId OR (r.OrganizationId IS NULL AND a.OrganizationId IS NULL))
           AND r.VehicleType=a.VehicleType
          WHERE r.IsActive=1
      ) v
      WHERE (v.NextFrom IS NULL AND v.EffectiveTo IS NOT NULL)
         OR (v.NextFrom IS NOT NULL AND (v.EffectiveTo IS NULL OR v.EffectiveTo<>DATEADD(day,-1,v.NextFrom)))
  )
      THROW 53835, N''MileageRate derived EffectiveTo invariant failed.'', 1;
END;';

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N'1.8.0-005',
           N'Project and VisitType lifecycle plus snapshot-safe MileageRate exact-series invariant',
           SYSUTCDATETIME(),N'v1.8.0 D-B Work1');

    COMMIT TRANSACTION;
    PRINT N'1.8.0-005 project / visit type / rate lifecycle completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
