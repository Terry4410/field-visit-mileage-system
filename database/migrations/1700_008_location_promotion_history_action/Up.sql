SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-008
       Allow LocationApprovalHistory.Action = PromotedToOfficial

       Why:
       The new admin "轉正式" flow keeps the same LocationId and changes
       Locations.IsTemporary from 1 to 0. It also records an auditable
       LocationApprovalHistory row with Action = PromotedToOfficial.

       Legacy CK_LocationApprovalHistory_Action does not allow this new
       action, causing the whole SaveChanges transaction to roll back.

       Safety:
       - Requires 1.7.0-007.
       - Refuses duplicate application.
       - Preserves the CURRENT constraint expression and only appends
         PromotedToOfficial.
       - Does not modify Location rows or historical Trip/Snapshot data.
       ================================================================ */

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 52300, N'找不到 dbo.SchemaVersions。', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-007'
    )
        THROW 52301,
              N'尚未套用 prerequisite Migration 1.7.0-007。',
              1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-008'
    )
        THROW 52302,
              N'Migration 1.7.0-008 已套用，不得重複執行。',
              1;

    IF OBJECT_ID(N'dbo.LocationApprovalHistory', N'U') IS NULL
        THROW 52303,
              N'找不到 dbo.LocationApprovalHistory。',
              1;

    IF COL_LENGTH(
           N'dbo.LocationApprovalHistory',
           N'Action'
       ) IS NULL
        THROW 52304,
              N'找不到 LocationApprovalHistory.Action。',
              1;

    /* PromotedToOfficial = 18 characters. Action is expected >= 18. */
    DECLARE @ActionMaxLength INT;

    SELECT @ActionMaxLength =
        CASE
            WHEN c.max_length = -1 THEN 2147483647
            WHEN t.name IN (N'nchar', N'nvarchar')
                THEN c.max_length / 2
            ELSE c.max_length
        END
    FROM sys.columns c
    JOIN sys.types t
      ON c.user_type_id = t.user_type_id
    WHERE c.object_id =
          OBJECT_ID(N'dbo.LocationApprovalHistory')
      AND c.name = N'Action';

    IF @ActionMaxLength < LEN(N'PromotedToOfficial')
        THROW 52305,
              N'LocationApprovalHistory.Action 欄位長度不足，停止 Migration。',
              1;

    DECLARE @ConstraintName sysname =
        N'CK_LocationApprovalHistory_Action';

    DECLARE @OldDefinition nvarchar(max);

    SELECT @OldDefinition = cc.definition
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id =
          OBJECT_ID(N'dbo.LocationApprovalHistory')
      AND cc.name = @ConstraintName;

    IF @OldDefinition IS NULL
        THROW 52306,
              N'找不到 CK_LocationApprovalHistory_Action；停止 Migration，請由 IT Review。',
              1;

    /*
       Do not hard-code the legacy Action list.
       Reuse the exact current definition, then append the new action.
    */
    IF UPPER(@OldDefinition)
       NOT LIKE N'%''PROMOTEDTOOFFICIAL''%'
    BEGIN
        ALTER TABLE dbo.LocationApprovalHistory
            DROP CONSTRAINT CK_LocationApprovalHistory_Action;

        DECLARE @Sql nvarchar(max) =
            N'ALTER TABLE dbo.LocationApprovalHistory WITH CHECK '
          + N'ADD CONSTRAINT CK_LocationApprovalHistory_Action '
          + N'CHECK (('
          + @OldDefinition
          + N') OR [Action] = N''PromotedToOfficial'');';

        EXEC sys.sp_executesql @Sql;

        ALTER TABLE dbo.LocationApprovalHistory
            CHECK CONSTRAINT CK_LocationApprovalHistory_Action;
    END;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();

    INSERT dbo.SchemaVersions
    (
        VersionNumber,
        Description,
        AppliedAt,
        AppliedBy
    )
    VALUES
    (
        N'1.7.0-008',
        N'Allow PromotedToOfficial in LocationApprovalHistory for admin temporary-to-official location promotion',
        @Now,
        N'v1.7.0 UAT temporary location promotion'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-008 completed. PromotedToOfficial is now allowed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
