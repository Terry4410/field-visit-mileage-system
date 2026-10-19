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
IF ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips',N'OBJECT',N'SELECT'),0)<>1 OR
ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripStops',N'OBJECT',N'SELECT'),0)<>1 OR
ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots',N'OBJECT',N'SELECT'),0)<>1 OR
ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshotStops',N'OBJECT',N'SELECT'),0)<>1
    THROW 51993,N'R3_ACCESS_STOP',1;
SELECT CAST(1 AS int) AS StagePass;
