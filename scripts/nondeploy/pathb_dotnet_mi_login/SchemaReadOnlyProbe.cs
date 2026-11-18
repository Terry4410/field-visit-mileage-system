// NONDEPLOY: post-login metadata check only; never claim schema absent when MI lacks metadata visibility.
using Microsoft.Data.SqlClient;

internal static class SchemaReadOnlyProbe
{
    internal static async Task<string> CheckAsync(SqlConnection connection)
    {
        // One fixed read-only SQL batch, only after a successful MI login. No business rows.
        // Database-level VIEW DEFINITION and table SELECT are required for a decisive count.
        const string sql = """
SET NOCOUNT ON;
IF DB_NAME() <> N'db-fieldvisit-pathb-uat'
    THROW 57001, N'EXACT_TARGET_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'),0) <> 1
    SELECT N'SCHEMA_UNKNOWN_METADATA_PERMISSION' AS result;
ELSE IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'SELECT'),0) <> 1
    SELECT N'SCHEMA_UNKNOWN_VERSION_SELECT_PERMISSION' AS result;
ELSE IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')) = 69
    AND (SELECT COUNT(*) FROM dbo.SchemaVersions) = 3
    AND (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN
        (N'1.8.0-CREATE-LOWRISK',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-EXCEL-CANONICAL-STATUS')) = 3
    SELECT N'SCHEMA_69_3_VISIBLE' AS result;
ELSE SELECT N'SCHEMA_MISMATCH_VISIBLE' AS result;
""";
        try
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 6 };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            var result = await command.ExecuteScalarAsync(timeout.Token);
            var code = result as string;
            return code is "SCHEMA_UNKNOWN_METADATA_PERMISSION"
                or "SCHEMA_UNKNOWN_VERSION_SELECT_PERMISSION"
                or "SCHEMA_69_3_VISIBLE"
                or "SCHEMA_MISMATCH_VISIBLE" ? code : "SCHEMA_UNKNOWN";
        }
        catch { return "SCHEMA_UNKNOWN"; }
    }
}
