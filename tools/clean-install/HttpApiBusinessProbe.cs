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
            TeamName = "Synthetic Other Team", IsActive = true,
            EffectiveFrom = since, CreatedAt = now
        };
        db.Teams.Add(otherTeam);
        await SaveSeedAsync(db, "S2");
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
        var ae = new Employment
        {
            OrganizationId = orgId, PersonId = admin.UserId, EmployeeNo = admin.Account,
            LegacyUserId = admin.UserId, HireDate = since, SourceType = "Synthetic"
        };
        db.Employments.AddRange(ve, le, ae);
        await SaveSeedAsync(db, "S3");
        (await db.UserIdentityProfiles.SingleAsync(x => x.UserId == v.UserId))
            .EmploymentId = ve.EmploymentId;
        (await db.UserIdentityProfiles.SingleAsync(x => x.UserId == l.UserId))
            .EmploymentId = le.EmploymentId;
        (await db.UserIdentityProfiles.SingleAsync(x => x.UserId == admin.UserId))
            .EmploymentId = ae.EmploymentId;
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
            },
            new EmploymentStatusPeriod
            {
                EmploymentId = ae.EmploymentId, EmploymentStatus = EmploymentStatuses.Active,
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
            },
            new TeamMembership
            {
                EmploymentId = ae.EmploymentId, TeamId = team.TeamId,
                IsPrimary = true, EffectiveFrom = since
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
            },
            new UserTeamScope
            {
                UserId = admin.UserId, TeamId = team.TeamId,
                IsPrimary = true, IsActive = true, AssignedAt = now
            });
        var center = new Center
        {
            OrganizationId = orgId, CenterCode = "CI-CTX",
            CenterName = "Synthetic Context Center", EffectiveFrom = since,
            IsActive = true
        };
        db.Centers.Add(center);
        await SaveSeedAsync(db, "S4");
        // Exercise the RC-derived EF mapping against the enabled 1800_002
        // trigger. The disposable candidate applies a pinned OUTPUT-clause
        // compatibility overlay; no trigger is disabled.
        db.TeamLeaderAssignments.Add(new TeamLeaderAssignment
        {
            TeamId = team.TeamId,
            EmploymentId = le.EmploymentId,
            EffectiveFrom = since,
            CreatedAt = now
        });
        await SaveSeedAsync(db, "S4_LEADER_ASSIGNMENT");
        if (!await db.TeamLeaderAssignments.AnyAsync(x =>
                x.TeamId == team.TeamId && x.EmploymentId == le.EmploymentId))
            throw Fail("LEADER_ASSIGNMENT_NOT_PERSISTED");
        Console.WriteLine("CLEAN_INSTALL=EF_TEAM_LEADER_TRIGGER_OUTPUT_COMPAT_PASS");

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
        await SaveSeedAsync(db, "S5");
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
        await SaveSeedAsync(db, "S6");
        db.DeploymentSiteLocationAssignments.Add(
            new DeploymentSiteLocationAssignment
            {
                DeploymentSiteId = site.DeploymentSiteId,
                LocationId = place.LocationId, EffectiveFrom = since,
                CreatedAt = now
            });
        // Real source-mapped compensation rule, confined to this disposable DB;
        // required by actual Leader.ApproveAsync, not a bypassed mock approval.
        db.MileageRateRules.Add(new MileageRateRule
        {
            OrganizationId = orgId, RuleName = "Synthetic Clean Install Mileage",
            VehicleType = "Motorcycle", RatePerKm = 4m,
            EffectiveFrom = since, IsActive = true, CreatedAt = now
        });
        await SaveSeedAsync(db, "S7");
        return new Fixture(visitor, leader, admin, team.TeamId, otherTeam.TeamId,
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

        // A trip may reference an already approved and in-scope location;
        // this is distinct from creating or publishing a new Location.
        // Distinct date avoids accidental overlap with the temporary visit.
        // This exact approved-location path is submitted, calculated and
        // approved over the real published candidate HTTP/DI/SQL stack.
        using var approvedTrip = await visitor.PostAsJsonAsync("/api/v1/trips",
            Trip(new TripStopInput(f.LocationId, null, null, "Location",
                "Synthetic Approved Site", "1 Disposable Testing Rd", "visit", null))
                with { VisitDate = new DateOnly(2026, 10, 16) });
        if (approvedTrip.StatusCode != HttpStatusCode.OK)
            throw Fail("APPROVED_LOCATION_TRIP_CREATE");
        using var approvedBody = JsonDocument.Parse(await approvedTrip.Content.ReadAsStringAsync());
        var approvedId = approvedBody.RootElement.GetProperty("visitTripId").GetInt64();
        var approvedVersion = approvedBody.RootElement.GetProperty("rowVersion").GetString();
        if (approvedId <= 0 || string.IsNullOrWhiteSpace(approvedVersion))
            throw Fail("APPROVED_LOCATION_DRAFT_SHAPE");

        // Live-disabled provider returns deterministic failure evidence. It
        // must be current and authoritative before ManualFallback is permitted.
        using (var failedRoute = await visitor.PostAsync(
            "/api/v1/trips/" + approvedId + "/route-preview", null))
        {
            if (failedRoute.StatusCode != HttpStatusCode.OK)
                throw Fail("APPROVED_ROUTE_PREVIEW_HTTP");
            using var failedRouteBody = JsonDocument.Parse(
                await failedRoute.Content.ReadAsStringAsync());
            if (failedRouteBody.RootElement.GetProperty("status").GetString() != "Failed")
                throw Fail("APPROVED_ROUTE_FAILURE_EVIDENCE");
        }

        using var submitApproved = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/trips/" + approvedId + "/submit")
        {
            Content = JsonContent.Create(new SubmitTripRequest(false))
        };
        submitApproved.Headers.TryAddWithoutValidation("If-Match", approvedVersion);
        using (var submitResponse = await visitor.SendAsync(submitApproved))
        {
            if (submitResponse.StatusCode != HttpStatusCode.OK)
                throw Fail("APPROVED_LOCATION_SUBMIT_HTTP");
            using var submittedBody = JsonDocument.Parse(
                await submitResponse.Content.ReadAsStringAsync());
            if (submittedBody.RootElement.GetProperty("status").GetString() != "Submitted")
                throw Fail("APPROVED_LOCATION_SUBMITTED_SHAPE");
        }

        using var leaderLogin = await client.PostAsJsonAsync("/api/v1/auth/demo-login",
            new { account = f.Leader.Account, password = demoPassword });
        if (leaderLogin.StatusCode != HttpStatusCode.OK)
            throw Fail("LEADER_MILEAGE_LOGIN");
        using var leaderLoginBody = JsonDocument.Parse(
            await leaderLogin.Content.ReadAsStringAsync());
        var leaderToken = leaderLoginBody.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(leaderToken))
            throw Fail("LEADER_MILEAGE_TOKEN");
        using var leaderClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = false
        }) { BaseAddress = client.BaseAddress, Timeout = TimeSpan.FromSeconds(8) };
        leaderClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", leaderToken);

        using (var queue = await leaderClient.GetAsync("/api/v1/leader/review-queue"))
        {
            if (queue.StatusCode != HttpStatusCode.OK)
                throw Fail("LEADER_MILEAGE_QUEUE_HTTP");
            using var queueBody = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
            if (!queueBody.RootElement.EnumerateArray()
                .Any(x => x.GetProperty("visitTripId").GetInt64() == approvedId))
                throw Fail("LEADER_MILEAGE_QUEUE_SCOPE");
        }

        using (var calculated = await leaderClient.PostAsJsonAsync(
            "/api/v1/leader/trips/calculate",
            new MileageBatchRequest("Selected", null, null, new[] { approvedId })))
        {
            if (calculated.StatusCode != HttpStatusCode.OK)
                throw Fail("LEADER_MILEAGE_CALCULATE_HTTP");
            using var calculatedBody = JsonDocument.Parse(
                await calculated.Content.ReadAsStringAsync());
            if (calculatedBody.RootElement.GetProperty("success").GetInt32() != 1)
                throw Fail("LEADER_MILEAGE_CALCULATE_RESULT");
        }

        using var pendingResponse = await visitor.GetAsync("/api/v1/trips/" + approvedId);
        if (pendingResponse.StatusCode != HttpStatusCode.OK)
            throw Fail("LEADER_MILEAGE_PENDING_GET");
        using var pendingBody = JsonDocument.Parse(
            await pendingResponse.Content.ReadAsStringAsync());
        var pendingRoot = pendingBody.RootElement;
        var pendingVersion = pendingRoot.GetProperty("rowVersion").GetString();
        if (pendingRoot.GetProperty("status").GetString() != "PendingApproval"
            || string.IsNullOrWhiteSpace(pendingVersion))
            throw Fail("LEADER_MILEAGE_PENDING_STATE");

        using (var approvedResponse = await leaderClient.PostAsJsonAsync(
            "/api/v1/trips/" + approvedId + "/approve",
            new ApproveTripRequest(8.5m, pendingVersion,
                "Synthetic current-route provider-failure fallback", "ManualFallback")))
        {
            if (approvedResponse.StatusCode != HttpStatusCode.OK)
                throw Fail("LEADER_MILEAGE_APPROVE_HTTP");
            using var approvalBody = JsonDocument.Parse(
                await approvedResponse.Content.ReadAsStringAsync());
            if (approvalBody.RootElement.GetProperty("status").GetString() != "Approved")
                throw Fail("LEADER_MILEAGE_APPROVED_STATE");
        }

        await using (var approvedPersistence = new AppDbContext(Options(cs)))
        {
            if (!await approvedPersistence.VisitTrips.AnyAsync(t =>
                    t.VisitTripId == approvedId && t.UserId == f.Visitor.UserId
                    && t.TeamId == f.TeamId && t.Status == "Approved")
                || !await approvedPersistence.VisitTripSnapshots.AnyAsync(t =>
                    t.VisitTripId == approvedId && t.SnapshotType == "Submitted")
                || !await approvedPersistence.ApprovalRecords.AnyAsync(t =>
                    t.VisitTripId == approvedId && t.Action == "Approved"
                    && t.ApproverUserId == f.Leader.UserId)
                || !await approvedPersistence.MileageCalculations.AnyAsync(t =>
                    t.VisitTripId == approvedId && t.ManualFallbackUsed
                    && t.ApprovedDistanceKm == 8.5m && t.ApprovedAmount == 34m)
                || !await approvedPersistence.MileageGovernanceEvents.AnyAsync(t =>
                    t.VisitTripId == approvedId && t.EventType == "ManualFallback")
                || !await approvedPersistence.AuditLogs.AnyAsync(t =>
                    t.EntityId == approvedId.ToString() && t.Action == "TripApprove"))
                throw Fail("LEADER_MILEAGE_SQL_TRANSACTION_AUDIT");
        }
        Console.WriteLine("CLEAN_INSTALL=REAL_HTTP_APPROVED_LOCATION_TRIP_SUBMIT_LEADER_MILEAGE_APPROVE_SQL_PASS");

        await using (var check = new SqlConnection(cs))
        {
            await check.OpenAsync();
            await using var count = new SqlCommand(
                "SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK' AND AppliedBy=N'clean-install-v180'", check);
            if (Convert.ToInt32(await count.ExecuteScalarAsync()) != 1)
                throw Fail("CLEAN_BASELINE_CAPABILITY_RECORD_MISSING");
        }
        await using (var final = new AppDbContext(Options(cs)))
        {
            if (await final.VisitTrips.CountAsync() != 2)
                throw Fail("APPROVED_LOCATION_TRIP_NOT_PERSISTED");
        }
    }
}
