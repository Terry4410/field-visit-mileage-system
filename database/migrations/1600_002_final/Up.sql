SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;

  /* Stable LocationCode: keep existing values, backfill only missing values. */
  UPDATE dbo.Locations
  SET LocationCode = CONCAT(N'LOC-', RIGHT(REPLICATE('0',8) + CONVERT(VARCHAR(20), LocationId), 8))
  WHERE LocationCode IS NULL OR LTRIM(RTRIM(LocationCode)) = N'';

  /* If historical manual data already contains duplicate codes inside one organization,
     make only the duplicate rows unique without changing the first occurrence. */
  ;WITH d AS (
    SELECT LocationId, OrganizationId, LocationCode,
           ROW_NUMBER() OVER(PARTITION BY OrganizationId, LocationCode ORDER BY LocationId) AS rn
    FROM dbo.Locations
    WHERE LocationCode IS NOT NULL AND LTRIM(RTRIM(LocationCode)) <> N''
  )
  UPDATE l SET LocationCode = CONCAT(l.LocationCode, N'-', l.LocationId)
  FROM dbo.Locations l JOIN d ON d.LocationId=l.LocationId
  WHERE d.rn>1;

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_Locations_Organization_LocationCode' AND object_id=OBJECT_ID(N'dbo.Locations'))
    CREATE UNIQUE INDEX UX_Locations_Organization_LocationCode ON dbo.Locations(OrganizationId, LocationCode)
    WHERE LocationCode IS NOT NULL;

  /* Exactly one active primary team per user. */
  IF EXISTS (
    SELECT UserId FROM dbo.UserTeamScopes WHERE IsActive=1 AND IsPrimary=1 GROUP BY UserId HAVING COUNT(*)>1
  ) THROW 51001, N'UserTeamScopes 存在多個有效主要小組，請先修正後再執行 Migration。', 1;

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_UserTeamScopes_OneActivePrimary' AND object_id=OBJECT_ID(N'dbo.UserTeamScopes'))
    CREATE UNIQUE INDEX UX_UserTeamScopes_OneActivePrimary ON dbo.UserTeamScopes(UserId)
    WHERE IsActive=1 AND IsPrimary=1;

  /* Rate versions may not share the same effective start date. */
  IF EXISTS (
    SELECT ISNULL(OrganizationId,-1) OrgKey, VehicleType, EffectiveFrom
    FROM dbo.MileageRateRules
    WHERE IsActive=1
    GROUP BY ISNULL(OrganizationId,-1), VehicleType, EffectiveFrom
    HAVING COUNT(*)>1
  ) THROW 51002, N'補助費率存在同日重複生效版本，請先修正後再執行 Migration。', 1;

  /* Normalize all ACTIVE rate series: end date is always next start date - 1 day. */
  ;WITH r AS (
    SELECT MileageRateRuleId,
           DATEADD(DAY,-1,LEAD(EffectiveFrom) OVER(PARTITION BY ISNULL(OrganizationId,-1),VehicleType ORDER BY EffectiveFrom)) AS AutoEnd
    FROM dbo.MileageRateRules
    WHERE IsActive=1
  )
  UPDATE m SET EffectiveTo=r.AutoEnd, UpdatedAt=SYSUTCDATETIME()
  FROM dbo.MileageRateRules m JOIN r ON r.MileageRateRuleId=m.MileageRateRuleId
  WHERE ISNULL(CONVERT(date,m.EffectiveTo),'9999-12-31') <> ISNULL(CONVERT(date,r.AutoEnd),'9999-12-31');

  /* Backfill a frozen Snapshot for Approved records created before v1.6.0.
     From this migration onward, approved history/query/report MUST NOT fall back to current master data. */
  INSERT dbo.VisitTripSnapshots(
      VisitTripId,SnapshotVersion,SnapshotType,TripNo,UserId,EmployeeNoSnapshot,DisplayNameSnapshot,
      OrganizationId,OrganizationNameSnapshot,TeamId,TeamNameSnapshot,VisitDate,StartTime,EndTime,
      StatusSnapshot,VehicleTypeSnapshot,ClaimedDistanceKmSnapshot,SystemDistanceKmSnapshot,ApprovedDistanceKmSnapshot,
      RatePerKmSnapshot,SubsidyAmountSnapshot,RouteProviderSnapshot,SubmittedAtSnapshot,ApprovedAtSnapshot,
      ApproverUserId,ApproverNameSnapshot,NotesSnapshot,CreatedAt,CreatedByUserId)
  SELECT t.VisitTripId,1,N'BackfillApproved',t.TripNo,t.UserId,u.EmployeeNo,u.DisplayName,
      t.OrganizationId,o.OrganizationName,t.TeamId,tm.TeamName,t.VisitDate,t.StartTime,t.EndTime,
      N'Approved',t.VehicleType,m.ClaimedDistanceKm,m.SystemDistanceKm,m.ApprovedDistanceKm,
      m.RatePerKmSnapshot,m.ApprovedAmount,m.CalculationSource,t.SubmittedAt,t.ApprovedAt,
      ap.ApproverUserId,ap.ApproverName,t.Notes,SYSUTCDATETIME(),ap.ApproverUserId
  FROM dbo.VisitTrips t
  JOIN dbo.Users u ON u.UserId=t.UserId
  JOIN dbo.Organizations o ON o.OrganizationId=t.OrganizationId
  LEFT JOIN dbo.Teams tm ON tm.TeamId=t.TeamId
  LEFT JOIN dbo.MileageCalculations m ON m.VisitTripId=t.VisitTripId
  OUTER APPLY(
      SELECT TOP(1) ar.ApproverUserId,au.DisplayName AS ApproverName
      FROM dbo.ApprovalRecords ar
      LEFT JOIN dbo.Users au ON au.UserId=ar.ApproverUserId
      WHERE ar.VisitTripId=t.VisitTripId AND ar.Action=N'Approved'
      ORDER BY ar.ActionAt DESC, ar.ApprovalRecordId DESC
  ) ap
  WHERE t.Status=N'Approved'
    AND NOT EXISTS(SELECT 1 FROM dbo.VisitTripSnapshots s WHERE s.VisitTripId=t.VisitTripId);

  INSERT dbo.VisitTripSnapshotStops(
      VisitTripSnapshotId,StopSequence,LocationId,LocationCodeSnapshot,LocationNameSnapshot,AddressSnapshot,
      ProjectId,ProjectCodeSnapshot,ProjectNameSnapshot,VisitTypeId,VisitTypeCodeSnapshot,VisitTypeNameSnapshot,
      VisitPurposeSnapshot,NotesSnapshot,CreatedAt)
  SELECT snap.VisitTripSnapshotId,st.StopSequence,st.LocationId,l.LocationCode,
      COALESCE(st.LocationNameSnapshot,l.LocationName,N''),COALESCE(st.AddressSnapshot,l.Address,l.PlusCode),
      st.ProjectId,p.ProjectCode,p.ProjectName,st.VisitTypeId,vt.VisitTypeCode,vt.VisitTypeName,
      st.VisitPurpose,st.Notes,SYSUTCDATETIME()
  FROM dbo.VisitTripSnapshots snap
  JOIN dbo.VisitTripStops st ON st.VisitTripId=snap.VisitTripId
  LEFT JOIN dbo.Locations l ON l.LocationId=st.LocationId
  LEFT JOIN dbo.Projects p ON p.ProjectId=st.ProjectId
  LEFT JOIN dbo.VisitTypes vt ON vt.VisitTypeId=st.VisitTypeId
  WHERE snap.SnapshotType=N'BackfillApproved'
    AND NOT EXISTS(SELECT 1 FROM dbo.VisitTripSnapshotStops ss WHERE ss.VisitTripSnapshotId=snap.VisitTripSnapshotId AND ss.StopSequence=st.StopSequence);

  IF OBJECT_ID(N'dbo.ImportBatches',N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.ImportBatches(
      ImportBatchId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ImportBatches PRIMARY KEY,
      ImportType NVARCHAR(30) NOT NULL,
      OrganizationId INT NOT NULL,
      RequestedByUserId INT NOT NULL,
      Status NVARCHAR(30) NOT NULL,
      TotalCount INT NOT NULL CONSTRAINT DF_ImportBatches_Total DEFAULT(0),
      ValidCount INT NOT NULL CONSTRAINT DF_ImportBatches_Valid DEFAULT(0),
      ErrorCount INT NOT NULL CONSTRAINT DF_ImportBatches_Error DEFAULT(0),
      CreatedAt DATETIME2(3) NOT NULL,
      ExpiresAt DATETIME2(3) NOT NULL,
      ConfirmedAt DATETIME2(3) NULL,
      CONSTRAINT FK_ImportBatches_Organizations FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),
      CONSTRAINT FK_ImportBatches_Users FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(UserId),
      CONSTRAINT CK_ImportBatches_Status CHECK(Status IN(N'Previewed',N'Confirmed',N'PartiallyFailed'))
    );
    CREATE INDEX IX_ImportBatches_User_Status ON dbo.ImportBatches(RequestedByUserId,Status,CreatedAt DESC);
  END;

  IF OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.ImportBatchItems(
      ImportBatchItemId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImportBatchItems PRIMARY KEY,
      ImportBatchId UNIQUEIDENTIFIER NOT NULL,
      RowNumber INT NOT NULL,
      EntityType NVARCHAR(40) NOT NULL,
      Action NVARCHAR(30) NOT NULL,
      Status NVARCHAR(30) NOT NULL,
      DisplayKey NVARCHAR(300) NOT NULL,
      DataJson NVARCHAR(MAX) NOT NULL,
      ErrorMessage NVARCHAR(2000) NULL,
      CreatedAt DATETIME2(3) NOT NULL,
      CONSTRAINT FK_ImportBatchItems_Batch FOREIGN KEY(ImportBatchId) REFERENCES dbo.ImportBatches(ImportBatchId) ON DELETE CASCADE,
      CONSTRAINT CK_ImportBatchItems_Status CHECK(Status IN(N'Valid',N'Error',N'Applied',N'Failed'))
    );
    CREATE INDEX IX_ImportBatchItems_Batch_Row ON dbo.ImportBatchItems(ImportBatchId,RowNumber);
  END;

  IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL
     AND NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.6.0')
  BEGIN
    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N'1.6.0',N'v1.6.0 FINAL: Snapshot backfill, LocationCode, import staging, team primary constraint, rate series normalization',SYSUTCDATETIME(),N'v1.6.0 FINAL RC');
  END;

  COMMIT TRANSACTION;
  PRINT N'v1.6.0 FINAL Migration completed.';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
