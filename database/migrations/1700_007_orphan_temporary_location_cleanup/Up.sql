SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-007
       Draft temporary-location orphan cleanup + ApprovalStatus alignment

       Why:
       Runtime cleanup uses ApprovalStatus = 'Abandoned', but the legacy
       CK_Locations_ApprovalStatus constraint only allowed:
       Pending / Approved / Rejected.

       This migration:
       1. Expands the constraint to include Abandoned.
       2. Cleans only true orphan temporary locations.
       3. Registers SchemaVersion 1.7.0-007.

       Safe to run only because the previous 007 attempt rolled back and
       SchemaVersions does not yet contain 1.7.0-007.
       ================================================================ */

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

    /* Refuse to narrow or overwrite any unexpected production status. */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.Locations
        WHERE ApprovalStatus IS NULL
           OR UPPER(LTRIM(RTRIM(ApprovalStatus)))
              NOT IN (N'PENDING', N'APPROVED', N'REJECTED', N'ABANDONED')
    )
        THROW 52204,
              N'Locations.ApprovalStatus 存在非預期值；停止 Migration，請由 IT Review。',
              1;

    /*
       Align DB constraint with runtime domain.
       Current UAT constraint allows Pending / Approved / Rejected only.
       Abandoned means: a temporary location no longer needed because all
       referencing trips were cancelled/deleted. It is not a human rejection.
    */
    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.Locations')
          AND name = N'CK_Locations_ApprovalStatus'
    )
    BEGIN
        ALTER TABLE dbo.Locations
            DROP CONSTRAINT CK_Locations_ApprovalStatus;
    END;

    ALTER TABLE dbo.Locations WITH CHECK
        ADD CONSTRAINT CK_Locations_ApprovalStatus
        CHECK
        (
            UPPER(LTRIM(RTRIM(ApprovalStatus)))
            IN (N'PENDING', N'APPROVED', N'REJECTED', N'ABANDONED')
        );

    ALTER TABLE dbo.Locations
        CHECK CONSTRAINT CK_Locations_ApprovalStatus;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Cleaned INT = 0;

    /*
       Clean only true orphan temporary locations:
       - temporary
       - still Pending
       - not referenced by ANY non-cancelled trip
    */
    UPDATE l
       SET l.ApprovalStatus = N'Abandoned',
           l.GeocodingStatus = N'NotRequired',
           l.IsActive = 0,
           l.UpdatedAt = @Now
    FROM dbo.Locations l
    WHERE
        l.IsTemporary = 1
        AND UPPER(LTRIM(RTRIM(l.ApprovalStatus))) = N'PENDING'
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
        N'Allow Abandoned location status and clean orphan temporary locations left by deleted drafts',
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
