SET NOCOUNT ON;

DECLARE @Errors TABLE
(
    ErrorMessage NVARCHAR(1000) NOT NULL
);

IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
    INSERT @Errors
    VALUES(N'Missing dbo.UserIdentityProfiles');

IF COL_LENGTH(
       N'dbo.UserIdentityProfiles',
       N'EntraTenantId'
   ) IS NULL
    INSERT @Errors
    VALUES(N'Missing UserIdentityProfiles.EntraTenantId');

IF COL_LENGTH(
       N'dbo.UserIdentityProfiles',
       N'EntraObjectId'
   ) IS NULL
    INSERT @Errors
    VALUES(N'Missing UserIdentityProfiles.EntraObjectId');

IF NOT EXISTS(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.7.0-003'
)
    INSERT @Errors
    VALUES(N'SchemaVersions does not contain 1.7.0-003');

IF COL_LENGTH(
       N'dbo.UserIdentityProfiles',
       N'EntraTenantId'
   ) IS NOT NULL
AND EXISTS(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t
      ON t.user_type_id = c.user_type_id
    WHERE c.object_id =
          OBJECT_ID(N'dbo.UserIdentityProfiles')
      AND c.name = N'EntraTenantId'
      AND t.name <> N'uniqueidentifier'
)
    INSERT @Errors
    VALUES(N'EntraTenantId is not UNIQUEIDENTIFIER');

IF COL_LENGTH(
       N'dbo.UserIdentityProfiles',
       N'EntraObjectId'
   ) IS NOT NULL
AND EXISTS(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t
      ON t.user_type_id = c.user_type_id
    WHERE c.object_id =
          OBJECT_ID(N'dbo.UserIdentityProfiles')
      AND c.name = N'EntraObjectId'
      AND t.name <> N'uniqueidentifier'
)
    INSERT @Errors
    VALUES(N'EntraObjectId is not UNIQUEIDENTIFIER');

IF NOT EXISTS(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID(N'dbo.UserIdentityProfiles')
      AND name =
          N'UX_UserIdentityProfiles_EntraIdentity'
      AND is_unique = 1
)
    INSERT @Errors
    VALUES(N'Missing unique Entra identity index');

IF EXISTS(
    SELECT 1
    FROM dbo.UserIdentityProfiles
    WHERE
        (
            EntraTenantId IS NULL
            AND EntraObjectId IS NOT NULL
        )
        OR
        (
            EntraTenantId IS NOT NULL
            AND EntraObjectId IS NULL
        )
)
    INSERT @Errors
    VALUES(N'Incomplete Entra identity binding pair');

IF EXISTS(
    SELECT
        EntraTenantId,
        EntraObjectId
    FROM dbo.UserIdentityProfiles
    WHERE
        EntraTenantId IS NOT NULL
        AND EntraObjectId IS NOT NULL
    GROUP BY
        EntraTenantId,
        EntraObjectId
    HAVING COUNT(*) > 1
)
    INSERT @Errors
    VALUES(N'Duplicate Entra tenant/object identity binding');

IF EXISTS(SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage
    FROM @Errors
    ORDER BY ErrorMessage;

    THROW 51890,
          N'v1.7.0-003 Verify FAILED.',
          1;
END;

SELECT
    N'PASS' AS VerifyStatus,

    (SELECT COUNT(*)
     FROM dbo.UserIdentityProfiles)
        AS IdentityProfiles,

    (SELECT COUNT(*)
     FROM dbo.UserIdentityProfiles
     WHERE
         EntraTenantId IS NOT NULL
         AND EntraObjectId IS NOT NULL)
        AS EntraBindings;

PRINT N'v1.7.0-003 Verify PASS.';
