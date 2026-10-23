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
/// NONDEPLOY real RC HTTP on exactly the fresh candidate DB, not EnsureCreated.
/// Positive Visitor temporal context/temporary-trip contract and negative
/// incomplete 017 reservation gate. No full Option C or IT certification.
/// </summary>
internal static class CleanInstallBusinessProbe
{
    internal sealed record Fixture(CleanInstallRoleProbe.Actor Visitor,
        int TeamId, int OtherTeamId, int SiteId, int LocationId);
    static InvalidOperationException Fail(string code) => new("CLEAN_BIZ_" + code);
    static DbContextOptions<AppDbContext> Options(string raw)
    {
        var b = new SqlConnectionStringBuilder(raw);
        if (b.DataSource != "localhost,1433"
            || b.InitialCatalog != "FieldVisitV180CleanInstall"
            || b.UserID != "sa" || b.IntegratedSecurity)
            throw Fail("TARGET_DENIED");
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(b.ConnectionString).Options;
    }

    internal static async Task<Fixture> SeedAsync(
        string cs, IReadOnlyList<CleanInstallRoleProbe.Actor> actors)
    {
        if (actors.Count != 3) throw Fail("ACTORS_MISSING");
        var visitor = actors.Single(a => a.Role == "visitor");
        var leader = actors.Single(a => a.Role == "leader");
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
        await db.SaveChangesAsync();
        var otherTeam = new Team
        {
            OrganizationId = otherOrg.OrganizationId,
            TeamCode = "CIEXT" + Guid.NewGuid().ToString("N")[..9],
            TeamName = "Synthetic Other Team", IsActive = true,
            EffectiveFrom = since, CreatedAt = now
        };
        db.Teams.Add(otherTeam);
        await db.SaveChangesAsync();
        var ve = new Employment
        {
            OrganizationId = orgId, PersonId = v.UserId, EmployeeNo = visitor.Account,
            LegacyUserId = v.UserId, HireDate = since, SourceType = "Synthetic"
        };
        var le = new Employment
        {
            OrganizationId = orgId, PersonId = l.UserId, EmployeeNo = leader.Account,
            LegacyUserId = l.UserId, HireDate = since, SourceType = "Synthetic"
        };
        db.Employments.AddRange(ve, le);
        await db.SaveChangesAsync();
        (await db.UserIdentityProfiles.SingleAsync(x => x.UserId == v.UserId))
            .EmploymentId = ve.EmploymentId;
        (await db.UserIdentityProfiles.SingleAsync(x => x.UserId == l.UserId))
            .EmploymentId = le.EmploymentId;
        db.EmploymentStatusPeriods.AddRange(
            new EmploymentStatusPeriod
            {
                EmploymentId = ve.EmploymentId, EmploymentStatus = EmploymentStatuses.Active,
                EffectiveFrom = since, SourceType = "Synthetic"
            },
            new EmploymentStatusPeriod
            {
                EmploymentId = le.EmploymentId, EmploymentStatus = EmploymentStatuses.Active,
                EffectiveFrom = since, SourceType = "Synthetic"
            });
        db.TeamMemberships.AddRange(
            new TeamMembership
            {
                EmploymentId = ve.EmploymentId, TeamId = team.TeamId,
                IsPrimary = true, EffectiveFrom = since
            },
            new TeamMembership
            {
                EmploymentId = le.EmploymentId, TeamId = team.TeamId,
                IsPrimary = true, EffectiveFrom = since
            });
        db.TeamLeaderAssignments.Add(new TeamLeaderAssignment
        {
            TeamId = team.TeamId, EmploymentId = le.EmploymentId,
            EffectiveFrom = since, CreatedAt = now
        });
        db.UserTeamScopes.AddRange(
            new UserTeamScope
            {
                UserId = v.UserId, TeamId = team.TeamId,
                IsPrimary = true, IsActive = true, AssignedAt = now
            },
            new UserTeamScope
            {
                UserId = l.UserId, TeamId = team.TeamId,
                IsPrimary = true, IsActive = true, AssignedAt = now
            });
        var center = new Center
        {
            OrganizationId = orgId, CenterCode = "CI-CTX",
            CenterName = "Synthetic Context Center", EffectiveFrom = since,
            IsActive = true
        };
        db.Centers.Add(center);
        await db.SaveChangesAsync();
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamId = team.TeamId, CenterId = center.CenterId,
            EffectiveFrom = since, CreatedAt = now
        });
        var site = new DeploymentSite
        {
            CenterId = center.CenterId, SiteCode = "CI-CTX",
            SiteName = "Synthetic Site", EffectiveFrom = since,
            IsActive = true, CreatedAt = now
        };
        db.DeploymentSites.Add(site);
        await db.SaveChangesAsync();
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamId = team.TeamId, DeploymentSiteId = site.DeploymentSiteId,
            EffectiveFrom = since, CreatedAt = now
        });
        db.EmploymentDeploymentSiteAssignments.Add(
            new EmploymentDeploymentSiteAssignment
            {
                EmploymentId = ve.EmploymentId,
                DeploymentSiteId = site.DeploymentSiteId,
                IsPrimary = true, EffectiveFrom = since, CreatedAt = now
            });
        var place = new Location
        {
            OrganizationId = orgId, TeamId = team.TeamId,
            LocationName = "Synthetic Approved Site", LocationType = "Customer",
            Address = "1 Disposable Testing Rd", ApprovalStatus = "Approved",
            GeocodingStatus = "Succeeded", IsActive = true,
            CreatedByUserId = v.UserId, CreatedAt = now
        };
        db.Locations.Add(place);
        await db.SaveChangesAsync();
        db.DeploymentSiteLocationAssignments.Add(
            new DeploymentSiteLocationAssignment
            {
                DeploymentSiteId = site.DeploymentSiteId,
                LocationId = place.LocationId, EffectiveFrom = since,
                CreatedAt = now
            });
        await db.SaveChangesAsync();
        return new Fixture(visitor, team.TeamId, otherTeam.TeamId,
            site.DeploymentSiteId, place.LocationId);
    }

    internal static async Task VerifyAsync(
        HttpClient client, string cs, Fixture f, string demoPassword)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/demo-login",
            new { account = f.Visitor.Account, password = demoPassword });
        if (login.StatusCode != HttpStatusCode.OK) throw Fail("VISITOR_LOGIN");
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginJson.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw Fail("TOKEN_MISSING");
        using var visitor = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = false
        })
        {
            BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromSeconds(5)
        };
        visitor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using (var contextResponse = await visitor.GetAsync(
            "/api/v1/trips/context?visitDate=2026-10-15&teamId=" + f.TeamId))
        {
            if (contextResponse.StatusCode != HttpStatusCode.OK)
                throw Fail("POSITIVE_CONTEXT_HTTP");
            using var body = JsonDocument.Parse(await contextResponse.Content.ReadAsStringAsync());
            var root = body.RootElement;
            if (!root.GetProperty("eligibleForTrip").GetBoolean()
                || root.GetProperty("validationCode").GetString() != "OK"
                || root.GetProperty("selectedTeamId").GetInt32() != f.TeamId
                || root.GetProperty("defaultStartDeploymentSiteId").GetInt32() != f.SiteId
                || root.GetProperty("defaultEndDeploymentSiteId").GetInt32() != f.SiteId)
                throw Fail("POSITIVE_CONTEXT_SHAPE");
        }

        using (var foreign = await visitor.GetAsync(
            "/api/v1/trips/context?visitDate=2026-10-15&teamId=" + f.OtherTeamId))
        {
            if (foreign.StatusCode != HttpStatusCode.UnprocessableEntity
                || !(await foreign.Content.ReadAsStringAsync()).Contains(
                    "TRIP_CONTEXT_TEAM_NOT_ASSIGNED", StringComparison.Ordinal))
                throw Fail("FOREIGN_TEAM_CONTEXT");
        }

        // Same JWT, live dated SQL membership disabled then restored.
        await using (var db = new AppDbContext(Options(cs)))
        {
            var profile = await db.UserIdentityProfiles.SingleAsync(
                x => x.UserId == f.Visitor.UserId);
            var member = await db.TeamMemberships.SingleAsync(
                x => x.EmploymentId == profile.EmploymentId);
            member.EffectiveTo = new DateOnly(2026, 10, 14);
            await db.SaveChangesAsync();
            using var revoked = await visitor.GetAsync(
                "/api/v1/trips/context?visitDate=2026-10-15&teamId=" + f.TeamId);
            if (revoked.StatusCode != HttpStatusCode.UnprocessableEntity
                || !(await revoked.Content.ReadAsStringAsync()).Contains(
                    "TRIP_CONTEXT_TEAM_NOT_ASSIGNED", StringComparison.Ordinal))
                throw Fail("REVOKED_MEMBERSHIP");
            member.EffectiveTo = null;
            await db.SaveChangesAsync();
        }

        SaveTripRequest Trip(TripStopInput stop) => new(
            new DateOnly(2026, 10, 15), new TimeOnly(9, 0), new TimeOnly(10, 0),
            8.5m, "Synthetic clean SQL trip", "NONDEPLOY", false,
            new[] { stop }, f.TeamId, f.SiteId, f.SiteId, "Motorcycle");
        // Use a Temporary stop (supported RC path) to prove actual Visitor
        // draft persistence without bypassing incomplete published reservation.
        using var created = await visitor.PostAsJsonAsync("/api/v1/trips",
            Trip(new TripStopInput(null, null, null, "Temporary",
                "Synthetic Temporary Customer", "2 Disposable Testing Rd", "visit", null)));
        if (created.StatusCode != HttpStatusCode.OK)
            throw Fail("TEMP_TRIP_CREATE");
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var trip = createdJson.RootElement;
        if (trip.GetProperty("status").GetString() != "Draft")
            throw Fail("TEMP_TRIP_DRAFT_STATUS");
        var tripId = trip.GetProperty("visitTripId").GetInt64();
        var rowVersion = trip.GetProperty("rowVersion").GetString();
        if (tripId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
            throw Fail("TEMP_TRIP_ID_VERSION");

        using var submit = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/trips/" + tripId + "/submit")
        {
            Content = JsonContent.Create(new SubmitTripRequest(false))
        };
        submit.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        using var submitted = await visitor.SendAsync(submit);
        if (submitted.StatusCode != HttpStatusCode.OK)
            throw Fail("TEMP_TRIP_SUBMIT");
        using var submittedJson = JsonDocument.Parse(
            await submitted.Content.ReadAsStringAsync());
        if (submittedJson.RootElement.GetProperty("status").GetString() != "Submitted")
            throw Fail("TEMP_TRIP_SUBMITTED_STATUS");

        await using (var check = new AppDbContext(Options(cs)))
        {
            if (!await check.VisitTrips.AnyAsync(x =>
                    x.VisitTripId == tripId && x.UserId == f.Visitor.UserId
                    && x.TeamId == f.TeamId && x.Status == "Submitted")
                || !await check.VisitTripSnapshots.AnyAsync(x =>
                    x.VisitTripId == tripId && x.SnapshotType == "Submitted")
                || !await check.AuditLogs.AnyAsync(x =>
                    x.EntityId == tripId.ToString() && x.Action == "TripSubmit"))
                throw Fail("TEMP_TRIP_PERSIST_AUDIT");
        }

        // The FINAL A+C published-location capability is not installed:
        // 017 reservation table exists but the NEW ledger is intentionally
        // empty. No forged 1.8.0-017/CREATE-LOWRISK migration applied.
        using var denied = await visitor.PostAsJsonAsync("/api/v1/trips",
            Trip(new TripStopInput(f.LocationId, null, null, "Location",
                "Synthetic Approved Site", "1 Disposable Testing Rd", "visit", null)));
        if (denied.StatusCode != HttpStatusCode.UnprocessableEntity
            || !(await denied.Content.ReadAsStringAsync()).Contains(
                "OPTIONC_CREATE_RESERVATION_SCHEMA_DRIFT", StringComparison.Ordinal))
            throw Fail("PARTIAL_017_NOT_FAIL_CLOSED");
        await using (var check = new SqlConnection(cs))
        {
            await check.OpenAsync();
            await using var count = new SqlCommand(
                "SELECT COUNT(*) FROM dbo.SchemaVersions", check);
            if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 0)
                throw Fail("FAKE_MIGRATION_MARKER");
        }
        await using (var final = new AppDbContext(Options(cs)))
        {
            if (await final.VisitTrips.CountAsync() != 1)
                throw Fail("DENIED_TRIP_PERSISTED");
        }
    }
}
