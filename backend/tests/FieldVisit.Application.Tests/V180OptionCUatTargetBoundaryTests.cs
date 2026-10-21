using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCUatTargetBoundaryTests
{
    [Theory]
    [InlineData("sql-fieldvisit-jpe-uat.database.windows.net", "db-fieldvisit-uat", true)]
    [InlineData("SQL-FIELDVISIT-JPE-UAT.DATABASE.WINDOWS.NET", "DB-FIELDVISIT-UAT", true)]
    [InlineData("sql-fieldvisit-jpe-uat.database.windows.net", "other", false)]
    [InlineData("localhost", "db-fieldvisit-uat", false)]
    [InlineData("sql-fieldvisit-jpe-uat.database.windows.net.evil.example", "db-fieldvisit-uat", false)]
    [InlineData("sql-fieldvisit-jpe-uat.database.windows.net,1433", "db-fieldvisit-uat", false)]
    [InlineData(null, null, false)]
    public void Endpoint_is_fixed_and_does_not_accept_aliases(string? server, string? database, bool expected)
    {
        Assert.Equal(expected, V180OptionCUatTargetBoundary.IsExpectedEndpoint(server, database));
    }

    [Fact]
    public void Matching_connection_string_without_open_connection_never_authorizes()
    {
        using var connection = new SqlConnection(
            "Server=sql-fieldvisit-jpe-uat.database.windows.net;Database=db-fieldvisit-uat;Encrypt=True");
        var error = Assert.Throws<InvalidOperationException>(() => V180OptionCUatTargetBoundary.RequireUat(connection));
        Assert.Equal("OPTIONC_UAT_CONNECTION_DENIED", error.Message);
    }

    [Fact]
    public void Missing_connection_never_authorizes()
    {
        var error = Assert.Throws<InvalidOperationException>(() => V180OptionCUatTargetBoundary.RequireUat(null));
        Assert.Equal("OPTIONC_UAT_CONNECTION_DENIED", error.Message);
    }

    [Fact]
    public void Existing_isolated_and_production_guards_remain_closed()
    {
        using var connection = new SqlConnection(
            "Server=localhost;Database=OptionCIsolated_fixture;Encrypt=True");
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection, "IsolatedSqlTest"));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCExecutionPolicy.RequireFutureUat(true, true, true));
        Assert.Throws<InvalidOperationException>(V180OptionCExecutionPolicy.RequireProduction);
    }
}
