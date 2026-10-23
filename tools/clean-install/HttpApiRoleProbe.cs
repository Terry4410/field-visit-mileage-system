using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Scoped NONDEPLOY positive login/role proof against the actual 69-table
/// clean-install candidate. Not a business-flow, self-approval or full RBAC PASS.
/// The database is dropped by ModelBaselineRunner even on failure.
/// </summary>
internal static class CleanInstallRoleProbe
{
    internal sealed record Actor(string Role, int UserId, string Account);

    private static DbContextOptions<AppDbContext> Options(string cs)
    {
        var parsed = new SqlConnectionStringBuilder(cs);
        if (parsed.DataSource != "localhost,1433"
            || parsed.InitialCatalog != "OptionCIsolated_FieldVisitV180CleanInstall"
            || parsed.UserID != "sa"
            || parsed.IntegratedSecurity)
            throw new InvalidOperationException("ROLE_PROBE_DISPOSABLE_SQL_ONLY");
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(parsed.ConnectionString).Options;
    }

    internal static async Task<IReadOnlyList<Actor>> SeedAsync(string cs)
    {
        await using var db = new AppDbContext(Options(cs));
        // Persist the minimum real login state: the child API must see committed
        // rows, so a transaction rolled back before HTTP would be a false test.
        var now = DateTime.UtcNow;
        var since = new DateOnly(2020, 1, 1);
        var nonce = Guid.NewGuid().ToString("N")[..10];
        var org = new Organization
        {
            OrganizationCode = "CIR-" + nonce,
            OrganizationName = "Disposable Role Probe",
            IsActive = true, CreatedAt = now
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        var team = new Team
        {
            OrganizationId = org.OrganizationId, TeamCode = "CIR-" + nonce,
            TeamName = "Disposable Role Team", IsActive = true, CreatedAt = now
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var actors = new List<Actor>();
        foreach (var roleCode in new[] { "visitor", "leader", "admin" })
        {
            var account = "CIR-" + nonce + "-" + roleCode[0];
            var user = new User
            {
                OrganizationId = org.OrganizationId, TeamId = team.TeamId,
                EmployeeNo = account, DisplayName = "Disposable " + roleCode,
                IsActive = true, CreatedAt = now
            };
            var role = new Role
            {
                RoleCode = roleCode, RoleName = roleCode,
                IsActive = true, CreatedAt = now
            };
            db.Users.Add(user);
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            db.UserIdentityProfiles.Add(new UserIdentityProfile
            {
                UserId = user.UserId, UserType = UserTypes.Internal,
                UserCode = account, IdentityProvider = "Demo", CreatedAt = now
            });
            db.UserEmploymentPeriods.Add(new UserEmploymentPeriod
            {
                UserId = user.UserId, EmploymentStatus = EmploymentStatuses.Active,
                EffectiveFrom = since, SourceType = "Synthetic", CreatedAt = now
            });
            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
             