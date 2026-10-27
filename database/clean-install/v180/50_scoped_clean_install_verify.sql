-- READ-ONLY scoped clean install verification for an EMPTY SQL Server 2022 target.
-- Not full 011-020 schema fidelity, production security grants or recovery evidence.
SET NOCOUNT ON;
IF CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))<>16 THROW 56500,N'SQL2022_REQUIRED',1;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL THROW 56501,N'LEDGER_MISSING',1;
IF (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions)<>1 OR
   NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
     WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK' AND AppliedBy=N'clean-install-v180')
  THROW 56502,N'REPLAY_LEDGER_MISMATCH',1;
IF (SELECT COUNT_BIG(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo'))<>69
  THROW 56503,N'REPLAY_TABLE_COUNT',1;
IF (SELECT COUNT_BIG(*) FROM sys.triggers WHERE is_ms_shipped=0 AND parent_class=1)<>21
  THROW 56504,N'REPLAY_TRIGGER_COUNT',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_ms_shipped=0 AND parent_class=1 AND is_disabled=1)
  THROW 56505,N'REPLAY_TRIGGER_DISABLED',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1)
  THROW 56506,N'REPLAY_FK_TRUST',1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1)
  THROW 56507,N'REPLAY_CHECK_TRUST',1;
IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL OR
   (SELECT COUNT_BIG(*) FROM sys.foreign_keys
     WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))<>4
  THROW 56508,N'REPLAY_017_FOREIGN_KEYS',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
      AND name=N'CK_OptionC_CreateReservation_Status' AND is_disabled=0 AND is_not_trusted=0)
  THROW 56509,N'REPLAY_017_STATUS_CHECK',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
      AND name=N'UX_OptionC_CreateReservation_Location'
      AND is_unique=1 AND is_disabled=0 AND has_filter=0)
  THROW 56510,N'REPLAY_017_UNIQUE_INDEX',1;
PRINT N'REPLAY_SCOPED_SQL2022_VERIFY_PASS';
