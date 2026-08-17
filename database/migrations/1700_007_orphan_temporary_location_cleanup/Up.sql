SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 52200, N'找不到 dbo.SchemaVersions。', 1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-006'
    )
        THROW 52201, N'尚未套用 prerequisite Migration 1.7.0-006。', 1;

    IF EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-007'
    )
        THROW 52202, N'Migration 1.7.0-007 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.Locations', N'U') IS NULL
       OR OBJECT_ID(N'dbo.VisitTripStops', N'U') IS NULL
       OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
        THROW 52203, N'缺少 Locations / VisitTripStops / VisitTrips。', 1;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Cleaned INT = 0;

    /*
      Clean only true orphan temporary locations:
      - temporary
      - still Pending
      - not referenced by ANY non-cancelled trip

      This safely fixes orphan rows already created before the runtime fix,
      including UAT draft-deletion test data.
    */
    UPDATE l
       SET l.ApprovalStatus = N'Abandoned',
           l.GeocodingStatus = N'NotRequired',
           l.IsActive = 0,
           l.UpdatedAt = @Now
    FROM dbo.Locations l
    WHERE
        l.IsTemporary = 1
        AND l.ApprovalStatus = N'Pending'
        AND NOT EXISTS
        (
            SELECT 1
            FROM dbo.VisitTripStops s
            JOIN dbo.VisitTrips t
              ON t.VisitTripId = s.VisitTripId
            WHERE
                s.LocationId = l.LocationId
                AND t.Status <> N'Cancelled'
        );

    SET @Cleaned = @@ROWCOUNT;

    INSERT dbo.SchemaVersions
    (
        VersionNumber,
        Description,
        AppliedAt,
        AppliedBy
    )
    VALUES
    (
        N'1.7.0-007',
        N'Clean orphan temporary locations left by deleted drafts',
        @Now,
        N'v1.7.0 draft temporary-location cleanup defect correction'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-007 completed. Orphan temporary locations cleaned: '
        + CONVERT(nvarchar(20), @Cleaned);
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
