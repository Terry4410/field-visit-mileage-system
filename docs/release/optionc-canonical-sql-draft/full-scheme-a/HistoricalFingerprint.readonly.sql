/*
  FULL SCHEME A PRE/POST HISTORICAL FINGERPRINT — REVIEW SOURCE ONLY.
  SELECT-only. This file is not wired to a UAT workflow and is not migration
  authorization. Capture immediately before the first approved forward step
  and after every Verify step; any difference is a STOP/UNKNOWN outcome.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IN (N'master',N'model',N'msdb',N'tempdb')
    THROW 55900,N'STOP: historical fingerprint target is invalid.',1;

IF OBJECT_ID(N'dbo.VisitTripStops',N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots',N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshotStops',N'U') IS NULL
    THROW 55901,N'STOP: historical Trip/Snapshot catalog is incomplete.',1;

/* Only counts and hashes leave SQL. Business values are never emitted. */
SELECT N'VisitTripStops' AS EvidenceName,
       COUNT_BIG(*) AS RowCount,
       CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE(STRING_AGG(
           CONVERT(nvarchar(max),HASHBYTES('SHA2_256',CONCAT(
             VisitTripStopId,N'|',VisitTripId,N'|',StopSequence,N'|',
             COALESCE(CONVERT(nvarchar(30),LocationId),N'<NULL>'),N'|',
             COALESCE(LocationNameSnapshot,N'<NULL>'),N'|',
             COALESCE(AddressSnapshot,N'<NULL>'))),2),N'')
           WITHIN GROUP (ORDER BY VisitTripStopId),N'')),2) AS FingerprintSha256
FROM dbo.VisitTripStops;

SELECT N'VisitTripSnapshots' AS EvidenceName,
       COUNT_BIG(*) AS RowCount,
       CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE(STRING_AGG(
           CONVERT(nvarchar(max),HASHBYTES('SHA2_256',CONCAT(
             VisitTripSnapshotId,N'|',VisitTripId,N'|',SnapshotVersion,N'|',
             SnapshotType,N'|',COALESCE(CONVERT(nvarchar(40),ApprovedDistanceKmSnapshot),N'<NULL>'),N'|',
             COALESCE(CONVERT(nvarchar(40),SubsidyAmountSnapshot),N'<NULL>'),N'|',
             COALESCE(StatusSnapshot,N'<NULL>'))),2),N'')
           WITHIN GROUP (ORDER BY VisitTripSnapshotId),N'')),2) AS FingerprintSha256
FROM dbo.VisitTripSnapshots;

SELECT N'VisitTripSnapshotStops' AS EvidenceName,
       COUNT_BIG(*) AS RowCount,
       CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE(STRING_AGG(
           CONVERT(nvarchar(max),HASHBYTES('SHA2_256',CONCAT(
             VisitTripSnapshotStopId,N'|',VisitTripSnapshotId,N'|',StopSequence,N'|',
             COALESCE(CONVERT(nvarchar(30),LocationId),N'<NULL>'),N'|',
             COALESCE(LocationNameSnapshot,N'<NULL>'),N'|',
             COALESCE(AddressSnapshot,N'<NULL>'))),2),N'')
           WITHIN GROUP (ORDER BY VisitTripSnapshotStopId),N'')),2) AS FingerprintSha256
FROM dbo.VisitTripSnapshotStops;

SELECT N'HISTORICAL_FINGERPRINT_COMPLETE_READ_ONLY' AS Result,
       DB_NAME() AS DatabaseName,
       SYSUTCDATETIME() AS CompletedUtc;
