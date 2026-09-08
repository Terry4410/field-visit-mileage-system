SET NOCOUNT ON;
SET XACT_ABORT ON;

-- REVIEWED PREPARATION SCRIPT ONLY. Run separately after evidence capture.
-- Revocation intentionally prevents any later migration until a new review and
-- grant. It retains the original db_datareader membership.

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 53820, N'Permission revoke refused: target is not db-fieldvisit-uat.', 1;

IF USER_ID(N'gh-fieldvisit-uat-migrate') IS NULL
    THROW 53821, N'Permission revoke refused: gh-fieldvisit-uat-migrate is missing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate];
    REVOKE UPDATE ON OBJECT::dbo.Organizations FROM [gh-fieldvisit-uat-migrate];
    REVOKE UPDATE ON OBJECT::dbo.Teams FROM [gh-fieldvisit-uat-migrate];

    IF IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate') = 1
        ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 53822, N'Permission revoke post-check failed: original db_datareader baseline is missing.', 1;

IF IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate') = 1
    THROW 53823, N'Permission revoke post-check failed: db_ddladmin membership remains.', 1;

SELECT
    N'REVOKED_1800_001_ELEVATION' AS PermissionState,
    DB_NAME() AS DatabaseName,
    N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal,
    N'db_datareader retained; future migration execution is blocked pending fresh review.' AS Result;
