using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Offline EF model / SQL-catalog text contract only. Does not open SQL Server,
/// execute DDL, or claim future migration is authorized.
/// </summary>
public sealed class V180B3CheckConstraintContractsTests
{
    private static AppDbContext ModelOnly() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;TrustServerCertificate=True;")
            .Options);

    [Theory]
    [InlineData("ChangeRequests","CK_B3_ChangeRequests_KnownCodes")]
    [InlineData("ChangeRequests","CK_B3_ChangeRequests_KnownStatus")]
    [InlineData("ChangeRequests","CK_B3_ChangeRequests_ExpectedLocationVersion")]
    [InlineData("ChangeRequests","CK_B3_ChangeRequests_ProposedJson")]
    [InlineData("ChangeRequests","CK_B3_ChangeRequests_ReviewState")]
    [InlineData("ChangeRequestEvents","CK_B3_ChangeRequestEvents_EventType")]
    [InlineData("ChangeRequestEvents","CK_B3_ChangeRequestEvents_DetailsJson")]
    public void Required_named_constraints_present_in_candidate_EF_and_readiness(
        string table,string constraint)
    {
        using var db=ModelOnly();
        var model=db.Model.GetEntityTypes().Single(x=>x.GetTableName()==table);
        var definition=model.GetCheckConstraints().Single(x=>x.Name==constraint).Sql;
        Assert.False(string.IsNullOrWhiteSpace(definition));
        var catalog=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Contains($"N'{table}',N'{constraint}'",catalog);
        Assert.Contains("cc.is_disabled=0 AND cc.is_not_trusted=0",catalog);
        Assert.Contains("sys.check_constraints",catalog);
    }

    [Fact]
    public void All_seven_required_constraints_are_checked_without_running_SQL()
    {
        var sql=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.Contains(")=7",sql);
        Assert.Contains("cc.parent_object_id=OBJECT_ID",sql);
        Assert.Contains("LOWER(cc.definition)",sql);
        Assert.DoesNotContain("ALTER TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC ",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Proposed_checks_preserve_review_and_audit_state_invariants()
    {
        using var db=ModelOnly();
        var requests=db.Model.FindEntityType(typeof(V180B3ChangeRequest))!;
        var checks=requests.GetCheckConstraints().ToArray();
        Assert.Equal(5,checks.Length);
        var status=checks.Single(x=>x.Name=="CK_B3_ChangeRequests_KnownStatus").Sql;
        Assert.Contains("Pending",status);
        Assert.Contains("Rejected",status);
        var review=checks.Single(x=>x.Name=="CK_B3_ChangeRequests_ReviewState").Sql;
        Assert.Contains("ReviewedByUserId",review);
        Assert.Contains("ReviewedAt",review);
        Assert.Contains("AppliedAt",review);
        var events=db.Model.FindEntityType(typeof(V180B3ChangeEvent))!;
        Assert.Equal(2,events.GetCheckConstraints().Count());
        Assert.Contains("ISJSON",events.GetCheckConstraints().Single(x=>
            x.Name=="CK_B3_ChangeRequestEvents_DetailsJson").Sql);
    }
}
