using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Nondeploy target boundary only. A target match is not authorization.
/// No UAT execution is permitted until a separately reviewed trust mechanism
/// binds schema, effective identity and the actual runtime to this connection.
/// </summary>
public static class V180OptionCUatTargetBoundary
{
    private const string Server = "sql-fieldvisit-jpe-uat.database.windows.net";
    private const string Database = "db-fieldvisit-uat";

    public static bool IsExpectedEndpoint(string? server, string? database) =>
        string.Equals(server, Server, StringComparison.OrdinalIgnoreCase)
        && string.Equals(database, Database, StringComparison.OrdinalIgnoreCase);

    public static void RequireUat(SqlConnection? connection)
    {
        if (connection is null || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("OPTIONC_UAT_CONNECTION_DENIED");

        if (!IsExpectedEndpoint(connection.DataSource, connection.Database))
            throw new InvalidOperationException("OPTIONC_UAT_TARGET_DENIED");

        // A connection string, flags or caller claims cannot prove identity,
        // schema or runtime provenance. This component never grants access.
        throw new InvalidOperationException("OPTIONC_UAT_TRUST_NOT_AVAILABLE");
    }
}
