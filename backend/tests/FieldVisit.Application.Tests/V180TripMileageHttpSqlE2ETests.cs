using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Disposable SQL + real ASP.NET HTTP coverage for the normal Trip path.
/// This intentionally uses the full EF model created in an OptionCIsolated_*
/// database so controller, authentication, context reader, repository and
/// snapshot persistence are exercised together.
/// </summary>
public sealed class V180TripMileageHttpSqlE2ETests
{
    [Fact]
    public async Task Trip_create_submit_and_leader_manual_fallback_preserve_location_identity_and_reject_reserved_location()
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_TRIP_HTTP_SQL_REQUIRED");
            return;
        }

        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
            || root.InitialCatalog != "master" || root.UserID != "sa"
            || root.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_TRIP_NONLOCAL_TARGET_DENIED");

        var name = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        await using (var admin = new SqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var cs = new SqlConnectionStringBuilder(root.ConnectionString)
            { InitialCatalog = name, Pooling = false }.ConnectionString;
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", cs);
                builder.UseSetting("Auth:Mode", "Demo");
                builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
                builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
                builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
            });

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                var fixture = await SeedAsync(db);
                // Add only the disposable reservation contract used by the
                // operational guard; no UAT/Production schema is touched.
                await db.Database.ExecuteSqlRawAsync("""
                    IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
                        CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
                    IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
                        CREATE TABLE dbo.LocationCreateReservations(LocationCreateReservationId bigint IDENTITY PRIMARY KEY,LocationId int NOT NULL,ReservationStatus nvarchar(30) NOT NULL,ChangeRequestId bigint NULL);
                    IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
                        INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'1.8.0-CREATE-LOWRISK',N'disposable trip guard',SYSUTCDATETIME(),N'E2E');
                    """);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT dbo.LocationCreateReservations(LocationId,ReservationStatus) VALUES ({0},N'Pending')",
                    fixture.Pending.LocationId);

                using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
                visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Visitor.UserId, fixture.Visitor.EmployeeNo!, "visitor", fixture.Team.TeamId));

                var request = Draft(fixture.Approved.LocationId);
                using var createdResponse = await visitor.PostAsJsonAsync("api/v1/trips", request);
                Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync());
                var created = await createdResponse.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_CREATE_RESPONSE_MISSING");
                Assert.Equal("Draft", created.Status);
                Assert.Contains(created.Stops, x => x.LocationId == fixture.Approved.LocationId);

                // Direct LocationId input is still subject to the same server
                // reservation boundary as the Picker. It must not create a
                // Trip or Stop, even when the malformed reserved row is active.
                fixture.Pending.IsActive = true;
                fixture.Pending.ApprovalStatus = "Pending";
                await db.SaveChangesAsync();
                var before = await db.VisitTrips.CountAsync();
                using var blockedResponse = await visitor.PostAsJsonAsync(
                    "api/v1/trips", Draft(fixture.Pending.LocationId));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedResponse.StatusCode);
                Assert.Equal(before, await db.VisitTrips.CountAsync());
                Assert.Equal(0, await db.VisitTripStops.CountAsync(x => x.LocationId == fixture.Pending.LocationId));

                // Use an explicit request so the real optimistic-concurrency
                // If-Match contract is exercised by the HTTP pipeline.
                using var submit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit")
                {
                    Content = JsonContent.Create(new SubmitTripRequest(false))
                };
                submit.Headers.TryAddWithoutValidation("If-Match", created.RowVersion);
                using var submitted = await visitor.SendAsync(submit);
                Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
                var submittedTrip = await submitted.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_SUBMIT_RESPONSE_MISSING");
                Assert.Equal("Submitted", submittedTrip.Status);

                var snapshotLocation = await db.VisitTripSnapshotStops
                    .Where(x => x.VisitTripSnapshot.VisitTripId == created.VisitTripId)
                    .Select(x => x.LocationId).SingleAsync();
                Assert.Equal(fixture.Approved.LocationId, snapshotLocation);

                using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
                leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Leader.UserId, fixture.Leader.EmployeeNo!, "leader", fixture.Team.TeamId));
                var approve = new ApproveTripRequest(8.5m, submittedTrip.RowVersion, "manual fallback", "ManualFallback");
                using var approved = await leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve);
                Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
                var approvedTrip = await approved.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_APPROVE_RESPONSE_MISSING");
                Assert.Equal("Approved", approvedTrip.Status);
                Assert.Equal(8.5m, approvedTrip.ApprovedDistanceKm);

                Assert.Equal(1, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted"));
                Assert.Equal(1, await db.VisitTripSnapshotStops.CountAsync(x => x.VisitTripSnapshot.VisitTripId == created.VisitTripId && x.LocationId == fixture.Approved.LocationId));
                Assert.Equal(0, await db.VisitTripStops.CountAsync(x => x.LocationId == fixture.Pending.LocationId));
            }
        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static SaveTripRequest Draft(int locationId) => new(
        new DateOnly(2026, 10, 15), new TimeOnly(9, 0), new TimeOnly(10, 0),
        8.5m, "HTTP trip", "isolated", false,
        [new TripStopInput(locationId, null, null, "Location", "fixture", "address", "visit", null)],
        1, 1, 1, "Motorcycle");

    private static async Task<(User Visitor, User Leader, Team Team, Location Approved, Location Pending)> SeedAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var org = new Organization { OrganizationCode = "E2E", OrganizationName = "E2E", IsActive = true, CreatedAt = now };
        var team = new Team { OrganizationId = 1, TeamCode = "E2E-T1", TeamName = "E2E Team", IsActive = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now };
        var visitorRole = new Role { RoleCode = "visitor", RoleName = "Visitor", IsActive = true, CreatedAt = now };
        var leaderRole = new Role { RoleCode = "leader", RoleName = "Leader", IsActive = true, CreatedAt = now };
        db.AddRange(org, visitorRole, leaderRole);
        await db.SaveChangesAsync();
        team.OrganizationId = org.OrganizationId;
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var visitor = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "E2E-V", DisplayName = "E2E Visitor", IsActive = true, CreatedAt = now };
        var leader = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "E2E-L", DisplayName = "E2E Leader", IsActive = true, CreatedAt = now };
        db.AddRange(visitor, leader);
        await db.SaveChangesAsync();

        var employment = new Employment { PersonId = visitor.UserId, OrganizationId = org.OrganizationId, EmployeeNo = visitor.EmployeeNo, LegacyUserId = visitor.UserId, HireDate = new DateOnly(2020, 1, 1), SourceType = "E2E", CreatedAt = now };
        var leaderEmployment = new Employment { PersonId = leader.UserId, OrganizationId = org.OrganizationId, EmployeeNo = leader.EmployeeNo, LegacyUserId = leader.UserId, HireDate = new DateOnly(2020, 1, 1), SourceType = "E2E", CreatedAt = now };
        db.AddRange(employment, leaderEmployment);
        await db.SaveChangesAsync();
        db.AddRange(
            new UserIdentityProfile { UserId = visitor.UserId, EmploymentId = employment.EmploymentId, UserType = UserTypes.Internal, UserCode = visitor.EmployeeNo!, IdentityProvider = "Demo", CreatedAt = now },
            new UserIdentityProfile { UserId = leader.UserId, EmploymentId = leaderEmployment.EmploymentId, UserType = UserTypes.Internal, UserCode = leader.EmployeeNo!, IdentityProvider = "Demo", CreatedAt = now },
            new EmploymentStatusPeriod { EmploymentId = employment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "E2E", CreatedAt = now },
            new EmploymentStatusPeriod { EmploymentId = leaderEmployment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "E2E", CreatedAt = now },
            new TeamMembership { EmploymentId = employment.EmploymentId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new TeamMembership { EmploymentId = leaderEmployment.EmploymentId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new TeamLeaderAssignment { TeamId = team.TeamId, EmploymentId = leaderEmployment.EmploymentId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserRole { UserId = visitor.UserId, RoleId = visitorRole.RoleId, AssignedAt = now },
            new UserRole { UserId = leader.UserId, RoleId = leaderRole.RoleId, AssignedAt = now },
            new UserRoleAssignment { UserId = visitor.UserId, RoleId = visitorRole.RoleId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserRoleAssignment { UserId = leader.UserId, RoleId = leaderRole.RoleId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });

        var center = new Center { OrganizationId = org.OrganizationId, CenterCode = "E2E-C", CenterName = "E2E Center", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true };
        db.Centers.Add(center);
        await db.SaveChangesAsync();
        db.Add(new TeamCenterAssignment { TeamId = team.TeamId, CenterId = center.CenterId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });
        var site = new DeploymentSite { CenterId = center.CenterId, SiteCode = "E2E-S", SiteName = "E2E Site", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = now };
        db.DeploymentSites.Add(site);
        await db.SaveChangesAsync();
        db.AddRange(
            new TeamDeploymentSiteAssignment { TeamId = team.TeamId, DeploymentSiteId = site.DeploymentSiteId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new EmploymentDeploymentSiteAssignment { EmploymentId = employment.EmploymentId, DeploymentSiteId = site.DeploymentSiteId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });

        var approved = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId, LocationName = "Approved", LocationType = "Customer", ApprovalStatus = "Approved", GeocodingStatus = "Succeeded", IsActive = true, CreatedByUserId = visitor.UserId, CreatedAt = now };
        var pending = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId, LocationName = "Pending", LocationType = "Customer", ApprovalStatus = "Pending", GeocodingStatus = "Pending", IsActive = false, CreatedByUserId = visitor.UserId, CreatedAt = now };
        db.AddRange(approved, pending);
        await db.SaveChangesAsync();
        db.AddRange(
            new DeploymentSiteLocationAssignment { DeploymentSiteId = site.DeploymentSiteId, LocationId = approved.LocationId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new VisitType { VisitTypeCode = "E2E", VisitTypeName = "E2E Visit", IsActive = true, CreatedAt = now },
            new MileageRateRule { OrganizationId = org.OrganizationId, RuleName = "E2E rate", VehicleType = "Motorcycle", RatePerKm = 3m, EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();
        return (visitor, leader, team, approved, pending);
    }

    private static string Token(int id, string employeeNo, string role, int teamId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, employeeNo),
            new Claim(ClaimTypes.Role, role),
            new Claim("organization_id", "1"),
            new Claim("team_id", teamId.ToString()),
            new Claim("team_name", "E2E Team")
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-characters-long"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: claims, expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }
}
