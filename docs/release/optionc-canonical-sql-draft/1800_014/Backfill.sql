/* OPTION C 014 CANDIDATE — deterministic historical version-1 backfill.
   NONEXECUTABLE DESIGN / disposable rehearsal only. Never run against UAT or
   Production. The script refuses any database outside OptionCIsolated_* and
   refuses a partially populated or ambiguous source. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51430, N'OPTIONC_014_BACKFILL_DISPOSABLE_DATABASE_ONLY', 1;
IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-014'
    THROW 51431, N'OPTIONC_014_BACKFILL_REQUIRES_014', 1;
IF EXISTS (SELECT 1 FROM dbo.LocationPublishedVersions)
    THROW 51432, N'OPTIONC_014_BACKFILL_ALREADY_APPLIED', 1;

/* The projection is deliberately explicit and ordered. Only Approved+Active
   masters are published; Pending/Inactive drafts remain outside the ledger. */
IF EXISTS (
    SELECT 1
    FROM dbo.Locations l
    WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1
      AND (l.LocationName IS NULL OR NULLIF(LTRIM(RTRIM(l.LocationName)),N'') IS NULL
        OR l.Address IS NULL OR NULLIF(LTRIM(RTRIM(l.Address)),N'') IS NULL
        OR l.CreatedByUserId<=0 OR l.RowVersion IS NULL)
)
    THROW 51433, N'OPTIONC_014_BACKFILL_AMBIGUOUS_PUBLISHED_SOURCE', 1;
IF EXISTS (
    SELECT 1
    FROM dbo.Locations l
    LEFT JOIN dbo.Organizations o ON o.OrganizationId=l.OrganizationId
    LEFT JOIN dbo.Teams t ON t.TeamId=l.TeamId
    LEFT JOIN dbo.Users u ON u.UserId=l.CreatedByUserId
    WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1
      AND (o.OrganizationId IS NULL OR t.TeamId IS NULL OR u.UserId IS NULL)
)
    THROW 51434, N'OPTIONC_014_BACKFILL_MISSING_AUTHORITY_REFERENCE', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    EXEC sys.sp_getapplock @Resource=N'FieldVisit.OptionC014.Backfill',
        @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;
    IF EXISTS (SELECT 1 FROM dbo.LocationPublishedVersions)
        THROW 51435, N'OPTIONC_014_BACKFILL_RACE_OR_RERUN', 1;

    INSERT dbo.LocationPublishedVersions(
        LocationId,VersionNumber,ChangeRequestId,IsCurrent,
        PublishedJson,PublishedSha256,PublishedAt,PublishedByUserId,
        SourceLocationRowVersion)
    SELECT l.LocationId,1,NULL,1,j.PublishedJson,
        HASHBYTES('SHA2_256',CONVERT(varbinary(max),j.PublishedJson)),
        SYSUTCDATETIME(),l.CreatedByUserId,l.RowVersion
    FROM dbo.Locations l
    CROSS APPLY (
        SELECT l.LocationId,l.OrganizationId,l.TeamId,l.CreatedByUserId,
               l.LocationType,l.LocationName,l.City,l.District,l.Address,
               l.PlusCode,l.TaxId,l.MasterNote,l.ApprovalStatus,l.IsActive
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
    ) j(PublishedJson)
    WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1;

    IF EXISTS (
        SELECT 1 FROM dbo.LocationPublishedVersions v
        WHERE v.IsCurrent<>1 OR v.VersionNumber<>1 OR v.ChangeRequestId IS NOT NULL
    )
        THROW 51436, N'OPTIONC_014_BACKFILL_LEDGER_INVARIANT_FAILED', 1;
    IF EXISTS (
        SELECT 1 FROM dbo.LocationPublishedVersions v
        WHERE v.PublishedSha256<>HASHBYTES('SHA2_256',CONVERT(varbinary(max),v.PublishedJson))
    )
        THROW 51437, N'OPTIONC_014_BACKFILL_HASH_INVARIANT_FAILED', 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
