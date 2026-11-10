SET NOCOUNT ON;
SELECT N'FV_SQL',
       LOWER(CONVERT(nvarchar(256), SERVERPROPERTY('ServerName'))),
       DB_NAME(), CONVERT(int, SERVERPROPERTY('EngineEdition')),
       CONVERT(varchar(170), SUSER_SID(), 2),
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'),
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'),
       (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0),
       c.encrypt_option, c.auth_scheme, c.net_transport,
       (SELECT COUNT_BIG(*) FROM sys.objects WHERE is_ms_shipped = 0),
       (SELECT COUNT_BIG(*) FROM sys.schemas WHERE name NOT IN
         (N'dbo', N'guest', N'sys', N'INFORMATION_SCHEMA', N'db_owner',
          N'db_accessadmin', N'db_securityadmin', N'db_ddladmin',
          N'db_backupoperator', N'db_datareader', N'db_datawriter',
          N'db_denydatareader', N'db_denydatawriter')),
       (SELECT COUNT_BIG(*) FROM sys.types WHERE is_user_defined = 1),
       (SELECT COUNT_BIG(*) FROM sys.triggers WHERE parent_class = 0 AND is_ms_shipped = 0),
       (SELECT COUNT_BIG(*) FROM sys.xml_schema_collections WHERE xml_collection_id <> 1),
       (SELECT COUNT_BIG(*) FROM sys.fulltext_catalogs),
       (SELECT COUNT_BIG(*) FROM sys.external_data_sources),
       (SELECT COUNT_BIG(*) FROM sys.database_scoped_credentials),
       (SELECT COUNT_BIG(*) FROM sys.security_policies),
       (SELECT COUNT_BIG(*) FROM sys.extended_properties WHERE class = 0),
       (SELECT COUNT_BIG(*) FROM sys.objects WHERE is_ms_shipped = 0
          AND name IN (N'SchemaVersions', N'MigrationLedger'))
FROM sys.dm_exec_connections AS c WHERE c.session_id = @@SPID;
