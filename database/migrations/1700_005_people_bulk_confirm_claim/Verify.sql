SET NOCOUNT ON;

DECLARE @Errors TABLE
(
    ErrorMessage NVARCHAR(1000) NOT NULL
);

IF NOT EXISTS(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.7.0-005'
)
    INSERT @Errors
    VALUES(N'SchemaVersions does not contain 1.7.0-005');

IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NULL
    INSERT @Errors
    VALUES(N'Missing dbo.ImportBatches');

IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NOT NULL
AND NOT EXISTS(
    SELECT 1
    FROM sys.check_constraints
    WHERE
        parent_object_id =
            OBJECT_ID(N'dbo.ImportBatches')
        AND name =
            N'CK_ImportBatches_Status'
        AND is_disabled = 0
        AND is_not_trusted = 0
        AND CHARINDEX(
            N'Confirming',
            definition
        ) > 0
)
    INSERT @Errors
    VALUES(
        N'CK_ImportBatches_Status does not allow trusted Confirming status'
    );

IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NOT NULL
AND EXISTS(
    SELECT 1
    FROM dbo.ImportBatches
    WHERE Status NOT IN(
        N'Previewed',
        N'Confirming',
        N'Confirmed',
        N'PartiallyFailed'
    )
)
    INSERT @Errors
    VALUES(N'ImportBatches contains invalid Status values');

IF EXISTS(SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage
    FROM @Errors
    ORDER BY ErrorMessage;

    THROW 52090,
          N'v1.7.0-005 Verify FAILED.',
          1;
END;

SELECT
    N'PASS' AS VerifyStatus,
    N'1.7.0-005' AS SchemaVersion,
    (
        SELECT COUNT(*)
        FROM dbo.ImportBatches
        WHERE Status = N'Confirming'
    ) AS CurrentlyConfirming;

PRINT N'v1.7.0-005 Verify PASS.';
