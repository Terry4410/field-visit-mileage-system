using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Actual ASP.NET Core login HTTP + AuthService + UserRepository +
/// V170AccessControl + EF HR periods, backed ONLY by a private InMemory DB.
/// B3 stays OFF, no existing SQL server, no UAT or production operations.
/// This does not prove deployed API token revocation or formal B4 acceptance.
/// </summary>
public sealed class B4IsolatedHrLoginFactory : WebApplicationFactory<V180B3ChangeRequestsController>
{
    public const string LoginAccount="B4-HR-FIXTURE";
    public const string LoginPassword="B4-fixture-password-local-only";
    private const int UserId=84321;
    private readonly string _databaseName="B4HttpHr-"+Guid.NewGuid().ToString("N");
    private readonly bool _liveGuardEnabled;
    public B4IsolatedHrLoginFactory(bool liveGuardEnabled=false)=>_liveGuardEnabled=liveGuardEnabled;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("B4IsolatedHrTests");
        builder.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
            new Dictionary<string,string?>
            {
                ["Auth:Mode"]="Demo",
                ["Auth:Issuer"]=B3DisabledHttpFactory.Issuer,
                ["Auth:Audience"]=B3DisabledHttpFactory.Audience,
                ["Auth:JwtKey"]=B3DisabledHttpFactory.TestKey,
                ["Auth:DemoPassword"]=LoginPassword,
                ["PackageB:B3:Enabled"]="false",
                ["Auth:LiveSessionGuard:Enabled"]=_liveGuardEnabled?"true":"false",
                ["ConnectionStrings:DefaultConnection"]=
                    "Server=127.0.0.1,14336;Database=NoConnectionAllowed;User ID=unused;Password=unused;Connect Timeout=1;Encrypt=False",
                ["BackgroundJobs:WorkerEnabled"]="false",
                ["BackgroundJobs:RecoverOnStartup"]="false",
                ["Swagger:Enabled"]="false"
            }));
        builder.ConfigureTestServices(services=>
        {
            // Replace ONLY the test host's registered AppDbContext.
            // The real UserRepository, HR evaluator and AuthService remain.
            services.RemoveAll<AppDbContext>();
            services.AddScoped<AppDbContext>(_=>new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(_databaseName).Options));
            services.PostConfigure<JwtBearerOptions>("AppJwt",options=>
            {
                options.TokenValidationParameters.ValidIssuer=
                    B3DisabledHttpFactory.Issuer;
                options.TokenValidationParameters.ValidAudience=
                    B3DisabledHttpFactory.Audience;
                options.TokenValidationParameters.IssuerSigningKey=
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        B3DisabledHttpFactory.TestKey));
            });
            foreach(var service in services.Where(s=>
                s.ServiceType==typeof(IHostedService) &&
                s.ImplementationType?.Name=="BackgroundJobHostedService").ToArray())
                services.Remove(service);
        });
    }

    public HttpClient Client()=>CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress=new Uri("https://localhost"),
        AllowAutoRedirect=false
    });

    public async Task SeedAsync(string? status, int fromDays=-30, int? toDays=null)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            UserId=UserId,OrganizationId=1,EmployeeNo=LoginAccount,
            DisplayName="Isolated HR Test User",IsActive=true,
            CreatedAt=DateTime.UtcNow
        });
        db.Roles.Add(new Role
        {
            RoleId=1,RoleCode="visitor",RoleName="Visitor",
            IsActive=true,CreatedAt=DateTime.UtcNow
        });
        db.UserRoles.Add(new UserRole
        {
            UserRoleId=1,UserId=UserId,RoleId=1,AssignedAt=DateTime.UtcNow
        });
        if(status is not null)
            db.UserEmploymentPeriods.Add(new UserEmploymentPeriod
            {
                UserEmploymentPeriodId=1001,UserId=UserId,EmploymentStatus=status,
                EffectiveFrom=BusinessTime.Today.AddDays(fromDays),
                EffectiveTo=toDays.HasValue?BusinessTime.Today.AddDays(toDays.Value):null
            });
        await db.SaveChangesAsync();
    }

    public async Task AddStatusAsync(string status,int daysFromToday=0)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod
        {
            UserEmploymentPeriodId=1002,UserId=UserId,EmploymentStatus=status,
            EffectiveFrom=BusinessTime.Today.AddDays(daysFromToday)
        });
        await db.SaveChangesAsync();
    }

    public async Task DisableAccountAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account=await db.Users.SingleAsync(x=>x.UserId==UserId);
        account.IsActive=false;
        await db.SaveChangesAsync();
    }

    public async Task ChangeOrganizationAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account=await db.Users.SingleAsync(x=>x.UserId==UserId);
        account.OrganizationId=2;
        await db.SaveChangesAsync();
    }

    public async Task ExpireActiveAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row=await db.UserEmploymentPeriods.SingleAsync(x=>x.UserId==UserId);
        row.EffectiveTo=BusinessTime.Today.AddDays(-1);
        await db.SaveChangesAsync();
    }

    public async Task SetupModernInternalIdentityAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserIdentityProfiles.Add(new UserIdentityProfile
        {
            UserId=UserId,UserCode=LoginAccount,UserType=UserTypes.Internal,
            CreatedAt=DateTime.UtcNow
        });
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserRoleAssignmentId=2001,UserId=UserId,RoleId=1,
            EffectiveFrom=BusinessTime.Today.AddDays(-30),
            CreatedAt=DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task RevokeRoleProjectionAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x=>x.UserId==UserId).ToListAsync());
        await db.SaveChangesAsync();
    }

    public async Task DisableRoleCatalogAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role=await db.Roles.SingleAsync(x=>x.RoleId==1);
        role.IsActive=false;
        await db.SaveChangesAsync();
    }

    public async Task ExpireModernRoleAssignmentAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row=await db.UserRoleAssignments.SingleAsync(x=>x.UserId==UserId);
        row.EffectiveTo=BusinessTime.Today.AddDays(-1);
        await db.SaveChangesAsync();
    }

    public async Task ConvertInternalToExternalWithoutRoleResetAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile=await db.UserIdentityProfiles.SingleAsync(x=>x.UserId==UserId);
        profile.UserType=UserTypes.External;
        profile.AuthorizationFrom=BusinessTime.Today.AddDays(-20);
        profile.AuthorizationTo=BusinessTime.Today.AddDays(20);
        await db.SaveChangesAsync();
    }

    public async Task SetupExternalSupervisorAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Roles.Add(new Role { RoleId=2,RoleCode="supervisor",
            RoleName="Supervisor",IsActive=true,CreatedAt=DateTime.UtcNow });
        db.UserIdentityProfiles.Add(new UserIdentityProfile
        {
            UserId=UserId,UserCode=LoginAccount,UserType=UserTypes.External,
            AuthorizationFrom=BusinessTime.Today.AddDays(-20),
            AuthorizationTo=BusinessTime.Today.AddDays(20),
            CreatedAt=DateTime.UtcNow
        });
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x=>x.UserId==UserId).ToListAsync());
        db.UserRoles.Add(new UserRole
        {
            UserRoleId=2,UserId=UserId,RoleId=2,AssignedAt=DateTime.UtcNow
        });
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserRoleAssignmentId=2002,UserId=UserId,RoleId=2,
            EffectiveFrom=BusinessTime.Today.AddDays(-20),
            CreatedAt=DateTime.UtcNow
        });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserDataScopeId=4001,UserId=UserId,
            ScopeType=DataScopeTypes.Organization,OrganizationId=1,
            EffectiveFrom=BusinessTime.Today.AddDays(-20),
            CreatedAt=DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task ExpireExternalAuthorizationAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile=await db.UserIdentityProfiles.SingleAsync(x=>x.UserId==UserId);
        profile.AuthorizationTo=BusinessTime.Today.AddDays(-1);
        await db.SaveChangesAsync();
    }

    public async Task SeedLegacyTeamAsync(bool twoTeams=false)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Teams.Add(new Team
        {
            TeamId=7,OrganizationId=1,TeamCode="TEAM7",
            TeamName="Primary Team 7",IsActive=true,CreatedAt=DateTime.UtcNow
        });
        db.UserTeamScopes.Add(new UserTeamScope
        {
            UserTeamScopeId=7001,UserId=UserId,TeamId=7,
            IsPrimary=true,IsActive=true,AssignedAt=DateTime.UtcNow
        });
        if(twoTeams)
        {
            db.Teams.Add(new Team
            {
                TeamId=8,OrganizationId=1,TeamCode="TEAM8",
                TeamName="Secondary Team 8",IsActive=true,CreatedAt=DateTime.UtcNow
            });
            db.UserTeamScopes.Add(new UserTeamScope
            {
                UserTeamScopeId=7002,UserId=UserId,TeamId=8,
                IsPrimary=false,IsActive=true,AssignedAt=DateTime.UtcNow
            });
        }
        var account=await db.Users.SingleAsync(x=>x.UserId==UserId);
        account.TeamId=7;
        await db.SaveChangesAsync();
    }

    public async Task SeedModernTeamAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Teams.Add(new Team
        {
            TeamId=7,OrganizationId=1,TeamCode="TEAM7",
            TeamName="Primary Team 7",IsActive=true,CreatedAt=DateTime.UtcNow
        });
        db.UserTeamAssignments.Add(new UserTeamAssignment
        {
            UserTeamAssignmentId=7101,UserId=UserId,TeamId=7,
            IsPrimary=true,EffectiveFrom=BusinessTime.Today.AddDays(-20),
            CreatedAt=DateTime.UtcNow
        });
        var account=await db.Users.SingleAsync(x=>x.UserId==UserId);
        account.TeamId=7;
        await db.SaveChangesAsync();
    }

    public async Task RevokeTeamAsync(bool modern=false)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if(modern)
        {
            var a=await db.UserTeamAssignments.SingleAsync(x=>x.UserId==UserId);
            a.EffectiveTo=BusinessTime.Today.AddDays(-1);
        }
        else
        {
            var a=await db.UserTeamScopes.SingleAsync(x=>x.UserId==UserId&&x.TeamId==7);
            a.IsActive=false;
        }
        await db.SaveChangesAsync();
    }

    public async Task ChangePrimaryTeamAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows=await db.UserTeamScopes.Where(x=>x.UserId==UserId).ToListAsync();
        foreach(var x in rows)x.IsPrimary=x.TeamId==8;
        var account=await db.Users.SingleAsync(x=>x.UserId==UserId);
        account.TeamId=8;
        await db.SaveChangesAsync();
    }

    public async Task RenameTeamAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team=await db.Teams.SingleAsync(x=>x.TeamId==7);
        team.TeamName="Renamed Team 7";
        await db.SaveChangesAsync();
    }

    public async Task DisableTeamAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team=await db.Teams.SingleAsync(x=>x.TeamId==7);
        team.IsActive=false;
        await db.SaveChangesAsync();
    }

    public async Task TransferTeamToOtherOrganizationAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team=await db.Teams.SingleAsync(x=>x.TeamId==7);
        team.OrganizationId=2;
        await db.SaveChangesAsync();
    }

    public async Task ExpireExternalDataScopeAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row=await db.UserDataScopes.SingleAsync(x=>x.UserId==UserId);
        row.EffectiveTo=BusinessTime.Today.AddDays(-1);
        await db.SaveChangesAsync();
    }

    public async Task RevokeExternalDataScopeAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserDataScopes.RemoveRange(
            await db.UserDataScopes.Where(x=>x.UserId==UserId).ToListAsync());
        await db.SaveChangesAsync();
    }

    public async Task ChangeExternalDataScopeToWrongOrgAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row=await db.UserDataScopes.SingleAsync(x=>x.UserId==UserId);
        row.OrganizationId=2;
        await db.SaveChangesAsync();
    }

    public async Task ChangeExternalDataScopeToTeamAsync(bool foreignTeam=false)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row=await db.UserDataScopes.SingleAsync(x=>x.UserId==UserId);
        db.UserDataScopes.Remove(row);
        db.Teams.Add(new Team
        {
            TeamId=7,OrganizationId=foreignTeam?2:1,TeamCode="SCOPE7",
            TeamName="Supervisor Team 7",IsActive=true,CreatedAt=DateTime.UtcNow
        });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserDataScopeId=4002,UserId=UserId,ScopeType=DataScopeTypes.Team,
            TeamId=7,OrganizationId=1,
            EffectiveFrom=BusinessTime.Today.AddDays(-10),
            CreatedAt=DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public static readonly DateOnly DatedTripDay=BusinessTime.Today.AddDays(14);

    public async Task SeedDatedTripContextAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var start=new DateOnly(2026,1,1);
        var profile=await db.UserIdentityProfiles.SingleAsync();
        profile.EmploymentId=9001;
        db.Employments.Add(new Employment
        {
            EmploymentId=9001,PersonId=701,OrganizationId=1,
            EmployeeNo=LoginAccount,HireDate=start
        });
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentStatusPeriodId=701,EmploymentId=9001,
            EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=start
        });
        db.TeamMemberships.Add(new TeamMembership
        {
            TeamMembershipId=702,EmploymentId=9001,
            TeamId=7,IsPrimary=true,EffectiveFrom=start
        });
        db.Centers.Add(new Center
        {
            CenterId=81,OrganizationId=1,CenterCode="C81",
            CenterName="Center 81",IsActive=true,EffectiveFrom=start
        });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId=703,TeamId=7,CenterId=81,
            EffectiveFrom=start
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId=101,CenterId=81,SiteCode="S101",
            SiteName="Official Site 101",EffectiveFrom=start,IsActive=true
        });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamDeploymentSiteAssignmentId=704,TeamId=7,
            DeploymentSiteId=101,EffectiveFrom=start
        });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId=705,EmploymentId=9001,
            DeploymentSiteId=101,IsPrimary=true,EffectiveFrom=start
        });
        db.Locations.Add(new Location
        {
            LocationId=1001,OrganizationId=1,LocationCode="L1001",
            LocationName="Office 1001",LocationType="Official",
            IsActive=true,ApprovalStatus="Approved",
            CreatedAt=DateTime.UtcNow
        });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId=706,DeploymentSiteId=101,
            LocationId=1001,EffectiveFrom=start
        });
        await db.SaveChangesAsync();
    }

    public async Task DisableDatedDeploymentSiteAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var site=await db.DeploymentSites.SingleAsync(x=>x.DeploymentSiteId==101);
        site.IsActive=false;
        await db.SaveChangesAsync();
    }

    public async Task SetDatedDeploymentFutureAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var site=await db.DeploymentSites.SingleAsync(x=>x.DeploymentSiteId==101);
        site.EffectiveFrom=DatedTripDay.AddDays(1);
        await db.SaveChangesAsync();
    }

    public async Task RemoveDatedTeamCenterAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TeamCenterAssignments.Remove(
            await db.TeamCenterAssignments.SingleAsync(x=>x.TeamId==7));
        await db.SaveChangesAsync();
    }

    public async Task ScheduleLeaveOnDatedTripDayAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active=await db.EmploymentStatusPeriods.SingleAsync();
        active.EffectiveTo=DatedTripDay.AddDays(-1);
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentStatusPeriodId=707,EmploymentId=9001,
            EmploymentStatus=EmploymentStatuses.Leave,
            EffectiveFrom=DatedTripDay
        });
        await db.SaveChangesAsync();
    }

    public async Task AddForeignDatedOfficialSiteAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var start=new DateOnly(2026,1,1);
        db.Centers.Add(new Center
        {
            CenterId=82,OrganizationId=2,CenterCode="C82",
            CenterName="Foreign Center 82",IsActive=true,EffectiveFrom=start
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId=102,CenterId=82,SiteCode="S102",
            SiteName="Foreign Official Site 102",IsActive=true,
            EffectiveFrom=start
        });
        db.Locations.Add(new Location
        {
            LocationId=1002,OrganizationId=2,LocationCode="L1002",
            LocationName="Foreign Office 1002",LocationType="Official",
            IsActive=true,ApprovalStatus="Approved",CreatedAt=DateTime.UtcNow
        });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId=708,
            DeploymentSiteId=102,LocationId=1002,EffectiveFrom=start
        });
        await db.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> Login(HttpClient client,
        string account=LoginAccount,string password=LoginPassword)=>
        client.PostAsJsonAsync("/api/v1/auth/demo-login",new { Account=account,Password=password });
}

public sealed class V180HrLoginHttpPipelineTests
{
    private static async Task DeniedAsync(string? status,int from=-30,int? to=null)
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(status,from,to);
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken",body,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HTTP_active_HR_login_issues_valid_app_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token=json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",token);
        using var me=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,me.StatusCode);
        Assert.Contains(B4IsolatedHrLoginFactory.LoginAccount,
            await me.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public Task HTTP_leave_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Leave);

    [Fact]
    public Task HTTP_terminated_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Terminated);

    [Fact]
    public Task HTTP_prehire_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.PreHire);

    [Fact]
    public Task HTTP_missing_HR_login_403_without_token()=>
        DeniedAsync(null);

    [Fact]
    public Task HTTP_future_active_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Active,from:1);

    [Fact]
    public Task HTTP_expired_active_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Active,from:-30,to:-1);

    [Fact]
    public async Task HTTP_future_leave_does_not_revoke_current_active_login()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.AddStatusAsync(EmploymentStatuses.Leave,daysFromToday:1);
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task HTTP_live_HR_change_blocks_relogin_without_new_identity()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using(var first=await B4IsolatedHrLoginFactory.Login(client))
            Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        await factory.AddStatusAsync(EmploymentStatuses.Leave);
        using var second=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.Forbidden,second.StatusCode);
        Assert.DoesNotContain("accessToken",
            await second.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HTTP_invalid_demo_password_rejected_without_token()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var response=await B4IsolatedHrLoginFactory.Login(client,
            password:"incorrect-local-password");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("accessToken",
            await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HTTP_unknown_account_rejected_without_token()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var response=await B4IsolatedHrLoginFactory.Login(client,
            account:"UNKNOWN-TEST-IDENTITY");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("accessToken",
            await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpClient> AuthenticatedAsync(B4IsolatedHrLoginFactory factory)
    {
        var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var login=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        using var json=JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token=json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        return client;
    }

    [Fact]
    public async Task SESSION_default_OFF_old_JWT_can_still_reach_me_after_HR_leave()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=await AuthenticatedAsync(factory);
        await factory.AddStatusAsync(EmploymentStatuses.Leave);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task SESSION_optin_ON_active_JWT_reaches_me()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    [InlineData(EmploymentStatuses.PreHire)]
    public async Task SESSION_optin_ON_old_JWT_denied_after_HR_revoke(string status)
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.AddStatusAsync(status);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_ACCESS_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public async Task SESSION_optin_ON_disabled_account_denies_existing_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.DisableAccountAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task SESSION_optin_ON_org_transfer_denies_stale_organization_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.ChangeOrganizationAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task SESSION_optin_ON_expired_active_period_denies_old_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.ExpireActiveAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task SESSION_optin_ON_future_leave_preserves_current_JWT_until_effective()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.AddStatusAsync(EmploymentStatuses.Leave,daysFromToday:1);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task SESSION_optin_ON_denies_existing_JWT_on_B3_mine_before_DISABLED()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        using(var before=await client.GetAsync("/api/v1/change-requests/mine"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable,before.StatusCode);
        await factory.AddStatusAsync(EmploymentStatuses.Leave);
        using var response=await client.GetAsync("/api/v1/change-requests/mine");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("B3_DISABLED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public async Task SESSION_optin_ON_public_health_live_still_anonymous()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=factory.Client();
        using var response=await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task SESSION_optin_ON_me_without_JWT_still_returns_401()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=factory.Client();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }


    [Fact]
    public async Task ROLE_default_OFF_removed_legacy_role_old_JWT_still_reaches_me()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=await AuthenticatedAsync(factory);
        await factory.RevokeRoleProjectionAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_legacy_role_revoke_denies_old_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.RevokeRoleProjectionAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_ROLE_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public async Task ROLE_optin_ON_role_catalog_disabled_denies_old_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await AuthenticatedAsync(factory);
        await factory.DisableRoleCatalogAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_modern_valid_effective_and_projected_role_allows()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupModernInternalIdentityAsync();
        using var client=await AuthenticatedExistingAsync(factory);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_modern_expired_assignment_denies_old_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupModernInternalIdentityAsync();
        using var client=await AuthenticatedExistingAsync(factory);
        await factory.ExpireModernRoleAssignmentAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_modern_projection_revoked_denies_old_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupModernInternalIdentityAsync();
        using var client=await AuthenticatedExistingAsync(factory);
        await factory.RevokeRoleProjectionAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_external_conversion_denies_visitor_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupModernInternalIdentityAsync();
        using var client=await AuthenticatedExistingAsync(factory);
        await factory.ConvertInternalToExternalWithoutRoleResetAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_external_supervisor_with_current_grant_allows()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(factory);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task ROLE_optin_ON_external_supervisor_expired_grant_denies_old_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(factory);
        await factory.ExpireExternalAuthorizationAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    private static async Task<HttpClient> AuthenticatedExistingAsync(
        B4IsolatedHrLoginFactory factory)
    {
        var client=factory.Client();
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token=json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",token);
        return client;
    }


    [Fact]
    public async Task TEAM_default_OFF_revoked_legacy_team_still_accepted_by_old_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory();
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.RevokeTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_valid_legacy_membership_primary_JWT_allowed()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_legacy_membership_revoked_JWT_403()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.RevokeTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_TEAM_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public async Task TEAM_optin_ON_primary_switch_invalidates_old_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync(twoTeams:true);
        using var client=await AuthenticatedExistingAsync(f);
        await f.ChangePrimaryTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_disabled_team_invalidates_old_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.DisableTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_cross_org_team_invalidates_old_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.TransferTeamToOtherOrganizationAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_team_rename_keeps_same_authority_valid()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SeedLegacyTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.RenameTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_modern_effective_team_assignment_allowed()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupModernInternalIdentityAsync();
        await f.SeedModernTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task TEAM_optin_ON_modern_team_assignment_expiry_revokes_old_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupModernInternalIdentityAsync();
        await f.SeedModernTeamAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.RevokeTeamAsync(modern:true);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_valid_org_scope_allows_existing_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_scope_removed_denies_existing_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.RevokeExternalDataScopeAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_DATA_SCOPE_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_scope_expired_denies_existing_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.ExpireExternalDataScopeAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_wrong_org_scope_denies_existing_JWT()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.ChangeExternalDataScopeToWrongOrgAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_DATA_SCOPE_INVALID",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_changed_to_valid_team_live_scope_not_old_org()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.ChangeExternalDataScopeToTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json=doc.RootElement.GetProperty("dataScopes").GetRawText();
        Assert.Contains("Team",json,StringComparison.Ordinal);
        Assert.DoesNotContain("Organization",json,StringComparison.Ordinal);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_team_scope_foreign_org_denied()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.ChangeExternalDataScopeToTeamAsync(foreignTeam:true);
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Fact]
    public async Task SCOPE_optin_ON_supervisor_team_scope_disabled_denied()
    {
        using var f=new B4IsolatedHrLoginFactory(true);
        await f.SeedAsync(EmploymentStatuses.Active);
        await f.SetupExternalSupervisorAsync();
        using var client=await AuthenticatedExistingAsync(f);
        await f.ChangeExternalDataScopeToTeamAsync();
        await f.DisableTeamAsync();
        using var response=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }


}

public sealed class V180DeploymentSiteHttpPipelineTests
{
    private static string ContextPath => $"/api/v1/trips/context?visitDate={B4IsolatedHrLoginFactory.DatedTripDay:yyyy-MM-dd}&teamId=7";

    private static async Task<HttpClient> PrepareAsync(B4IsolatedHrLoginFactory factory)
    {
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.SetupModernInternalIdentityAsync();
        await factory.SeedModernTeamAsync();
        await factory.SeedDatedTripContextAsync();
        var client=factory.Client();
        using var login=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        using var j=JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token=j.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        return client;
    }

    private static async Task<JsonDocument> GetOkContext(HttpClient client)
    {
        using var response=await client.GetAsync(ContextPath);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SITE_HTTP_real_endpoint_valid_primary_site_returns_eligible_context()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await PrepareAsync(factory);
        using var doc=await GetOkContext(client);
        Assert.True(doc.RootElement.GetProperty("eligibleForTrip").GetBoolean());
        Assert.Equal(101,doc.RootElement.GetProperty("primaryDeploymentSiteId").GetInt32());
    }

    [Fact]
    public async Task SITE_HTTP_real_endpoint_disabled_site_returns_ineligible_context()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await PrepareAsync(factory);
        await factory.DisableDatedDeploymentSiteAsync();
        using var doc=await GetOkContext(client);
        Assert.False(doc.RootElement.GetProperty("eligibleForTrip").GetBoolean());
    }

    [Fact]
    public async Task SITE_HTTP_real_endpoint_future_site_not_currently_eligible()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await PrepareAsync(factory);
        await factory.SetDatedDeploymentFutureAsync();
        using var doc=await GetOkContext(client);
        Assert.False(doc.RootElement.GetProperty("eligibleForTrip").GetBoolean());
    }

    [Fact]
    public async Task SITE_HTTP_real_endpoint_removed_team_center_returns_no_eligible_sites()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await PrepareAsync(factory);
        await factory.RemoveDatedTeamCenterAsync();
        using var doc=await GetOkContext(client);
        Assert.False(doc.RootElement.GetProperty("eligibleForTrip").GetBoolean());
        Assert.Equal("NO_ELIGIBLE_DEPLOYMENT_SITE",
            doc.RootElement.GetProperty("validationCode").GetString());
    }

    [Fact]
    public async Task SITE_HTTP_real_endpoint_future_leave_is_ineligible_for_visit_date()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await PrepareAsync(factory);
        await factory.ScheduleLeaveOnDatedTripDayAsync();
        using var doc=await GetOkContext(client);
        Assert.False(doc.RootElement.GetProperty("eligibleForTrip").GetBoolean());
        Assert.Equal("EMPLOYMENT_NOT_ACTIVE",
            doc.RootElement.GetProperty("validationCode").GetString());
    }

    [Fact]
    public async Task SITE_HTTP_real_endpoint_foreign_org_official_site_excluded()
    {
        using var factory=new B4IsolatedHrLoginFactory(true);
        using var client=await PrepareAsync(factory);
        await factory.AddForeignDatedOfficialSiteAsync();
        using var doc=await GetOkContext(client);
        var sites=doc.RootElement.GetProperty("officialDeploymentSites");
        Assert.Equal(1,sites.GetArrayLength());
        Assert.Equal(101,sites[0].GetProperty("deploymentSiteId").GetInt32());
    }
}
