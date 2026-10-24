/*
 * Option C STAGING TABLE CONTRACT — DISPOSABLE SQL SERVER ONLY.
 * NOT A MIGRATION. No version assignment. No UAT/production execution.
 * A future reviewed migration must remove this isolated-db hard stop and
 * add its own preflight, hash pinning, Verify and rollback plan.
 */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51340, N'OPTIONC_STAGE_DRAFT_DISPOSABLE_DATABASE_ONLY', 1;
IF OBJECT_ID(N'dbo.ChangeRequests', N'U') IS NULL
    OR OBJECT_ID(N'dbo.Locations', N'U') IS NULL
    OR OBJECT_ID(N'dbo.LocationPublicationStages', N'U') IS NOT NULL
    THROW 51341, N'OPTIONC_STAGE_DRAFT_PREREQUISITE_OR_DRIFT', 1;
IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-012'
    THROW 51342, N'OPTIONC_STAGE_DRAFT_REQUIRES_ISOLATED_012', 1;

CREATE TABLE dbo.LocationPublicationStages
(
    ChangeRequestId bigint NOT NULL
        CONSTRAINT PK_OptionC_LocationStages PRIMARY KEY,
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
    CONSTRAINT CK_OptionC_Stage_ProposedJson CHECK (ISJSON(ProposedJson)=1),
    CONSTRAINT CK_OptionC_Stage_Hash CHECK
        (ProposedSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),ProposedJson))),
    CONSTRAINT CK_OptionC_Stage_Status CHECK
        (StageStatus IN (N'PendingGeocode',N'PendingDuplicateReview',
            N'ReadyForPublish',N'Published',N'Abandoned')),
    CONSTRAINT CK_OptionC_Stage_Geocode CHECK
        (GeocodeStatus IN (N'Pending',N'Succeeded',N'Failed',N'NotRequired')),
    CONSTRAINT CK_OptionC_Stage_Duplicate CHECK
        (DuplicateReviewStatus IN (N'Pending',N'Cleared',N'Blocked')),
    CONSTRAINT CK_OptionC_Stage_Coordinates CHECK
        ((Latitude IS NULL AND Longitude IS NULL)
          OR (Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180)),
    CONSTRAINT CK_OptionC_Stage_GeocodeEvidence CHECK
        (GeocodeStatus<>N'Succeeded'
          OR (GeocodeAddressSha256 IS NOT NULL
              AND Latitude IS NOT NULL AND Longitude IS NOT NULL)),
    -- A successful geocode is valid ONLY for the exact staged address.
    -- Prevent stale callbacks for a previous address from satisfying publish.
    CONSTRAINT CK_OptionC_Stage_GeocodeAddressBinding CHECK
        (GeocodeStatus<>N'Succeeded'
          OR (JSON_VALUE(ProposedJson,N'$.Address') IS NOT NULL
              AND GeocodeAddressSha256=HASHBYTES('SHA2_256',
                  CONVERT(varbinary(max),JSON_VALUE(ProposedJson,N'$.Address'))))),
    -- Duplicate-review evidence is version-scoped, not arbitrary proof bytes.
    CONSTRAINT CK_OptionC_Stage_DuplicateVersionBinding CHECK
        (DuplicateReviewStatus<>N'Cleared'
          OR DuplicateEvidenceSha256=ProposedSha256),
    CONSTRAINT CK_OptionC_Stage_Ready CHECK
        (StageStatus<>N'ReadyForPublish'
          OR (DuplicateReviewStatus=N'Cleared'
              AND DuplicateEvidenceSha256 IS NOT NULL
              AND GeocodeStatus IN(N'Succeeded',N'NotRequired')))
);
CREATE UNIQUE INDEX UX_OptionC_Stage_Org_Location_Open
    ON dbo.LocationPublicationStages(OrganizationId,LocationId)
    WHERE StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish');
CREATE INDEX IX_OptionC_Stage_Status
    ON dbo.LocationPublicationStages(OrganizationId,StageStatus,UpdatedAt);

/*
 * Published-version ledger (isolated-contract rehearsal only).
 * LocationId remains stable for VisitTripStop, Snapshot, merge aliases and
 * reports. Every applied publication creates one NEW ledger row; older
 * versions are retained. No delete cascade and no backfill guesses.
 * The eventual atomic writer must acquire Request -> Location -> Stage locks
 * and update the current marker plus original Locations in one transaction.
 */
CREATE TABLE dbo.LocationPublishedVersions
(
    LocationPublishedVersionId bigint IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_OptionC_LocationPublishedVersions PRIMARY KEY,
    LocationId int NOT NULL,
    VersionNumber int NOT NULL,
    ChangeRequestId bigint NULL,
    IsCurrent bit NOT NULL,
    PublishedJson nvarchar(max) NOT NULL,
    PublishedSha256 binary(32) NOT NULL,
    PublishedAt datetime2(3) NOT NULL,
    PublishedByUserId int NOT NULL,
    SourceLocationRowVersion binary(8) NOT NULL,
    CONSTRAINT FK_OptionC_Version_Location FOREIGN KEY(LocationId)
        REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
    CONSTRAINT FK_OptionC_Version_Request FOREIGN KEY(ChangeRequestId)
        REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
    CONSTRAINT CK_OptionC_Version_Number CHECK (VersionNumber > 0),
    CONSTRAINT CK_OptionC_Version_Actor CHECK (PublishedByUserId > 0),
    CONSTRAINT CK_OptionC_Version_Json CHECK (ISJSON(PublishedJson)=1),
    CONSTRAINT CK_OptionC_Version_Hash CHECK
        (PublishedSha256=HASHBYTES('SHA2_256',
            CONVERT(varbinary(max),PublishedJson))),
    CONSTRAINT CK_OptionC_Version_Origin CHECK
        ((VersionNumber=1 AND ChangeRequestId IS NULL)
          OR (VersionNumber>1 AND ChangeRequestId IS NOT NULL))
);
CREATE UNIQUE INDEX UX_OptionC_Version_Location_Number
    ON dbo.LocationPublishedVersions(LocationId,VersionNumber);
CREATE UNIQUE INDEX UX_OptionC_Version_Current
    ON dbo.LocationPublishedVersions(LocationId) WHERE IsCurrent=1;
CREATE UNIQUE INDEX UX_OptionC_Version_ApprovedRequest
    ON dbo.LocationPublishedVersions(ChangeRequestId) WHERE ChangeRequestId IS NOT NULL;
/* Ledger immutability must be additionally enforced by the future migration's
 * SQL permission boundaries. Only IsCurrent may transition 1 -> 0 during an
 * authorized serializable publication; no public update/delete route exists. */

/* Never alter SchemaVersions: this is not a migration. */
