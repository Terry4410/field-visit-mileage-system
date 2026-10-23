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
            || parsed.InitialCatalog != "FieldVisitV180CleanInstall"
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
                UserId = user.UserId, RoleId = role.RoleId,
                EffectiveFrom = since, CreatedAt = now
            });
            db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId, RoleId = role.RoleId, AssignedAt = now
            });
            db.UserTeamAssignments.Add(new UserTeamAssignment
            {
                UserId = user.UserId, TeamId = team.TeamId,
                IsPrimary = true, EffectiveFrom = since, CreatedAt = now
            });
            await db.SaveChangesAsync();
            actors.Add(new Actor(roleCode, user.UserId, account));
        }
        return actors;
    }

    internal static async Task VerifyAsync(
        HttpClient client, string cs, IReadOnlyList<Actor> actors, string password)
    {
        if (actors.Count != 3 || actors.Select(x => x.Role).Distinct().Count() != 3)
            throw new InvalidOperationException("ROLE_PROBE_ACTOR_INVENTORY_INVALID");
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var actor in actors)
        {
            using var login = await client.PostAsJsonAsync(
                "/api/v1/auth/demo-login", new { account = actor.Account, password });
            if (login.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("ROLE_PROBE_DEMO_LOGIN_FAILED");
            using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var token = loginJson.RootElement.GetProperty("accessToken").GetString();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("ROLE_PROBE_TOKEN_MISSING");
            tokens.Add(actor.Role, token);

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var me = await client.SendAsync(meRequest);
            if (me.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("ROLE_PROBE_ME_HTTP_SQL_FAILED");
            using var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
            if (meJson.RootElement.GetProperty("userId").GetInt32() != actor.UserId
                || !meJson.RootElement.GetProperty("roles").EnumerateArray()
                    .Any(r => r.GetString() == actor.Role))
                throw new InvalidOperationException("ROLE_PROBE_SQL_PROFILE_ROLE_MISMATCH");
        }

        async Task MustDeny(string role, string route)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens[role]);
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException("ROLE_PROBE_CROSS_ROLE_GATE_NOT_DENIED");
        }

        await MustDeny("visitor", "/api/v1/leader/review-queue");
        await MustDeny("visitor", "/api/v1/admin/master-data/readiness");
        await MustDeny("leader", "/api/v1/trips/context?visitDate=2026-10-23");
        await MustDeny("leader", "/api/v1/admin/master-data/readiness");
        await MustDeny("admin", "/api/v1/trips/context?visitDate=2026-10-23");

        using (var badPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/demo-login",
            new { account = actors[0].Account, password = password + "-wrong" }))
        {
            if (badPassword.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException("ROLE_PROBE_WRONG_PASSWORD_ALLOWED");
        }

        // Real SQL employment change must deny a NEW login: a JWT-only test
        // would miss that fail-closed account lifecycle contract.
        await using (var db = new AppDbContext(Options(cs)))
        {
            var visitor = actors.Single(x => x.Role == "visitor");
            var employment = await db.UserEmploymentPeriods.SingleAsync(
                x => x.UserId == visitor.UserId);
            employment.EmploymentStatus = EmploymentStatuses.Leave;
            await db.SaveChangesAsync();
            using var revoked = await client.PostAsJsonAsync(
                "/api/v1/auth/demo-login",
                new { account = visitor.Account, password });
            if (revoked.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException("ROLE_PROBE_INACTIVE_RELOGIN_ALLOWED");
        }
        // This scope does not claim existing JWT revocation, same-owner data
        // access, approval workflow, Option C activation, or full schema parity.
    }
}
