SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-006')
    THROW 54100, N'Verify failed: 找不到 SchemaVersion 1.8.0-006。', 1;

IF OBJECT_ID(N'dbo.NotificationEnvironmentPolicies', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NotificationEmailAllowlist', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NotificationSettings', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NotificationSettingRecipients', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MailOutbox', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MailDeliveryLogs', N'U') IS NULL
   OR COL_LENGTH(N'dbo.Employments', N'OptionalEmailNotificationEnabled') IS NULL
   OR COL_LENGTH(N'dbo.NotificationSettings', N'HonorsOptionalPreference') IS NULL
    THROW 54101, N'Verify failed: notification framework schema 不完整。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NotificationEmailAllowlist') AND name=N'UX_NotificationEmailAllowlist_Environment_Email')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MailOutbox') AND name=N'IX_MailOutbox_Dispatch')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MailDeliveryLogs') AND name=N'UQ_MailDeliveryLogs_Attempt')
    THROW 54108, N'Verify failed: 1.8.0-006 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.NotificationEnvironmentPolicies'), OBJECT_ID(N'dbo.NotificationEmailAllowlist'),
        OBJECT_ID(N'dbo.NotificationSettings'), OBJECT_ID(N'dbo.NotificationSettingRecipients'),
        OBJECT_ID(N'dbo.MailOutbox'), OBJECT_ID(N'dbo.MailDeliveryLogs')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.NotificationEnvironmentPolicies'), OBJECT_ID(N'dbo.NotificationEmailAllowlist'),
        OBJECT_ID(N'dbo.NotificationSettings'), OBJECT_ID(N'dbo.NotificationSettingRecipients'),
        OBJECT_ID(N'dbo.MailOutbox'), OBJECT_ID(N'dbo.MailDeliveryLogs')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 54109, N'Verify failed: 1.8.0-006 FK/CHECK constraint 未啟用或不受信任。', 1;

IF NOT EXISTS
(
    SELECT 1 FROM dbo.NotificationEnvironmentPolicies
    WHERE EnvironmentCode = N'UAT' AND EmailMode = N'Test' AND IsEnabled = 0
)
    THROW 54102, N'Verify failed: UAT 必須以 Test mode 且預設停用。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.NotificationEnvironmentPolicies
    WHERE EnvironmentCode = N'UAT' AND EmailMode = N'Live'
)
    THROW 54103, N'Verify failed: UAT 不得使用 Live Email mode。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.NotificationEmailAllowlist
    WHERE EnvironmentCode = N'UAT' AND IsActive = 1
)
    THROW 54110, N'Verify failed: UAT initial Email allowlist 必須為空。', 1;

IF (SELECT COUNT(*) FROM dbo.NotificationSettings) <> 14
    THROW 54104, N'Verify failed: notification event seed 數量不正確。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.NotificationSettings
    WHERE (NotificationType = N'Transaction' AND HonorsOptionalPreference <> 0)
       OR (NotificationType = N'Reminder' AND HonorsOptionalPreference <> 1)
       OR (NotificationType = N'System' AND HonorsOptionalPreference <> 0)
)
    THROW 54111, N'Verify failed: Transaction/System 通知不得受個人 Optional preference 關閉；Reminder 必須套用該 preference。', 1;

IF COL_LENGTH(N'dbo.Employments', N'EmailNotificationEnabled') IS NOT NULL
    THROW 54112, N'Verify failed: 不得使用語意模糊的 EmailNotificationEnabled 欄位。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.NotificationSettings s
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.NotificationSettingRecipients r
        WHERE r.NotificationSettingId = s.NotificationSettingId AND r.IsActive = 1
    )
)
    THROW 54105, N'Verify failed: notification event 缺少收件規則。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.NotificationSettingRecipients'),
        OBJECT_ID(N'dbo.MailOutbox'),
        OBJECT_ID(N'dbo.MailDeliveryLogs')
    )
      AND delete_referential_action_desc <> N'NO_ACTION'
)
    THROW 54106, N'Verify failed: Notification/Audit FK 不得 Cascade Delete。', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
   < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
    THROW 54107, N'Verify failed: v1.7.2 Snapshot 筆數少於 migration baseline。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-006' AS MigrationVersion,
    p.EnvironmentCode,
    p.EmailMode,
    p.IsEnabled AS EnvironmentEnabled,
    (SELECT COUNT(*) FROM dbo.NotificationEmailAllowlist a WHERE a.EnvironmentCode=p.EnvironmentCode AND a.IsActive=1) AS ActiveAllowlistCount,
    (SELECT COUNT(*) FROM dbo.MailOutbox) AS OutboxCount,
    (SELECT COUNT(*) FROM dbo.MailDeliveryLogs) AS DeliveryLogCount
FROM dbo.NotificationEnvironmentPolicies p
WHERE p.EnvironmentCode = N'UAT';

SELECT
    s.EventCode,
    s.NotificationType,
    s.IsEnabled,
    s.HonorsOptionalPreference,
    s.ReminderDays,
    STRING_AGG(r.RecipientRuleCode, N',') WITHIN GROUP(ORDER BY r.RecipientRuleCode) AS RecipientRules
FROM dbo.NotificationSettings s
JOIN dbo.NotificationSettingRecipients r ON r.NotificationSettingId = s.NotificationSettingId AND r.IsActive = 1
GROUP BY s.EventCode, s.NotificationType, s.IsEnabled, s.HonorsOptionalPreference, s.ReminderDays
ORDER BY s.EventCode;
