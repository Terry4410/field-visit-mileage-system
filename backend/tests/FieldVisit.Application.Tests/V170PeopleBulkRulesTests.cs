using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170PeopleBulkRulesTests
{
    [Fact]
    public void Internal_normalizes_roles_and_team_codes()
    {
        var row =
            V170PeopleBulkRules.NormalizeInternal(
                Internal() with
                {
                    Roles = "Visitor; ADMIN",
                    TeamCodes = "team-n01、TEAM-N02",
                    PrimaryTeamCode = "team-n01"
                });

        Assert.Equal(
            new[] { "admin", "visitor" },
            row.Roles);

        Assert.Equal(
            new[] { "TEAM-N01", "TEAM-N02" },
            row.TeamCodes);

        Assert.Equal(
            "TEAM-N01",
            row.PrimaryTeamCode);
    }

    [Fact]
    public void Internal_rejects_supervisor_role()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeInternal(
                    Internal() with
                    {
                        Roles = "supervisor"
                    }));
    }

    [Fact]
    public void Internal_visitor_requires_team()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeInternal(
                    Internal() with
                    {
                        Roles = "visitor",
                        TeamCodes = "",
                        PrimaryTeamCode = ""
                    }));
    }

    [Fact]
    public void Internal_primary_must_be_in_team_codes()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeInternal(
                    Internal() with
                    {
                        TeamCodes = "TEAM-N01",
                        PrimaryTeamCode = "TEAM-N02"
                    }));
    }

    [Fact]
    public void External_organization_scope_rejects_team_codes()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeExternal(
                    External() with
                    {
                        ScopeType = "Organization",
                        ScopeTeamCodes = "TEAM-N01"
                    }));
    }

    [Fact]
    public void External_team_scope_requires_team_codes()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeExternal(
                    External() with
                    {
                        ScopeType = "Team",
                        ScopeTeamCodes = ""
                    }));
    }

    [Fact]
    public void Entra_identity_requires_tenant_and_object_id()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeInternal(
                    Internal() with
                    {
                        IdentityProvider = "EntraId",
                        EntraTenantId =
                            "11111111-1111-1111-1111-111111111111",
                        EntraObjectId = ""
                    }));
    }

    [Fact]
    public void Demo_identity_rejects_entra_binding()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeInternal(
                    Internal() with
                    {
                        IdentityProvider = "Demo",
                        EntraTenantId =
                            "11111111-1111-1111-1111-111111111111"
                    }));
    }

    [Fact]
    public void Boolean_parser_accepts_localized_values()
    {
        Assert.True(
            V170PeopleBulkRules.ParseBoolean(
                "啟用",
                "AdminEnabled"));

        Assert.False(
            V170PeopleBulkRules.ParseBoolean(
                "否",
                "AdminEnabled"));
    }

    [Fact]
    public void External_change_date_must_be_inside_authorization()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkRules.NormalizeExternal(
                    External() with
                    {
                        AuthorizationFrom = "2026-08-01",
                        AuthorizationTo = "2026-08-31",
                        ChangeEffectiveFrom = "2026-09-01"
                    }));
    }

    private static V170InternalAuthorizationRawRow
        Internal()
        => new(
            RowNumber: 2,
            UserCode: "visitor01",
            EmployeeNo: "000001",
            DisplayName: "王小明",
            Email: "visitor01@example.com",
            EmploymentStatus: "Active",
            AdminEnabled: "Y",
            Roles: "visitor",
            TeamCodes: "TEAM-N01",
            PrimaryTeamCode: "TEAM-N01",
            ChangeEffectiveFrom: "2026-08-14",
            IdentityProvider: "Demo",
            EntraTenantId: "",
            EntraObjectId: "");

    private static V170ExternalSupervisorRawRow
        External()
        => new(
            RowNumber: 2,
            UserCode: "EXT-001",
            DisplayName: "外部督導",
            Email: "supervisor@example.com",
            ExternalOrganization: "政府機關",
            ExternalTitle: "督導",
            AuthorizationFrom: "2026-08-01",
            AuthorizationTo: "2026-12-31",
            AdminEnabled: "Y",
            ScopeType: "Organization",
            ScopeTeamCodes: "",
            CanExportExcel: "N",
            CanExportPdf: "N",
            IdentityProvider: "Demo",
            EntraTenantId: "",
            EntraObjectId: "",
            ChangeEffectiveFrom: "2026-08-14");
}
