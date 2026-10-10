using System.Text.RegularExpressions;
using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Candidate 011 catalog contract inspected only as a SQL string.
/// Does not execute SQL, create any table, or connect to any database.
/// </summary>
public sealed class V180B3ReadinessColumnContractTests
{
    [Fact]
    public void Sql_server_column_type_width_nullable_and_scale_must_all_match()
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Contains("sys.columns c",sql);
        Assert.Contains("c.system_type_id=required.SqlType",sql);
        Assert.Contains("c.max_length=required.MaxBytes",sql);
        Assert.Contains("c.is_nullable=required.IsNullable",sql);
        Assert.Contains("c.scale=required.ScaleValue",sql);
        Assert.Contains(")=28",sql);
        Assert.Equal(28,Regex.Matches(sql,@"\(N'ChangeRequests?',N'").Count
            +Regex.Matches(sql,@"\(N'ChangeRequestEvents',N'").Count - 7);
    }

    [Theory]
    [InlineData("ChangeRequests","EntityKind",231,80,0,-1)]
    [InlineData("ChangeRequests","EntityId",231,160,0,-1)]
    [InlineData("ChangeRequests","OperationCode",231,160,0,-1)]
    [InlineData("ChangeRequests","RiskCode",231,40,0,-1)]
    [InlineData("ChangeRequests","Status",231,60,0,-1)]
    [InlineData("ChangeRequests","ExpectedEntityRowVersion",165,8,1,-1)]
    [InlineData("ChangeRequests","ProposedJson",231,-1,0,-1)]
    [InlineData("ChangeRequests","ReviewedAt",42,-2,1,3)]
    [InlineData("ChangeRequests","SubmittedAt",42,-2,0,3)]
    [InlineData("ChangeRequests","ReviewReason",231,2000,1,-1)]
    [InlineData("ChangeRequestEvents","EventType",231,80,0,-1)]
    [InlineData("ChangeRequestEvents","OccurredAt",42,-2,0,3)]
    [InlineData("ChangeRequestEvents","DetailsJson",231,-1,1,-1)]
    public void Risk_sensitive_column_catalog_shapes_are_explicit(
        string table,string column,int type,int byteWidth,int nullable,int scale)
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Contains($"(N'{table}',N'{column}',{type},{byteWidth},{nullable},{scale})",sql);
    }

    [Fact]
    public void Catalog_query_remains_single_select_without_ddl_dml()
    {
        var query=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.StartsWith("SELECT CAST(CASE WHEN",query.TrimStart(),StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THEN 1 ELSE 0 END AS int) AS Value",query);
        Assert.DoesNotContain("CREATE TABLE",query,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO",query,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE",query,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.",query,StringComparison.OrdinalIgnoreCase);
    }
}
