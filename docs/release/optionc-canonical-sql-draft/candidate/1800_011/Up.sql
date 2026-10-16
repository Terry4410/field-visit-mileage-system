/*
  1800_011 B3 schema candidate -- FORWARD ONLY.
  Execution is prohibited until this exact immutable file, Preflight.sql and
  Verify.sql are independently reviewed and explicitly approved for one UAT DB.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock
        @Resource=N'FieldVisit.SchemaMigration', @LockMode=N'Exclusive',
        @LockOwner=N'Transaction', @LockTimeout=0;
    IF @LockResult < 0 THROW 51110, N'Unable to acquire FieldVisit migration lock.', 1;

    IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
        THROW 51111, N'1800_011 target database is invalid.', 1;
    IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
       OR OBJECT_ID(N'dbo.Organizations',N'U') IS NULL
       OR OBJECT_ID(N'dbo.Teams',N'U') IS NULL
       OR OBJECT_ID(N'dbo.Users',N'U') IS NULL
       OR OBJECT_ID(N'dbo.Locations',N'U') IS NULL
        THROW 51112, N'1800_011 prerequisite catalog is incomplete.', 1;
    IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-011')
        THROW 51113, N'1800_011 already exists; no rerun allowed.', 1;
    IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions
        ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-010'
        THROW 51114, N'Latest SchemaVersion must be exactly 1.8.0-010.', 1;
    IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NOT NULL
        THROW 51115, N'B3 tables already exist; stop for DBA drift review.', 1;

    CREATE TABLE dbo.ChangeRequests
    (
        ChangeRequestId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_B3_ChangeRequests PRIMARY KEY,
        RequestPublicId uniqueidentifier NOT NULL,
        OrganizationId int NOT NULL,
        TeamId int NULL,
        EntityKind nvarchar(40) NOT NULL,
        EntityId nvarchar(80) NOT NULL,
        OperationCode nvarchar(80) NOT NULL,
        RiskCode nvarchar(20) NOT NULL,
        ExpectedEntityRowVersion varbinary(8) NULL,
        BeforeJson nvarchar(max) NULL,
        ProposedJson nvarchar(max) NOT NULL,
        EvidenceJson nvarchar(max) NULL,
        RequestedByUserId int NOT NULL,
        SubmittedAt datetime2(3) NOT NULL,
        Status nvarchar(30) NOT NULL,
        ReviewedByUserId int NULL,
        ReviewedAt datetime2(3) NULL,
        ReviewReason nvarchar(1000) NULL,
        AppliedAt datetime2(3) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_B3_Requests_Org FOREIGN KEY(OrganizationId)
            REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Requests_Team FOREIGN KEY(TeamId)
            REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Requests_Requester FOREIGN KEY(RequestedByUserId)
            REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Requests_Reviewer FOREIGN KEY(ReviewedByUserId)
            REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
        CONSTRAINT CK_B3_ChangeRequests_KnownCodes CHECK
            ([EntityKind]=N'Location' AND [OperationCode]=N'UpdatePublishedLocation' AND [RiskCode]=N'High'),
        CONSTRAINT CK_B3_ChangeRequests_KnownStatus CHECK
            ([Status] IN (N'Pending',N'Rejected')),
        CONSTRAINT CK_B3_ChangeRequests_ExpectedLocationVersion CHECK
            ([ExpectedEntityRowVersion] IS NOT NULL AND DATALENGTH([ExpectedEntityRowVersion])=8),
        CONSTRAINT CK_B3_ChangeRequests_ProposedJson CHECK (ISJSON([ProposedJson])=1),
        CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK
            (([Status]=N'Pending' AND [ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL)
             OR ([Status]=N'Rejected' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId]
                 AND [ReviewedAt] IS NOT NULL AND [ReviewReason] IS NOT NULL
                 AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NULL))
    );
    CREATE UNIQUE INDEX UX_B3_ChangeRequests_RequestPublicId
        ON dbo.ChangeRequests(RequestPublicId);
    CREATE UNIQUE INDEX UX_B3_ChangeRequests_Org_Entity_Pending
        ON dbo.ChangeRequests(OrganizationId,EntityKind,EntityId) WHERE [Status]=N'Pending';
    CREATE INDEX IX_B3_ChangeRequests_Org_Status_SubmittedAt
        ON dbo.ChangeRequests(OrganizationId,Status,SubmittedAt);
    CREATE INDEX IX_B3_ChangeRequests_Requester_SubmittedAt
        ON dbo.ChangeRequests(RequestedByUserId,SubmittedAt);

    CREATE TABLE dbo.ChangeRequestEvents
    (
        ChangeRequestEventId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_B3_ChangeRequestEvents PRIMARY KEY,
        ChangeRequestId bigint NOT NULL,
        EventType nvarchar(40) NOT NULL,
        ActorUserId int NULL,
        OccurredAt datetime2(3) NOT NULL,
        CorrelationId uniqueidentifier NOT NULL,
        DecisionKey uniqueidentifier NULL,
        DetailsJson nvarchar(max) NULL,
        CONSTRAINT FK_B3_Events_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Events_Actor FOREIGN KEY(ActorUserId)
            REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
        CONSTRAINT CK_B3_ChangeRequestEvents_EventType CHECK
            ([EventType] IN (N'Submitted',N'Rejected')),
        CONSTRAINT CK_B3_ChangeRequestEvents_DetailsJson CHECK
            ([DetailsJson] IS NULL OR ISJSON([DetailsJson])=1),
        CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState CHECK
            (([EventType]=N'Submitted' AND [DecisionKey] IS NULL AND [ActorUserId] IS NOT NULL)
             OR ([EventType]=N'Rejected' AND [DecisionKey] IS NOT NULL AND [ActorUserId] IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_B3_ChangeRequestEvents_DecisionKey
        ON dbo.ChangeRequestEvents(DecisionKey) WHERE [DecisionKey] IS NOT NULL;
    CREATE INDEX IX_B3_ChangeRequestEvents_Request_OccurredAt
        ON dbo.ChangeRequestEvents(ChangeRequestId,OccurredAt,ChangeRequestEventId);
    CREATE INDEX IX_B3_ChangeRequestEvents_CorrelationId
        ON dbo.ChangeRequestEvents(CorrelationId);

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N'1.8.0-011',N'Package B candidate: high-risk Location change request persistence',SYSUTCDATETIME(),N'Package B candidate; execution requires Owner approval');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
