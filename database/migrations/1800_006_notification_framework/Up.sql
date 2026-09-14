SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'FieldVisit.SchemaMigration',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;

    IF @LockResult < 0
        THROW 54000, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-005')
        THROW 54001, N'尚未套用 prerequisite Migration 1.8.0-005。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-006')
        THROW 54002, N'Migration 1.8.0-006 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.NotificationEnvironmentPolicies', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.NotificationEmailAllowlist', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.NotificationSettings', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.NotificationSettingRecipients', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.MailOutbox', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.MailDeliveryLogs', N'U') IS NOT NULL
       OR COL_LENGTH(N'dbo.Employments', N'OptionalEmailNotificationEnabled') IS NOT NULL
       OR COL_LENGTH(N'dbo.Employments', N'EmailNotificationEnabled') IS NOT NULL
        THROW 54003, N'偵測到 1.8.0-006 部分物件或欄位已存在；請由 IT Review。', 1;

    ALTER TABLE dbo.Employments ADD
        OptionalEmailNotificationEnabled BIT NOT NULL
            CONSTRAINT DF_Employments_OptionalEmailNotificationEnabled DEFAULT(1) WITH VALUES;

    CREATE TABLE dbo.NotificationEnvironmentPolicies
    (
        EnvironmentCode NVARCHAR(20) NOT NULL CONSTRAINT PK_NotificationEnvironmentPolicies PRIMARY KEY,
        EmailMode NVARCHAR(20) NOT NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_NotificationEnvironmentPolicies_IsEnabled DEFAULT(0),
        SenderIdentityReference NVARCHAR(200) NULL,
        UpdatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_NotificationEnvironmentPolicies_UpdatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_NotificationEnvironmentPolicies_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_NotificationEnvironmentPolicies_Mode CHECK(EmailMode IN(N'Disabled', N'Test', N'Live')),
        CONSTRAINT CK_NotificationEnvironmentPolicies_UatSafety CHECK(EnvironmentCode <> N'UAT' OR EmailMode IN(N'Disabled', N'Test'))
    );

    INSERT dbo.NotificationEnvironmentPolicies
        (EnvironmentCode, EmailMode, IsEnabled, SenderIdentityReference, UpdatedAt)
    VALUES
        (N'UAT', N'Test', 0, NULL, SYSUTCDATETIME());

    CREATE TABLE dbo.NotificationEmailAllowlist
    (
        NotificationEmailAllowlistId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificationEmailAllowlist PRIMARY KEY,
        EnvironmentCode NVARCHAR(20) NOT NULL,
        Email NVARCHAR(320) NOT NULL,
        NormalizedEmail AS LOWER(LTRIM(RTRIM(Email))) PERSISTED,
        IsActive BIT NOT NULL CONSTRAINT DF_NotificationEmailAllowlist_IsActive DEFAULT(1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_NotificationEmailAllowlist_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_NotificationEmailAllowlist_Environment FOREIGN KEY(EnvironmentCode) REFERENCES dbo.NotificationEnvironmentPolicies(EnvironmentCode),
        CONSTRAINT FK_NotificationEmailAllowlist_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_NotificationEmailAllowlist_InactivatedByUser FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_NotificationEmailAllowlist_NotBlank CHECK(LEN(LTRIM(RTRIM(Email))) > 3)
    );
    CREATE UNIQUE INDEX UX_NotificationEmailAllowlist_Environment_Email
        ON dbo.NotificationEmailAllowlist(EnvironmentCode, NormalizedEmail);

    CREATE TABLE dbo.NotificationSettings
    (
        NotificationSettingId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificationSettings PRIMARY KEY,
        EventCode NVARCHAR(80) NOT NULL,
        NotificationType NVARCHAR(20) NOT NULL,
        IsEnabled BIT NOT NULL,
        HonorsOptionalPreference BIT NOT NULL,
        ReminderDays INT NULL,
        TemplateCode NVARCHAR(80) NOT NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_NotificationSettings_CreatedAt DEFAULT(SYSUTCDATETIME()),
        UpdatedAt DATETIME2(3) NULL,
        UpdatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_NotificationSettings_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_NotificationSettings_Type CHECK(NotificationType IN(N'Transaction', N'Reminder', N'System')),
        CONSTRAINT CK_NotificationSettings_OptionalPreference CHECK
        (
            (NotificationType = N'Reminder' AND HonorsOptionalPreference = 1)
            OR
            (NotificationType IN(N'Transaction', N'System') AND HonorsOptionalPreference = 0)
        ),
        CONSTRAINT CK_NotificationSettings_ReminderDays CHECK(ReminderDays IS NULL OR ReminderDays BETWEEN 0 AND 365),
        CONSTRAINT UQ_NotificationSettings_EventCode UNIQUE(EventCode)
    );

    INSERT dbo.NotificationSettings(EventCode, NotificationType, IsEnabled, HonorsOptionalPreference, ReminderDays, TemplateCode)
    VALUES
        (N'TripSubmitted', N'Transaction', 1, 0, NULL, N'TripSubmitted.v1'),
        (N'TripApproved', N'Transaction', 1, 0, NULL, N'TripApproved.v1'),
        (N'TripReturned', N'Transaction', 1, 0, NULL, N'TripReturned.v1'),
        (N'CorrectionRequested', N'Transaction', 1, 0, NULL, N'CorrectionRequested.v1'),
        (N'CorrectionApproved', N'Transaction', 1, 0, NULL, N'CorrectionApproved.v1'),
        (N'CorrectionReturned', N'Transaction', 1, 0, NULL, N'CorrectionReturned.v1'),
        (N'LocationReviewRequested', N'Transaction', 1, 0, NULL, N'LocationReviewRequested.v1'),
        (N'LocationApproved', N'Transaction', 1, 0, NULL, N'LocationApproved.v1'),
        (N'LocationReturned', N'Transaction', 1, 0, NULL, N'LocationReturned.v1'),
        (N'EmploymentAuthorizationExpiring', N'Reminder', 0, 1, 30, N'EmploymentAuthorizationExpiring.v1'),
        (N'DeploymentSiteChangeEffective', N'Reminder', 0, 1, 0, N'DeploymentSiteChangeEffective.v1'),
        (N'ProjectExpiring', N'Reminder', 0, 1, 30, N'ProjectExpiring.v1'),
        (N'ImportCompleted', N'System', 1, 0, NULL, N'ImportCompleted.v1'),
        (N'ImportFailed', N'System', 1, 0, NULL, N'ImportFailed.v1');

    CREATE TABLE dbo.NotificationSettingRecipients
    (
        NotificationSettingRecipientId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificationSettingRecipients PRIMARY KEY,
        NotificationSettingId INT NOT NULL,
        RecipientRuleCode NVARCHAR(50) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_NotificationSettingRecipients_IsActive DEFAULT(1),
        CONSTRAINT FK_NotificationSettingRecipients_Setting FOREIGN KEY(NotificationSettingId) REFERENCES dbo.NotificationSettings(NotificationSettingId),
        CONSTRAINT CK_NotificationSettingRecipients_Rule CHECK(RecipientRuleCode IN
            (N'TeamLeader', N'DelegatedLeader', N'TripOwner', N'Administrator', N'Initiator', N'AffectedEmployment', N'ProjectManager')),
        CONSTRAINT UQ_NotificationSettingRecipients_Rule UNIQUE(NotificationSettingId, RecipientRuleCode)
    );

    INSERT dbo.NotificationSettingRecipients(NotificationSettingId, RecipientRuleCode, IsActive)
    SELECT
        s.NotificationSettingId,
        v.RecipientRuleCode,
        CASE
            WHEN s.EventCode = N'ProjectExpiring' AND v.RecipientRuleCode = N'ProjectManager' THEN 0
            ELSE 1
        END
    FROM dbo.NotificationSettings s
    JOIN
    (
        VALUES
            (N'TripSubmitted', N'TeamLeader'),
            (N'TripSubmitted', N'DelegatedLeader'),
            (N'TripApproved', N'TripOwner'),
            (N'TripReturned', N'TripOwner'),
            (N'CorrectionRequested', N'TeamLeader'),
            (N'CorrectionRequested', N'DelegatedLeader'),
            (N'CorrectionApproved', N'TripOwner'),
            (N'CorrectionReturned', N'TripOwner'),
            (N'LocationReviewRequested', N'TeamLeader'),
            (N'LocationReviewRequested', N'Administrator'),
            (N'LocationApproved', N'Initiator'),
            (N'LocationReturned', N'Initiator'),
            (N'EmploymentAuthorizationExpiring', N'Administrator'),
            (N'DeploymentSiteChangeEffective', N'AffectedEmployment'),
            (N'ProjectExpiring', N'Administrator'),
            (N'ProjectExpiring', N'ProjectManager'),
            (N'ImportCompleted', N'Initiator'),
            (N'ImportFailed', N'Initiator')
    ) v(EventCode, RecipientRuleCode) ON v.EventCode = s.EventCode;

    CREATE TABLE dbo.MailOutbox
    (
        MailOutboxId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MailOutbox PRIMARY KEY,
        EnvironmentCode NVARCHAR(20) NOT NULL,
        EventCode NVARCHAR(80) NOT NULL,
        BusinessEventKey NVARCHAR(200) NOT NULL,
        EventOccurredAt DATETIME2(3) NOT NULL,
        AggregateType NVARCHAR(80) NOT NULL,
        AggregateId NVARCHAR(100) NOT NULL,
        RecipientKey NVARCHAR(200) NOT NULL,
        RecipientEmploymentId BIGINT NULL,
        RecipientUserId INT NULL,
        RecipientEmail NVARCHAR(320) NULL,
        NormalizedRecipientEmail AS LOWER(LTRIM(RTRIM(RecipientEmail))) PERSISTED,
        TemplateCode NVARCHAR(80) NOT NULL,
        TemplateDataJson NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_MailOutbox_Status DEFAULT(N'Pending'),
        AttemptCount INT NOT NULL CONSTRAINT DF_MailOutbox_AttemptCount DEFAULT(0),
        AvailableAt DATETIME2(3) NOT NULL,
        ProcessingToken UNIQUEIDENTIFIER NULL,
        ProcessingLeaseUntil DATETIME2(3) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_MailOutbox_CreatedAt DEFAULT(SYSUTCDATETIME()),
        SentAt DATETIME2(3) NULL,
        FinalizedAt DATETIME2(3) NULL,
        LastErrorCode NVARCHAR(100) NULL,
        LastErrorMessage NVARCHAR(2000) NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT FK_MailOutbox_Environment FOREIGN KEY(EnvironmentCode) REFERENCES dbo.NotificationEnvironmentPolicies(EnvironmentCode),
        CONSTRAINT FK_MailOutbox_Setting FOREIGN KEY(EventCode) REFERENCES dbo.NotificationSettings(EventCode),
        CONSTRAINT FK_MailOutbox_RecipientEmployment FOREIGN KEY(RecipientEmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_MailOutbox_RecipientUser FOREIGN KEY(RecipientUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_MailOutbox_Status CHECK(Status IN(N'Pending', N'Processing', N'Sent', N'Failed', N'Cancelled')),
        CONSTRAINT CK_MailOutbox_AttemptCount CHECK(AttemptCount >= 0),
        CONSTRAINT CK_MailOutbox_TemplateDataJson CHECK(ISJSON(TemplateDataJson) = 1),
        CONSTRAINT CK_MailOutbox_ProcessingOwnership CHECK
        (
            (Status = N'Processing' AND ProcessingToken IS NOT NULL AND ProcessingLeaseUntil IS NOT NULL)
            OR
            (Status <> N'Processing' AND ProcessingToken IS NULL AND ProcessingLeaseUntil IS NULL)
        ),
        CONSTRAINT CK_MailOutbox_Finalization CHECK
        (
            (Status IN(N'Sent', N'Failed', N'Cancelled') AND FinalizedAt IS NOT NULL)
            OR
            (Status IN(N'Pending', N'Processing') AND FinalizedAt IS NULL)
        )
    );
    CREATE UNIQUE INDEX UX_MailOutbox_BusinessEvent_RecipientKey
        ON dbo.MailOutbox(BusinessEventKey, RecipientKey);
    CREATE UNIQUE INDEX UX_MailOutbox_BusinessEvent_NormalizedRecipientEmail
        ON dbo.MailOutbox(BusinessEventKey, NormalizedRecipientEmail)
        WHERE RecipientEmail IS NOT NULL;
    CREATE INDEX IX_MailOutbox_Dispatch
        ON dbo.MailOutbox(Status, AvailableAt, MailOutboxId)
        INCLUDE(EventCode, AttemptCount, EnvironmentCode);
    CREATE INDEX IX_MailOutbox_Aggregate
        ON dbo.MailOutbox(AggregateType, AggregateId, CreatedAt DESC);
    CREATE INDEX IX_MailOutbox_Correlation
        ON dbo.MailOutbox(CorrelationId);

    CREATE TABLE dbo.MailDeliveryLogs
    (
        MailDeliveryLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MailDeliveryLogs PRIMARY KEY,
        MailOutboxId BIGINT NOT NULL,
        AttemptNumber INT NOT NULL,
        Provider NVARCHAR(80) NULL,
        Status NVARCHAR(20) NOT NULL,
        ProviderMessageId NVARCHAR(200) NULL,
        AttemptedAt DATETIME2(3) NOT NULL CONSTRAINT DF_MailDeliveryLogs_AttemptedAt DEFAULT(SYSUTCDATETIME()),
        ErrorCode NVARCHAR(100) NULL,
        ErrorMessage NVARCHAR(2000) NULL,
        CONSTRAINT FK_MailDeliveryLogs_Outbox FOREIGN KEY(MailOutboxId) REFERENCES dbo.MailOutbox(MailOutboxId),
        CONSTRAINT CK_MailDeliveryLogs_Status CHECK(Status IN(N'Sent', N'Failed')),
        CONSTRAINT CK_MailDeliveryLogs_Attempt CHECK(AttemptNumber > 0),
        CONSTRAINT UQ_MailDeliveryLogs_Attempt UNIQUE(MailOutboxId, AttemptNumber)
    );
    CREATE INDEX IX_MailDeliveryLogs_Status_Attempted
        ON dbo.MailDeliveryLogs(Status, AttemptedAt DESC);

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-006',
        N'Notification settings with mandatory transaction semantics, optional reminder preference, UAT Test-mode allowlist, transactional outbox and delivery audit log',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-006 notification framework completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
