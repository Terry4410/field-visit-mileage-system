using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180LocationGovernanceRuntimeTests
{
    private static readonly string ValidVersion=Convert.ToBase64String(new byte[8]);

    [Fact]
    public void Governance_strings_are_trimmed_and_blank_values_become_null()
    {
        var row=V180LocationGovernanceRules.Normalize(10,new(" 12345678 "," note ",null," ignored ",ValidVersion));
        Assert.Equal("12345678",row.TaxId);Assert.Equal("note",row.MasterNote);Assert.Null(row.DuplicateOfLocationId);Assert.Null(row.DuplicateReason);
        Assert.Null(V180LocationGovernanceRules.NormalizeOptional("   "));
    }

    [Fact]
    public void Duplicate_reference_requires_reason_and_cannot_target_self()
    {
        Assert.Throws<InvalidOperationException>(()=>V180LocationGovernanceRules.Normalize(10,new(null,null,10,"duplicate",ValidVersion)));
        Assert.Throws<InvalidOperationException>(()=>V180LocationGovernanceRules.Normalize(10,new(null,null,11,"  ",ValidVersion)));
        var row=V180LocationGovernanceRules.Normalize(10,new(null,null,11," evidence ",ValidVersion));
        Assert.Equal(11,row.DuplicateOfLocationId);Assert.Equal("evidence",row.DuplicateReason);
    }

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("not-base64")][InlineData("AQ==")]
    public void Rowversion_is_required_valid_base64_and_eight_bytes(string? value) =>
        Assert.Throws<InvalidOperationException>(()=>V180LocationGovernanceRules.RequireRowVersion(value));

    [Fact]
    public void Governance_route_is_admin_only_and_soft_delete_requires_query_rowversion()
    {
        var source=Source("backend/src/FieldVisit.Api/V160FinalController.cs");
        Assert.Contains("managed-locations/{locationId:int}/governance",source);
        Assert.Contains("ManagedLocationGovernanceRequest",source);
        Assert.Contains("[Authorize(Roles = \"admin\")]",source);
        Assert.Contains("DeactivateLocation(int locationId, [FromQuery] string rowVersion",source);
        Assert.DoesNotContain("managed-locations/{locationId:int}/reactivate",source);
    }

    [Fact]
    public void Runtime_uses_authoritative_v18_admin_organization_and_denies_non_admin_shared_and_cross_org()
    {
        var source=Compact(Runtime());
        Assert.Contains("UserIdentityProfiles",source);Assert.Contains("Employments",source);Assert.Contains("EmploymentStatusPeriods",source);
        Assert.Contains("EmploymentRoleAssignments",source);Assert.Contains("RoleCode==\"admin\"",source);
        Assert.Contains("LOCATION_GOVERNANCE_ADMIN_ONLY",source);Assert.Contains("LOCATION_GOVERNANCE_SHARED_DENIED",source);Assert.Contains("LOCATION_GOVERNANCE_CROSS_ORG_DENIED",source);
        Assert.DoesNotContain("request.OrganizationId",source);Assert.DoesNotContain("Email",source);
    }

    [Fact]
    public void TaxId_is_non_unique_search_evidence_and_returns_all_scoped_matching_ids()
    {
        var runtime=Runtime();
        var migration=Source("database/migrations/1800_004_location_governance/Up.sql");
        Assert.Contains("WHERE TaxId LIKE",runtime);Assert.Contains("taxIdIds.Contains(x.LocationId)",runtime);
        Assert.DoesNotContain("FirstOrDefaultAsync(x => x.TaxId",runtime);
        Assert.DoesNotContain("UNIQUE (TaxId",migration.ToUpperInvariant());
    }

    [Fact]
    public void Duplicate_target_must_exist_be_same_org_non_global_and_never_remaps_relations()
    {
        var source=Compact(Runtime());
        Assert.Contains("LOCATION_DUPLICATE_TARGET_NOT_FOUND",source);Assert.Contains("LOCATION_DUPLICATE_TARGET_SCOPE",source);
        Assert.Contains("!duplicate.OrganizationId.HasValue||duplicate.OrganizationId.Value!=orgId",source);
        Assert.Contains("DuplicateOfLocationId={normalized.DuplicateOfLocationId}",source);
        Assert.DoesNotContain("VisitTrips",source);Assert.DoesNotContain("VisitTripSnapshots",source);Assert.DoesNotContain("ProjectLocations",source);
        Assert.DoesNotContain("UserFavoriteLocations",source);Assert.DoesNotContain("TeamLocationNotes",source);Assert.DoesNotContain("DeploymentSiteLocationAssignments.Remove",source);
    }

    [Fact]
    public void Governance_update_only_writes_governance_columns_and_uses_matching_rowversion()
    {
        var source=Compact(Runtime());
        Assert.Contains("EnsureVersion(row.RowVersion,expected)",source);
        Assert.Contains("SETTaxId={normalized.TaxId},MasterNote={normalized.MasterNote}",source);
        Assert.Contains("WHERELocationId={locationId}ANDRowVersion={expected}",source);Assert.Contains("DbUpdateConcurrencyException",source);
        var start=source.IndexOf("UPDATEdbo.LocationsSETTaxId",StringComparison.Ordinal);var end=source.IndexOf("awaitdb.Entry(row).ReloadAsync",StringComparison.Ordinal);
        Assert.True(start>=0&&end>start);var block=source[start..end];
        Assert.DoesNotContain("LocationName",block);Assert.DoesNotContain("Address",block);Assert.DoesNotContain("TeamId",block);Assert.DoesNotContain("IsActive",block);
    }

    [Fact]
    public void Ordinary_put_cannot_change_active_state_but_equal_state_remains_editable()
    {
        var source=Compact(Runtime());
        Assert.Contains("if(request.IsActive!=row.IsActive)",source);Assert.Contains("LOCATION_ACTIVE_STATE_REQUIRES_DEACTIVATION_ROUTE",source);
        Assert.DoesNotContain("row.IsActive=request.IsActive",source);
        Assert.Contains("row.LocationName=request.LocationName.Trim()",source);
    }

    [Fact]
    public void Deactivation_requires_rowversion_and_uses_inclusive_business_today_dependency_boundary()
    {
        var source=Compact(Runtime());
        Assert.Contains("RequireRowVersion(rowVersion)",source);Assert.Contains("EnsureVersion(row.RowVersion,expected)",source);
        Assert.Contains("vartoday=BusinessTime.Today",source);
        Assert.Contains("!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today",source);
        Assert.Contains("LOCATION_INACTIVATION_BLOCKED",source);
    }

    [Fact]
    public void Historical_only_dependency_is_not_in_the_blocking_predicate()
    {
        var source=Compact(Runtime());
        Assert.Contains("x.EffectiveTo.Value>=today",source);
        Assert.DoesNotContain("x.EffectiveTo.Value<today",source);
        var start=source.IndexOf("varblocked=",StringComparison.Ordinal);var end=source.IndexOf("if(blocked)",StringComparison.Ordinal);
        Assert.True(start>=0&&end>start);Assert.DoesNotContain("EffectiveFrom",source[start..end]);
    }

    [Fact]
    public void Successful_deactivation_sets_metadata_and_already_inactive_returns_before_mutation()
    {
        var source=Compact(Runtime());
        var noop=source.IndexOf("if(!row.IsActive)",StringComparison.Ordinal);
        var update=source.IndexOf("UPDATEdbo.LocationsSETIsActive=0",StringComparison.Ordinal);
        Assert.True(noop>=0&&update>noop);
        Assert.Contains("InactivatedAt=SYSUTCDATETIME()",source);Assert.Contains("InactivatedByUserId={user.UserId}",source);
        Assert.Contains("awaittx.CommitAsync(ct);return;",source);
    }

    [Fact]
    public void Da0b_location_trigger_failures_map_to_deterministic_non500_domain_failure()
    {
        var source=Compact(Runtime());
        Assert.Contains("ex.Numberis53605or53606",source);
        Assert.Contains("awaittx.RollbackAsync(ct)",source);
        Assert.Contains("thrownewInvalidOperationException(\"LOCATION_INACTIVATION_BLOCKED",source);
        var migration=Source("database/migrations/1800_004_location_governance/Up.sql");
        Assert.Contains("THROW 53605",migration);Assert.Contains("THROW 53606",migration);
    }

    [Fact]
    public void Trip_snapshot_team_note_and_permanent_delete_paths_are_not_reworked()
    {
        var runtime=Runtime();
        Assert.DoesNotContain("Snapshot",runtime);Assert.DoesNotContain("Trip",runtime);Assert.DoesNotContain("TeamLocationNote",runtime);
        var controller=Source("backend/src/FieldVisit.Api/V160FinalController.cs");Assert.Contains("managed-locations/{locationId:int}/permanent",controller);
        var service=Source("backend/src/FieldVisit.Application/V160FinalService.cs");Assert.Contains("repository.DeleteManagedLocationAsync(RequireRole(\"admin\"), id, ct)",service);
    }

    [Fact]
    public void Frontend_soft_delete_keeps_rowversion_permanent_delete_separate_and_avoids_api_cache()
    {
        var admin=Source("frontend/src/pages/AdminPage.tsx");
        Assert.Contains("`/managed-locations/${l.locationId}?rowVersion=${encodeURIComponent(l.rowVersion)}`",admin);
        Assert.Contains("`/managed-locations/${l.locationId}/permanent`",admin);
        Assert.Contains("LocationGovernanceModal",admin);
        var api=Source("frontend/src/api.ts");Assert.DoesNotContain("managedLocationVersions",api);Assert.DoesNotContain("rememberManagedLocationVersions",api);
    }

    private static string Runtime()=>Source("backend/src/FieldVisit.Infrastructure/V180ManagedLocationGovernanceRepository.cs");
    private static string Compact(string value)=>new(value.Where(c=>!char.IsWhiteSpace(c)).ToArray());
    private static string Source(string relative)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"backend","FieldVisitSystem.sln")))directory=directory.Parent;
        Assert.NotNull(directory);return File.ReadAllText(Path.Combine(directory!.FullName,relative));
    }
}
