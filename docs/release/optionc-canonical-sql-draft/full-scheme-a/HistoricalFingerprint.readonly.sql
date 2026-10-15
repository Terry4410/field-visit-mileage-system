/*
  FULL SCHEME A PRE/POST HISTORICAL FINGERPRINT — REVIEW SOURCE ONLY.
  SELECT/THROW only. Not connected to the protected UAT workflow.

  Run the immutable 1.8.0-001 verifier first. Then capture this script with
  all four MAX_* values set to 9223372036854775807. For post-comparison, pass
  the four ObservedHighWatermark values from that capture. New rows above the
  captured boundary are allowed; any change at/below it is STOP/UNKNOWN.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @expectedServer nvarchar(255)=N'$(EXPECTED_SERVER)';
DECLARE @expectedDatabase sysname=N'$(EXPECTED_DATABASE)';
DECLARE @maxTrip bigint=TRY_CONVERT(bigint,N'$(MAX_VISIT_TRIP_ID)');
DECLARE @maxStop bigint=TRY_CONVERT(bigint,N'$(MAX_VISIT_TRIP_STOP_ID)');
DECLARE @maxSnapshot bigint=TRY_CONVERT(bigint,N'$(MAX_VISIT_TRIP_SNAPSHOT_ID)');
DECLARE @maxSnapshotStop bigint=TRY_CONVERT(bigint,N'$(MAX_VISIT_TRIP_SNAPSHOT_STOP_ID)');
DECLARE @actualServer nvarchar(255)=CONVERT(nvarchar(255),SERVERPROPERTY('ServerName'));
DECLARE @expectedLogical nvarchar(255)=CASE
  WHEN LOWER(@expectedServer) LIKE N'%.database.windows.net'
  THEN LEFT(@expectedServer,LEN(@expectedServer)-LEN(N'.database.windows.net'))
  ELSE @expectedServer END;
DECLARE @isolated bit=CASE WHEN LOWER(@expectedServer) IN(N'localhost',N'127.0.0.1',N'::1')
   AND @expectedDatabase LIKE N'OptionCIsolated[_]%' THEN 1 ELSE 0 END;

IF NULLIF(LTRIM(RTRIM(@expectedServer)),N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(@expectedDatabase)),N'') IS NULL
   OR @maxTrip IS NULL OR @maxStop IS NULL OR @maxSnapshot IS NULL
   OR @maxSnapshotStop IS NULL OR @maxTrip<0 OR @maxStop<0
   OR @maxSnapshot<0 OR @maxSnapshotStop<0
    THROW 55900,N'STOP: historical fingerprint inputs are missing or invalid.',1;
IF DB_NAME()<>@expectedDatabase
    THROW 55901,N'STOP: historical fingerprint database allow-list mismatch.',1;
IF @isolated=0 AND (@actualServer IS NULL OR
   (LOWER(@actualServer)<>LOWER(@expectedServer)
    AND LOWER(@actualServer)<>LOWER(@expectedLogical)))
    THROW 55902,N'STOP: historical fingerprint server allow-list mismatch.',1;
IF DB_NAME() IN(N'master',N'model',N'msdb',N'tempdb')
    THROW 55903,N'STOP: historical fingerprint system target denied.',1;
IF OBJECT_ID(N'dbo.SchemaMigrationDataBaselines',N'U') IS NULL
   OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-001')
   OR NOT EXISTS(SELECT 1 FROM dbo.SchemaMigrationDataBaselines
                 WHERE MigrationVersion=N'1.8.0-001')
    THROW 55904,N'STOP: immutable 1.8.0-001 baseline prerequisite missing.',1;
IF OBJECT_ID(N'dbo.VisitTrips',N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripStops',N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots',N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshotStops',N'U') IS NULL
    THROW 55905,N'STOP: historical Trip/Snapshot catalog is incomplete.',1;

/* FOR JSON PATH + INCLUDE_NULL_VALUES is unambiguous field serialization:
   field boundaries, escaping, NULL and delimiter-bearing strings cannot
   collide. Only counts/high-watermarks/hashes leave SQL. */
SELECT N'VisitTrips' AS EvidenceName,@maxTrip AS RequestedHighWatermark,
 COALESCE(MAX(VisitTripId),CONVERT(bigint,0)) AS ObservedHighWatermark,
 COUNT_BIG(*) AS [RowCount],
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),COALESCE((
  SELECT VisitTripId,TripNo,UserId,OrganizationId,TeamId,Status,VehicleType,
         Purpose,Notes,ReturnReason FROM dbo.VisitTrips WHERE VisitTripId<=@maxTrip
  ORDER BY VisitTripId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'))),2) AS FingerprintSha256
FROM dbo.VisitTrips WHERE VisitTripId<=@maxTrip;

SELECT N'VisitTripStops' AS EvidenceName,@maxStop AS RequestedHighWatermark,
 COALESCE(MAX(VisitTripStopId),CONVERT(bigint,0)) AS ObservedHighWatermark,
 COUNT_BIG(*) AS [RowCount],
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),COALESCE((
  SELECT VisitTripStopId,VisitTripId,StopSequence,LocationId,LocationNameSnapshot,
         AddressSnapshot FROM dbo.VisitTripStops WHERE VisitTripStopId<=@maxStop
  ORDER BY VisitTripStopId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'))),2) AS FingerprintSha256
FROM dbo.VisitTripStops WHERE VisitTripStopId<=@maxStop;

SELECT N'VisitTripSnapshots' AS EvidenceName,@maxSnapshot AS RequestedHighWatermark,
 COALESCE(MAX(VisitTripSnapshotId),CONVERT(bigint,0)) AS ObservedHighWatermark,
 COUNT_BIG(*) AS [RowCount],
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),COALESCE((
  SELECT VisitTripSnapshotId,VisitTripId,SnapshotVersion,SnapshotType,
         ApprovedDistanceKmSnapshot,SubsidyAmountSnapshot,StatusSnapshot
  FROM dbo.VisitTripSnapshots WHERE VisitTripSnapshotId<=@maxSnapshot
  ORDER BY VisitTripSnapshotId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'))),2) AS FingerprintSha256
FROM dbo.VisitTripSnapshots WHERE VisitTripSnapshotId<=@maxSnapshot;

SELECT N'VisitTripSnapshotStops' AS EvidenceName,@maxSnapshotStop AS RequestedHighWatermark,
 COALESCE(MAX(VisitTripSnapshotStopId),CONVERT(bigint,0)) AS ObservedHighWatermark,
 COUNT_BIG(*) AS [RowCount],
 CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),COALESCE((
  SELECT VisitTripSnapshotStopId,VisitTripSnapshotId,StopSequence,LocationId,
         LocationNameSnapshot,AddressSnapshot FROM dbo.VisitTripSnapshotStops
  WHERE VisitTripSnapshotStopId<=@maxSnapshotStop
  ORDER BY VisitTripSnapshotStopId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]'))),2) AS FingerprintSha256
FROM dbo.VisitTripSnapshotStops WHERE VisitTripSnapshotStopId<=@maxSnapshotStop;

SELECT N'HISTORICAL_FINGERPRINT_COMPLETE_READ_ONLY' AS Result,
 DB_NAME() AS DatabaseName,SYSUTCDATETIME() AS CompletedUtc;
