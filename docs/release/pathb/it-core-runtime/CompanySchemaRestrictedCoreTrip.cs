using System.Net;
using System.Net.Http.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace FieldVisit.Application.Tests;

public sealed partial class V180TripMileageHttpSqlE2ETests
{
    [Fact]
    public async Task CompanySchemaRestrictedCoreTrip()
    {
        var dba=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FV_CORE_DBA")??throw new Exception("DBA_REQUIRED"));
        var runtime=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FV_CORE_RUNTIME")??throw new Exception("RUNTIME_REQUIRED"));
        Assert.Equal("FieldVisitCompanyCoreTest",dba.InitialCatalog);
        Assert.Equal(dba.InitialCatalog,runtime.InitialCatalog);
        Assert.Equal("127.0.0.1,1433",dba.DataSource);
        Assert.Equal(dba.DataSource,runtime.DataSource);
        Assert.Equal("sa",dba.UserID);Assert.Equal("fv_core_test",runtime.UserID);
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(dba.ConnectionString).Options);
        var fixture=await CompanySeedAsync(db);
        Console.WriteLine("COMPANY_SCHEMA_CORE_FIXTURE=PASS");
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.UseSetting("ConnectionStrings:DefaultConnection",runtime.ConnectionString);
            builder.UseSetting("Auth:Mode","Demo");
            builder.UseSetting("Auth:JwtKey","test-key-that-is-at-least-32-characters-long");
            builder.UseSetting("OptionC:Candidate:Enabled","true");
            builder.UseSetting("BackgroundJobs:WorkerEnabled","false");
            builder.UseSetting("BackgroundJobs:RecoverOnStartup","false");
            builder.ConfigureTestServices(services=>services.AddScoped<IV180RouteProvider,SuccessfulRouteProvider>());
        });
        using var visitor=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        visitor.DefaultRequestHeaders.Authorization=new("Bearer",Token(fixture.Visitor.UserId,fixture.Visitor.EmployeeNo!,"visitor",fixture.Team.TeamId));
        using var leader=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        leader.DefaultRequestHeaders.Authorization=new("Bearer",Token(fixture.Leader.UserId,fixture.Leader.EmployeeNo!,"leader",fixture.Team.TeamId));
        async Task<T> Read<T>(HttpResponseMessage response,string label){
            using(response){Assert.True(response.IsSuccessStatusCode,label+" HTTP "+(int)response.StatusCode+" "+await response.Content.ReadAsStringAsync());
            var value=await response.Content.ReadFromJsonAsync<T>();Assert.NotNull(value);Console.WriteLine(label+"=PASS");return value!;}
        }
        async Task<TripDto> Submit(TripDto trip,string label){
            using var req=new HttpRequestMessage(HttpMethod.Post,$"api/v1/trips/{trip.VisitTripId}/submit"){Content=JsonContent.Create(new SubmitTripRequest(false))};
            req.Headers.TryAddWithoutValidation("If-Match",trip.RowVersion);
            return await Read<TripDto>(await visitor.SendAsync(req),label);
        }
        var input=Draft(fixture.Approved.LocationId,false);
        var trip=await Read<TripDto>(await visitor.PostAsJsonAsync("api/v1/trips",input),"RESTRICTED_VISITOR_CREATE");
        var preview=await Read<V180RouteOrchestrationResult>(await visitor.PostAsync($"api/v1/trips/{trip.VisitTripId}/route-preview",null),"RESTRICTED_PROVIDER_STUB_PREVIEW");
        Assert.Equal("Succeeded",preview.Status);Assert.Equal(12.5m,preview.SuggestedDistanceKm);
        trip=await Read<TripDto>(await visitor.GetAsync($"api/v1/trips/{trip.VisitTripId}"),"RESTRICTED_VISITOR_DETAIL");
        var pending=await Submit(trip,"RESTRICTED_SUBMIT");Assert.Equal("PendingApproval",pending.Status);
        var approval=new ApproveTripRequest(12.5m,pending.RowVersion,null,"ProviderSuggested",preview.RouteCalculationAttemptId);
        using(var forbidden=await visitor.PostAsJsonAsync($"api/v1/trips/{trip.VisitTripId}/approve",approval))
            Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        Console.WriteLine("VISITOR_APPROVE_DENIED=PASS");
        var emp=await db.Employments.SingleAsync(x=>x.LegacyUserId==fixture.Leader.UserId);
        await db.EmploymentStatusPeriods.Where(x=>x.EmploymentId==emp.EmploymentId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EmploymentStatus,EmploymentStatuses.Leave));
        using(var forbidden=await leader.PostAsJsonAsync($"api/v1/trips/{trip.VisitTripId}/approve",approval))
            Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        Console.WriteLine("ISSUED_LEADER_TOKEN_HR_LEAVE_APPROVAL_DENIED=PASS");
        await db.EmploymentStatusPeriods.Where(x=>x.EmploymentId==emp.EmploymentId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.EmploymentStatus,EmploymentStatuses.Active));
        var returned=await Read<TripDto>(await leader.PostAsJsonAsync($"api/v1/trips/{trip.VisitTripId}/return",new ReturnTripRequest("Synthetic note correction",pending.RowVersion)),"RESTRICTED_LEADER_RETURN");
        Assert.Equal("Returned",returned.Status);
        using(var edit=new HttpRequestMessage(HttpMethod.Put,$"api/v1/trips/{trip.VisitTripId}"){Content=JsonContent.Create(input with{Notes="Corrected synthetic note"})}){
            edit.Headers.TryAddWithoutValidation("If-Match",returned.RowVersion);
            returned=await Read<TripDto>(await visitor.SendAsync(edit),"RESTRICTED_VISITOR_EDIT");
        }
        pending=await Submit(returned,"RESTRICTED_VISITOR_RESUBMIT");Assert.Equal("PendingApproval",pending.Status);
        var attempt=await db.RouteCalculationAttempts.AsNoTracking().Where(x=>x.VisitTripId==trip.VisitTripId&&x.Status=="Succeeded").OrderByDescending(x=>x.RouteCalculationAttemptId).FirstAsync();
        var approved=await Read<TripDto>(await leader.PostAsJsonAsync($"api/v1/trips/{trip.VisitTripId}/approve",new ApproveTripRequest(11.5m,pending.RowVersion,null,"LeaderAdjusted",attempt.RouteCalculationAttemptId,"Travel time differs due to dispatch conditions.")),"RESTRICTED_LEADER_APPROVE");
        Assert.Equal("Approved",approved.Status);Assert.Equal(34.5m,approved.ApprovedAmount);
        Assert.Equal(2,await db.VisitTripSnapshots.CountAsync(x=>x.VisitTripId==trip.VisitTripId&&x.SnapshotType=="Submitted"));
        Assert.Equal(1,await db.VisitTripSnapshots.CountAsync(x=>x.VisitTripId==trip.VisitTripId&&x.SnapshotType=="Approved"));
        Assert.Equal(1,await db.ApprovalRecords.CountAsync(x=>x.VisitTripId==trip.VisitTripId&&x.Action=="Approved"));
        Console.WriteLine("FINANCIAL_AND_IMMUTABLE_SNAPSHOT_READBACK=PASS");
        await using var conn=new SqlConnection(runtime.ConnectionString);await conn.OpenAsync();
        await using var cmd=new SqlCommand("SELECT IS_ROLEMEMBER(N'db_owner')+IS_ROLEMEMBER(N'db_datareader')+IS_ROLEMEMBER(N'db_datawriter')",conn);
        Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        foreach(var sql in new[]{"SELECT NewValues FROM dbo.AuditLogs","UPDATE dbo.VisitTripSnapshots SET NotesSnapshot=NotesSnapshot WHERE 1=0","DELETE FROM dbo.VisitTrips WHERE 1=0","UPDATE dbo.SchemaVersions SET VersionNumber=VersionNumber WHERE 1=0"}){
            await using var deny=new SqlCommand(sql,conn);
            var ex=await Assert.ThrowsAsync<SqlException>(async()=>{await deny.ExecuteNonQueryAsync();});Assert.Contains(ex.Number,new[]{229,230});
        }
        Console.WriteLine("CORE_RUNTIME_NO_BROAD_ROLES_AND_PROTECTED_WRITES_DENIED=PASS");
    }
    private static async Task<(User Visitor, User Leader, Team Team, Location Approved, Location Pending)> CompanySeedAsync(AppDbContext db)
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

        var vp = new Person { DisplayName=visitor.DisplayName, LegacyUserId=visitor.UserId, CreatedAt=now };
        var lp = new Person { DisplayName=leader.DisplayName, LegacyUserId=leader.UserId, CreatedAt=now };
        db.AddRange(vp,lp);
        await db.SaveChangesAsync();
        var employment = new Employment { PersonId = vp.PersonId, OrganizationId = org.OrganizationId, EmployeeNo = visitor.EmployeeNo, LegacyUserId = visitor.UserId, HireDate = new DateOnly(2020, 1, 1), SourceType = "Manual" };
        var leaderEmployment = new Employment { PersonId = lp.PersonId, OrganizationId = org.OrganizationId, EmployeeNo = leader.EmployeeNo, LegacyUserId = leader.UserId, HireDate = new DateOnly(2020, 1, 1), SourceType = "Manual" };
        db.AddRange(employment, leaderEmployment);
        await db.SaveChangesAsync();
        db.AddRange(
            new UserIdentityProfile { UserId = visitor.UserId, EmploymentId = employment.EmploymentId, UserType = UserTypes.Internal, UserCode = visitor.EmployeeNo!, IdentityProvider = "Demo", CreatedAt = now },
            new UserIdentityProfile { UserId = leader.UserId, EmploymentId = leaderEmployment.EmploymentId, UserType = UserTypes.Internal, UserCode = leader.EmployeeNo!, IdentityProvider = "Demo", CreatedAt = now },
            new EmploymentStatusPeriod { EmploymentId = employment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "Manual" },
            new EmploymentStatusPeriod { EmploymentId = leaderEmployment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "Manual" },
            new TeamMembership { EmploymentId = employment.EmploymentId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1) },
            new TeamMembership { EmploymentId = leaderEmployment.EmploymentId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1) },
            new UserEmploymentPeriod { UserId = visitor.UserId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "Manual", CreatedAt = now },
            new UserEmploymentPeriod { UserId = leader.UserId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "Manual", CreatedAt = now },
            new UserTeamAssignment { UserId = visitor.UserId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserTeamAssignment { UserId = leader.UserId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserTeamScope { UserId = visitor.UserId, TeamId = team.TeamId, IsPrimary = true, IsActive = true, AssignedAt = now },
            new UserTeamScope { UserId = leader.UserId, TeamId = team.TeamId, IsPrimary = true, IsActive = true, AssignedAt = now },
            new UserRole { UserId = visitor.UserId, RoleId = visitorRole.RoleId, AssignedAt = now },
            new UserRole { UserId = leader.UserId, RoleId = leaderRole.RoleId, AssignedAt = now },
            new UserRoleAssignment { UserId = visitor.UserId, RoleId = visitorRole.RoleId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserRoleAssignment { UserId = leader.UserId, RoleId = leaderRole.RoleId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });

        // DBA-only synthetic fixture: preserve actual database trigger, avoid EF OUTPUT on its unconfigured table.
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt) VALUES({team.TeamId},{leaderEmployment.EmploymentId},{new DateTime(2020,1,1)},{now})");

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

        var approved = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId, LocationName = "Approved", Address="Synthetic approved road 1", Latitude=25.05m, Longitude=121.50m, LocationType = "Customer", ApprovalStatus = "Approved", GeocodingStatus = "Succeeded", IsActive = true, CreatedByUserId = visitor.UserId, CreatedAt = now };
        var pending = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId, LocationName = "Pending", Address="Synthetic pending road 2", LocationType = "Customer", ApprovalStatus = "Pending", GeocodingStatus = "Pending", IsActive = false, CreatedByUserId = visitor.UserId, CreatedAt = now };
        db.AddRange(approved, pending);
        await db.SaveChangesAsync();
        db.AddRange(
            new DeploymentSiteLocationAssignment { DeploymentSiteId = site.DeploymentSiteId, LocationId = approved.LocationId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new VisitType { VisitTypeCode = "E2E", VisitTypeName = "E2E Visit", IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();
        // Provision the rate using the actual schema contract, not the legacy EF fixture.
        // Preserve all CHECK constraints and triggers. Verify persisted data before any HTTP operation.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,IsActive,CreatedAt) VALUES({org.OrganizationId},{"E2E rate"},{"MOTORCYCLE"},{3m},{new DateTime(2020,1,1)},{true},{now})");
        var persistedRate = await db.MileageRateRules.AsNoTracking().SingleAsync(x => x.OrganizationId == org.OrganizationId && x.RuleName == "E2E rate");
        Assert.Equal("MOTORCYCLE", persistedRate.VehicleType);
        Assert.Equal(3m, persistedRate.RatePerKm);
        Assert.True(persistedRate.IsActive);
        Assert.True(await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x => x.TeamId == team.TeamId && x.EmploymentId == leaderEmployment.EmploymentId));
        Console.WriteLine("COMPANY_SCHEMA_CANONICAL_FIXTURE_READBACK=PASS");
        return (visitor, leader, team, approved, pending);
    }


}
