/* R3 SELECT-only diagnostic. Rows remain in memory; no inventory export. */
DECLARE @ExpectedServer sysname=N'$(R3ExpectedServer)';
DECLARE @ExpectedDatabase sysname=N'$(R3ExpectedDatabase)';
IF CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2 <> @ExpectedServer COLLATE Latin1_General_100_BIN2
    THROW 51991,N'R3_TARGET_STOP',1;
IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @ExpectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 51990,N'R3_TARGET_STOP',1;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
IF (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions WHERE CONVERT(nvarchar(40),VersionNumber) COLLATE Latin1_General_100_BIN2=N'1.8.0-010')<>1
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
IF (SELECT TOP(1) CONVERT(nvarchar(40),VersionNumber) FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC) COLLATE Latin1_General_100_BIN2<>N'1.8.0-010'
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
;WITH aggregates AS (
SELECT COUNT_BIG(*) AS RowCount,MAX(VisitTripId) AS HighWatermark FROM dbo.VisitTrips
UNION ALL
SELECT COUNT_BIG(*) AS RowCount,MAX(VisitTripStopId) AS HighWatermark FROM dbo.VisitTripStops
UNION ALL
SELECT COUNT_BIG(*) AS RowCount,MAX(VisitTripSnapshotId) AS HighWatermark FROM dbo.VisitTripSnapshots
UNION ALL
SELECT COUNT_BIG(*) AS RowCount,MAX(VisitTripSnapshotStopId) AS HighWatermark FROM dbo.VisitTripSnapshotStops
)
SELECT CAST(CASE WHEN COUNT_BIG(*)=4 AND MIN(RowCount)>=0
 AND SUM(CASE WHEN RowCount>0 AND HighWatermark IS NULL THEN 1 ELSE 0 END)=0
 THEN 1 ELSE 0 END AS int) AS StagePass FROM aggregates;
