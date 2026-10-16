/*
  1800_012 B3 atomic-apply candidate -- FORWARD ONLY.
  It only makes the schema capable of immutable Approved/Applied audit pairs.
  It does not enable PackageB:B3, expose ApproveAsync, grant SQL permissions,
  or authorize any execution.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
    IF @LockResult<0 THROW 51210,N'Unable to acquire FieldVisit migration lock.',1;
    IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-011'
        THROW 51211,N'Latest SchemaVersion must be exactly 1.8.0-011.',1;
    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-012')
        THROW 51212,N'1800_012 already exists; no rerun allowed.',1;
    IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents')))
        THROW 51213,N'Enabled B3 table trigger is prohibited.',1;

    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownStatus;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownStatus
        CHECK ([Status] IN(N'Pending',N'Rejected',N'Applied'));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_KnownStatus;
    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_ReviewState;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK
    (([Status]=N'Pending' AND [ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL)
     OR ([Status]=N'Rejected' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId]
         AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt] AND [ReviewReason] IS NOT NULL
         AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NULL)
     OR ([Status]=N'Applied' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId]
         AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt] AND [ReviewReason] IS NOT NULL
         AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NOT NULL AND [AppliedAt]>=[ReviewedAt]));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState;

    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
        CHECK ([EventType] IN(N'Submitted',N'Rejected',N'Approved',N'Applied'));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState CHECK
    ([ActorUserId] IS NOT NULL AND [ActorUserId]>0 AND [CorrelationId]<>'00000000-0000-0000-0000-000000000000'
     AND (([EventType] IN(N'Submitted',N'Applied') AND [DecisionKey] IS NULL)
       OR ([EventType] IN(N'Rejected',N'Approved') AND [DecisionKey] IS NOT NULL
           AND [DecisionKey]<>'00000000-0000-0000-0000-000000000000')));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N'1.8.0-012',N'Package B candidate: B3 atomic approval/apply audit states',SYSUTCDATETIME(),N'Package B candidate; execution requires Owner approval');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
