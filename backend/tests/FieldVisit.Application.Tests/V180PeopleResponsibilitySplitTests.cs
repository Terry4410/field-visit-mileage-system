using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180PeopleResponsibilitySplitTests
{
    [Fact]
    public void Team_membership_ui_never_calls_access_writer()
    {
        var source = ReadRepositoryFile("frontend/src/pages/TeamManagementPage.tsx");
        Assert.Contains("/team-memberships", source);
        Assert.Contains("batch-add", source);
        Assert.DoesNotContain("/access", source);
        Assert.Contains("角色（唯讀）", source);
    }

    [Fact]
    public void Internal_role_ui_uses_role_only_endpoint_and_has_no_supervisor_checkbox()
    {
        var source = ReadRepositoryFile("frontend/src/pages/PeopleAndAccessPage.tsx");
        Assert.Contains("/roles", source);
        Assert.DoesNotContain("code:\"supervisor\"", source);
        Assert.DoesNotContain("帳號啟用", source);
        Assert.DoesNotContain("setEnabled", source);
        Assert.Contains("實際登入", source);
        Assert.Contains("小組歸屬（唯讀）", source);
    }

    [Fact]
    public void Membership_command_updates_both_membership_models_but_not_roles()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V180TeamMembershipCommandService.cs");
        Assert.Contains("db.UserTeamAssignments.Add", source);
        Assert.Contains("db.TeamMemberships.Add", source);
        Assert.DoesNotContain("db.UserRoleAssignments.Add", source);
        Assert.DoesNotContain("db.UserRoles.Add", source);
        Assert.Contains("TEAM_MEMBERSHIP_MODEL_DRIFT", source);
    }

    [Fact]
    public void Role_command_does_not_write_team_membership_or_scope_models()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V180InternalRoleCommandService.cs");
        Assert.Contains("db.UserRoleAssignments.Add", source);
        Assert.Contains("db.UserRoles.Add", source);
        Assert.DoesNotContain("db.UserTeamAssignments.Add", source);
        Assert.DoesNotContain("db.TeamMemberships.Add", source);
        Assert.DoesNotContain("db.UserTeamScopes.Add", source);
        Assert.DoesNotContain("request.AdminEnabled", source);
        Assert.DoesNotContain("trackedUser.IsActive", source);
    }

    [Fact]
    public void Legacy_internal_access_writer_cannot_toggle_account_enabled()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V170PeopleAdminWriter.cs");
        var start = source.IndexOf("public async Task UpdateInternalUserAccessAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task PrepareInternalRoleVersionsAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.DoesNotContain("request.AdminEnabled", method);
        Assert.DoesNotContain("user.IsActive =", method);
    }

    [Fact]
    public void Internal_login_denial_reason_is_hr_driven_and_account_disabled_is_external_only()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V170AccessControl.cs");

        var externalBranch = source.IndexOf("UserTypes.External", StringComparison.Ordinal);
        var disabledReason = source.IndexOf("此帳號未啟用。", StringComparison.Ordinal);
        var missingHrReason = source.IndexOf("缺少有效人事狀態，無法登入系統。", StringComparison.Ordinal);

        Assert.True(externalBranch >= 0);
        Assert.True(disabledReason > externalBranch);
        Assert.True(missingHrReason > disabledReason);
        Assert.Contains("string.IsNullOrWhiteSpace(employmentStatus)", source);
    }

    [Fact]
    public void Dedicated_personnel_bulk_uses_usercode_as_identity_and_keeps_employee_number_editable()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V180PersonnelBulkService.cs");
        Assert.Contains("\"UserCode\", \"EmployeeNo\", \"DisplayName\", \"Email\", \"HireDate\", \"TerminationDate\"", source);
        Assert.Contains("x.UserCode==userCode", source);
        Assert.Contains("employment.EmployeeNo = employeeNo", source);
        Assert.Contains("db.EmploymentStatusPeriods", source);
        Assert.DoesNotContain("db.UserRoleAssignments", source);
        Assert.DoesNotContain("db.UserTeamAssignments", source);
        Assert.DoesNotContain("db.TeamMemberships", source);
    }

    [Fact]
    public void Final_gap_closure_does_not_add_an_1800_011_migration()
    {
        var root = RepositoryRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "database", "migrations", "1800_011")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "database", "migrations"), "1800_011*"));
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
