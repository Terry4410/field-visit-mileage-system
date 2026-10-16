/*
  SCHEME A CREATE / LOW-RISK PUBLICATION CONTRACT — NONDEPLOY CANDIDATE.
  Disposable OptionCIsolated_* SQL Server 2022 ONLY.
  Requires 011 -> 012 -> 014 -> StageEvidenceAudit -> LeaderReviewDetails.
  No UAT/Production migration, no feature activation, no publication writer.
  A reservation row alone does NOT authorize a published Location.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51090, N'OPTIONC_CREATE_LOWRISK_ISOLATION_REQUIRED', 1;
IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL
   OR OBJECT_ID(N'dbo.ChangeRequestLeaderReviews',N'U') IS NULL
   OR OBJECT_ID(N'dbo.LocationPublicationStages',N'U') IS NULL
   OR OBJECT_ID(N'dbo.LocationPublishedVersions',N'U') IS NULL
   OR OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
    THROW 51091, N'OPTIONC_CREATE_LOWRISK_DEPENDENCIES_MISSING', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
              WHERE VersionNumber=N'1.8.0-LEADER-REVIEW')
    THROW 51092, N'OPTIONC_CREATE_LOWRISK_REQUIRES_LEADER_REVIEW', 1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions
          WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
    THROW 51093, N'OPTIONC_CREATE_LOWRISK_ALREADY_APPLIED', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock
        @Resource=N'FieldVisit.SchemaMigration',@LockMode=N'Exclusive',
        @LockOwner=N'Transaction',@LockTimeout=0;
    IF @LockResult<0 THROW 51094,N'OPTIONC_CREATE_LOWRISK_LOCK_DENIED',1;
    IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
        ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-LEADER-REVIEW'
        THROW 51095,N'OPTIONC_CREATE_LOWRISK_UNEXPECTED_MIGRATION_ORDER',1;
    IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NOT NULL
        THROW 51096,N'OPTIONC_CREATE_LOWRISK_PARTIAL_SCHEMA',1;
    IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequests')
          AND name=N'CK_B3_ChangeRequests_KnownCodes'
          AND is_disabled=0 AND is_not_trusted=0)
       OR NOT EXISTS(SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.LocationPublishedVersions')
          AND name=N'CK_OptionC_Published_Origin'
          AND is_disabled=0 AND is_not_trusted=0)
        THROW 51097,N'OPTIONC_CREATE_LOWRISK_CANONICAL_CONSTRAINT_DRIFT',1;

    /* Preserve the same canonical request ledger and RowVersion requirement.
       CreateLocation uses the RowVersion of its inactive Pending reservation;
       Low updates use the RowVersion of the existing published Location. */
    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownCodes;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownCodes
        CHECK(EntityKind=N'Location' AND
          ((OperationCode=N'CreateLocation' AND RiskCode=N'High')
           OR (OperationCode=N'UpdatePublishedLocation'
               AND RiskCode IN(N'Low',N'High'))));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_KnownCodes;

    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_OptionC_CreateLowRisk_BeforeImage
        CHECK((OperationCode=N'CreateLocation' AND BeforeJson IS NULL)
           OR (OperationCode=N'UpdatePublishedLocation'
               AND BeforeJson IS NOT NULL AND ISJSON(BeforeJson)=1));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_OptionC_CreateLowRisk_BeforeImage;

    /* Canonical backfill v1 has no request. Canonical CreateLocation v1 has a
       request. Both are allowed; later versions must have a request. The
       application writer MUST verify that the v1 request is CreateLocation
       and that the reservation is Pending/inactive before atomic publication. */
    ALTER TABLE dbo.LocationPublishedVersions DROP CONSTRAINT CK_OptionC_Published_Origin;
    ALTER TABLE dbo.LocationPublishedVersions WITH CHECK ADD CONSTRAINT CK_OptionC_Published_Origin
        CHECK(VersionNumber=1 OR
              (VersionNumber>1 AND ChangeRequestId IS NOT NULL));
    ALTER TABLE dbo.LocationPublishedVersions CHECK CONSTRAINT CK_OptionC_Published_Origin;

    CREATE TABLE dbo.LocationCreateReservations(
        ChangeRequestId bigint NOT NULL
            CONSTRAINT PK_OptionC_CreateReservation PRIMARY KEY,
        LocationId int NOT NULL,
        OrganizationId int NOT NULL,
        TeamId int NOT NULL,
        ReservedLocationRowVersion binary(8) NOT NULL,
        ReservationStatus nvarchar(20) NOT NULL,
        CreatedAt datetime2(3) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_OptionC_CreateReservation_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_CreateReservation_Location FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_CreateReservation_Org FOREIGN KEY(OrganizationId)
            REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_CreateReservation_Team FOREIGN KEY(TeamId)
            REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_CreateReservation_Status CHECK
            (ReservationStatus IN(N'Pending',N'Published',N'Abandoned'))
    );
    CREATE UNIQUE INDEX UX_OptionC_CreateReservation_Location
        ON dbo.LocationCreateReservations(LocationId);

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
      VALUES(N'1.8.0-CREATE-LOWRISK',
        N'Scheme A disposable CreateLocation/LowRisk canonical contract',
        SYSUTCDATETIME(),N'isolated-candidate');
    IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-017')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N'1.8.0-017',N'R5 canonical additive contract 1800_017',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')),N'isolated-candidate');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
