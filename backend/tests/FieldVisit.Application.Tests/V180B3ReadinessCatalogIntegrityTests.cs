using System.Text.RegularExpressions;
using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>Read-only catalog SQL text contract. Never connects to any DB.
/// Physical SQL Server verification is separately authorized and still HOLD.</summary>
public sealed class V180B3ReadinessCatalogIntegrityTests
{
    [Fact]
    public void Three_unique_index_gates_reject_disabled_or_hypothetical_indexes()
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Equal(3,Regex.Matches(sql,
            @"i\.is_disabled=0 AND i\.is_hypothetical=0").Count);
        Assert.Contains(V180B3SqlSafetyRules.RequestPublicIdIndex,sql);
        Assert.Contains(V180B3SqlSafetyRules.PendingRequestIndex,sql);
        Assert.Contains(V180B3SqlSafetyRules.DecisionKeyIndex,sql);
    }

    [Theory]
    [InlineData("ChangeRequests","OrganizationId","Organizations","OrganizationId")]
    [InlineData("ChangeRequests","TeamId","Teams","TeamId")]
    [InlineData("ChangeRequests","RequestedByUserId","Users","UserId")]
    [InlineData("ChangeRequests","ReviewedByUserId","Users","UserId")]
    [InlineData("ChangeRequestEvents","ChangeRequestId","ChangeRequests","ChangeRequestId")]
    [InlineData("ChangeRequestEvents","ActorUserId","Users","UserId")]
    public void Required_FKs_have_exact_child_and_parent_columns(
        string table,string child,string referenced,string parent)
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Matches(
            $@"\(N'{table}',N'{child}',N'{referenced}',N'{parent}'\)",sql);
        Assert.Contains("fk.is_disabled=0 AND fk.is_not_trusted=0",sql);
        Assert.Contains("fk.delete_referential_action=0",sql);
        Assert.Contains("parent_column.name=required.ParentColumn",sql);
        Assert.Contains("reference_column.name=required.ReferencedColumn",sql);
        Assert.Contains("other.constraint_column_id>1",sql);
        Assert.Contains(")=6",sql);
    }

    [Fact]
    public void Enabled_table_trigger_blocks_B3_readiness_even_if_indexes_are_valid()
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Contains("sys.triggers tr",sql);
        Assert.Contains("tr.is_disabled=0",sql);
        Assert.Contains("OBJECT_ID(N'dbo.ChangeRequests',N'U')",sql);
        Assert.Contains("OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')",sql);
        Assert.Contains("AND NOT EXISTS",sql);
        Assert.DoesNotContain("DISABLE TRIGGER",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalog_rejects_future_apply_return_or_cancel_status_extensions()
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Contains("CK_B3_ChangeRequests_KnownStatus",sql);
        Assert.Contains("LOWER(cc.definition) NOT LIKE N'%applied%'",sql);
        Assert.Contains("LOWER(cc.definition) NOT LIKE N'%returned%'",sql);
        Assert.Contains("LOWER(cc.definition) NOT LIKE N'%cancelled%'",sql);
    }

    [Fact]
    public void Readiness_probe_is_a_select_only_fail_closed_check()
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql.TrimStart();
        Assert.StartsWith("SELECT CAST(CASE WHEN",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THEN 1 ELSE 0 END AS int) AS Value",sql);
        Assert.DoesNotContain("EXEC ",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO",sql,StringComparison.OrdinalIgnoreCase);
    }
}
