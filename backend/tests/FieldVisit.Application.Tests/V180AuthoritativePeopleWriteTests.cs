using System.Reflection;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180AuthoritativePeopleWriteTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public void Identity_bridge_prefers_profile_and_supports_exact_legacy_fallback()
    {
        Assert.Equal(12, V180IdentityBridgeRules.ResolveEmploymentId([12], [12]));
        Assert.Equal(12, V180IdentityBridgeRules.ResolveEmploymentId([], [12]));
    }

    [Fact]
    public void Mismatching_or_ambiguous_identity_bridge_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => V180IdentityBridgeRules.ResolveEmploymentId([12], [13]));
        Assert.Throws<InvalidOperationException>(() => V180IdentityBridgeRules.ResolveEmploymentId([12, 13], []));
    }

    [Fact]
    public void Missing_stable_bridge_never_falls_back_to_name_or_email() =>
        Assert.Throws<InvalidOperationException>(() => V180IdentityBridgeRules.ResolveEmploymentId([], []));

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public void Invalid_version_is_rejected(string version) =>
        Assert.Throws<InvalidOperationException>(() => V180PeopleAccessRules.DecodeVersion(version));

    [Fact]
    public void Eight_byte_rowversion_is_accepted()
    {
        var bytes = Enumerable.Range(1, 8).Select(x => (byte)x).ToArray();
        Assert.Equal(bytes, V180PeopleAccessRules.DecodeVersion(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Retroactive_change_requires_confirmation()
    {
        var request = ValidRequest(change: Today.AddDays(-1), confirm: false);
        Assert.Throws<InvalidOperationException>(() => V180PeopleAccessRules.Normalize(request, Today));
        Assert.Equal(Today.AddDays(-1), V180PeopleAccessRules.Normalize(request with { ConfirmRetroactive = true }, Today).ChangeEffectiveFrom);
    }

    [Fact]
    public void Exactly_one_primary_team_is_required()
    {
        Assert.Throws<InvalidOperationException>(() => V180PeopleAccessRules.Normalize(
            ValidRequest(teams: [new(1, false), new(2, false)]), Today));
        Assert.Throws<InvalidOperationException>(() => V180PeopleAccessRules.Normalize(
            ValidRequest(teams: [new(1, true), new(2, true)]), Today));
    }

    [Theory]
    [InlineData("visitor")]
    [InlineData("leader")]
    public void Visitor_or_leader_requires_team(string role) =>
        Assert.Throws<InvalidOperationException>(() => V180PeopleAccessRules.Normalize(
            ValidRequest(roles: [role], teams: []), Today));

    [Fact]
    public void Internal_roles_are_normalized_and_supervisor_is_rejected()
    {
        var normalized = V180PeopleAccessRules.Normalize(ValidRequest(roles: ["ADMIN", "visitor"]), Today);
        Assert.Equal(new[] { "admin", "visitor" }, normalized.Roles);
        Assert.Throws<InvalidOperationException>(() => V180PeopleAccessRules.Normalize(
            ValidRequest(roles: ["supervisor"]), Today));
    }

    [Fact]
    public void Role_and_membership_effective_boundaries_are_inclusive()
    {
        var role = new EmploymentRoleAssignment { RoleId = 1, EffectiveFrom = Today, EffectiveTo = Today };
        var membership = new TeamMembership { TeamId = 1, EffectiveFrom = Today, EffectiveTo = Today, IsPrimary = true };
        Assert.Single(V180AsOfRules.EmploymentRoles([role], Today));
        Assert.Same(membership, V180AsOfRules.PrimaryTeam([membership], Today));
    }

    [Fact]
    public void New_endpoint_is_employment_based_and_admin_scoped()
    {
        var method = typeof(V180OrganizationPeopleAdminController).GetMethod("UpdateAccess")!;
        var route = Assert.Single(method.GetCustomAttributes<HttpPutAttribute>()).Template;
        Assert.Equal("people/{employmentId:long}/access", route);
    }

    [Fact]
    public void Both_legacy_access_routes_remain_unchanged()
    {
        var peopleMethod = typeof(V170PeopleAdminController).GetMethod("UpdateInternalUserAccess")!;
        Assert.Equal("internal-users/{userId:int}/access", Assert.Single(peopleMethod.GetCustomAttributes<HttpPutAttribute>()).Template);
        var oldMethod = typeof(V160FinalController).GetMethod("SaveUserAccess")!;
        Assert.Equal("admin/users/{userId:int}/access", Assert.Single(oldMethod.GetCustomAttributes<HttpPutAttribute>()).Template);
    }

    [Fact]
    public void Legacy_writers_delegate_to_the_authoritative_writer()
    {
        var v170 = Source("backend/src/FieldVisit.Infrastructure/V170PeopleAdminWriter.cs");
        var old = Source("backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        Assert.Contains("v180Writer.UpdateAccessFromLegacyAsync", v170);
        Assert.Contains("v180PeopleWriter.UpdateAccessAsync", old);
    }

    [Fact]
    public void Compatibility_projection_covers_all_required_v17_structures()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        foreach (var name in new[] { "UserEmploymentPeriods", "UserRoleAssignments", "UserTeamAssignments",
                     "UserRoles", "UserTeamScopes", "user.TeamId" })
            Assert.Contains(name, source);
    }

    [Fact]
    public void Leader_assignments_are_derived_for_every_current_membership()
    {
        Assert.Equal(new[] { 10, 20 }, V180LeaderAssignmentRules.DeriveTeamIds(
            ["leader"], [new(20, false), new(10, true)]));
    }

    [Fact]
    public void Removing_leader_role_removes_all_derived_assignments() =>
        Assert.Empty(V180LeaderAssignmentRules.DeriveTeamIds(
            ["visitor"], [new(10, true)]));

    [Fact]
    public void Removing_a_team_removes_its_derived_leader_assignment() =>
        Assert.Equal(new[] { 20 }, V180LeaderAssignmentRules.DeriveTeamIds(
            ["leader"], [new(20, true)]));

    [Fact]
    public void Leader_assignment_periods_are_closed_before_replacement()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        Assert.Contains("PrepareEffectiveRowsAsync(db.TeamLeaderAssignments", source);
        Assert.Contains("PreviousDay(effectiveFrom)", source);
        Assert.Contains("V180LeaderAssignmentRules.DeriveTeamIds", source);
    }

    [Fact]
    public void External_creation_builds_person_employment_and_supervisor_role_without_membership()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        var create = Slice(source, "CreateExternalIdentityAsync", "UpdateExternalIdentityAsync");
        Assert.Contains("new Person", create);
        Assert.Contains("new Employment", create);
        Assert.Contains("new EmploymentRoleAssignment", create);
        Assert.DoesNotContain("TeamMemberships.Add", create);
    }

    [Fact]
    public void External_create_uses_authoritative_role_then_projects_compatibility()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V170PeopleAdminWriter.cs");
        var create = Slice(source, "CreateExternalSupervisorAsync", "UpdateExternalSupervisorAsync");
        Assert.Contains("CreateExternalIdentityAsync", create);
        Assert.Contains("ProjectExternalCompatibilityAsync", create);
        Assert.DoesNotContain("db.UserRoleAssignments", create);
        Assert.DoesNotContain("db.UserRoles.AddAsync", create);
    }

    [Fact]
    public void External_update_rejects_team_membership()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        Assert.Contains("External Supervisor 不得具有 TeamMembership", source);
    }

    [Fact]
    public void External_scope_and_capability_models_are_preserved()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V170PeopleAdminWriter.cs");
        Assert.Contains("db.UserDataScopes", source);
        Assert.Contains("db.UserCapabilities", source);
        Assert.Contains("ProjectExternalCompatibilityAsync", source);
    }

    [Fact]
    public void External_update_does_not_rewrite_v17_role_assignment_independently()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V170PeopleAdminWriter.cs");
        var update = Slice(source, "UpdateExternalSupervisorAsync", "UpdateInternalUserAccessAsync");
        Assert.DoesNotContain("roleAssignment.EffectiveFrom", update);
        Assert.Contains("UpdateExternalIdentityAsync", update);
        Assert.Contains("ProjectExternalCompatibilityAsync", update);
    }

    [Fact]
    public void Bulk_confirm_still_delegates_through_the_authoritative_adapter()
    {
        var bulk = Source("backend/src/FieldVisit.Infrastructure/V170PeopleBulkWorkbookService.cs");
        Assert.Contains("IV170PeopleAdminWriter writer", bulk);
        Assert.Contains("UpdateInternalUserAccessAsync", bulk);
        Assert.Contains("CreateExternalSupervisorAsync", bulk);
    }

    [Fact]
    public void Future_schedules_fail_closed_before_replacement()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        Assert.Contains("已有較晚生效的", source);
        Assert.Contains("PreviousDay(effectiveFrom)", source);
        Assert.Contains("db.RemoveRange(sameStart)", source);
    }

    [Fact]
    public void Stale_rowversion_has_deterministic_conflict_marker()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        Assert.Contains("employment.RowVersion.SequenceEqual(expectedVersion)", source);
        Assert.Contains("ROWVERSION_CONFLICT", source);
    }

    [Fact]
    public void Projection_is_inside_the_same_transaction_and_precedes_commit()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        var write = Slice(source,
            "private async Task<V180PeopleAccessWriteResult> UpdateCoreAsync",
            "private async Task ProjectCompatibilityAsync");
        Assert.True(write.IndexOf("BeginTransactionAsync", StringComparison.Ordinal) <
                    write.IndexOf("ProjectCompatibilityAsync", StringComparison.Ordinal));
        Assert.True(write.IndexOf("ProjectCompatibilityAsync", StringComparison.Ordinal) <
                    write.IndexOf("CommitAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_contains_stable_ids_dates_roles_teams_and_versions()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleWriter.cs");
        foreach (var value in new[] { "PersonId", "EmploymentId", "LegacyUserId", "ChangeEffectiveFrom",
                     "Roles", "TeamMemberships", "VersionBefore", "VersionAfter" })
            Assert.Contains(value, source);
    }

    private static V180UpdatePeopleAccessRequest ValidRequest(
        IReadOnlyList<string>? roles = null, IReadOnlyList<V180TeamMembershipWriteDto>? teams = null,
        DateOnly? change = null, bool confirm = false) => new(
            roles ?? ["visitor"], teams ?? [new(1, true)], true,
            change ?? Today, confirm, Convert.ToBase64String(new byte[8]));

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "FieldVisit.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        return source[from..to];
    }
}
