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
/// NONDEPLOY real candidate API HTTP on exactly the fresh clean-install DB,
/// not EnsureCreated. Covers Visitor temporal context and trip submission,
/// plus approved-location create through the dedicated approval probe.
/// No full 25-case UAT or IT certification.
/// </summary>
internal static class CleanInstallBusinessProbe
{
    internal sealed record Fixture(CleanInstallRoleProbe.Actor Visitor,
        CleanInstallRoleProbe.Actor Leader, CleanInstallRoleProbe.Actor Admin,
        int TeamId, int OtherTeamId, int SiteId, int LocationId);
    static InvalidOperationException Fail(string code) => new("CLEAN_BIZ_" + code);
    static DbContextOptions<AppDbContext> Options(string raw)
    {
        var b = new SqlConnectionStringBuilder(raw);
        if (b.DataSource != "localhost,1433"
            || b.InitialCatalog != "OptionCIsolated_FieldVisitV180CleanInstall"
            || b.UserID != "sa" || b.IntegratedSecurity)
            throw Fail("TARGET_DENIED");
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(b.ConnectionString).Options;
    }

    // Never emit SQL text, exception body, credentials or row values in CI.
    // Stage and SQL error number are enough to localize an isolated failure.
    private static async Task SaveSeedAsync(AppDbContext db, string stage)
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql)
        {
            throw Fail("SEED_" + stage + "_SQL_" + Math.Abs(sql.Number));
        }
        catch (SqlException sql)
        {
            throw Fail("SEED_" + stage + "_SQL_" + Math.Abs(sql.Number));
        }
        catch (Exception)
        {
            throw Fail("SEED_" + stage + "_UNKNOWN");
        }
    }

    internal static async Task<Fixture> SeedAsync(
        string cs, IReadOnlyList<CleanInstallRoleProbe.Actor> actors)
    {
        if (actors.Count != 3) throw Fail("ACTORS_MISSING");
        var visitor = actors.Single(a => a.Role == "visitor");
        var leader = actors.Single(a => a.Role == "leader");
        var admin = actors.Single(a => a.Role == "admin");
        var now = DateTime.UtcNow;
        var since = new DateOnly(2020, 1, 1);
        await using var db = new AppDbContext(Options(cs));
        var v = await db.Users.SingleAsync(u => u.UserId == visitor.UserId);
        var l = await db.Users.SingleAsync(u => u.UserId == leader.UserId);
        var team = await db.Teams.SingleAsync(t => t.TeamId == v.TeamId);
        var orgId = v.OrganizationId ?? throw Fail("ORG_MISSING");
        if (l.OrganizationId != orgId || l.TeamId != team.TeamId)
            throw Fail("ACTOR_SCOPE");
        team.EffectiveFrom = since;
        var otherOrg = new Organization
        {
            OrganizationCode = "CIEXT" + Guid.NewGuid().ToString("N")[..9],
            OrganizationName = "Synthetic Other Org", IsActive = true, CreatedAt = now
        };
        db.Organizations.Add(otherOrg);
        await SaveSeedAsync(db, "S1");
        var otherTeam = new Team
        {
            OrganizationId = otherOrg.OrganizationId,
            TeamCode = "CIEXT" + Guid.NewGuid().ToString("N")[..9],
            T