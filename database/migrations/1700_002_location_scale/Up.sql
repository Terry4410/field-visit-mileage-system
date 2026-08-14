SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-002 Location Scale Foundation

       IMPORTANT:
       - Government/open-data rows are reference candidates only.
       - They MUST NOT automatically overwrite dbo.Locations.
       - They MUST NOT modify VisitTripStops or approved Snapshots.
       ================================================================ */

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 51720,
              N'找不到 dbo.SchemaVersions，無法確認 Migration 順序。',
              1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-001'
    )
        THROW 51721,
              N'尚未套用 prerequisite Migration 1.7.0-001。',
              1;

    IF EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-002'
    )
        THROW 51722,
              N'Migration 1.7.0-002 已套用，不得重複執行。',
              1;

    IF OBJECT_ID(N'dbo.GovernmentLocationSources', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.GovernmentLocationSourceAreas', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.GovernmentLocationMasters', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.UserFavoriteLocations', N'U') IS NOT NULL
        THROW 51723,
              N'偵測到 v1.7.0-002 目標 Table 已存在，請先由 IT Review，不得直接覆蓋。',
              1;

    CREATE TABLE dbo.GovernmentLocationSources
    (
        GovernmentLocationSourceId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_GovernmentLocationSources PRIMARY KEY,

        SourceCode NVARCHAR(50) NOT NULL,
        SourceName NVARCHAR(200) NOT NULL,
        SourceType NVARCHAR(50) NOT NULL,

        SourceUrl NVARCHAR(1000) NULL,
        LicenseNote NVARCHAR(1000) NULL,

        IsEnabled BIT NOT NULL
            CONSTRAINT DF_GovernmentLocationSources_IsEnabled DEFAULT(0),

        LastSyncStartedAt DATETIME2(3) NULL,
        LastSyncCompletedAt DATETIME2(3) NULL,
        LastSyncStatus NVARCHAR(30) NULL,
        LastSyncMessage NVARCHAR(2000) NULL,

        CreatedAt DATETIME2(3) NOT NULL,
        UpdatedAt DATETIME2(3) NULL,

        CONSTRAINT UQ_GovernmentLocationSources_SourceCode
            UNIQUE(SourceCode),

        CONSTRAINT CK_GovernmentLocationSources_SourceType
            CHECK(SourceType IN(N'OpenData', N'FileImport')),

        CONSTRAINT CK_GovernmentLocationSources_SyncStatus
            CHECK(
                LastSyncStatus IS NULL
                OR LastSyncStatus IN(
                    N'Running',
                    N'Succeeded',
                    N'Failed',
                    N'Partial'
                )
            )
    );

    CREATE TABLE dbo.GovernmentLocationSourceAreas
    (
        GovernmentLocationSourceAreaId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_GovernmentLocationSourceAreas PRIMARY KEY,

        GovernmentLocationSourceId INT NOT NULL,

        City NVARCHAR(50) NOT NULL,
        District NVARCHAR(50) NULL,

        IsActive BIT NOT NULL
            CONSTRAINT DF_GovernmentLocationSourceAreas_IsActive DEFAULT(1),

        CreatedAt DATETIME2(3) NOT NULL,
        UpdatedAt DATETIME2(3) NULL,

        CONSTRAINT FK_GovernmentLocationSourceAreas_Source
            FOREIGN KEY(GovernmentLocationSourceId)
            REFERENCES dbo.GovernmentLocationSources(
                GovernmentLocationSourceId
            ),

        CONSTRAINT UQ_GovernmentLocationSourceAreas_Source_Area
            UNIQUE(
                GovernmentLocationSourceId,
                City,
                District
            ),

        CONSTRAINT CK_GovernmentLocationSourceAreas_City
            CHECK(LEN(LTRIM(RTRIM(City))) > 0)
    );

    CREATE TABLE dbo.GovernmentLocationMasters
    (
        GovernmentLocationMasterId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_GovernmentLocationMasters PRIMARY KEY,

        GovernmentLocationSourceId INT NOT NULL,

        SourceRecordKey NVARCHAR(200) NOT NULL,
        TaxId NVARCHAR(20) NULL,

        LocationName NVARCHAR(300) NOT NULL,
        PostalCode NVARCHAR(20) NULL,
        City NVARCHAR(50) NULL,
        District NVARCHAR(50) NULL,
        Address NVARCHAR(500) NULL,

        Latitude DECIMAL(10,7) NULL,
        Longitude DECIMAL(10,7) NULL,

        SourceHash NVARCHAR(128) NOT NULL,

        ReviewStatus NVARCHAR(30) NOT NULL
            CONSTRAINT DF_GovernmentLocationMasters_ReviewStatus
            DEFAULT(N'PendingReview'),

        MatchedLocationId INT NULL,

        FirstSeenAt DATETIME2(3) NOT NULL,
        LastSeenAt DATETIME2(3) NOT NULL,
        SourceUpdatedAt DATETIME2(3) NULL,

        ReviewedAt DATETIME2(3) NULL,
        ReviewedByUserId INT NULL,

        IsActive BIT NOT NULL
            CONSTRAINT DF_GovernmentLocationMasters_IsActive DEFAULT(1),

        CreatedAt DATETIME2(3) NOT NULL,
        UpdatedAt DATETIME2(3) NULL,

        CONSTRAINT FK_GovernmentLocationMasters_Source
            FOREIGN KEY(GovernmentLocationSourceId)
            REFERENCES dbo.GovernmentLocationSources(
                GovernmentLocationSourceId
            ),

        CONSTRAINT FK_GovernmentLocationMasters_MatchedLocation
            FOREIGN KEY(MatchedLocationId)
            REFERENCES dbo.Locations(LocationId),

        CONSTRAINT FK_GovernmentLocationMasters_ReviewedBy
            FOREIGN KEY(ReviewedByUserId)
            REFERENCES dbo.Users(UserId),

        CONSTRAINT UQ_GovernmentLocationMasters_Source_Record
            UNIQUE(
                GovernmentLocationSourceId,
                SourceRecordKey
            ),

        CONSTRAINT CK_GovernmentLocationMasters_ReviewStatus
            CHECK(
                ReviewStatus IN(
                    N'PendingReview',
                    N'Matched',
                    N'Ignored'
                )
            ),

        CONSTRAINT CK_GovernmentLocationMasters_Coordinates
            CHECK(
                (
                    Latitude IS NULL
                    AND Longitude IS NULL
                )
                OR
                (
                    Latitude BETWEEN -90 AND 90
                    AND Longitude BETWEEN -180 AND 180
                )
            ),

        CONSTRAINT CK_GovernmentLocationMasters_SeenDates
            CHECK(LastSeenAt >= FirstSeenAt)
    );

    CREATE INDEX IX_GovernmentLocationMasters_Search
        ON dbo.GovernmentLocationMasters(
            City,
            District,
            LocationName
        );

    CREATE INDEX IX_GovernmentLocationMasters_TaxId
        ON dbo.GovernmentLocationMasters(TaxId);

    CREATE INDEX IX_GovernmentLocationMasters_Review
        ON dbo.GovernmentLocationMasters(
            ReviewStatus,
            IsActive
        );

    CREATE TABLE dbo.UserFavoriteLocations
    (
        UserFavoriteLocationId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_UserFavoriteLocations PRIMARY KEY,

        UserId INT NOT NULL,
        LocationId INT NOT NULL,

        SortOrder INT NOT NULL
            CONSTRAINT DF_UserFavoriteLocations_SortOrder DEFAULT(0),

        CreatedAt DATETIME2(3) NOT NULL,

        CONSTRAINT FK_UserFavoriteLocations_User
            FOREIGN KEY(UserId)
            REFERENCES dbo.Users(UserId),

        CONSTRAINT FK_UserFavoriteLocations_Location
            FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId),

        CONSTRAINT UQ_UserFavoriteLocations_User_Location
            UNIQUE(UserId, LocationId),

        CONSTRAINT CK_UserFavoriteLocations_SortOrder
            CHECK(SortOrder >= 0)
    );

    CREATE INDEX IX_UserFavoriteLocations_User_Order
        ON dbo.UserFavoriteLocations(
            UserId,
            SortOrder,
            UserFavoriteLocationId
        );

    INSERT dbo.SchemaVersions
    (
        VersionNumber,
        Description,
        AppliedAt,
        AppliedBy
    )
    VALUES
    (
        N'1.7.0-002',
        N'Location scale foundation: government source cache, configured service areas and personal location favorites',
        SYSUTCDATETIME(),
        N'v1.7.0 Phase 2A'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-002 Location Scale Migration completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
