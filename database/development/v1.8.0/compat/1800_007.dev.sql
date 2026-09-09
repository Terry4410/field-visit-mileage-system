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
        THROW 54200, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-006')
        THROW 54201, N'尚未套用 prerequisite Migration 1.8.0-006。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
        THROW 54202, N'Migration 1.8.0-007 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.GeocodingAttempts', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.MileageGovernanceEvents', N'U') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageCalculations', N'SelectedRouteCalculationAttemptId') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageCalculations', N'ManualFallbackUsed') IS NOT NULL
       OR COL_LENGTH(N'dbo.MileageCalculations', N'ApprovedDistanceSource') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'MileageRouteAttemptIdSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'ApprovedDistanceSourceSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'SelectedGeocodingAttemptId') IS NOT NULL
       OR OBJECT_ID(N'dbo.TR_MileageCalculations_DecisionEvidence', N'TR') IS NOT NULL
        THROW 54203, N'偵測到 1.8.0-007 部分物件或欄位已存在；請由 IT Review。', 1;

    CREATE TABLE dbo.GeocodingAttempts
    (
        GeocodingAttemptId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GeocodingAttempts PRIMARY KEY,
        LocationId INT NOT NULL,
        Provider NVARCHAR(80) NOT NULL,
        AddressBasisHash VARBINARY(32) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        ErrorCode NVARCHAR(100) NULL,
        ErrorMessage NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        RequestedAt DATETIME2(3) NOT NULL,
        CompletedAt DATETIME2(3) NULL,
        RequestedByUserId INT NOT NULL,
        CONSTRAINT FK_GeocodingAttempts_Locations FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId),
        CONSTRAINT FK_GeocodingAttempts_RequestedByUser FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_GeocodingAttempts_Status CHECK(Status IN(N'Pending', N'Succeeded', N'Failed')),
        CONSTRAINT CK_GeocodingAttempts_Result CHECK
        (
            (Status = N'Succeeded' AND ErrorCode IS NULL)
            OR (Status = N'Failed' AND ErrorCode IS NOT NULL)
            OR (Status = N'Pending' AND ErrorCode IS NULL)
        ),
        CONSTRAINT UQ_GeocodingAttempts_Correlation UNIQUE(CorrelationId)
    );
    CREATE INDEX IX_GeocodingAttempts_Location_Requested
        ON dbo.GeocodingAttempts(LocationId, RequestedAt DESC)
        INCLUDE(Status, Provider, ErrorCode, CorrelationId);

    CREATE TABLE dbo.RouteCalculationAttempts
    (
        RouteCalculationAttemptId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RouteCalculationAttempts PRIMARY KEY,
        VisitTripId BIGINT NOT NULL,
        BasisType NVARCHAR(30) NOT NULL,
        BasisVisitTripSnapshotId BIGINT NULL,
        CalculationReason NVARCHAR(30) NOT NULL,
        RequestedVehicleType NVARCHAR(20) NOT NULL,
        TravelMode NVARCHAR(20) NOT NULL,
        Provider NVARCHAR(80) NOT NULL,
        StopCount INT NOT NULL,
        RequestBasisHash VARBINARY(32) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        ErrorCode NVARCHAR(100) NULL,
        ErrorMessage NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        RequestedAt DATETIME2(3) NOT NULL,
        CompletedAt DATETIME2(3) NULL,
        RequestedByUserId INT NOT NULL,
        CONSTRAINT FK_RouteCalculationAttempts_Trips FOREIGN KEY(VisitTripId) REFERENCES dbo.VisitTrips(VisitTripId),
        CONSTRAINT FK_RouteCalculationAttempts_BasisSnapshot FOREIGN KEY(BasisVisitTripSnapshotId) REFERENCES dbo.VisitTripSnapshots(VisitTripSnapshotId),
        CONSTRAINT FK_RouteCalculationAttempts_RequestedByUser FOREIGN KEY(RequestedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_RouteCalculationAttempts_Basis CHECK
        (
            (BasisType = N'Draft' AND BasisVisitTripSnapshotId IS NULL)
            OR
            (BasisType = N'SubmittedSnapshot' AND BasisVisitTripSnapshotId IS NOT NULL)
        ),
        CONSTRAINT CK_RouteCalculationAttempts_Reason CHECK(CalculationReason IN(N'VisitorCalculate', N'LeaderRetry', N'CorrectionRecalculate')),
        CONSTRAINT CK_RouteCalculationAttempts_LeaderBasis CHECK(CalculationReason <> N'LeaderRetry' OR BasisType = N'SubmittedSnapshot'),
        CONSTRAINT CK_RouteCalculationAttempts_Vehicle CHECK(RequestedVehicleType IN(N'Motorcycle', N'Car')),
        CONSTRAINT CK_RouteCalculationAttempts_Mode CHECK(TravelMode IN(N'DRIVE', N'TWO_WHEELER')),
        CONSTRAINT CK_RouteCalculationAttempts_VehicleMode CHECK
        (
            (RequestedVehicleType = N'Car' AND TravelMode = N'DRIVE')
            OR
            (RequestedVehicleType = N'Motorcycle' AND TravelMode = N'TWO_WHEELER')
        ),
        CONSTRAINT CK_RouteCalculationAttempts_StopCount CHECK(StopCount >= 2),
        CONSTRAINT CK_RouteCalculationAttempts_Status CHECK(Status IN(N'Pending', N'Succeeded', N'Failed')),
        CONSTRAINT CK_RouteCalculationAttempts_Result CHECK
        (
            (Status = N'Succeeded' AND ErrorCode IS NULL)
            OR (Status = N'Failed' AND ErrorCode IS NOT NULL)
            OR (Status = N'Pending' AND ErrorCode IS NULL)
        ),
        CONSTRAINT UQ_RouteCalculationAttempts_Correlation UNIQUE(CorrelationId)
    );
    CREATE INDEX IX_RouteCalculationAttempts_Trip_Requested
        ON dbo.RouteCalculationAttempts(VisitTripId, RequestedAt DESC)
        INCLUDE(Status, Provider, CalculationReason, BasisVisitTripSnapshotId, CorrelationId);
    CREATE INDEX IX_RouteCalculationAttempts_BasisSnapshot
        ON dbo.RouteCalculationAttempts(BasisVisitTripSnapshotId, RequestedAt DESC)
        WHERE BasisVisitTripSnapshotId IS NOT NULL;

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_RouteCalculationAttempts_BasisTrip
        ON dbo.RouteCalculationAttempts AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.VisitTripSnapshots s ON s.VisitTripSnapshotId = i.BasisVisitTripSnapshotId
                WHERE s.VisitTripId <> i.VisitTripId
            )
                THROW 54221, N''Route calculation basis Snapshot 必須屬於同一 Trip。'', 1;
        END;';

    CREATE TABLE dbo.MileageGovernanceEvents
    (
        MileageGovernanceEventId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MileageGovernanceEvents PRIMARY KEY,
        VisitTripId BIGINT NOT NULL,
        VisitTripSnapshotId BIGINT NULL,
        RouteCalculationAttemptId BIGINT NULL,
        EventType NVARCHAR(40) NOT NULL,
        ReasonCode NVARCHAR(100) NULL,
        Message NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        OccurredAt DATETIME2(3) NOT NULL CONSTRAINT DF_MileageGovernanceEvents_OccurredAt DEFAULT(SYSUTCDATETIME()),
        ActorUserId INT NULL,
        CONSTRAINT FK_MileageGovernanceEvents_Trips FOREIGN KEY(VisitTripId) REFERENCES dbo.VisitTrips(VisitTripId),
        CONSTRAINT FK_MileageGovernanceEvents_Snapshot FOREIGN KEY(VisitTripSnapshotId) REFERENCES dbo.VisitTripSnapshots(VisitTripSnapshotId),
        CONSTRAINT FK_MileageGovernanceEvents_RouteAttempt FOREIGN KEY(RouteCalculationAttemptId) REFERENCES dbo.RouteCalculationAttempts(RouteCalculationAttemptId),
        CONSTRAINT FK_MileageGovernanceEvents_ActorUser FOREIGN KEY(ActorUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_MileageGovernanceEvents_Type CHECK(EventType IN
            (N'Calculated', N'CalculationFailed', N'ManualFallback', N'Invalidated', N'LeaderRetry', N'Approved'))
    );
    CREATE INDEX IX_MileageGovernanceEvents_Trip_Occurred
        ON dbo.MileageGovernanceEvents(VisitTripId, OccurredAt DESC);
    CREATE INDEX IX_MileageGovernanceEvents_Correlation
        ON dbo.MileageGovernanceEvents(CorrelationId);

    ALTER TABLE dbo.Locations ADD SelectedGeocodingAttemptId BIGINT NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.Locations WITH CHECK ADD
            CONSTRAINT FK_Locations_SelectedGeocodingAttempt
            FOREIGN KEY(SelectedGeocodingAttemptId) REFERENCES dbo.GeocodingAttempts(GeocodingAttemptId);
        CREATE INDEX IX_Locations_SelectedGeocodingAttempt
            ON dbo.Locations(SelectedGeocodingAttemptId)
            WHERE SelectedGeocodingAttemptId IS NOT NULL;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_Locations_SelectedGeocodingAttempt
        ON dbo.Locations AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.GeocodingAttempts g ON g.GeocodingAttemptId = i.SelectedGeocodingAttemptId
                WHERE g.LocationId <> i.LocationId OR g.Status <> N''Succeeded''
            )
                THROW 54220, N''Location 只能選取同一 Location 的成功 Geocoding attempt。'', 1;
        END;';

    ALTER TABLE dbo.MileageCalculations ADD
        SelectedRouteCalculationAttemptId BIGINT NULL,
        ManualFallbackUsed BIT NOT NULL CONSTRAINT DF_MileageCalculations_ManualFallbackUsed DEFAULT(0) WITH VALUES,
        DistanceDecisionGovernanceVersion NVARCHAR(20) NULL,
        ApprovedDistanceSource NVARCHAR(30) NULL,
        ApprovalBasisCode NVARCHAR(80) NULL,
        ApprovalBasisHash VARBINARY(32) NULL,
        DistanceApprovedAt DATETIME2(3) NULL,
        DistanceApprovedByUserId INT NULL,
        InvalidatedAt DATETIME2(3) NULL,
        InvalidatedByUserId INT NULL,
        InvalidationReason NVARCHAR(100) NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.MileageCalculations WITH CHECK ADD
            CONSTRAINT FK_MileageCalculations_SelectedRouteAttempt FOREIGN KEY(SelectedRouteCalculationAttemptId) REFERENCES dbo.RouteCalculationAttempts(RouteCalculationAttemptId),
            CONSTRAINT FK_MileageCalculations_DistanceApprovedByUser FOREIGN KEY(DistanceApprovedByUserId) REFERENCES dbo.Users(UserId),
            CONSTRAINT FK_MileageCalculations_InvalidatedByUser FOREIGN KEY(InvalidatedByUserId) REFERENCES dbo.Users(UserId);
        ALTER TABLE dbo.MileageCalculations WITH CHECK ADD
            CONSTRAINT CK_MileageCalculations_ApprovedDistanceSource CHECK
            (
                ApprovedDistanceSource IS NULL
                OR ApprovedDistanceSource IN(N''Claimed'', N''ProviderSuggested'', N''LeaderAdjusted'', N''ManualFallback'')
            ),
            CONSTRAINT CK_MileageCalculations_ApprovalEvidence CHECK
            (
                DistanceDecisionGovernanceVersion IS NULL
                OR
                (DistanceDecisionGovernanceVersion = N''1.8.0''
                 AND ApprovedDistanceKm IS NOT NULL AND ApprovedDistanceSource IS NOT NULL
                 AND ApprovalBasisCode IS NOT NULL AND ApprovalBasisHash IS NOT NULL
                 AND DistanceApprovedAt IS NOT NULL AND DistanceApprovedByUserId IS NOT NULL)
            );';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_MileageCalculations_SelectedRouteTrip
        ON dbo.MileageCalculations AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.RouteCalculationAttempts a ON a.RouteCalculationAttemptId = i.SelectedRouteCalculationAttemptId
                WHERE a.VisitTripId <> i.VisitTripId OR a.Status <> N''Succeeded''
            )
                THROW 54222, N''Mileage 只能選取同一 Trip 的成功 Route attempt。'', 1;
        END;';

    /* Existing v1.7.2 approvals are left untouched. New approvals and any changed
       approved distance must carry complete company decision evidence. */
    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_MileageCalculations_DecisionEvidence
        ON dbo.MileageCalculations AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                LEFT JOIN deleted d ON d.MileageCalculationId = i.MileageCalculationId
                WHERE i.ApprovedDistanceKm IS NOT NULL
                  AND
                  (
                      d.MileageCalculationId IS NULL
                      OR d.ApprovedDistanceKm IS NULL
                      OR d.ApprovedDistanceKm <> i.ApprovedDistanceKm
                      OR i.DistanceDecisionGovernanceVersion IS NOT NULL
                  )
                  AND
                  (
                      i.DistanceDecisionGovernanceVersion <> N''1.8.0''
                      OR i.DistanceDecisionGovernanceVersion IS NULL
                      OR i.ApprovedDistanceSource IS NULL
                      OR i.ApprovalBasisCode IS NULL
                      OR i.ApprovalBasisHash IS NULL
                      OR i.DistanceApprovedAt IS NULL
                      OR i.DistanceApprovedByUserId IS NULL
                  )
            )
                THROW 54224, N''New or changed ApprovedDistanceKm requires complete company decision evidence.'', 1;
        END;';

    ALTER TABLE dbo.VisitTripSnapshots ADD
        MileageRouteAttemptIdSnapshot BIGINT NULL,
        RouteTravelModeSnapshot NVARCHAR(20) NULL,
        RouteCalculatedAtSnapshot DATETIME2(3) NULL,
        RouteCalculationStatusSnapshot NVARCHAR(20) NULL,
        RouteErrorCodeSnapshot NVARCHAR(100) NULL,
        RouteCorrelationIdSnapshot UNIQUEIDENTIFIER NULL,
        ApprovedDistanceSourceSnapshot NVARCHAR(30) NULL,
        ApprovalBasisCodeSnapshot NVARCHAR(80) NULL,
        ApprovalBasisHashSnapshot VARBINARY(32) NULL,
        DistanceApprovedAtSnapshot DATETIME2(3) NULL;
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.VisitTripSnapshots WITH CHECK ADD
            CONSTRAINT FK_VisitTripSnapshots_RouteAttempt FOREIGN KEY(MileageRouteAttemptIdSnapshot) REFERENCES dbo.RouteCalculationAttempts(RouteCalculationAttemptId),
            CONSTRAINT CK_VisitTripSnapshots_RouteMode CHECK(RouteTravelModeSnapshot IS NULL OR RouteTravelModeSnapshot IN(N''DRIVE'', N''TWO_WHEELER'')),
            CONSTRAINT CK_VisitTripSnapshots_RouteStatus CHECK(RouteCalculationStatusSnapshot IS NULL OR RouteCalculationStatusSnapshot IN(N''Succeeded'', N''Failed'', N''ManualFallback'')),
            CONSTRAINT CK_VisitTripSnapshots_ApprovedDistanceSource CHECK
            (
                ApprovedDistanceSourceSnapshot IS NULL
                OR ApprovedDistanceSourceSnapshot IN(N''Claimed'', N''ProviderSuggested'', N''LeaderAdjusted'', N''ManualFallback'')
            ),
            CONSTRAINT CK_VisitTripSnapshots_ApprovalBasis CHECK
            (
                (ApprovalBasisCodeSnapshot IS NULL AND ApprovalBasisHashSnapshot IS NULL)
                OR (ApprovalBasisCodeSnapshot IS NOT NULL AND ApprovalBasisHashSnapshot IS NOT NULL)
            );';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_VisitTripSnapshots_RouteAttemptTrip
        ON dbo.VisitTripSnapshots AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.RouteCalculationAttempts a ON a.RouteCalculationAttemptId = i.MileageRouteAttemptIdSnapshot
                WHERE a.VisitTripId <> i.VisitTripId
            )
                THROW 54223, N''Snapshot route attempt 必須屬於同一 Trip。'', 1;
        END;';

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-007',
        N'Google request audit and company-approved mileage decision metadata, snapshot-based retry and manual fallback without Google-derived output persistence',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-007 mileage / Google governance completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
