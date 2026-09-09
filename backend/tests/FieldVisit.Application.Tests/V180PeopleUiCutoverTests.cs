using System.Reflection;
using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180PeopleUiCutoverTests
{
    [Fact]
    public void People_dto_exposes_nullable_admin_enabled_state()
    {
        var property = typeof(V180PersonRowDto).GetProperty("AdminEnabled", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(typeof(bool?), property!.PropertyType);
    }

    [Fact]
    public void Reader_resolves_admin_enabled_only_through_stable_legacy_user_bridge()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleReader.cs");
        Assert.Contains("ResolveLegacyUserId", source);
        Assert.Contains("LegacyUserId 對應不到 User 登入帳號", source);
        Assert.Contains("IDENTITY_BRIDGE_MISMATCH", source);
        Assert.Contains("adminEnabled = loginUser.IsActive", source);
        Assert.DoesNotContain("person.DisplayName ==", source);
        Assert.DoesNotContain("employment.Email ==", source);
    }

    [Fact]
    public void People_query_supports_role_account_team_and_internal_filters()
    {
        var contract = Source("backend/src/FieldVisit.Application/V180OrganizationPeopleContracts.cs");
        var reader = Source("backend/src/FieldVisit.Infrastructure/V180OrganizationPeopleReader.cs");
        Assert.Contains("string? Role = null", contract);
        Assert.Contains("bool? AdminEnabled = null", contract);
        Assert.Contains("int? TeamId = null", contract);
        Assert.Contains("bool InternalOnly = false", contract);
        Assert.Contains("q.TeamId.HasValue", reader);
        Assert.Contains("q.AdminEnabled.HasValue", reader);
        Assert.Contains("q.InternalOnly", reader);
    }

    [Fact]
    public void Admin_users_route_is_cut_over_to_v180_page()
    {
        var app = Source("frontend/src/App.tsx");
        Assert.Contains("import V180PeopleAdminPage", app);
        Assert.Contains("path=\"/admin/users\" element={role==='admin'?<V180PeopleAdminPage/>", app);
    }

    [Fact]
    public void V180_admin_people_page_reads_and_writes_by_employment_with_version()
    {
        var source = Source("frontend/src/pages/V180PeopleAdminPage.tsx");
        Assert.Contains("usePagedQuery<V180PersonRow>(\"/admin/v180/people\"", source);
        Assert.Contains("/admin/v180/people/${edit.employmentId}/access", source);
        Assert.Contains("teamMemberships:memberships", source);
        Assert.Contains("version:edit.version", source);
        Assert.Contains("changeEffectiveFrom:todayTaipei()", source);
        Assert.Contains("confirmRetroactive:false", source);
        Assert.Contains("/admin/v180/people/${edit.employmentId}`", source);
        Assert.DoesNotContain("/admin/users/${", source);
    }

    [Fact]
    public void Internal_access_ui_excludes_supervisor_and_preserves_bulk_panel()
    {
        var page = Source("frontend/src/pages/V180PeopleAdminPage.tsx");
        var helper = Source("frontend/src/v180-people-ui.ts");
        var internalLabels = Slice(helper, "export const V180_INTERNAL_ROLE_LABELS", "};");
        Assert.DoesNotContain("supervisor", internalLabels.ToLowerInvariant());
        Assert.Contains("External Supervisor 專用管理流程", page);
        Assert.Contains("<PeopleBulkPanel onConfirmed={load}/>", page);
    }

    [Fact]
    public void Rowversion_conflict_has_clear_reload_message()
    {
        var helper = Source("frontend/src/v180-people-ui.ts");
        Assert.Contains("ROWVERSION_CONFLICT", helper);
        Assert.Contains("重新整理後再試", helper);
    }

    [Fact]
    public void Team_membership_ui_reads_and_writes_v180_employment_authority()
    {
        var source = Source("frontend/src/pages/TeamManagementPage.tsx");
        Assert.Contains("usePagedQuery<V180PersonRow>(\"/admin/v180/people\"", source);
        Assert.Contains("internalOnly:true", source);
        Assert.Contains("teamId:onlyMembers?selectedTeamId:undefined", source);
        Assert.Contains("/admin/v180/people/${u.employmentId}/access", source);
        Assert.Contains("version:u.version", source);
        Assert.Contains("teamMemberships", source);
        Assert.DoesNotContain("/admin/people/internal-users/", source);
        Assert.DoesNotContain("usePagedQuery<V170PeopleRow>", source);
    }

    [Fact]
    public void Team_membership_guards_and_v18_wording_remain()
    {
        var source = Source("frontend/src/pages/TeamManagementPage.tsx");
        Assert.Contains("停用中的小組不可新增成員", source);
        Assert.Contains("const requiresTeam=v180RoleCodes(u).some(r=>[\"visitor\",\"leader\"].includes(r));", source);
        Assert.Contains("if(requiresTeam&&next.length===0)", source);
        Assert.Contains("return setMsg(`${u.displayName} 具有「外訪員／小組長」角色", source);
        Assert.Contains("主要小組", source);
        Assert.DoesNotContain("v1.7 小組成員設定", source);
        Assert.Contains("v1.8：以 Employment 與有效 TeamMembership", source);
    }

    [Fact]
    public void Team_master_ui_is_now_cut_over_to_v18_lifecycle_authority()
    {
        var source = Source("frontend/src/pages/TeamManagementPage.tsx");
        Assert.Contains("usePagedQuery<V180TeamAdmin>(\"/admin/v180/teams\"", source);
        Assert.Contains("api(\"/admin/v180/teams\"", source);
        Assert.Contains("/admin/v180/teams/${editTeam.teamId}", source);
        Assert.Contains("/admin/v180/teams/${t.teamId}/deactivate", source);
        Assert.DoesNotContain("'/admin/teams/search'", source);
        Assert.DoesNotContain("api(\"/admin/teams\"", source);
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "FieldVisitSystem.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        return source[from..(to + end.Length)];
    }
}
