using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Explicit target boundary. Isolated test entry points remain isolated.
/// Formal location admits the existing exact Path B target or an explicitly
/// configured corporate SQL2022 Windows service identity. No default corporate
/// admission; existing schema checks and business authorization still apply.
/// Other isolated-only operations remain denied.
/// </summary>
public static class V180OptionCExecutionPolicy
{
    public const string ModeVariable = "OPTIONC_EXECUTION_MODE";
    public const string IsolatedSqlTestMode = "IsolatedSqlTest";
    public const string PathBUatFormalLocationsMode = "PathBUatFormalLocations";
    public const string CorporateFormalLocationsMode = "CorporateFormalLocations";
    private const string CorporateEndpointVariable = "FIELDVISIT_CORPORATE_SQL_ENDPOINT";
    private const string CorporateDatabaseVariable = "FIELDVISIT_CORPORATE_SQL_DATABASE";
    private const string CorporateServerVariable = "FIELDVISIT_CORPORATE_SQL_SERVER_NAME";
    private const string CorporateLoginVariable = "FIELDVISIT_CORPORATE_SQL_LOGIN";
    private const string PathBHost = "sql-fieldvisit-jpe-pathb-uat.database.windows.net";
    private const string PathBDatabase = "db-fieldvisit-pathb-uat";

    internal static bool IsPathBUatMode =>
        string.Equals(Environment.GetEnvironmentVariable(ModeVariable),
            PathBUatFormalLocationsMode, StringComparison.Ordinal);

    internal static bool IsRestrictedFormalLocationMode =>
        IsPathBUatMode || string.Equals(Environment.GetEnvironmentVariable(ModeVariable),
            CorporateFormalLocationsMode, StringComparison.Ordinal);

    // No defaults: only an explicit IT-approved Windows-integrated endpoint
    // can be admitted. This pure predicate does not certify a live connection.
    public static bool MatchesCorporateTarget(string? mode, string? endpoint,
        string? expectedDatabase, string dataSource, string database, string connectionString)
    {
        if (mode != CorporateFormalLocationsMode ||
            string.IsNullOrWhiteSpace(endpoint) ||
            !System.Text.RegularExpressions.Regex.IsMatch(endpoint,
                @"^tcp:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+,[1-9][0-9]{0,4}$") ||
            !int.TryParse(endpoint[(endpoint.LastIndexOf(',')+1)..],out var port) || port>65535 ||
            endpoint.Contains(".database.windows.net,",StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(endpoint[4..endpoint.LastIndexOf(',')],out _) ||
            endpoint[4..].StartsWith("localhost.",StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(expectedDatabase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(expectedDatabase,@"^[A-Za-z][A-Za-z0-9_-]{0,127}$") ||
            expectedDatabase.StartsWith("OptionCIsolated_",StringComparison.OrdinalIgnoreCase) ||
            expectedDatabase.StartsWith("db-fieldvisit-",StringComparison.OrdinalIgnoreCase) ||
            new[]{"master","model","msdb","tempdb"}.Contains(expectedDatabase,StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(database,expectedDatabase,StringComparison.Ordinal) ||
            !string.Equals(dataSource,endpoint,StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            var encryption = b.Encrypt.ToString();
            return string.Equals(b.DataSource,endpoint,StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.InitialCatalog,expectedDatabase,StringComparison.Ordinal)
                && b.IntegratedSecurity && string.IsNullOrEmpty(b.UserID) && string.IsNullOrEmpty(b.Password)
                && b.Authentication == SqlAuthenticationMethod.NotSpecified
                && !b.TrustServerCertificate
                && (encryption.Equals("True",StringComparison.OrdinalIgnoreCase)
                    || encryption.Equals("Mandatory",StringComparison.OrdinalIgnoreCase)
                    || encryption.Equals("Strict",StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrEmpty(b.FailoverPartner)
                && string.IsNullOrEmpty(b.AttachDBFilename)
                && (!b.TryGetValue("Host Name In Certificate",out var certHost) ||
                    string.IsNullOrEmpty(Convert.ToString(certHost)));
        }
        catch (ArgumentException) { return false; }
    }

    // Actual SQL identity and privilege check is read-only, using the caller's
    // transaction when the publication writer already holds its locks.
    private static bool CorporateIdentityVerified(SqlConnection connection, SqlTransaction? transaction)
    {
        var expectedServer=Environment.GetEnvironmentVariable(CorporateServerVariable);
        var expectedLogin=Environment.GetEnvironmentVariable(CorporateLoginVariable);
        if (string.IsNullOrWhiteSpace(expectedServer) || string.IsNullOrWhiteSpace(expectedLogin)
            || !expectedLogin.Contains('\\') || expectedLogin.StartsWith("BUILTIN\\",StringComparison.OrdinalIgnoreCase)
            || expectedLogin.StartsWith("NT AUTHORITY\\",StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var command=new SqlCommand("""
                SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),
                       ORIGINAL_LOGIN(),SUSER_SNAME(),USER_NAME(),
                       CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion')),
                       CONVERT(int,SERVERPROPERTY(N'EngineEdition')),
                       IS_SRVROLEMEMBER(N'sysadmin'),IS_ROLEMEMBER(N'db_owner'),
                       IS_ROLEMEMBER(N'db_datareader'),IS_ROLEMEMBER(N'db_datawriter'),
                       HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),
                       HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER'),
                       HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'CONTROL'),
                       HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER')
                """,connection,transaction) { CommandTimeout=5 };
            using var reader=command.ExecuteReader();
            if (!reader.Read()) return false;
            for (int i=0;i<14;i++) if (reader.IsDBNull(i)) return false;
            return string.Equals(reader.GetString(0),expectedServer,StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(1),expectedLogin,StringComparison.OrdinalIgnoreCase)
                && string.Equals(reader.GetString(2),expectedLogin,StringComparison.OrdinalIgnoreCase)
                && !reader.GetString(3).Equals("dbo",StringComparison.OrdinalIgnoreCase)
                && reader.GetInt32(4)==16 && reader.GetInt32(5) is 2 or 3 or 4
                && Enumerable.Range(6,8).All(i=>reader.GetInt32(i)==0);
        }
        catch (SqlException) { return false; }
    }

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
    internal static void RequireFormalLocationTarget(SqlConnection connection, SqlTransaction? transaction = null)
    {
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (string.Equals(mode, IsolatedSqlTestMode, StringComparison.Ordinal))
        {
            RequireIsolatedSqlTest(connection, mode);
            return;
        }
        if (string.Equals(mode,CorporateFormalLocationsMode,StringComparison.Ordinal))
        {
            if (connection.State != System.Data.ConnectionState.Open ||
                !MatchesCorporateTarget(mode,Environment.GetEnvironmentVariable(CorporateEndpointVariable),
                    Environment.GetEnvironmentVariable(CorporateDatabaseVariable),connection.DataSource,
                    connection.Database,connection.ConnectionString) ||
                !CorporateIdentityVerified(connection,transaction))
                throw new InvalidOperationException("OPTIONC_EXECUTION_TARGET_DENIED");
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
