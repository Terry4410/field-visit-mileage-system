using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Does NOT execute any SQL or connect to UAT. Static read-only catalog
/// contract + conflict codes only; SQL Server integration remains a HOLD gate.
/// </summary>
public sealed class V180B3SqlSafetyRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.8.0-010")]
    [InlineData("1.8.0-012")]
    [InlineData("1.8.0-011 ")]
    public void Missing_previous_future_or_malformed_latest_version_fails_closed(string? latest)
        => Assert.Throws<InvalidOperationException>(()=>
            V180B3SqlSafetyRules.RequireLatestSchemaVersion(latest));

    [Fact]
    public void Only_exact_latest_011_is_eligible_for_further_catalog_checks()
        => V180B3SqlSafetyRules.RequireLatestSchemaVersion("1.8.0-011");

    [Fact]
    public void Readiness_probe_is_select_only_and_requires_physical_unique_indexes()
    {
        var query=V180B3SqlSafetyRules.CatalogCheckSql;
        Assert.StartsWith("SELECT",query.TrimStart(),StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OBJECT_ID(N'dbo.SchemaVersions'",query);
        Assert.Contains("OBJECT_ID(N'dbo.ChangeRequests'",query);
        Assert.Contains("OBJECT_ID(N'dbo.ChangeRequestEvents'",query);
        Assert.Contains("system_type_id=189",query);
        Assert.Contains(V180B3SqlSafetyRules.RequestPublicIdIndex,query);
        Assert.Contains(V180B3SqlSafetyRules.PendingRequestIndex,query);
        Assert.Contains(V180B3SqlSafetyRules.DecisionKeyIndex,query);
        Assert.Contains("i.is_unique=1",query);
        Assert.Contains("i.has_filter=1",query);
        Assert.Contains("key_ordinal=3",query);
        Assert.DoesNotContain("CREATE TABLE",query,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE",query,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO",query,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY AppliedAt DESC, VersionNumber DESC",
            V180B3SqlSafetyRules.LatestSchemaVersionSql);
    }

    [Theory]
    [InlineData(2601,"Cannot insert duplicate key row in object 'dbo.ChangeRequests' with unique index 'UX_B3_ChangeRequests_Org_Entity_Pending'.","B3_PENDING_REQUEST_EXISTS")]
    [InlineData(2627,"Violation of UNIQUE KEY constraint 'UX_B3_ChangeRequestEvents_DecisionKey'.","B3_DECISION_KEY_REPLAY")]
    public void Named_B3_uniqueness_conflicts_are_classified_for_409(
        int number,string message,string code)
    {
        Assert.Equal(code,V180B3SqlSafetyRules.RecognizedUniqueConflict(number,message));
        Assert.True(V180B3SqlSafetyRules.IsConflictCode(code));
    }

    [Theory]
    [InlineData(2627,"Other unique constraint.")]
    [InlineData(2601,"UX_B3_ChangeRequests_RequestPublicId")]
    [InlineData(547,"UX_B3_ChangeRequests_Org_Entity_Pending")]
    [InlineData(1205,"UX_B3_ChangeRequestEvents_DecisionKey")]
    [InlineData(2601,null)]
    public void Unrecognized_errors_never_become_expected_B3_conflicts(int number,string? message)
        => Assert.Null(V180B3SqlSafetyRules.RecognizedUniqueConflict(number,message));

    [Theory]
    [InlineData("B3_DISABLED")]
    [InlineData("B3_SCHEMA_NOT_VERIFIED")]
    public void Disabled_or_unverifiable_schema_maps_only_to_service_unavailable(string code)
    {
        Assert.True(V180B3SqlSafetyRules.IsUnavailableCode(code));
        Assert.False(V180B3SqlSafetyRules.IsConflictCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("B3_PENDING_REQUEST_EXISTS")]
    [InlineData("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED")]
    [InlineData("B3_RANDOM_ERROR")]
    public void Unrelated_B3_failures_do_not_impersonate_readiness_errors(string? code)
        => Assert.False(V180B3SqlSafetyRules.IsUnavailableCode(code));

    [Fact]
    public void Unapproved_apply_executor_is_explicitly_forbidden_not_a_validation_error()
    {
        Assert.True(V180B3SqlSafetyRules.IsForbiddenCode(
            "B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED"));
        foreach(var other in new[]{
            "B3_DISABLED","B3_SCHEMA_NOT_VERIFIED",
            "B3_PENDING_REQUEST_EXISTS","B3_DECISION_KEY_REPLAY",
            "ROWVERSION_CONFLICT"})
            Assert.False(V180B3SqlSafetyRules.IsForbiddenCode(other));
        Assert.False(V180B3SqlSafetyRules.IsForbiddenCode(null));
    }

    [Fact]
    public void Non_conflict_message_is_not_an_http409_exception()
    {
        Assert.False(V180B3SqlSafetyRules.IsConflictCode("B3_DISABLED"));
        Assert.False(V180B3SqlSafetyRules.IsConflictCode("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED"));
        Assert.False(V180B3SqlSafetyRules.IsConflictCode(null));
    }

    [Fact]
    public void Unrelated_DbUpdateException_is_never_swallowed()
    {
        // Helper must not interpret a different provider failure as a
        // duplicate key or an authorization/approval success.
        var error=new DbUpdateException("Not a SqlClient duplicate",new Exception("Other"));
        V180B3SqlSafetyRules.RethrowRecognizedUniqueConflict(error);
    }
}
