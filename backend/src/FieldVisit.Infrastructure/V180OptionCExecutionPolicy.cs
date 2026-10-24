using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Explicit target boundary for the non-deploy Option C engine. This batch
/// permits only the Recovery workflow's disposable local SQL target. UAT
/// requires a separate enablement plus exact server/database and verified
/// schema evidence; Production is always denied by this candidate.
/// </summary>
public static class V180OptionCExecutionPolicy
{
    public const string ModeVariable = "OPTIONC_EXECUTION_MODE";
    public const string IsolatedSqlTestMode = "IsolatedSqlTest";

    public static void RequireIsolatedSqlTest(SqlConnection connection)
        => RequireIsolatedSqlTest(connection,
            Environment.GetEnvironmentVariable(ModeVariable));

    public static void RequireIsolatedSqlTest(SqlConnection connection,
        string? executionMode)
    {
        if (connection.State != System.Data.ConnectionState.Open
            || !string.Equals(executionMode,
                IsolatedSqlTestMode, StringComparison.Ordinal)
            || !connection.Database.StartsWith("OptionCIsolated_", StringComparison.Ordinal)
            || !IsLoopback(connection.DataSource))
            throw new InvalidOperationException("OPTIONC_EXECUTION_TARGET_DENIED");
    }

    public static void RequireFutureUat(bool enabled, bool exactTargetMatched,
        bool schemaPreflightPassed)
    {
        // The route is deliberately not implemented in this batch. An
        // environment name or a feature flag alone is never sufficient.
        if (!enabled || !exactTargetMatched || !schemaPreflightPassed)
            throw new InvalidOperationException("OPTIONC_UAT_EXECUTION_NOT_AUTHORIZED");
        throw new InvalidOperationException("OPTIONC_UAT_EXECUTION_BOUNDARY_NOT_IMPLEMENTED");
    }

    public static void RequireProduction()
        => throw new InvalidOperationException("OPTIONC_PRODUCTION_EXECUTION_NOT_AUTHORIZED");

    private static bool IsLoopback(string dataSource)
    {
        var host = new SqlConnectionStringBuilder { DataSource = dataSource }.DataSource;
        host = host.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            host = host[4..];
        var comma = host.LastIndexOf(',');
        if (comma >= 0) host = host[..comma];
        var slash = host.LastIndexOf('\\');
        if (slash >= 0) host = host[(slash + 1)..];
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.Ordinal)
            || host.Equals("::1", StringComparison.Ordinal);
    }
}
