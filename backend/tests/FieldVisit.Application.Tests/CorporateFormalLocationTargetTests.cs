using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class CorporateFormalLocationTargetTests
{
    const string Mode = V180OptionCExecutionPolicy.CorporateFormalLocationsMode;
    const string Endpoint = "tcp:sqltest.corp.example,1433";
    const string Db = "FieldVisitCorporateTest";
    const string Valid = "Server="+Endpoint+";Database="+Db+";Integrated Security=True;Encrypt=True;TrustServerCertificate=False";
    [Fact]
    public void Exact_configured_target_only() =>
        Assert.True(V180OptionCExecutionPolicy.MatchesCorporateTarget(Mode,Endpoint,Db,Endpoint,Db,Valid));

    [Theory]
    [InlineData("PathBUatFormalLocations")]
    [InlineData("IsolatedSqlTest")]
    [InlineData("Production")]
    [InlineData("")]
    public void Other_modes_denied(string mode) =>
        Assert.False(V180OptionCExecutionPolicy.MatchesCorporateTarget(mode,Endpoint,Db,Endpoint,Db,Valid));

    [Theory]
    [InlineData("tcp:other.corp.example,1433")]
    [InlineData("tcp:sqltest.corp.example,1434")]
    [InlineData("sqltest.corp.example")]
    public void Wrong_live_endpoint_denied(string host) =>
        Assert.False(V180OptionCExecutionPolicy.MatchesCorporateTarget(Mode,Endpoint,Db,host,Db,Valid));

    [Theory]
    [InlineData("OtherDb")]
    [InlineData("fieldvisitcorporatetest")]
    public void Wrong_live_database_denied(string db) =>
        Assert.False(V180OptionCExecutionPolicy.MatchesCorporateTarget(Mode,Endpoint,Db,Endpoint,db,Valid));

    [Theory]
    [InlineData(";TrustServerCertificate=True")]
    [InlineData(";Encrypt=False")]
    [InlineData(";Integrated Security=False;User ID=sa;Password=test")]
    [InlineData(";User ID=sa")]
    [InlineData(";Failover Partner=other")]
    [InlineData(";Database=OtherDb")]
    [InlineData(";Server=tcp:other.corp.example,1433")]
    [InlineData(";Host Name In Certificate=other.corp.example")]
    public void Insecure_or_mismatched_connection_denied(string suffix) =>
        Assert.False(V180OptionCExecutionPolicy.MatchesCorporateTarget(Mode,Endpoint,Db,Endpoint,Db,Valid+suffix));

    [Theory]
    [InlineData("tcp:sql-fieldvisit-jpe-pathb-uat.database.windows.net,1433")]
    [InlineData("tcp:127.0.0.1,1433")]
    [InlineData("tcp:localhost,1433")]
    [InlineData("tcp:sqltest.corp.example,65536")]
    [InlineData("")]
    public void Uat_loopback_and_invalid_config_denied(string host) =>
        Assert.False(V180OptionCExecutionPolicy.MatchesCorporateTarget(Mode,host,Db,host,Db,
            "Server="+host+";Database="+Db+";Integrated Security=True;Encrypt=True"));

    [Theory]
    [InlineData("master")]
    [InlineData("db-fieldvisit-pathb-uat")]
    [InlineData("OptionCIsolated_FieldVisitV180Replay")]
    [InlineData("")]
    public void Reserved_or_missing_database_denied(string db) =>
        Assert.False(V180OptionCExecutionPolicy.MatchesCorporateTarget(Mode,Endpoint,db,Endpoint,db,
            "Server="+Endpoint+";Database="+db+";Integrated Security=True;Encrypt=True"));

    [Fact]
    public void Original_unconditional_production_entry_stays_denied() =>
        Assert.Throws<InvalidOperationException>(V180OptionCExecutionPolicy.RequireProduction);
}
