/*
  Option C Leader first-level review additive candidate.
  NONDEPLOY: executable only on disposable OptionCIsolated_* databases.
  Apply after canonical 011 -> 012 -> 014 and the StageEvidence candidate.
  Never execute against UAT/Production.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51080, 'OPTIONC_LEADER_REVIEW_ISOLATION_REQUIRED', 1;
IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL
   OR OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL
   OR OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
    THROW 51081, 'OPTIONC_LEADER_REVIEW_CANONICAL_SCHEMA_REQUIRED', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-014')
    THROW 51082, 'OPTIONC_LEADER_REVIEW_REQUIRES_014', 1;
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-LEADER-REVIEW')
    THROW 51083, 'OPTIONC_LEADER_REVIEW_ALREADY_APPLIED', 1;

BEGIN TRY
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ChangeRequestLeaderReviews',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChangeRequestLeaderReviews(
        ChangeRequestLeaderReviewId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_ChangeRequestLeaderReviews PRIMARY KEY,
        ChangeRequestId bigint NOT NULL,
        ReviewStage nvarchar(30) NOT NULL
            CONSTRAINT CK_OptionC_LeaderReview_Stage CHECK(ReviewStage=N'LeaderFirstLevel'),
        Status nvarchar(20) NOT NULL
            CONSTRAINT CK_OptionC_LeaderReview_Status CHECK(Status IN(N'Pending',N'Approved',N'Rejected')),
        ReviewerUserId int NULL,
        ReviewedAt datetime2(3) NULL,
        Reason nvarchar(1000) NULL,
        DecisionKey uniqueidentifier NULL,
        EvidenceJson nvarchar(max) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_OptionC_LeaderReview_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId),
        CONSTRAINT FK_OptionC_LeaderReview_Reviewer FOREIGN KEY(ReviewerUserId)
            REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_OptionC_LeaderReview_DecisionState CHECK(
            (Status=N'Pending' AND ReviewerUserId IS NULL AND ReviewedAt IS NULL
                AND DecisionKey IS NULL)
            OR
            (Status IN(N'Approved',N'Rejected') AND ReviewerUserId IS NOT NULL
                AND ReviewedAt IS NOT NULL AND DecisionKey IS NOT NULL
                AND LEN(LTRIM(RTRIM(ISNULL(Reason,N''))))>0))
    );
    CREATE UNIQUE INDEX UX_OptionC_LeaderReview_Request_Stage
        ON dbo.ChangeRequestLeaderReviews(ChangeRequestId,ReviewStage);
    CREATE UNIQUE INDEX UX_OptionC_LeaderReview_DecisionKey
        ON dbo.ChangeRequestLeaderReviews(DecisionKey)
        WHERE DecisionKey IS NOT NULL;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_B3_ChangeRequests_KnownCodes'
    AND parent_object_id=OBJECT_ID(N'dbo.ChangeRequests'))
BEGIN
    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownCodes;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownCodes
      CHECK(EntityKind=N'Location'
        AND OperationCode IN(N'CreateLocation',N'UpdateLocation',N'UpdatePublishedLocation')
        AND RiskCode IN(N'Low',N'High'));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_KnownCodes;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_B3_ChangeRequestEvents_EventType'
    AND parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents'))
BEGIN
    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
      CHECK(EventType IN(N'Submitted',N'Rejected',N'Approved',N'Applied',
                         N'GeocodeEvidenceRecorded',N'DuplicateReviewEvidenceRecorded',
                         N'LeaderApproved',N'LeaderRejected'));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE name IN(N'CK_B3_ChangeRequestEvents_DecisionState',
                  N'CK_B3_ChangeRequestEvents_StageEvidenceEventType')
      AND parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name=N'CK_B3_ChangeRequestEvents_DecisionState'
          AND parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents'))
        ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name=N'CK_B3_ChangeRequestEvents_StageEvidenceEventType'
          AND parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents'))
        ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_StageEvidenceEventType;

    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState
      CHECK(ActorUserId IS NOT NULL AND ActorUserId>0
        AND CorrelationId<>'00000000-0000-0000-0000-000000000000'
        AND ((EventType IN(N'Submitted',N'Applied') AND DecisionKey IS NULL)
          OR (EventType IN(N'Rejected',N'Approved',
                           N'GeocodeEvidenceRecorded',N'DuplicateReviewEvidenceRecorded',
                           N'LeaderApproved',N'LeaderRejected')
              AND DecisionKey IS NOT NULL
              AND DecisionKey<>'00000000-0000-0000-0000-000000000000')));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
END;

INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N'1.8.0-LEADER-REVIEW',N'Option C additive Leader first-level review candidate',SYSUTCDATETIME(),N'isolated-candidate');

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-016')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N'1.8.0-016',N'R5 canonical additive contract 1800_016',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-LEADER-REVIEW')),N'isolated-candidate');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
