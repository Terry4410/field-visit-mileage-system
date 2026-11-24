using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Explicit target boundary. Isolated test entry points remain isolated.
/// The formal-location surface may admit only the exact Path B UAT SQL
/// target with its explicit execution mode; callers still enforce the
/// feature flag, catalog evidence and live business authorization.
/// Production is not admitted by this candidate.
/// </summary>
public static class V180OptionCExecutionPolicy
{
    public const string ModeVariable = "OPTIONC_EXECUTION_MODE";
    public const string IsolatedSqlTestMode = "IsolatedSqlTest";
    public const string PathBUatFormalLocationsMode = "PathBUatFormalLocations";
    private const string PathBHost = "sql-fieldvisit-jpe-pathb-uat.database.windows.net";
    private const string PathBDatabase = "db-fieldvisit-pathb-uat";

    internal static bool IsPathBUatMode =>
        string.Equals(Environment.GetEnvironmentVariable(ModeVariable),
            PathBUatFormalLocationsMode, StringComparison.Ordinal);

    internal static bool MatchesPathBUatTarget(string? mode, string dataSource,
        string database, string connectionString)
    {
        if (!string.Equals(mode, PathBUatFormalLocationsMode, StringComparison.Ordinal)
            || !string.Equals(database, PathBDatabase, StringComparison.Ordinal)
            || !ExactPathBHost(dataSource)) return false;
        var builder = new SqlConnectionStringBuilder(connectionString);
        var encryption = builder.Encrypt.ToString();
        return ExactPathBHost(builder.DataSource)
            && string.Equals(builder.InitialCatalog, PathBDatabase, StringComparison.Ordinal)
            && !builder.TrustServerCertificate
            && (encryption.Equals("True", StringComparison.OrdinalIgnoreCase)
                || encryption.Equals("Mandatory", StringComparison.OrdinalIgnoreCase)
                || encryption.Equals("Strict", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrEmpty(builder.FailoverPartner);
    }

    private static bool ExactPathBHost(string value)
    {
        if (value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        return value.Equals(PathBHost, StringComparison.OrdinalIgnoreCase)
            || value.Equals(PathBHost + ",1433", StringComparison.OrdinalIgnoreCase);
    }

    // Only formal-location services and their internal writers use this
    // boundary. The service feature flag must also be enabled. Excel and
    // merge entry points continue to call RequireIsolatedSqlTest.
    internal static void RequireFormalLocationTarget(SqlConnection connection)
    {
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (string.Equals(mode, IsolatedSqlTestMode, StringComparison.Ordinal))
        {
            RequireIsolatedSqlTest(connection, mode);
            return;
        }
        if (connection.State != System.Data.ConnectionState.Open
            || !MatchesPathBUatTarget(mode, connection.DataSource,
                connection.Database, connection.ConnectionString))
            throw new InvalidOperationException("OPTIONC_EXECUTION_TARGET_DENIED");
    }

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
