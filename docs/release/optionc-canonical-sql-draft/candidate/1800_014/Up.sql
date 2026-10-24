/* OPTION C 014 CANDIDATE — STAGED LOCATION VERSIONS / PUBLISHED LEDGER.
   NONEXECUTABLE DESIGN. Do not run against UAT or Production. A future DBA
   review must replace this file with the approved forward-only migration,
   immutable source fingerprint, backup plan, permissions and rollback plan. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51410, N'OPTIONC_014_CANDIDATE_DISPOSABLE_DATABASE_ONLY', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',
        @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;
    IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
        ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-012'
        THROW 51411, N'OPTIONC_014_CANDIDATE_REQUIRES_012', 1;

    CREATE TABLE dbo.LocationPublicationStages
    (
        ChangeRequestId bigint NOT NULL CONSTRAINT PK_OptionC_Stage PRIMARY KEY,
        LocationId int NOT NULL,
        OrganizationId int NOT NULL,
        TeamId int NOT NULL,
        ExpectedLocationRowVersion binary(8) NOT NULL,
        ProposedJson nvarchar(max) NOT NULL,
        ProposedSha256 binary(32) NOT NULL,
        StageStatus nvarchar(30) NOT NULL,
        GeocodeStatus nvarchar(20) NOT NULL,
        GeocodeAddressSha256 binary(32) NULL,
        Latitude decimal(10,7) NULL,
        Longitude decimal(10,7) NULL,
        DuplicateReviewStatus nvarchar(20) NOT NULL,
        DuplicateEvidenceSha256 binary(32) NULL,
        CreatedAt datetime2(3) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_OptionC_Stage_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Stage_Location FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Stage_Org FOREIGN KEY(OrganizationId)
            REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Stage_Team FOREIGN KEY(TeamId)
            REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_Stage_Json CHECK (ISJSON(ProposedJson)=1),
        CONSTRAINT CK_OptionC_Stage_Hash CHECK
            (ProposedSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),ProposedJson))),
        CONSTRAINT CK_OptionC_Stage_Status CHECK
            (StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish',N'Published',N'Abandoned')),
        CONSTRAINT CK_OptionC_Stage_Geocode CHECK
            (GeocodeStatus IN(N'Pending',N'Succeeded',N'Failed',N'NotRequired')),
        CONSTRAINT CK_OptionC_Stage_Duplicate CHECK
            (DuplicateReviewStatus IN(N'Pending',N'Cleared',N'Blocked')),
        CONSTRAINT CK_OptionC_Stage_Coordinates CHECK
            ((Latitude IS NULL AND Longitude IS NULL) OR
             (Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180)),
        CONSTRAINT CK_OptionC_Stage_GeocodeEvidence CHECK
            (GeocodeStatus<>N'Succeeded' OR
             (GeocodeAddressSha256 IS NOT NULL AND Latitude IS NOT NULL AND Longitude IS NOT NULL)),
        CONSTRAINT CK_OptionC_Stage_AddressBinding CHECK
            (GeocodeStatus<>N'Succeeded' OR
             (JSON_VALUE(ProposedJson,N'$.Address') IS NOT NULL AND
              GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(ProposedJson,N'$.Address'))))),
        CONSTRAINT CK_OptionC_Stage_DuplicateBinding CHECK
            (DuplicateReviewStatus<>N'Cleared' OR DuplicateEvidenceSha256=ProposedSha256),
        CONSTRAINT CK_OptionC_Stage_Ready CHECK
            (StageStatus<>N'ReadyForPublish' OR
             (DuplicateReviewStatus=N'Cleared' AND DuplicateEvidenceSha256 IS NOT NULL
              AND GeocodeStatus IN(N'Succeeded',N'NotRequired')))
    );
    CREATE UNIQUE INDEX UX_OptionC_Stages_Request ON dbo.LocationPublicationStages(ChangeRequestId);
    CREATE UNIQUE INDEX UX_OptionC_Stages_OpenLocation
        ON dbo.LocationPublicationStages(OrganizationId,LocationId)
        WHERE StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish');

    CREATE TABLE dbo.LocationPublishedVersions
    (
        LocationPublishedVersionId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_OptionC_PublishedVersion PRIMARY KEY,
        LocationId int NOT NULL,
        VersionNumber int NOT NULL,
        ChangeRequestId bigint NULL,
        IsCurrent bit NOT NULL,
        PublishedJson nvarchar(max) NOT NULL,
        PublishedSha256 binary(32) NOT NULL,
        PublishedAt datetime2(3) NOT NULL,
        PublishedByUserId int NOT NULL,
        SourceLocationRowVersion binary(8) NOT NULL,
        CONSTRAINT FK_OptionC_Published_Location FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Published_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_Published_Number CHECK(VersionNumber>0),
        CONSTRAINT CK_OptionC_Published_Json CHECK(ISJSON(PublishedJson)=1),
        CONSTRAINT CK_OptionC_Published_Hash CHECK
            (PublishedSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),PublishedJson))),
        CONSTRAINT CK_OptionC_Published_Origin CHECK
            ((VersionNumber=1 AND ChangeRequestId IS NULL) OR
             (VersionNumber>1 AND ChangeRequestId IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_OptionC_Published_Current
        ON dbo.LocationPublishedVersions(LocationId) WHERE IsCurrent=1;
    CREATE UNIQUE INDEX UX_OptionC_Published_Request
        ON dbo.LocationPublishedVersions(ChangeRequestId)
        WHERE ChangeRequestId IS NOT NULL;
    CREATE UNIQUE INDEX UX_OptionC_Published_Number
        ON dbo.LocationPublishedVersions(LocationId,VersionNumber);

    /* No blind backfill: a future migration must insert version 1 only after
       DBA proves a canonical JSON projection for every existing Location and
       records the source RowVersion. Ambiguous rows must stop the migration. */
    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N'1.8.0-014',N'OPTION C candidate: staged and published location versions',
           SYSUTCDATETIME(),N'OPTION C candidate; execution prohibited');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
