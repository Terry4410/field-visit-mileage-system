/*
  SCHEME A DIRECT LOW-RISK CONTRACT — NONDEPLOY CANDIDATE.
  Disposable OptionCIsolated_* SQL Server 2022 ONLY.
  Requires 011 -> 012 -> 014 -> StageEvidenceAudit -> LeaderReviewDetails
  -> CreateLowRiskContract. This preserves the canonical request/version/event
  ledger while allowing an authorized note-only change to be applied without
  fabricating an independent review.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51700,N'OPTIONC_LOW_DIRECT_ISOLATION_REQUIRED',1;
IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL
   OR OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL
   OR OBJECT_ID(N'dbo.LocationPublishedVersions',N'U') IS NULL
   OR OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
    THROW 51701,N'OPTIONC_LOW_DIRECT_DEPENDENCIES_MISSING',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
              WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
    THROW 51702,N'OPTIONC_LOW_DIRECT_REQUIRES_CREATE_LOWRISK',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions
          WHERE VersionNumber=N'1.8.0-LOWRISK-DIRECT')
    THROW 51703,N'OPTIONC_LOW_DIRECT_ALREADY_APPLIED',1;

BEGIN TRY
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',
    @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
IF @lock<0 THROW 51704,N'OPTIONC_LOW_DIRECT_LOCK_DENIED',1;
IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-CREATE-LOWRISK'
    THROW 51705,N'OPTIONC_LOW_DIRECT_UNEXPECTED_ORDER',1;

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequests')
      AND name=N'CK_B3_ChangeRequests_ReviewState'
      AND is_disabled=0 AND is_not_trusted=0)
    THROW 51706,N'OPTIONC_LOW_DIRECT_REQUEST_CONSTRAINT_DRIFT',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents')
      AND name=N'CK_B3_ChangeRequestEvents_EventType'
      AND is_disabled=0 AND is_not_trusted=0)
    THROW 51707,N'OPTIONC_LOW_DIRECT_EVENT_CONSTRAINT_DRIFT',1;

ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_ReviewState;
ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK
((Status=N'Pending' AND ReviewedByUserId IS NULL AND ReviewedAt IS NULL
    AND ReviewReason IS NULL AND AppliedAt IS NULL)
 OR (Status=N'Rejected' AND ReviewedByUserId IS NOT NULL
    AND ReviewedByUserId<>RequestedByUserId AND ReviewedAt IS NOT NULL
    AND ReviewedAt>=SubmittedAt AND ReviewReason IS NOT NULL
    AND LEN(LTRIM(RTRIM(ReviewReason)))>0 AND AppliedAt IS NULL)
 OR (Status=N'Applied' AND
    ((ReviewedByUserId IS NOT NULL AND ReviewedByUserId<>RequestedByUserId
      AND ReviewedAt IS NOT NULL AND ReviewedAt>=SubmittedAt)
     OR
     (OperationCode=N'UpdatePublishedLocation' AND RiskCode=N'Low'
      AND ReviewedByUserId IS NULL AND ReviewedAt IS NULL
      AND ISJSON(EvidenceJson)=1
      AND JSON_VALUE(EvidenceJson,N'$.PublicationMode')=N'DirectLowRisk'))
    AND ReviewReason IS NOT NULL AND LEN(LTRIM(RTRIM(ReviewReason)))>0
    AND AppliedAt IS NOT NULL AND AppliedAt>=SubmittedAt));
ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState;

ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
 CHECK(EventType IN(N'Submitted',N'Rejected',N'Approved',N'Applied',
    N'GeocodeEvidenceRecorded',N'DuplicateReviewEvidenceRecorded',
    N'LeaderApproved',N'LeaderRejected',N'DirectLowRiskApplied'));
ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;

ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState
 CHECK(ActorUserId IS NOT NULL AND ActorUserId>0
  AND CorrelationId<>'00000000-0000-0000-0000-000000000000'
  AND ((EventType IN(N'Submitted',N'Applied') AND DecisionKey IS NULL)
    OR (EventType IN(N'Rejected',N'Approved',N'GeocodeEvidenceRecorded',
         N'DuplicateReviewEvidenceRecorded',N'LeaderApproved',N'LeaderRejected',
         N'DirectLowRiskApplied')
        AND DecisionKey IS NOT NULL
        AND DecisionKey<>'00000000-0000-0000-0000-000000000000')));
ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;

INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
VALUES(N'1.8.0-LOWRISK-DIRECT',
 N'Scheme A authorized direct LowRisk version and audit contract',
 SYSUTCDATETIME(),N'isolated-candidate');
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-018')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N'1.8.0-018',N'R5 canonical additive contract 1800_018',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-LOWRISK-DIRECT')),N'isolated-candidate');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
