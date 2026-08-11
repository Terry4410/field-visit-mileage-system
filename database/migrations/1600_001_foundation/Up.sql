SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.UserTeamScopes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserTeamScopes(
        UserTeamScopeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserTeamScopes PRIMARY KEY,
        UserId INT NOT NULL,
        TeamId INT NOT NULL,
        IsPrimary BIT NOT NULL CONSTRAINT DF_UserTeamScopes_IsPrimary DEFAULT(0),
        IsActive BIT NOT NULL CONSTRAINT DF_UserTeamScopes_IsActive DEFAULT(1),
        AssignedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserTeamScopes_AssignedAt DEFAULT(SYSUTCDATETIME()),
        AssignedByUserId INT NULL,
        EndedAt DATETIME2(3) NULL,
        CONSTRAINT FK_UserTeamScopes_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_UserTeamScopes_Teams FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_UserTeamScopes_AssignedBy FOREIGN KEY(AssignedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT UQ_UserTeamScopes_User_Team UNIQUE(UserId, TeamId)
    );
    CREATE INDEX IX_UserTeamScopes_Team_Active ON dbo.UserTeamScopes(TeamId, IsActive, UserId);
END;

INSERT INTO dbo.UserTeamScopes(UserId, TeamId, IsPrimary, IsActive, AssignedAt)
SELECT u.UserId, u.TeamId, 1, 1, SYSUTCDATETIME()
FROM dbo.Users u
WHERE u.TeamId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.UserTeamScopes s WHERE s.UserId=u.UserId AND s.TeamId=u.TeamId
  );

IF OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VisitTripSnapshots(
        VisitTripSnapshotId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VisitTripSnapshots PRIMARY KEY,
        VisitTripId BIGINT NOT NULL,
        SnapshotVersion INT NOT NULL,
        SnapshotType NVARCHAR(30) NOT NULL,
        TripNo NVARCHAR(50) NOT NULL,
        UserId INT NOT NULL,
        EmployeeNoSnapshot NVARCHAR(50) NOT NULL,
        DisplayNameSnapshot NVARCHAR(200) NOT NULL,
        OrganizationId INT NOT NULL,
        OrganizationNameSnapshot NVARCHAR(200) NOT NULL,
        TeamId INT NULL,
        TeamNameSnapshot NVARCHAR(200) NULL,
        VisitDate DATE NOT NULL,
        StartTime TIME(0) NULL,
        EndTime TIME(0) NULL,
        StatusSnapshot NVARCHAR(50) NOT NULL,
        VehicleTypeSnapshot NVARCHAR(50) NULL,
        ClaimedDistanceKmSnapshot DECIMAL(10,2) NULL,
        SystemDistanceKmSnapshot DECIMAL(10,2) NULL,
        ApprovedDistanceKmSnapshot DECIMAL(10,2) NULL,
        RatePerKmSnapshot DECIMAL(10,2) NULL,
        SubsidyAmountSnapshot DECIMAL(12,2) NULL,
        RouteProviderSnapshot NVARCHAR(100) NULL,
        SubmittedAtSnapshot DATETIME2(3) NULL,
        ApprovedAtSnapshot DATETIME2(3) NULL,
        ApproverUserId INT NULL,
        ApproverNameSnapshot NVARCHAR(200) NULL,
        NotesSnapshot NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_VisitTripSnapshots_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        CONSTRAINT FK_VisitTripSnapshots_VisitTrips FOREIGN KEY(VisitTripId) REFERENCES dbo.VisitTrips(VisitTripId),
        CONSTRAINT UQ_VisitTripSnapshots_Trip_Version UNIQUE(VisitTripId, SnapshotVersion)
    );
    CREATE INDEX IX_VisitTripSnapshots_VisitDate ON dbo.VisitTripSnapshots(VisitDate, OrganizationId, TeamId);
END;

IF OBJECT_ID(N'dbo.VisitTripSnapshotStops', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VisitTripSnapshotStops(
        VisitTripSnapshotStopId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VisitTripSnapshotStops PRIMARY KEY,
        VisitTripSnapshotId BIGINT NOT NULL,
        StopSequence INT NOT NULL,
        LocationId INT NULL,
        LocationCodeSnapshot NVARCHAR(50) NULL,
        LocationNameSnapshot NVARCHAR(200) NOT NULL,
        AddressSnapshot NVARCHAR(500) NULL,
        ProjectId INT NULL,
        ProjectCodeSnapshot NVARCHAR(50) NULL,
        ProjectNameSnapshot NVARCHAR(200) NULL,
        VisitTypeId INT NULL,
        VisitTypeCodeSnapshot NVARCHAR(50) NULL,
        VisitTypeNameSnapshot NVARCHAR(200) NULL,
        VisitPurposeSnapshot NVARCHAR(500) NULL,
        NotesSnapshot NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_VisitTripSnapshotStops_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_VisitTripSnapshotStops_Snapshot FOREIGN KEY(VisitTripSnapshotId) REFERENCES dbo.VisitTripSnapshots(VisitTripSnapshotId) ON DELETE CASCADE,
        CONSTRAINT UQ_VisitTripSnapshotStops_Snapshot_Sequence UNIQUE(VisitTripSnapshotId, StopSequence)
    );
END;

IF OBJECT_ID(N'dbo.CorrectionRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CorrectionRequests(
        CorrectionRequestId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CorrectionRequests PRIMARY KEY,
        VisitTripId BIGINT NOT NULL,
        BaseSnapshotId BIGINT NOT NULL,
        ResultSnapshotId BIGINT NULL,
        Status NVARCHAR(40) NOT NULL,
        Reason NVARCHAR(1000) NOT NULL,
        ProposedChangesJson NVARCHAR(MAX) NULL,
        RequestedByUserId INT NOT NULL,
        RequestedAt DATETIME2(3) NOT NULL CONSTRAINT DF_CorrectionRequests_RequestedAt DEFAULT(SYSUTCDATETIME()),
        LeaderReviewedByUserId INT NULL,
        LeaderReviewedAt DATETIME2(3) NULL,
        LeaderComments NVARCHAR(1000) NULL,
        AdminClosedByUserId INT NULL,
        AdminClosedAt DATETIME2(3) NULL,
        AdminComments NVARCHAR(1000) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_CorrectionRequests_Trip FOREIGN KEY(VisitTripId) REFERENCES dbo.VisitTrips(VisitTripId),
        CONSTRAINT FK_CorrectionRequests_BaseSnapshot FOREIGN KEY(BaseSnapshotId) REFERENCES dbo.VisitTripSnapshots(VisitTripSnapshotId),
        CONSTRAINT FK_CorrectionRequests_ResultSnapshot FOREIGN KEY(ResultSnapshotId) REFERENCES dbo.VisitTripSnapshots(VisitTripSnapshotId),
        CONSTRAINT CK_CorrectionRequests_Status CHECK(Status IN (N'PendingLeaderReview',N'PendingAdminClose',N'Approved',N'Rejected',N'Closed'))
    );
    CREATE INDEX IX_CorrectionRequests_Trip_Status ON dbo.CorrectionRequests(VisitTripId, Status, RequestedAt DESC);
END;

IF OBJECT_ID(N'dbo.CorrectionRequestChanges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CorrectionRequestChanges(
        CorrectionRequestChangeId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CorrectionRequestChanges PRIMARY KEY,
        CorrectionRequestId BIGINT NOT NULL,
        FieldName NVARCHAR(200) NOT NULL,
        OldValue NVARCHAR(MAX) NULL,
        NewValue NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_CorrectionRequestChanges_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_CorrectionRequestChanges_Request FOREIGN KEY(CorrectionRequestId) REFERENCES dbo.CorrectionRequests(CorrectionRequestId) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BackgroundJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BackgroundJobs(
        BackgroundJobId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BackgroundJobs PRIMARY KEY CONSTRAINT DF_BackgroundJobs_Id DEFAULT(NEWID()),
        JobType NVARCHAR(50) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        Mode NVARCHAR(50) NULL,
        OrganizationId INT NULL,
        TeamScopeJson NVARCHAR(MAX) NULL,
        StartDate DATE NULL,
        EndDate DATE NULL,
        RequestedByUserId INT NOT NULL,
        TotalCount INT NOT NULL CONSTRAINT DF_BackgroundJobs_Total DEFAULT(0),
        SuccessCount INT NOT NULL CONSTRAINT DF_BackgroundJobs_Success DEFAULT(0),
        FailedCount INT NOT NULL CONSTRAINT DF_BackgroundJobs_Failed DEFAULT(0),
        SkippedCount INT NOT NULL CONSTRAINT DF_BackgroundJobs_Skipped DEFAULT(0),
        PayloadJson NVARCHAR(MAX) NULL,
        ResultJson NVARCHAR(MAX) NULL,
        ErrorMessage NVARCHAR(2000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BackgroundJobs_CreatedAt DEFAULT(SYSUTCDATETIME()),
        StartedAt DATETIME2(3) NULL,
        CompletedAt DATETIME2(3) NULL,
        CONSTRAINT CK_BackgroundJobs_Status CHECK(Status IN (N'Waiting',N'Processing',N'Succeeded',N'PartiallySucceeded',N'Failed'))
    );
    CREATE INDEX IX_BackgroundJobs_Requester_Status ON dbo.BackgroundJobs(RequestedByUserId, Status, CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.BackgroundJobItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BackgroundJobItems(
        BackgroundJobItemId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BackgroundJobItems PRIMARY KEY,
        BackgroundJobId UNIQUEIDENTIFIER NOT NULL,
        EntityType NVARCHAR(50) NOT NULL,
        EntityId NVARCHAR(100) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        ResultJson NVARCHAR(MAX) NULL,
        ErrorCode NVARCHAR(100) NULL,
        ErrorMessage NVARCHAR(2000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BackgroundJobItems_CreatedAt DEFAULT(SYSUTCDATETIME()),
        StartedAt DATETIME2(3) NULL,
        CompletedAt DATETIME2(3) NULL,
        CONSTRAINT FK_BackgroundJobItems_Job FOREIGN KEY(BackgroundJobId) REFERENCES dbo.BackgroundJobs(BackgroundJobId) ON DELETE CASCADE,
        CONSTRAINT CK_BackgroundJobItems_Status CHECK(Status IN (N'Waiting',N'Processing',N'Succeeded',N'Failed',N'Skipped'))
    );
    CREATE INDEX IX_BackgroundJobItems_Job_Status ON dbo.BackgroundJobItems(BackgroundJobId, Status);
END;

COMMIT TRANSACTION;
