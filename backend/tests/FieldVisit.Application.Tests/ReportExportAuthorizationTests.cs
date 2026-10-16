using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class ReportExportAuthorizationTests
{
    [Fact]
    public async Task Visitor_export_requires_live_visitor_role_and_returns_no_other_roles()
    {
        var harness = new Harness("visitor", ["visitor", "leader"]);
        var live = await harness.Authorizer.AuthorizeAsync(["visitor", "leader"], "xlsx", default);
        Assert.Equal(new[] { "visitor" }, live.Roles);
        Assert.Equal(new[] { 10, 11 }, live.TeamIds);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.Authorizer.AuthorizeAsync(["admin"], "xlsx", default));
    }

    [Fact]
    public async Task Inactive_account_and_failed_current_HR_eligibility_are_denied()
    {
        var inactive = new Harness("visitor", ["visitor"]) { IsActive = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            inactive.Authorizer.AuthorizeAsync(["visitor"], "xlsx", default));

        var suspended = new Harness("visitor", ["visitor"]) { EligibilityAllowed = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            suspended.Authorizer.AuthorizeAsync(["visitor"], "xlsx", default));
    }

    [Fact]
    public async Task Removed_role_and_changed_organization_are_denied()
    {
        var revoked = new Harness("leader", ["visitor"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            revoked.Authorizer.AuthorizeAsync(["leader"], "xlsx", default));

        var moved = new Harness("admin", ["admin"], liveOrganizationId: 99);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            moved.Authorizer.AuthorizeAsync(["admin"], "xlsx", default));
    }

    [Fact]
    public async Task Changed_team_assignment_or_data_scope_is_denied()
    {
        var changedTeam = new Harness("leader", ["leader"], liveTeamIds: [10]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            changedTeam.Authorizer.AuthorizeAsync(["leader"], "xlsx", default));

        var changedDataScope = new Harness("admin", ["admin"], liveDataScopes:
            [new DataScopeDto("Organization", 7, null, null)]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            changedDataScope.Authorizer.AuthorizeAsync(["admin"], "xlsx", default));
    }

    [Fact]
    public async Task Admin_without_organization_scope_and_multi_role_token_are_denied()
    {
        var noOrg = new Harness("admin", ["admin"], tokenOrganizationId: null, liveOrganizationId: null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            noOrg.Authorizer.AuthorizeAsync(["admin"], "xlsx", default));

        var ambiguous = new Harness("visitor", ["visitor", "leader"], tokenRoles: ["visitor", "leader"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ambiguous.Authorizer.AuthorizeAsync(["visitor", "leader"], "xlsx", default));
    }

    [Fact]
    public async Task Supervisor_export_rechecks_live_export_capability()
    {
        var denied = new Harness("supervisor", ["supervisor"]) { SupervisorExportAllowed = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            denied.Authorizer.AuthorizeAsync(["supervisor"], "xlsx", default));
        Assert.Equal(1, denied.Access.ExportChecks);

        var allowed = new Harness("supervisor", ["supervisor"]);
        var user = await allowed.Authorizer.AuthorizeAsync(["supervisor"], "xlsx", default);
        Assert.Equal(new[] { "supervisor" }, user.Roles);
        Assert.Equal(1, allowed.Access.ExportChecks);
    }

    private sealed class Harness
    {
        public bool IsActive { get; set; } = true;
        public bool EligibilityAllowed { get; set; } = true;
        public bool SupervisorExportAllowed { get; set; } = true;
        public int? TokenOrganizationId { get; }
        public int? LiveOrganizationId { get; }
        public FakeAccess Access { get; } = new();
        public IReportExportAuthorizationService Authorizer { get; }

        public Harness(
            string role,
            IReadOnlyList<string> liveRoles,
            IReadOnlyList<string>? tokenRoles = null,
            int? tokenOrganizationId = 7,
            int? liveOrganizationId = 7,
            IReadOnlyList<int>? liveTeamIds = null,
            IReadOnlyList<DataScopeDto>? liveDataScopes = null)
        {
            TokenOrganizationId = tokenOrganizationId;
            LiveOrganizationId = liveOrganizationId;
            var token = new CurrentUserDto(5, "E005", "Test", "test@example.invalid",
                TokenOrganizationId, 10, "North", tokenRoles ?? [role],
                [new TeamScopeDto(10, "North", true), new TeamScopeDto(11, "South", false)]);
            var profile = new CurrentUserDto(5, "E005", "Test", "test@example.invalid",
                LiveOrganizationId, 10, "North", liveRoles,
                (liveTeamIds ?? [10, 11]).Select((id, i) => new TeamScopeDto(id, i == 0 ? "North" : "South", i == 0)).ToArray(),
                liveDataScopes);
            var repository = new FakeUsers(() => IsActive, () => profile);
            Access.Eligibility = () => EligibilityAllowed;
            Access.ExportAllowed = () => SupervisorExportAllowed;
            Authorizer = new ReportExportAuthorizationService(new FakeCurrent(token), repository, Access);
        }
    }

    private sealed class FakeCurrent(CurrentUserDto user) : ICurrentUserService
    {
        public CurrentUserDto GetRequired() => user;
    }

    private sealed class FakeUsers(Func<bool> active, Func<CurrentUserDto> profile) : IUserRepository
    {
        public Task<bool?> GetIsActiveAsync(int userId, CancellationToken ct) => Task.FromResult<bool?>(active());
        public Task<CurrentUserDto?> GetProfileAsync(int userId, CancellationToken ct) => Task.FromResult<CurrentUserDto?>(profile());
        public Task<User?> FindByAccountAsync(string account, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<User?> FindByEntraIdentityAsync(Guid tenantId, Guid objectId, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<User?> BindEntraIdentityByEmailAsync(Guid tenantId, Guid objectId, string email, CancellationToken ct) => Task.FromResult<User?>(null);
    }

    private sealed class FakeAccess : IV170AccessControl
    {
        public Func<bool> Eligibility { get; set; } = () => true;
        public Func<bool> ExportAllowed { get; set; } = () => true;
        public int ExportChecks { get; private set; }
        public Task<V170LoginEligibility> EvaluateLoginAsync(int userId, bool adminEnabled, CancellationToken ct) =>
            Task.FromResult(new V170LoginEligibility(Eligibility() && adminEnabled, "Internal", "Active", null));
        public Task<V170ReadScope> ResolveReadScopeAsync(CurrentUserDto user, CancellationToken ct) =>
            Task.FromResult(new V170ReadScope(false, user.TeamIds));
        public Task<bool> HasCapabilityAsync(int userId, string capabilityCode, CancellationToken ct) => Task.FromResult(ExportAllowed());
        public Task EnsureExportAllowedAsync(CurrentUserDto user, string format, CancellationToken ct)
        {
            ExportChecks++;
            if (!ExportAllowed()) throw new UnauthorizedAccessException("DENIED");
            return Task.CompletedTask;
        }
        public Task AuditSupervisorQueryAsync(CurrentUserDto user, TripQueryRequest request, int resultCount, CancellationToken ct) => Task.CompletedTask;
    }
}
