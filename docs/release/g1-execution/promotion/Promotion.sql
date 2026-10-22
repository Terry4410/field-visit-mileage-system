-- NONDEPLOY REVIEW CANDIDATE. No execution or registry assignment authorized.
SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N'EngineEdition'))=3
 AND CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N'Edition')) LIKE N'Developer Edition%'
 AND DB_NAME() LIKE N'OptionCIsolated[_]%' AND LEN(DB_NAME())=LEN(N'OptionCIsolated_')+32
 AND SUBSTRING(DB_NAME(),LEN(N'OptionCIsolated_')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%'
 AND TRY_CONVERT(int,SESSION_CONTEXT(N'FieldVisit.G1.NonDeploy'))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),N'') COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat')
 THROW 52001,N'G1_TARGET_STOP',1;
IF @G1Offline=0 AND (TRY_CONVERT(binary(32),SESSION_CONTEXT(N'FieldVisit.G1.OwnerApprovedContract')) IS NULL OR TRY_CONVERT(binary(32),SESSION_CONTEXT(N'FieldVisit.G1.OwnerApprovedContract'))<>0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d) THROW 52004,N'G1_AUTHORIZATION_STOP',1;
IF @@TRANCOUNT<>0 THROW 52002,N'G1_AMBIENT_TRANSACTION_STOP',1;
SET XACT_ABORT ON; SET LOCK_TIMEOUT 0; SET DEADLOCK_PRIORITY LOW;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1 THROW 52005,N'G1_PERMISSION_STOP',1;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.Organizations',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.Teams',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.Users',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.Locations',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.VisitTrips',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.VisitTripStops',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.VisitTripSnapshots',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.VisitTripSnapshotStops',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.ImportBatches',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NULL THROW 52010,N'G1_PREREQUISITE_STOP',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE'),0)<>1 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER'),0)<>1 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'REFERENCES'),0)<>1 THROW 52005,N'G1_PERMISSION_STOP',1;
BEGIN TRY BEGIN TRANSACTION;
DECLARE @G1Lock int,@G1Hash varchar(64),@G1History varchar(64),@G1After varchar(64),@G1Rows bigint;
EXEC @G1Lock=sys.sp_getapplock @Resource=N'FieldVisit.SchemaMigration',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
IF @G1Lock IS NULL OR @G1Lock<0 THROW 52003,N'G1_LOCK_STOP',1;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.SchemaVersions WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.Organizations WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.Teams WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.Users WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.Locations WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.VisitTrips WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.VisitTripStops WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.VisitTripSnapshots WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.ImportBatches WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
EXEC sys.sp_executesql N'SELECT @rows=COUNT_BIG(*) FROM dbo.ImportBatchItems WITH(TABLOCKX,HOLDLOCK);',N'@rows bigint OUTPUT',@rows=@G1Rows OUTPUT;
IF OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U') IS NOT NULL BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-EXCEL-CANONICAL-STATUS''
 THROW 52014,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS''))
 THROW 52014,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS''))<>13 THROW 52014,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'cfaaae20bde78da8ea1327fef077abeca48b7d4579303bf2cb7b62ddda22bd66' THROW 52014,N'G1_CATALOG_STOP',1;
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.FieldVisitMigrationLedger)<>9 OR EXISTS(SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256 FROM (VALUES(N''1.8.0-011'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xb55ed30e002541a17bc0600667d1d1629b6544d3279b6fd5763a2a45109253ca,0x3caffee23251e45a1e1e3e90a1003256cf0ad0be29ab8eeb23bbd5076c4e6405),(N''1.8.0-012'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x50812ed02e784d7140cfa7d1fe04b92d4fdfb4ea013c04272ef1f39f4d8d3ac4,0xd46be3bb81f8b4645c5732a68b20bd58f6b16125eeba6c0fce1051ebb6dd135a),(N''1.8.0-014'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x017ed453fc8c7d87052974d0eb327ac4a11a785f0a6c2c1a54e38efd5cee16f5,0x89672cbd083bdca08df15f38d9eb7ad8f67ca5319cfe94e97a4e60ee22ae34fe),(N''1.8.0-015'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xae07dd70550c00eaac15c3664d15d557739ef14d681cafb9a472fab256188f7d,0x40825212db9403724893125cdeca4dbe7071d2d6cb25de623093f0bd67fb8879),(N''1.8.0-016'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xf7be77add8fcab7f0b4c7c798817f2d399ad1dac4ccf87f335ed69d25de05f94,0x538e93637633603de40410f41ba8dd957c10d01571e7280aa564cbec5dbda6f7),(N''1.8.0-017'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xa90f97ff90dd634e2c57a53db5aec06ee1754d4e7da4beabaf27f4a5a3cc6c89,0xdcff6fcb1095204e8a2b1e7bebb9fc8b5a7f3950794b2be71e6b54dfb90b6205),(N''1.8.0-018'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x0787be84c88f841831fdea94a32e678cc204c24218729540d8e36895dcf700c7,0xd619301f85b9061fdaf16bd3da22c1fe37e71088d53eed0abde0ffe679662033),(N''1.8.0-019'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xe30b1dfa3563fb706e1d1358fb5b42807b2c86ac6494902dc7e2ecb1b492e46b,0xe168885f8f9b9482791b20a2cf301e771380c514c0334d13b904c78e403c558e),(N''1.8.0-020'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x992eb565487f8c24535fd89b3dbdbb28ad292b62120e42287538f940c31c64a7,0xcfaaae20bde78da8ea1327fef077abeca48b7d4579303bf2cb7b62ddda22bd66)) v(VersionNumber,PackageId,PayloadSha256,CatalogSha256) EXCEPT SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256 FROM dbo.FieldVisitMigrationLedger) THROW 52014,N''G1_CHECKSUM_STOP'',1;';
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-011'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-012'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-014'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-015'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-016'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-017'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-018'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-019'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-020'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
THROW 52020,N'G1_ALREADY_APPLIED_VERIFIED_NO_REPLAY',1; END;
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-010''
 THROW 52011,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010''))
 THROW 52011,N''G1_MARKER_SET_STOP'',1;
';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'b53e33323668f16ba7d3c05becea2e64a42345afdbb9a49b0a91fcbd59d0b20a' THROW 52013,N'G1_PARTIAL_OBJECT_STATE_STOP',1;
EXEC sys.sp_executesql N'-- History data stay in SQL memory; return digest only to controlled callers.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT * FROM dbo.VisitTrips ORDER BY VisitTripId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT * FROM dbo.VisitTripStops ORDER BY VisitTripStopId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT * FROM dbo.VisitTripSnapshots ORDER BY VisitTripSnapshotId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT * FROM dbo.VisitTripSnapshotStops ORDER BY VisitTripSnapshotStopId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1History OUTPUT;
EXEC sys.sp_executesql N'CREATE TABLE dbo.FieldVisitMigrationLedger
(
 VersionNumber nvarchar(40) NOT NULL CONSTRAINT PK_FieldVisitMigrationLedger PRIMARY KEY,
 PackageId binary(32) NOT NULL,
 PayloadSha256 binary(32) NOT NULL,
 CatalogSha256 binary(32) NOT NULL,
 MarkersSha256 binary(32) NOT NULL,
 AppliedAt datetime2(3) NOT NULL,
 CONSTRAINT CK_FieldVisitMigrationLedger_Version CHECK(VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-017'',N''1.8.0-018'',N''1.8.0-019'',N''1.8.0-020''))
);
';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF LOWER(@G1Hash)<>N'573b6f21cefaa6eaace213f950d469ebe9c080fb073261522b6a3e7adbf78380' THROW 52013,N'G1_LEDGER_CONTRACT_STOP',1;
-- G1_STEP_011_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-010''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
/*
  1800_011 B3 schema candidate -- FORWARD ONLY.
  Execution is prohibited until this exact immutable file, Preflight.sql and
  Verify.sql are independently reviewed and explicitly approved for one UAT DB.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock
        @Resource=N''FieldVisit.SchemaMigration'', @LockMode=N''Exclusive'',
        @LockOwner=N''Transaction'', @LockTimeout=0;
    IF @LockResult < 0 THROW 51110, N''Unable to acquire FieldVisit migration lock.'', 1;

    IF DB_NAME() IN (N''master'', N''model'', N''msdb'', N''tempdb'')
        THROW 51111, N''1800_011 target database is invalid.'', 1;
    IF OBJECT_ID(N''dbo.SchemaVersions'',N''U'') IS NULL
       OR OBJECT_ID(N''dbo.Organizations'',N''U'') IS NULL
       OR OBJECT_ID(N''dbo.Teams'',N''U'') IS NULL
       OR OBJECT_ID(N''dbo.Users'',N''U'') IS NULL
       OR OBJECT_ID(N''dbo.Locations'',N''U'') IS NULL
        THROW 51112, N''1800_011 prerequisite catalog is incomplete.'', 1;
    IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-011'')
        THROW 51113, N''1800_011 already exists; no rerun allowed.'', 1;
    IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions
        ORDER BY AppliedAt DESC,VersionNumber DESC)<>N''1.8.0-010''
        THROW 51114, N''Latest SchemaVersion must be exactly 1.8.0-010.'', 1;
    IF OBJECT_ID(N''dbo.ChangeRequests'',N''U'') IS NOT NULL
       OR OBJECT_ID(N''dbo.ChangeRequestEvents'',N''U'') IS NOT NULL
        THROW 51115, N''B3 tables already exist; stop for DBA drift review.'', 1;

    CREATE TABLE dbo.ChangeRequests
    (
        ChangeRequestId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_B3_ChangeRequests PRIMARY KEY,
        RequestPublicId uniqueidentifier NOT NULL,
        OrganizationId int NOT NULL,
        TeamId int NULL,
        EntityKind nvarchar(40) NOT NULL,
        EntityId nvarchar(80) NOT NULL,
        OperationCode nvarchar(80) NOT NULL,
        RiskCode nvarchar(20) NOT NULL,
        ExpectedEntityRowVersion varbinary(8) NULL,
        BeforeJson nvarchar(max) NULL,
        ProposedJson nvarchar(max) NOT NULL,
        EvidenceJson nvarchar(max) NULL,
        RequestedByUserId int NOT NULL,
        SubmittedAt datetime2(3) NOT NULL,
        Status nvarchar(30) NOT NULL,
        ReviewedByUserId int NULL,
        ReviewedAt datetime2(3) NULL,
        ReviewReason nvarchar(1000) NULL,
        AppliedAt datetime2(3) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_B3_Requests_Org FOREIGN KEY(OrganizationId)
            REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Requests_Team FOREIGN KEY(TeamId)
            REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Requests_Requester FOREIGN KEY(RequestedByUserId)
            REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Requests_Reviewer FOREIGN KEY(ReviewedByUserId)
            REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
        CONSTRAINT CK_B3_ChangeRequests_KnownCodes CHECK
            ([EntityKind]=N''Location'' AND [OperationCode]=N''UpdatePublishedLocation'' AND [RiskCode]=N''High''),
        CONSTRAINT CK_B3_ChangeRequests_KnownStatus CHECK
            ([Status] IN (N''Pending'',N''Rejected'')),
        CONSTRAINT CK_B3_ChangeRequests_ExpectedLocationVersion CHECK
            ([ExpectedEntityRowVersion] IS NOT NULL AND DATALENGTH([ExpectedEntityRowVersion])=8),
        CONSTRAINT CK_B3_ChangeRequests_ProposedJson CHECK (ISJSON([ProposedJson])=1),
        CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK
            (([Status]=N''Pending'' AND [ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL)
             OR ([Status]=N''Rejected'' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId]
                 AND [ReviewedAt] IS NOT NULL AND [ReviewReason] IS NOT NULL
                 AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NULL))
    );
    CREATE UNIQUE INDEX UX_B3_ChangeRequests_RequestPublicId
        ON dbo.ChangeRequests(RequestPublicId);
    CREATE UNIQUE INDEX UX_B3_ChangeRequests_Org_Entity_Pending
        ON dbo.ChangeRequests(OrganizationId,EntityKind,EntityId) WHERE [Status]=N''Pending'';
    CREATE INDEX IX_B3_ChangeRequests_Org_Status_SubmittedAt
        ON dbo.ChangeRequests(OrganizationId,Status,SubmittedAt);
    CREATE INDEX IX_B3_ChangeRequests_Requester_SubmittedAt
        ON dbo.ChangeRequests(RequestedByUserId,SubmittedAt);

    CREATE TABLE dbo.ChangeRequestEvents
    (
        ChangeRequestEventId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_B3_ChangeRequestEvents PRIMARY KEY,
        ChangeRequestId bigint NOT NULL,
        EventType nvarchar(40) NOT NULL,
        ActorUserId int NULL,
        OccurredAt datetime2(3) NOT NULL,
        CorrelationId uniqueidentifier NOT NULL,
        DecisionKey uniqueidentifier NULL,
        DetailsJson nvarchar(max) NULL,
        CONSTRAINT FK_B3_Events_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT FK_B3_Events_Actor FOREIGN KEY(ActorUserId)
            REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
        CONSTRAINT CK_B3_ChangeRequestEvents_EventType CHECK
            ([EventType] IN (N''Submitted'',N''Rejected'')),
        CONSTRAINT CK_B3_ChangeRequestEvents_DetailsJson CHECK
            ([DetailsJson] IS NULL OR ISJSON([DetailsJson])=1),
        CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState CHECK
            (([EventType]=N''Submitted'' AND [DecisionKey] IS NULL AND [ActorUserId] IS NOT NULL)
             OR ([EventType]=N''Rejected'' AND [DecisionKey] IS NOT NULL AND [ActorUserId] IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_B3_ChangeRequestEvents_DecisionKey
        ON dbo.ChangeRequestEvents(DecisionKey) WHERE [DecisionKey] IS NOT NULL;
    CREATE INDEX IX_B3_ChangeRequestEvents_Request_OccurredAt
        ON dbo.ChangeRequestEvents(ChangeRequestId,OccurredAt,ChangeRequestEventId);
    CREATE INDEX IX_B3_ChangeRequestEvents_CorrelationId
        ON dbo.ChangeRequestEvents(CorrelationId);

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N''1.8.0-011'',N''Package B candidate: high-risk Location change request persistence'',SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-011''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011''))<>1 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'3caffee23251e45a1e1e3e90a1003256cf0ad0be29ab8eeb23bbd5076c4e6405' THROW 52013,N'G1_POSTCONDITION_011_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-011'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xb55ed30e002541a17bc0600667d1d1629b6544d3279b6fd5763a2a45109253ca,0x3caffee23251e45a1e1e3e90a1003256cf0ad0be29ab8eeb23bbd5076c4e6405,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_011_END
-- G1_STEP_012_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-011''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011''))<>1 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
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
    EXEC @LockResult=sys.sp_getapplock @Resource=N''FieldVisit.SchemaMigration'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=0;
    IF @LockResult<0 THROW 51210,N''Unable to acquire FieldVisit migration lock.'',1;
    IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)<>N''1.8.0-011''
        THROW 51211,N''Latest SchemaVersion must be exactly 1.8.0-011.'',1;
    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-012'')
        THROW 51212,N''1800_012 already exists; no rerun allowed.'',1;
    IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN(OBJECT_ID(N''dbo.ChangeRequests''),OBJECT_ID(N''dbo.ChangeRequestEvents'')))
        THROW 51213,N''Enabled B3 table trigger is prohibited.'',1;

    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownStatus;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownStatus
        CHECK ([Status] IN(N''Pending'',N''Rejected'',N''Applied''));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_KnownStatus;
    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_ReviewState;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK
    (([Status]=N''Pending'' AND [ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL)
     OR ([Status]=N''Rejected'' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId]
         AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt] AND [ReviewReason] IS NOT NULL
         AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NULL)
     OR ([Status]=N''Applied'' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId]
         AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt] AND [ReviewReason] IS NOT NULL
         AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NOT NULL AND [AppliedAt]>=[ReviewedAt]));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState;

    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
        CHECK ([EventType] IN(N''Submitted'',N''Rejected'',N''Approved'',N''Applied''));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState CHECK
    ([ActorUserId] IS NOT NULL AND [ActorUserId]>0 AND [CorrelationId]<>''00000000-0000-0000-0000-000000000000''
     AND (([EventType] IN(N''Submitted'',N''Applied'') AND [DecisionKey] IS NULL)
       OR ([EventType] IN(N''Rejected'',N''Approved'') AND [DecisionKey] IS NOT NULL
           AND [DecisionKey]<>''00000000-0000-0000-0000-000000000000'')));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N''1.8.0-012'',N''Package B candidate: B3 atomic approval/apply audit states'',SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-012''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012''))<>2 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'd46be3bb81f8b4645c5732a68b20bd58f6b16125eeba6c0fce1051ebb6dd135a' THROW 52013,N'G1_POSTCONDITION_012_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-012'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x50812ed02e784d7140cfa7d1fe04b92d4fdfb4ea013c04272ef1f39f4d8d3ac4,0xd46be3bb81f8b4645c5732a68b20bd58f6b16125eeba6c0fce1051ebb6dd135a,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_012_END
-- G1_STEP_014_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-012''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012''))<>2 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
/* OPTION C 014 CANDIDATE — STAGED LOCATION VERSIONS / PUBLISHED LEDGER.
   NONEXECUTABLE DESIGN. Do not run against UAT or Production. A future DBA
   review must replace this file with the approved forward-only migration,
   immutable source fingerprint, backup plan, permissions and rollback plan. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Strict promotion target guard is above; original disposable file is unchanged.
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @G1LocalLock int; EXEC @G1LocalLock=sys.sp_getapplock @Resource=N''FieldVisit.SchemaMigration'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=0; IF @G1LocalLock IS NULL OR @G1LocalLock<0 THROW 52003,N''G1_LOCK_STOP'',1;
    IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
        ORDER BY AppliedAt DESC, VersionNumber DESC) <> N''1.8.0-012''
        THROW 51411, N''OPTIONC_014_CANDIDATE_REQUIRES_012'', 1;

    CREATE TABLE dbo.LocationPublicationStages
    (
        ChangeRequestId bigint NOT NULL CONSTRAINT PK_OptionC_Stage PRIMARY KEY,
        LocationId int NOT NULL,
        OrganizationId int NOT NULL,
        TeamId int NOT NULL,
        ExpectedLocationRowVersion binary(8) NOT NULL,
        ProposedJson nvarchar(max) NOT NULL,
        ProposedSha256 binary(32) NOT NULL,
        StageStatus nvarchar(30) NOT NULL,
        GeocodeStatus nvarchar(20) NOT NULL,
        GeocodeAddressSha256 binary(32) NULL,
        Latitude decimal(10,7) NULL,
        Longitude decimal(10,7) NULL,
        DuplicateReviewStatus nvarchar(20) NOT NULL,
        DuplicateEvidenceSha256 binary(32) NULL,
        CreatedAt datetime2(3) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_OptionC_Stage_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Stage_Location FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Stage_Org FOREIGN KEY(OrganizationId)
            REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Stage_Team FOREIGN KEY(TeamId)
            REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_Stage_Json CHECK (ISJSON(ProposedJson)=1),
        CONSTRAINT CK_OptionC_Stage_Hash CHECK
            (ProposedSha256=HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),ProposedJson))),
        CONSTRAINT CK_OptionC_Stage_Status CHECK
            (StageStatus IN(N''PendingGeocode'',N''PendingDuplicateReview'',N''ReadyForPublish'',N''Published'',N''Abandoned'')),
        CONSTRAINT CK_OptionC_Stage_Geocode CHECK
            (GeocodeStatus IN(N''Pending'',N''Succeeded'',N''Failed'',N''NotRequired'')),
        CONSTRAINT CK_OptionC_Stage_Duplicate CHECK
            (DuplicateReviewStatus IN(N''Pending'',N''Cleared'',N''Blocked'')),
        CONSTRAINT CK_OptionC_Stage_Coordinates CHECK
            ((Latitude IS NULL AND Longitude IS NULL) OR
             (Latitude BETWEEN -90 AND 90 AND Longitude BETWEEN -180 AND 180)),
        CONSTRAINT CK_OptionC_Stage_GeocodeEvidence CHECK
            (GeocodeStatus<>N''Succeeded'' OR
             (GeocodeAddressSha256 IS NOT NULL AND Latitude IS NOT NULL AND Longitude IS NOT NULL)),
        CONSTRAINT CK_OptionC_Stage_AddressBinding CHECK
            (GeocodeStatus<>N''Succeeded'' OR
             (JSON_VALUE(ProposedJson,N''$.Address'') IS NOT NULL AND
              GeocodeAddressSha256=HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),JSON_VALUE(ProposedJson,N''$.Address''))))),
        CONSTRAINT CK_OptionC_Stage_DuplicateBinding CHECK
            (DuplicateReviewStatus<>N''Cleared'' OR DuplicateEvidenceSha256=ProposedSha256),
        CONSTRAINT CK_OptionC_Stage_Ready CHECK
            (StageStatus<>N''ReadyForPublish'' OR
             (DuplicateReviewStatus=N''Cleared'' AND DuplicateEvidenceSha256 IS NOT NULL
              AND GeocodeStatus IN(N''Succeeded'',N''NotRequired'')))
    );
    CREATE UNIQUE INDEX UX_OptionC_Stages_Request ON dbo.LocationPublicationStages(ChangeRequestId);
    CREATE UNIQUE INDEX UX_OptionC_Stages_OpenLocation
        ON dbo.LocationPublicationStages(OrganizationId,LocationId)
        WHERE StageStatus IN(N''PendingGeocode'',N''PendingDuplicateReview'',N''ReadyForPublish'');

    CREATE TABLE dbo.LocationPublishedVersions
    (
        LocationPublishedVersionId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_OptionC_PublishedVersion PRIMARY KEY,
        LocationId int NOT NULL,
        VersionNumber int NOT NULL,
        ChangeRequestId bigint NULL,
        IsCurrent bit NOT NULL,
        PublishedJson nvarchar(max) NOT NULL,
        PublishedSha256 binary(32) NOT NULL,
        PublishedAt datetime2(3) NOT NULL,
        PublishedByUserId int NOT NULL,
        SourceLocationRowVersion binary(8) NOT NULL,
        CONSTRAINT FK_OptionC_Published_Location FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_Published_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_Published_Number CHECK(VersionNumber>0),
        CONSTRAINT CK_OptionC_Published_Json CHECK(ISJSON(PublishedJson)=1),
        CONSTRAINT CK_OptionC_Published_Hash CHECK
            (PublishedSha256=HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),PublishedJson))),
        CONSTRAINT CK_OptionC_Published_Origin CHECK
            ((VersionNumber=1 AND ChangeRequestId IS NULL) OR
             (VersionNumber>1 AND ChangeRequestId IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_OptionC_Published_Current
        ON dbo.LocationPublishedVersions(LocationId) WHERE IsCurrent=1;
    CREATE UNIQUE INDEX UX_OptionC_Published_Request
        ON dbo.LocationPublishedVersions(ChangeRequestId)
        WHERE ChangeRequestId IS NOT NULL;
    CREATE UNIQUE INDEX UX_OptionC_Published_Number
        ON dbo.LocationPublishedVersions(LocationId,VersionNumber);

    /* No blind backfill: a future migration must insert version 1 only after
       DBA proves a canonical JSON projection for every existing Location and
       records the source RowVersion. Ambiguous rows must stop the migration. */
    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N''1.8.0-014'',N''OPTION C candidate: staged and published location versions'',
           SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
-- G1_BACKFILL_BEGIN
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
/* OPTION C 014 CANDIDATE — deterministic historical version-1 backfill.
   NONEXECUTABLE DESIGN / disposable rehearsal only. Never run against UAT or
   Production. The script refuses any database outside OptionCIsolated_* and
   refuses a partially populated or ambiguous source. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Strict promotion target guard is above; original disposable file is unchanged.
IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC) <> N''1.8.0-014''
    THROW 51431, N''OPTIONC_014_BACKFILL_REQUIRES_014'', 1;
IF EXISTS (SELECT 1 FROM dbo.LocationPublishedVersions)
    THROW 51432, N''OPTIONC_014_BACKFILL_ALREADY_APPLIED'', 1;

/* The projection is deliberately explicit and ordered. Only Approved+Active
   masters are published; Pending/Inactive drafts remain outside the ledger. */
IF EXISTS (
    SELECT 1
    FROM dbo.Locations l
    WHERE l.ApprovalStatus=N''Approved'' AND l.IsActive=1
      AND (l.LocationName IS NULL OR NULLIF(LTRIM(RTRIM(l.LocationName)),N'''') IS NULL
        OR l.Address IS NULL OR NULLIF(LTRIM(RTRIM(l.Address)),N'''') IS NULL
        OR l.CreatedByUserId<=0 OR l.RowVersion IS NULL)
)
    THROW 51433, N''OPTIONC_014_BACKFILL_AMBIGUOUS_PUBLISHED_SOURCE'', 1;
IF EXISTS (
    SELECT 1
    FROM dbo.Locations l
    LEFT JOIN dbo.Organizations o ON o.OrganizationId=l.OrganizationId
    LEFT JOIN dbo.Teams t ON t.TeamId=l.TeamId
    LEFT JOIN dbo.Users u ON u.UserId=l.CreatedByUserId
    WHERE l.ApprovalStatus=N''Approved'' AND l.IsActive=1
      AND (o.OrganizationId IS NULL OR u.UserId IS NULL
           OR u.OrganizationId IS NULL OR u.OrganizationId<>l.OrganizationId
           OR (l.TeamId IS NOT NULL AND
               (t.TeamId IS NULL OR t.OrganizationId IS NULL OR t.OrganizationId<>l.OrganizationId)))
)
    THROW 51434, N''OPTIONC_014_BACKFILL_MISSING_AUTHORITY_REFERENCE'', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @G1LocalLock int; EXEC @G1LocalLock=sys.sp_getapplock @Resource=N''FieldVisit.OptionC014.Backfill'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=0; IF @G1LocalLock IS NULL OR @G1LocalLock<0 THROW 52003,N''G1_LOCK_STOP'',1;
    IF EXISTS (SELECT 1 FROM dbo.LocationPublishedVersions)
        THROW 51435, N''OPTIONC_014_BACKFILL_RACE_OR_RERUN'', 1;

    INSERT dbo.LocationPublishedVersions(
        LocationId,VersionNumber,ChangeRequestId,IsCurrent,
        PublishedJson,PublishedSha256,PublishedAt,PublishedByUserId,
        SourceLocationRowVersion)
    SELECT l.LocationId,1,NULL,1,j.PublishedJson,
        HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),j.PublishedJson)),
        SYSUTCDATETIME(),l.CreatedByUserId,l.RowVersion
    FROM dbo.Locations l
    CROSS APPLY (
        SELECT l.LocationId,l.OrganizationId,l.TeamId,l.CreatedByUserId,
               l.LocationType,l.LocationName,l.City,l.District,l.Address,
               l.PlusCode,l.TaxId,l.MasterNote,l.ApprovalStatus,l.IsActive
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
    ) j(PublishedJson)
    WHERE l.ApprovalStatus=N''Approved'' AND l.IsActive=1;

    IF EXISTS (
        SELECT 1 FROM dbo.LocationPublishedVersions v
        WHERE v.IsCurrent<>1 OR v.VersionNumber<>1 OR v.ChangeRequestId IS NOT NULL
    )
        THROW 51436, N''OPTIONC_014_BACKFILL_LEDGER_INVARIANT_FAILED'', 1;
    IF EXISTS (
        SELECT 1 FROM dbo.LocationPublishedVersions v
        WHERE v.PublishedSha256<>HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),v.PublishedJson))
    )
        THROW 51437, N''OPTIONC_014_BACKFILL_HASH_INVARIANT_FAILED'', 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-014''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014''))<>3 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.Locations l WHERE l.ApprovalStatus=N''Approved'' AND l.IsActive=1 AND NOT EXISTS(SELECT 1 FROM dbo.LocationPublishedVersions v WHERE v.LocationId=l.LocationId AND v.VersionNumber=1 AND v.IsCurrent=1 AND v.ChangeRequestId IS NULL AND v.SourceLocationRowVersion=l.RowVersion AND v.PublishedSha256=HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),v.PublishedJson)))) OR (SELECT COUNT_BIG(*) FROM dbo.LocationPublishedVersions)<>(SELECT COUNT_BIG(*) FROM dbo.Locations WHERE ApprovalStatus=N''Approved'' AND IsActive=1) THROW 52015,N''G1_BACKFILL_COMPLETION_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'89672cbd083bdca08df15f38d9eb7ad8f67ca5319cfe94e97a4e60ee22ae34fe' THROW 52013,N'G1_POSTCONDITION_014_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-014'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x017ed453fc8c7d87052974d0eb327ac4a11a785f0a6c2c1a54e38efd5cee16f5,0x89672cbd083bdca08df15f38d9eb7ad8f67ca5319cfe94e97a4e60ee22ae34fe,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_014_END
-- G1_STEP_015_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-014''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014''))<>3 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
/*
  Disposable additive candidate: canonical Stage evidence audit events.
  This is not migration 013 and must only run in OptionCIsolated_* SQL Server databases.
  It preserves existing ChangeRequestEvents rows and approval ledger semantics.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Strict promotion target guard is above; original disposable file is unchanged.
IF OBJECT_ID(N''dbo.ChangeRequestEvents'',N''U'') IS NULL
    THROW 51301, N''Canonical ChangeRequestEvents table is missing.'', 1;

DECLARE @eventDefinition nvarchar(max);
SELECT @eventDefinition = cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N''dbo.ChangeRequestEvents'')
  AND cc.name = N''CK_B3_ChangeRequestEvents_EventType'';

IF @eventDefinition IS NULL
   OR @eventDefinition NOT LIKE N''%EventType%''
   OR @eventDefinition NOT LIKE N''%Submitted%''
   OR @eventDefinition NOT LIKE N''%Rejected%''
   OR @eventDefinition NOT LIKE N''%Approved%''
   OR @eventDefinition NOT LIKE N''%Applied%''
   OR @eventDefinition LIKE N''%GeocodeEvidenceRecorded%''
   OR @eventDefinition LIKE N''%DuplicateReviewEvidenceRecorded%''
    THROW 51302, N''Canonical 012 EventType CHECK does not match the expected pre-extension contract.'', 1;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N''dbo.ChangeRequestEvents'')
      AND name = N''CK_B3_ChangeRequestEvents_StageEvidenceEventType'')
    THROW 51303, N''Stage evidence audit candidate is already applied.'', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
        CHECK ([EventType] IN
            (N''Submitted'',N''Rejected'',N''Approved'',N''Applied'',
             N''GeocodeEvidenceRecorded'',N''DuplicateReviewEvidenceRecorded''));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;

    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_StageEvidenceEventType
        CHECK
        (
            [ActorUserId] IS NOT NULL
            AND [CorrelationId] <> N''00000000-0000-0000-0000-000000000000''
            AND
            (
                ([EventType] IN(N''Submitted'',N''Applied'') AND [DecisionKey] IS NULL)
                OR
                ([EventType] IN(N''Rejected'',N''Approved'',
                                N''GeocodeEvidenceRecorded'',
                                N''DuplicateReviewEvidenceRecorded'')
                 AND [DecisionKey] IS NOT NULL
                 AND [DecisionKey] <> N''00000000-0000-0000-0000-000000000000'')
            )
        );
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_StageEvidenceEventType;

    IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-015'')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N''1.8.0-015'',N''R5 canonical additive contract 1800_015'',SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-015''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015''))<>4 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'40825212db9403724893125cdeca4dbe7071d2d6cb25de623093f0bd67fb8879' THROW 52013,N'G1_POSTCONDITION_015_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-015'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xae07dd70550c00eaac15c3664d15d557739ef14d681cafb9a472fab256188f7d,0x40825212db9403724893125cdeca4dbe7071d2d6cb25de623093f0bd67fb8879,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_015_END
-- G1_STEP_016_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-015''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015''))<>4 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
/*
  Option C Leader first-level review additive candidate.
  NONDEPLOY: executable only on disposable OptionCIsolated_* databases.
  Apply after canonical 011 -> 012 -> 014 and the StageEvidence candidate.
  Never execute against UAT/Production.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Strict promotion target guard is above; original disposable file is unchanged.
IF OBJECT_ID(N''dbo.ChangeRequests'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.ChangeRequestEvents'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.SchemaVersions'',N''U'') IS NULL
    THROW 51081, ''OPTIONC_LEADER_REVIEW_CANONICAL_SCHEMA_REQUIRED'', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-014'')
    THROW 51082, ''OPTIONC_LEADER_REVIEW_REQUIRES_014'', 1;
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-LEADER-REVIEW'')
    THROW 51083, ''OPTIONC_LEADER_REVIEW_ALREADY_APPLIED'', 1;

BEGIN TRY
BEGIN TRANSACTION;

IF OBJECT_ID(N''dbo.ChangeRequestLeaderReviews'',N''U'') IS NULL
BEGIN
    CREATE TABLE dbo.ChangeRequestLeaderReviews(
        ChangeRequestLeaderReviewId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_ChangeRequestLeaderReviews PRIMARY KEY,
        ChangeRequestId bigint NOT NULL,
        ReviewStage nvarchar(30) NOT NULL
            CONSTRAINT CK_OptionC_LeaderReview_Stage CHECK(ReviewStage=N''LeaderFirstLevel''),
        Status nvarchar(20) NOT NULL
            CONSTRAINT CK_OptionC_LeaderReview_Status CHECK(Status IN(N''Pending'',N''Approved'',N''Rejected'')),
        ReviewerUserId int NULL,
        ReviewedAt datetime2(3) NULL,
        Reason nvarchar(1000) NULL,
        DecisionKey uniqueidentifier NULL,
        EvidenceJson nvarchar(max) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_OptionC_LeaderReview_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId),
        CONSTRAINT FK_OptionC_LeaderReview_Reviewer FOREIGN KEY(ReviewerUserId)
            REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_OptionC_LeaderReview_DecisionState CHECK(
            (Status=N''Pending'' AND ReviewerUserId IS NULL AND ReviewedAt IS NULL
                AND DecisionKey IS NULL)
            OR
            (Status IN(N''Approved'',N''Rejected'') AND ReviewerUserId IS NOT NULL
                AND ReviewedAt IS NOT NULL AND DecisionKey IS NOT NULL
                AND LEN(LTRIM(RTRIM(ISNULL(Reason,N''''))))>0))
    );
    CREATE UNIQUE INDEX UX_OptionC_LeaderReview_Request_Stage
        ON dbo.ChangeRequestLeaderReviews(ChangeRequestId,ReviewStage);
    CREATE UNIQUE INDEX UX_OptionC_LeaderReview_DecisionKey
        ON dbo.ChangeRequestLeaderReviews(DecisionKey)
        WHERE DecisionKey IS NOT NULL;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N''CK_B3_ChangeRequests_KnownCodes''
    AND parent_object_id=OBJECT_ID(N''dbo.ChangeRequests''))
BEGIN
    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownCodes;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownCodes
      CHECK(EntityKind=N''Location''
        AND OperationCode IN(N''CreateLocation'',N''UpdateLocation'',N''UpdatePublishedLocation'')
        AND RiskCode IN(N''Low'',N''High''));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_KnownCodes;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N''CK_B3_ChangeRequestEvents_EventType''
    AND parent_object_id=OBJECT_ID(N''dbo.ChangeRequestEvents''))
BEGIN
    ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
      CHECK(EventType IN(N''Submitted'',N''Rejected'',N''Approved'',N''Applied'',
                         N''GeocodeEvidenceRecorded'',N''DuplicateReviewEvidenceRecorded'',
                         N''LeaderApproved'',N''LeaderRejected''));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE name IN(N''CK_B3_ChangeRequestEvents_DecisionState'',
                  N''CK_B3_ChangeRequestEvents_StageEvidenceEventType'')
      AND parent_object_id=OBJECT_ID(N''dbo.ChangeRequestEvents''))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name=N''CK_B3_ChangeRequestEvents_DecisionState''
          AND parent_object_id=OBJECT_ID(N''dbo.ChangeRequestEvents''))
        ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name=N''CK_B3_ChangeRequestEvents_StageEvidenceEventType''
          AND parent_object_id=OBJECT_ID(N''dbo.ChangeRequestEvents''))
        ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_StageEvidenceEventType;

    ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState
      CHECK(ActorUserId IS NOT NULL AND ActorUserId>0
        AND CorrelationId<>''00000000-0000-0000-0000-000000000000''
        AND ((EventType IN(N''Submitted'',N''Applied'') AND DecisionKey IS NULL)
          OR (EventType IN(N''Rejected'',N''Approved'',
                           N''GeocodeEvidenceRecorded'',N''DuplicateReviewEvidenceRecorded'',
                           N''LeaderApproved'',N''LeaderRejected'')
              AND DecisionKey IS NOT NULL
              AND DecisionKey<>''00000000-0000-0000-0000-000000000000'')));
    ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
END;

INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
    VALUES(N''1.8.0-LEADER-REVIEW'',N''Option C additive Leader first-level review candidate'',SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-016'')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N''1.8.0-016'',N''R5 canonical additive contract 1800_016'',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-LEADER-REVIEW'')),N''G1 fixed-target promotion candidate'');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-LEADER-REVIEW''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW''))<>6 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'538e93637633603de40410f41ba8dd957c10d01571e7280aa564cbec5dbda6f7' THROW 52013,N'G1_POSTCONDITION_016_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-016'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xf7be77add8fcab7f0b4c7c798817f2d399ad1dac4ccf87f335ed69d25de05f94,0x538e93637633603de40410f41ba8dd957c10d01571e7280aa564cbec5dbda6f7,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_016_END
-- G1_STEP_017_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-LEADER-REVIEW''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW''))<>6 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
/*
  SCHEME A CREATE / LOW-RISK PUBLICATION CONTRACT — NONDEPLOY CANDIDATE.
  Disposable OptionCIsolated_* SQL Server 2022 ONLY.
  Requires 011 -> 012 -> 014 -> StageEvidenceAudit -> LeaderReviewDetails.
  No UAT/Production migration, no feature activation, no publication writer.
  A reservation row alone does NOT authorize a published Location.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Strict promotion target guard is above; original disposable file is unchanged.
IF OBJECT_ID(N''dbo.ChangeRequests'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.ChangeRequestLeaderReviews'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.LocationPublicationStages'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.LocationPublishedVersions'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.SchemaVersions'',N''U'') IS NULL
    THROW 51091, N''OPTIONC_CREATE_LOWRISK_DEPENDENCIES_MISSING'', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
              WHERE VersionNumber=N''1.8.0-LEADER-REVIEW'')
    THROW 51092, N''OPTIONC_CREATE_LOWRISK_REQUIRES_LEADER_REVIEW'', 1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions
          WHERE VersionNumber=N''1.8.0-CREATE-LOWRISK'')
    THROW 51093, N''OPTIONC_CREATE_LOWRISK_ALREADY_APPLIED'', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock
        @Resource=N''FieldVisit.SchemaMigration'',@LockMode=N''Exclusive'',
        @LockOwner=N''Transaction'',@LockTimeout=0;
    IF @LockResult<0 THROW 51094,N''OPTIONC_CREATE_LOWRISK_LOCK_DENIED'',1;
    IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
        ORDER BY AppliedAt DESC,VersionNumber DESC)<>N''1.8.0-LEADER-REVIEW''
        THROW 51095,N''OPTIONC_CREATE_LOWRISK_UNEXPECTED_MIGRATION_ORDER'',1;
    IF OBJECT_ID(N''dbo.LocationCreateReservations'',N''U'') IS NOT NULL
        THROW 51096,N''OPTIONC_CREATE_LOWRISK_PARTIAL_SCHEMA'',1;
    IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N''dbo.ChangeRequests'')
          AND name=N''CK_B3_ChangeRequests_KnownCodes''
          AND is_disabled=0 AND is_not_trusted=0)
       OR NOT EXISTS(SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N''dbo.LocationPublishedVersions'')
          AND name=N''CK_OptionC_Published_Origin''
          AND is_disabled=0 AND is_not_trusted=0)
        THROW 51097,N''OPTIONC_CREATE_LOWRISK_CANONICAL_CONSTRAINT_DRIFT'',1;

    /* Preserve the same canonical request ledger and RowVersion requirement.
       CreateLocation uses the RowVersion of its inactive Pending reservation;
       Low updates use the RowVersion of the existing published Location. */
    ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownCodes;
    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownCodes
        CHECK(EntityKind=N''Location'' AND
          ((OperationCode=N''CreateLocation'' AND RiskCode=N''High'')
           OR (OperationCode=N''UpdatePublishedLocation''
               AND RiskCode IN(N''Low'',N''High''))));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_KnownCodes;

    ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_OptionC_CreateLowRisk_BeforeImage
        CHECK((OperationCode=N''CreateLocation'' AND BeforeJson IS NULL)
           OR (OperationCode=N''UpdatePublishedLocation''
               AND BeforeJson IS NOT NULL AND ISJSON(BeforeJson)=1));
    ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_OptionC_CreateLowRisk_BeforeImage;

    /* Canonical backfill v1 has no request. Canonical CreateLocation v1 has a
       request. Both are allowed; later versions must have a request. The
       application writer MUST verify that the v1 request is CreateLocation
       and that the reservation is Pending/inactive before atomic publication. */
    ALTER TABLE dbo.LocationPublishedVersions DROP CONSTRAINT CK_OptionC_Published_Origin;
    ALTER TABLE dbo.LocationPublishedVersions WITH CHECK ADD CONSTRAINT CK_OptionC_Published_Origin
        CHECK(VersionNumber=1 OR
              (VersionNumber>1 AND ChangeRequestId IS NOT NULL));
    ALTER TABLE dbo.LocationPublishedVersions CHECK CONSTRAINT CK_OptionC_Published_Origin;

    CREATE TABLE dbo.LocationCreateReservations(
        ChangeRequestId bigint NOT NULL
            CONSTRAINT PK_OptionC_CreateReservation PRIMARY KEY,
        LocationId int NOT NULL,
        OrganizationId int NOT NULL,
        TeamId int NOT NULL,
        ReservedLocationRowVersion binary(8) NOT NULL,
        ReservationStatus nvarchar(20) NOT NULL,
        CreatedAt datetime2(3) NOT NULL,
        UpdatedAt datetime2(3) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_OptionC_CreateReservation_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_CreateReservation_Location FOREIGN KEY(LocationId)
            REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_CreateReservation_Org FOREIGN KEY(OrganizationId)
            REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
        CONSTRAINT FK_OptionC_CreateReservation_Team FOREIGN KEY(TeamId)
            REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_CreateReservation_Status CHECK
            (ReservationStatus IN(N''Pending'',N''Published'',N''Abandoned''))
    );
    CREATE UNIQUE INDEX UX_OptionC_CreateReservation_Location
        ON dbo.LocationCreateReservations(LocationId);

    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
      VALUES(N''1.8.0-CREATE-LOWRISK'',
        N''Scheme A disposable CreateLocation/LowRisk canonical contract'',
        SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
    IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-017'')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N''1.8.0-017'',N''R5 canonical additive contract 1800_017'',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-CREATE-LOWRISK'')),N''G1 fixed-target promotion candidate'');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-CREATE-LOWRISK''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK''))<>8 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'dcff6fcb1095204e8a2b1e7bebb9fc8b5a7f3950794b2be71e6b54dfb90b6205' THROW 52013,N'G1_POSTCONDITION_017_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-017'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xa90f97ff90dd634e2c57a53db5aec06ee1754d4e7da4beabaf27f4a5a3cc6c89,0xdcff6fcb1095204e8a2b1e7bebb9fc8b5a7f3950794b2be71e6b54dfb90b6205,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_017_END
-- G1_STEP_018_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-CREATE-LOWRISK''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK''))<>8 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
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
-- Strict promotion target guard is above; original disposable file is unchanged.
IF OBJECT_ID(N''dbo.ChangeRequests'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.ChangeRequestEvents'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.LocationPublishedVersions'',N''U'') IS NULL
   OR OBJECT_ID(N''dbo.SchemaVersions'',N''U'') IS NULL
    THROW 51701,N''OPTIONC_LOW_DIRECT_DEPENDENCIES_MISSING'',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
              WHERE VersionNumber=N''1.8.0-CREATE-LOWRISK'')
    THROW 51702,N''OPTIONC_LOW_DIRECT_REQUIRES_CREATE_LOWRISK'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions
          WHERE VersionNumber=N''1.8.0-LOWRISK-DIRECT'')
    THROW 51703,N''OPTIONC_LOW_DIRECT_ALREADY_APPLIED'',1;

BEGIN TRY
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N''FieldVisit.SchemaMigration'',
    @LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=0;
IF @lock<0 THROW 51704,N''OPTIONC_LOW_DIRECT_LOCK_DENIED'',1;
IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC,VersionNumber DESC)<>N''1.8.0-CREATE-LOWRISK''
    THROW 51705,N''OPTIONC_LOW_DIRECT_UNEXPECTED_ORDER'',1;

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N''dbo.ChangeRequests'')
      AND name=N''CK_B3_ChangeRequests_ReviewState''
      AND is_disabled=0 AND is_not_trusted=0)
    THROW 51706,N''OPTIONC_LOW_DIRECT_REQUEST_CONSTRAINT_DRIFT'',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N''dbo.ChangeRequestEvents'')
      AND name=N''CK_B3_ChangeRequestEvents_EventType''
      AND is_disabled=0 AND is_not_trusted=0)
    THROW 51707,N''OPTIONC_LOW_DIRECT_EVENT_CONSTRAINT_DRIFT'',1;

ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_ReviewState;
ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK
((Status=N''Pending'' AND ReviewedByUserId IS NULL AND ReviewedAt IS NULL
    AND ReviewReason IS NULL AND AppliedAt IS NULL)
 OR (Status=N''Rejected'' AND ReviewedByUserId IS NOT NULL
    AND ReviewedByUserId<>RequestedByUserId AND ReviewedAt IS NOT NULL
    AND ReviewedAt>=SubmittedAt AND ReviewReason IS NOT NULL
    AND LEN(LTRIM(RTRIM(ReviewReason)))>0 AND AppliedAt IS NULL)
 OR (Status=N''Applied'' AND
    ((ReviewedByUserId IS NOT NULL AND ReviewedByUserId<>RequestedByUserId
      AND ReviewedAt IS NOT NULL AND ReviewedAt>=SubmittedAt)
     OR
     (OperationCode=N''UpdatePublishedLocation'' AND RiskCode=N''Low''
      AND ReviewedByUserId IS NULL AND ReviewedAt IS NULL
      AND ISJSON(EvidenceJson)=1
      AND JSON_VALUE(EvidenceJson,N''$.PublicationMode'')=N''DirectLowRisk''))
    AND ReviewReason IS NOT NULL AND LEN(LTRIM(RTRIM(ReviewReason)))>0
    AND AppliedAt IS NOT NULL AND AppliedAt>=SubmittedAt));
ALTER TABLE dbo.ChangeRequests CHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState;

ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
 CHECK(EventType IN(N''Submitted'',N''Rejected'',N''Approved'',N''Applied'',
    N''GeocodeEvidenceRecorded'',N''DuplicateReviewEvidenceRecorded'',
    N''LeaderApproved'',N''LeaderRejected'',N''DirectLowRiskApplied''));
ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_EventType;

ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState
 CHECK(ActorUserId IS NOT NULL AND ActorUserId>0
  AND CorrelationId<>''00000000-0000-0000-0000-000000000000''
  AND ((EventType IN(N''Submitted'',N''Applied'') AND DecisionKey IS NULL)
    OR (EventType IN(N''Rejected'',N''Approved'',N''GeocodeEvidenceRecorded'',
         N''DuplicateReviewEvidenceRecorded'',N''LeaderApproved'',N''LeaderRejected'',
         N''DirectLowRiskApplied'')
        AND DecisionKey IS NOT NULL
        AND DecisionKey<>''00000000-0000-0000-0000-000000000000'')));
ALTER TABLE dbo.ChangeRequestEvents CHECK CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;

INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
VALUES(N''1.8.0-LOWRISK-DIRECT'',
 N''Scheme A authorized direct LowRisk version and audit contract'',
 SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-018'')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N''1.8.0-018'',N''R5 canonical additive contract 1800_018'',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-LOWRISK-DIRECT'')),N''G1 fixed-target promotion candidate'');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-LOWRISK-DIRECT''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT''))<>10 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'd619301f85b9061fdaf16bd3da22c1fe37e71088d53eed0abde0ffe679662033' THROW 52013,N'G1_POSTCONDITION_018_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-018'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x0787be84c88f841831fdea94a32e678cc204c24218729540d8e36895dcf700c7,0xd619301f85b9061fdaf16bd3da22c1fe37e71088d53eed0abde0ffe679662033,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_018_END
-- G1_STEP_019_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-LOWRISK-DIRECT''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT''))<>10 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
/* OPTION C MERGE DETAILS — DISPOSABLE CANDIDATE ONLY.
   Additive detail table, not an approval ledger. Never execute on UAT/Production.
   013 remains reserved. The canonical ChangeRequests row remains the authority. */
SET NOCOUNT ON;
-- Strict promotion target guard is above; original disposable file is unchanged.
IF OBJECT_ID(N''dbo.ChangeRequests'',N''U'') IS NULL
    THROW 51461, N''OPTIONC_MERGE_REQUIRES_011'', 1;
IF OBJECT_ID(N''dbo.LocationPublicationStages'',N''U'') IS NULL
    THROW 51462, N''OPTIONC_MERGE_REQUIRES_014'', 1;
IF OBJECT_ID(N''dbo.LocationMergeDetails'',N''U'') IS NULL
BEGIN
    CREATE TABLE dbo.LocationMergeDetails
    (
        ChangeRequestId bigint NOT NULL CONSTRAINT PK_OptionC_MergeDetails PRIMARY KEY,
        SourceLocationId int NOT NULL,
        SurvivorLocationId int NOT NULL,
        SourceOrganizationId int NOT NULL,
        SurvivorOrganizationId int NOT NULL,
        SourceTeamId int NOT NULL,
        SurvivorTeamId int NOT NULL,
        SourceLocationRowVersion binary(8) NOT NULL,
        SurvivorLocationRowVersion binary(8) NOT NULL,
        BeforeJson nvarchar(max) NOT NULL,
        ProposedJson nvarchar(max) NOT NULL,
        EvidenceJson nvarchar(max) NULL,
        CONSTRAINT FK_OptionC_MergeDetails_Request FOREIGN KEY(ChangeRequestId)
            REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
        CONSTRAINT CK_OptionC_MergeDetails_Different CHECK(SourceLocationId<>SurvivorLocationId),
        CONSTRAINT CK_OptionC_MergeDetails_SameOrg CHECK(SourceOrganizationId=SurvivorOrganizationId),
        CONSTRAINT CK_OptionC_MergeDetails_Teams CHECK(SourceTeamId=SurvivorTeamId AND SourceTeamId>0),
        CONSTRAINT CK_OptionC_MergeDetails_RowVersions CHECK(DATALENGTH(SourceLocationRowVersion)=8 AND DATALENGTH(SurvivorLocationRowVersion)=8),
        CONSTRAINT CK_OptionC_MergeDetails_Json CHECK(ISJSON(BeforeJson)=1 AND ISJSON(ProposedJson)=1 AND (EvidenceJson IS NULL OR ISJSON(EvidenceJson)=1))
    );
END;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-019'') INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N''1.8.0-019'',N''R5 canonical additive contract 1800_019'',SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-019''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019''))<>11 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'e168885f8f9b9482791b20a2cf301e771380c514c0334d13b904c78e403c558e' THROW 52013,N'G1_POSTCONDITION_019_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-019'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xe30b1dfa3563fb706e1d1358fb5b42807b2c86ac6494902dc7e2ecb1b492e46b,0xe168885f8f9b9482791b20a2cf301e771380c514c0334d13b904c78e403c558e,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_019_END
-- G1_STEP_020_BEGIN
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-019''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019''))<>11 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'SET NOCOUNT ON;
DECLARE @G1Offline bit=CASE WHEN CONVERT(int,SERVERPROPERTY(N''EngineEdition''))=3
 AND CONVERT(int,SERVERPROPERTY(N''ProductMajorVersion''))=16
 AND CONVERT(nvarchar(128),SERVERPROPERTY(N''Edition'')) LIKE N''Developer Edition%''
 AND DB_NAME() LIKE N''OptionCIsolated[_]%'' AND LEN(DB_NAME())=LEN(N''OptionCIsolated_'')+32
 AND SUBSTRING(DB_NAME(),LEN(N''OptionCIsolated_'')+1,32) COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
 AND TRY_CONVERT(int,SESSION_CONTEXT(N''FieldVisit.G1.NonDeploy''))=1 THEN 1 ELSE 0 END;
IF @G1Offline=0 AND (COALESCE(CONVERT(int,SERVERPROPERTY(N''EngineEdition'')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N''ServerName'')),N'''') COLLATE Latin1_General_100_BIN2<>N''sql-fieldvisit-jpe-uat''
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N''db-fieldvisit-uat'')
 THROW 52001,N''G1_TARGET_STOP'',1;
IF COALESCE(APPLOCK_MODE(N''public'',N''FieldVisit.SchemaMigration'',N''Transaction''),N'''')<>N''Exclusive'' THROW 52003,N''G1_LOCK_STOP'',1;
-- SCHEME A / EXCEL CANONICAL STATUS -- NONEXECUTABLE PRODUCTION DRAFT
-- Execution is permitted only by isolated disposable SQL tests. Do not
-- deploy this file or run it on UAT/Production without separate Owner+DBA
-- authorization, preflight, rollback plan and future version sequencing.
SET XACT_ABORT ON;
-- Strict promotion target guard is above; original disposable file is unchanged.
BEGIN TRY
  BEGIN TRANSACTION;
  IF OBJECT_ID(N''dbo.ImportBatches'',N''U'') IS NULL
      OR OBJECT_ID(N''dbo.ImportBatchItems'',N''U'') IS NULL
      OR OBJECT_ID(N''dbo.SchemaVersions'',N''U'') IS NULL
      THROW 55111,N''OPTIONC_EXCEL_LEGACY_IMPORT_SCHEMA_REQUIRED'',1;
  IF COL_LENGTH(N''dbo.ImportBatches'',N''Status'') < 30
      OR COL_LENGTH(N''dbo.ImportBatchItems'',N''Status'') < 18
      THROW 55112,N''OPTIONC_EXCEL_STATUS_WIDTH_INVALID'',1;
  IF EXISTS(SELECT 1 FROM sys.check_constraints
      WHERE parent_object_id=OBJECT_ID(N''dbo.ImportBatches'')
        AND name=N''CK_ImportBatches_Status'')
      ALTER TABLE dbo.ImportBatches DROP CONSTRAINT CK_ImportBatches_Status;
  IF EXISTS(SELECT 1 FROM sys.check_constraints
      WHERE parent_object_id=OBJECT_ID(N''dbo.ImportBatchItems'')
        AND name=N''CK_ImportBatchItems_Status'')
      ALTER TABLE dbo.ImportBatchItems DROP CONSTRAINT CK_ImportBatchItems_Status;
  ALTER TABLE dbo.ImportBatches WITH CHECK ADD
    CONSTRAINT CK_ImportBatches_Status CHECK(Status IN(
      N''Previewed'',N''Confirmed'',N''PartiallyFailed'',
      N''PartiallySubmittedForReview'',N''SubmittedForReview''));
  ALTER TABLE dbo.ImportBatchItems WITH CHECK ADD
    CONSTRAINT CK_ImportBatchItems_Status CHECK(Status IN(
      N''Valid'',N''Error'',N''Applied'',N''Failed'',N''SubmittedForReview''));
  IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
      WHERE VersionNumber=N''1.8.0-EXCEL-CANONICAL-STATUS'')
      INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
      VALUES(N''1.8.0-EXCEL-CANONICAL-STATUS'',
             N''EXCEL canonical reviewed submission status contract / disposable test'',
             SYSUTCDATETIME(),N''G1 fixed-target promotion candidate'');
  IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-020'')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N''1.8.0-020'',N''R5 canonical additive contract 1800_020'',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-EXCEL-CANONICAL-STATUS'')),N''G1 fixed-target promotion candidate'');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
';
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-EXCEL-CANONICAL-STATUS''
 THROW 52012,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS''))
 THROW 52012,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS''))<>13 THROW 52012,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'cfaaae20bde78da8ea1327fef077abeca48b7d4579303bf2cb7b62ddda22bd66' THROW 52013,N'G1_POSTCONDITION_020_STOP',1;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
EXEC sys.sp_executesql N'INSERT dbo.FieldVisitMigrationLedger(VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt) VALUES(N''1.8.0-020'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x992eb565487f8c24535fd89b3dbdbb28ad292b62120e42287538f940c31c64a7,0xcfaaae20bde78da8ea1327fef077abeca48b7d4579303bf2cb7b62ddda22bd66,CONVERT(binary(32),@mh,2),SYSUTCDATETIME());',N'@mh varchar(64)',@mh=@G1After;
-- G1_STEP_020_END
EXEC sys.sp_executesql N'-- History data stay in SQL memory; return digest only to controlled callers.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT * FROM dbo.VisitTrips ORDER BY VisitTripId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT * FROM dbo.VisitTripStops ORDER BY VisitTripStopId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT * FROM dbo.VisitTripSnapshots ORDER BY VisitTripSnapshotId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT * FROM dbo.VisitTripSnapshotStops ORDER BY VisitTripSnapshotStopId FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1After OUTPUT;
IF @G1History IS NULL OR @G1After IS NULL OR @G1History<>@G1After THROW 52016,N'G1_HISTORY_STOP',1;
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N''1.8.0-010'')<>1
 OR COALESCE((SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC),N'''') COLLATE Latin1_General_100_BIN2<>N''1.8.0-EXCEL-CANONICAL-STATUS''
 THROW 52014,N''G1_PREDECESSOR_STOP'',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N''1.8.0-%'' AND VersionNumber NOT IN(N''1.8.0-001'',N''1.8.0-002'',N''1.8.0-003'',N''1.8.0-004'',N''1.8.0-005'',N''1.8.0-006'',N''1.8.0-007'',N''1.8.0-008'',N''1.8.0-009'',N''1.8.0-010'',N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS''))
 THROW 52014,N''G1_MARKER_SET_STOP'',1;
IF (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS''))<>13 THROW 52014,N''G1_MARKER_SET_STOP'',1;';
EXEC sys.sp_executesql N'-- Scoped canonical metadata digest; no metadata rows are returned.
SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),
 COALESCE((SELECT t.name,c.name AS Col,ty.name AS Typ,SCHEMA_NAME(ty.schema_id) AS TypeSchema,c.system_type_id,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_ansi_padded,c.is_rowguidcol,c.is_sparse,c.is_hidden,c.generated_always_type,c.encryption_type,c.is_masked,c.collation_name,c.is_computed,cc.definition AS Computed,CONVERT(nvarchar(40),ident.seed_value) AS Seed,CONVERT(nvarchar(40),ident.increment_value) AS Increment,dc.name AS DefaultName,dc.definition AS Def FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id LEFT JOIN sys.identity_columns ident ON ident.object_id=c.object_id AND ident.column_id=c.column_id WHERE t.name IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(parent_object_id) AS Tbl,name,definition,is_disabled,is_not_trusted,is_not_for_replication FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') OR (OBJECT_NAME(parent_object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND name IN(N''CK_ImportBatches_Status'',N''CK_ImportBatchItems_Status'')) ORDER BY Tbl,name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(i.object_id) AS Tbl,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.ignore_dup_key,i.filter_definition,i.is_disabled,c.name AS Col,ic.key_ordinal,ic.is_included_column,ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_hypothetical=0 AND OBJECT_NAME(i.object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,i.name,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(f.parent_object_id) AS Tbl,f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action,f.update_referential_action,f.is_not_for_replication,pc.name AS Col,OBJECT_NAME(f.referenced_object_id) AS Ref,rc.name AS RefCol FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id WHERE OBJECT_NAME(f.parent_object_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'') ORDER BY Tbl,f.name,fc.constraint_column_id FOR JSON PATH),N''[]'')+
 COALESCE((SELECT name,is_disabled,is_instead_of_trigger,OBJECT_DEFINITION(object_id) AS Def FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN(N''ChangeRequests'',N''ChangeRequestEvents'',N''LocationPublicationStages'',N''LocationPublishedVersions'',N''ChangeRequestLeaderReviews'',N''LocationCreateReservations'',N''LocationMergeDetails'',N''FieldVisitMigrationLedger'',N''Locations'',N''ImportBatches'',N''ImportBatchItems'') ORDER BY name FOR JSON PATH),N''[]'')+
 COALESCE((SELECT OBJECT_NAME(c.object_id) AS Tbl,c.name,ty.name AS Typ,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE OBJECT_NAME(c.object_id) IN(N''ImportBatches'',N''ImportBatchItems'') AND c.name=N''Status'' ORDER BY Tbl FOR JSON PATH),N''[]''))),2);
',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
IF @G1Hash IS NULL OR LOWER(@G1Hash)<>N'cfaaae20bde78da8ea1327fef077abeca48b7d4579303bf2cb7b62ddda22bd66' THROW 52014,N'G1_CATALOG_STOP',1;
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.FieldVisitMigrationLedger)<>9 OR EXISTS(SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256 FROM (VALUES(N''1.8.0-011'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xb55ed30e002541a17bc0600667d1d1629b6544d3279b6fd5763a2a45109253ca,0x3caffee23251e45a1e1e3e90a1003256cf0ad0be29ab8eeb23bbd5076c4e6405),(N''1.8.0-012'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x50812ed02e784d7140cfa7d1fe04b92d4fdfb4ea013c04272ef1f39f4d8d3ac4,0xd46be3bb81f8b4645c5732a68b20bd58f6b16125eeba6c0fce1051ebb6dd135a),(N''1.8.0-014'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x017ed453fc8c7d87052974d0eb327ac4a11a785f0a6c2c1a54e38efd5cee16f5,0x89672cbd083bdca08df15f38d9eb7ad8f67ca5319cfe94e97a4e60ee22ae34fe),(N''1.8.0-015'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xae07dd70550c00eaac15c3664d15d557739ef14d681cafb9a472fab256188f7d,0x40825212db9403724893125cdeca4dbe7071d2d6cb25de623093f0bd67fb8879),(N''1.8.0-016'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xf7be77add8fcab7f0b4c7c798817f2d399ad1dac4ccf87f335ed69d25de05f94,0x538e93637633603de40410f41ba8dd957c10d01571e7280aa564cbec5dbda6f7),(N''1.8.0-017'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xa90f97ff90dd634e2c57a53db5aec06ee1754d4e7da4beabaf27f4a5a3cc6c89,0xdcff6fcb1095204e8a2b1e7bebb9fc8b5a7f3950794b2be71e6b54dfb90b6205),(N''1.8.0-018'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x0787be84c88f841831fdea94a32e678cc204c24218729540d8e36895dcf700c7,0xd619301f85b9061fdaf16bd3da22c1fe37e71088d53eed0abde0ffe679662033),(N''1.8.0-019'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0xe30b1dfa3563fb706e1d1358fb5b42807b2c86ac6494902dc7e2ecb1b492e46b,0xe168885f8f9b9482791b20a2cf301e771380c514c0334d13b904c78e403c558e),(N''1.8.0-020'',0x8ba0e373234e3b9d01f3508d55eab7639e74e846e54c15141e44686ca682806d,0x992eb565487f8c24535fd89b3dbdbb28ad292b62120e42287538f940c31c64a7,0xcfaaae20bde78da8ea1327fef077abeca48b7d4579303bf2cb7b62ddda22bd66)) v(VersionNumber,PackageId,PayloadSha256,CatalogSha256) EXCEPT SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256 FROM dbo.FieldVisitMigrationLedger) THROW 52014,N''G1_CHECKSUM_STOP'',1;';
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-011'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-012'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-014'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-015'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-016'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-017'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-018'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-019'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
EXEC sys.sp_executesql N'SELECT @G1Hash=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONVERT(varbinary(max),(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN(N''1.8.0-011'',N''1.8.0-012'',N''1.8.0-014'',N''1.8.0-015'',N''1.8.0-016'',N''1.8.0-LEADER-REVIEW'',N''1.8.0-017'',N''1.8.0-CREATE-LOWRISK'',N''1.8.0-018'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-019'',N''1.8.0-020'',N''1.8.0-EXCEL-CANONICAL-STATUS'') ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2);',N'@G1Hash varchar(64) OUTPUT',@G1Hash=@G1Hash OUTPUT;
EXEC sys.sp_executesql N'IF NOT EXISTS(SELECT 1 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=N''1.8.0-020'' AND MarkersSha256=CONVERT(binary(32),@h,2)) THROW 52014,N''G1_MARKER_CHECKSUM_STOP'',1;',N'@h varchar(64)',@h=@G1Hash;
COMMIT TRANSACTION; SELECT N'G1_PROMOTION_COMPLETE' AS Status;
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; IF @G1Offline=1 AND ERROR_NUMBER()=52990 THROW; IF ERROR_NUMBER() BETWEEN 52001 AND 52020 THROW; THROW 52090,N'G1_PROMOTION_STOP',1; END CATCH;
