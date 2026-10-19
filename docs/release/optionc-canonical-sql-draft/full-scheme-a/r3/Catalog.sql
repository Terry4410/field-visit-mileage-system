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
IF COL_LENGTH(N'dbo.VisitTrips',N'VisitTripId') IS NULL
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
IF COL_LENGTH(N'dbo.VisitTripStops',N'VisitTripStopId') IS NULL
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
IF COL_LENGTH(N'dbo.VisitTripSnapshots',N'VisitTripSnapshotId') IS NULL
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
IF COL_LENGTH(N'dbo.VisitTripSnapshotStops',N'VisitTripSnapshotStopId') IS NULL
    THROW 51992,N'R3_PREREQUISITE_STOP',1;
SELECT CAST(CASE WHEN
 (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped=0)>0
 AND (SELECT COUNT_BIG(*) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id)>0
 AND (SELECT COUNT_BIG(*) FROM sys.default_constraints)>=0
 AND (SELECT COUNT_BIG(*) FROM sys.indexes)>=0
 AND (SELECT COUNT_BIG(*) FROM sys.foreign_keys)>=0
 AND (SELECT COUNT_BIG(*) FROM sys.foreign_key_columns)>=0
 AND (SELECT COUNT_BIG(*) FROM sys.check_constraints)>=0
 THEN 1 ELSE 0 END AS int) AS StagePass;
