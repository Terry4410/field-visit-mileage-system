SET NOCOUNT ON;

DECLARE @Errors TABLE
(
    ErrorMessage NVARCHAR(1000) NOT NULL
);

IF OBJECT_ID(N'dbo.GovernmentLocationSources', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing dbo.GovernmentLocationSources');

IF OBJECT_ID(N'dbo.GovernmentLocationSourceAreas', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing dbo.GovernmentLocationSourceAreas');

IF OBJECT_ID(N'dbo.GovernmentLocationMasters', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing dbo.GovernmentLocationMasters');

IF OBJECT_ID(N'dbo.UserFavoriteLocations', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing dbo.UserFavoriteLocations');

IF NOT EXISTS(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.7.0-002'
)
    INSERT @Errors VALUES(N'SchemaVersions does not contain 1.7.0-002');

IF OBJECT_ID(N'dbo.GovernmentLocationSources', N'U') IS NOT NULL
AND EXISTS(
    SELECT SourceCode
    FROM dbo.GovernmentLocationSources
    GROUP BY SourceCode
    HAVING COUNT(*) > 1
)
    INSERT @Errors VALUES(N'Duplicate GovernmentLocationSources.SourceCode');

IF OBJECT_ID(N'dbo.GovernmentLocationMasters', N'U') IS NOT NULL
AND EXISTS(
    SELECT
        GovernmentLocationSourceId,
        SourceRecordKey
    FROM dbo.GovernmentLocationMasters
    GROUP BY
        GovernmentLocationSourceId,
        SourceRecordKey
    HAVING COUNT(*) > 1
)
    INSERT @Errors VALUES(N'Duplicate government source record key');

IF OBJECT_ID(N'dbo.GovernmentLocationMasters', N'U') IS NOT NULL
AND EXISTS(
    SELECT 1
    FROM dbo.GovernmentLocationMasters
    WHERE ReviewStatus NOT IN(
        N'PendingReview',
        N'Matched',
        N'Ignored'
    )
)
    INSERT @Errors VALUES(N'Invalid GovernmentLocationMaster ReviewStatus');

IF OBJECT_ID(N'dbo.GovernmentLocationMasters', N'U') IS NOT NULL
AND EXISTS(
    SELECT 1
    FROM dbo.GovernmentLocationMasters
    WHERE
        (Latitude IS NULL AND Longitude IS NOT NULL)
        OR
        (Latitude IS NOT NULL AND Longitude IS NULL)
        OR Latitude NOT BETWEEN -90 AND 90
        OR Longitude NOT BETWEEN -180 AND 180
)
    INSERT @Errors VALUES(N'Invalid government location coordinate pair');

IF OBJECT_ID(N'dbo.UserFavoriteLocations', N'U') IS NOT NULL
AND EXISTS(
    SELECT UserId, LocationId
    FROM dbo.UserFavoriteLocations
    GROUP BY UserId, LocationId
    HAVING COUNT(*) > 1
)
    INSERT @Errors VALUES(N'Duplicate UserFavoriteLocation');

IF EXISTS(SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage
    FROM @Errors
    ORDER BY ErrorMessage;

    THROW 51790,
          N'v1.7.0-002 Verify FAILED.',
          1;
END;

SELECT
    N'PASS' AS VerifyStatus,

    (SELECT COUNT(*)
     FROM dbo.GovernmentLocationSources)
        AS Sources,

    (SELECT COUNT(*)
     FROM dbo.GovernmentLocationSourceAreas)
        AS SourceAreas,

    (SELECT COUNT(*)
     FROM dbo.GovernmentLocationMasters)
        AS GovernmentLocations,

    (SELECT COUNT(*)
     FROM dbo.UserFavoriteLocations)
        AS Favorites;

PRINT N'v1.7.0-002 Verify PASS.';
