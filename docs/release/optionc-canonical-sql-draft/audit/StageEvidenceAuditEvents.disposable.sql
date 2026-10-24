/*
  Disposable additive candidate: canonical Stage evidence audit events.
  This is not migration 013 and must only run in OptionCIsolated_* SQL Server databases.
  It preserves existing ChangeRequestEvents rows and approval ledger semantics.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51300, N'Stage evidence audit candidate requires an OptionCIsolated_* database.', 1;
IF OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL
    THROW 51301, N'Canonical ChangeRequestEvents table is missing.', 1;

DECLARE @eventDefinition nvarchar(max);
SELECT @eventDefinition = cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.ChangeRequestEvents')
  AND cc.name = N'CK_B3_ChangeRequestEvents_EventType';

IF @eventDefinition IS NULL
   OR @eventDefinition NOT LIKE N'%EventType%'
   OR @eventDefinition NOT LIKE N'%Submitted%'
   OR @eventDefinition NOT LIKE N'%Rejected%'
   OR @eventDefinition NOT LIKE N'%Approved%'
   OR @eventDefinition NOT LIKE N'%Applied%'
   OR @eventDefinition LIKE N'%GeocodeEvidenceRecorded%'
   OR @eventDefinition LIKE N'%DuplicateReviewEvidenceRecorded%'
    THROW 51302, N'Canonical 012 EventType CHECK does not match the expected pre-extension contract.', 1;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ChangeRequestEvents')
      AND name = N'CK_B3_ChangeRequestEvents_StageEvidenceEventType')
    THROW 51303, N'Stage evidence audit candidate is already applied.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
        CHECK ([EventType] IN
            (N'Submitted',N'Rejected',N'Approved',N'Applied',
             N'GeocodeEvidenceRecorded',N'DuplicateReviewEvidenceRecorded'));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;

    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_StageEvidenceEventType
        CHECK
        (
            [ActorUserId] IS NOT NULL
            AND [CorrelationId] <> N'00000000-0000-0000-0000-000000000000'
            AND
            (
                ([EventType] IN(N'Submitted',N'Applied') AND [DecisionKey] IS NULL)
                OR
                ([EventType] IN(N'Rejected',N'Approved',
                                N'GeocodeEvidenceRecorded',
                                N'DuplicateReviewEvidenceRecorded')
                 AND [DecisionKey] IS NOT NULL
                 AND [DecisionKey] <> N'00000000-0000-0000-0000-000000000000')
            )
        );
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_StageEvidenceEventType;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
