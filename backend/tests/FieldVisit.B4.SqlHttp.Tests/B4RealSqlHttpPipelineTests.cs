using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldVisit.Api;
using FieldVisit.Api.Controllers;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.B4.SqlHttp.Tests;

/// <summary>
/// REAL HTTP JWT + real disposable SQL Server 2022, never UAT/Production.
/// Every fact creates and drops its own randomly named local database.
/// The B3 OFF/Manager blocked/011 not authorized/HARD HOLD gates remain.
/// </summary>
public sealed class B4RealSqlHttpPipelineTests
{
    private static readonly DateOnly Start=new(2026,1,1);
    private static readonly DateOnly VisitDay=new(2026,10,12);
    private const string Path="/api/v1/trips/context?visitDate=2026-10-12&teamId=";
    private const string LocalPassword="B4-local-fixture-only-not-user-secret";
    private const string LocalJwtKey="B4SqlHttpIsolatedSigningKeyNotFromAnyDeployment_2026";
    private const string LocalIssuer="B4.SqlHttp.Isolated";
    private const string LocalAudience="B4.SqlHttp.LocalUsers";

    private sealed class DisposableTestDatabase : IAsyncDisposable
    {
        private readonly string _master, _name;
        public string Connection {get;}
        public string Account {get;}="B4SQLHTTP-"+Guid.NewGuid().ToString("N")[..15];
        public int UserId,TeamId,SiteId,LocationId,OrgId;
        public long EmploymentId;

        private DisposableTestDatabase(string master,string name,string connection)
        { _master=master;_name=name;Connection=connection; }

        public AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(Connection).Options);

        public static async Task<DisposableTestDatabase> StartAsync()
        {
            var raw=Environment.GetEnvironmentVariable("B3_SQL_TEST_CONNECTION")
                ??throw new InvalidOperationException("B4_SQL_HTTP_LOCAL_ONLY_CONNECTION_REQUIRED");
            var b=new SqlConnectionStringBuilder(raw);
            if(b.DataSource is not ("127.0.0.1,14335" or "localhost,14335")
               ||!string.Equals(b.InitialCatalog,"master",StringComparison.OrdinalIgnoreCase)
               ||!string.Equals(b.UserID,"sa",StringComparison.OrdinalIgnoreCase)
               ||b.IntegratedSecurity)
                throw new InvalidOperationException("B4_SQL_HTTP_REQUIRES_DISPOSABLE_LOCAL_MASTER_14335");
            var master=b.ConnectionString;
            var name="B4SqlHttp_"+Guid.NewGuid().ToString("N");
            await using(var conn=new SqlConnection(master))
            {
                await conn.OpenAsync();
                await using var cmd=conn.CreateCommand();
                cmd.CommandText=$"CREATE DATABASE [{name}]";
                await cmd.ExecuteNonQueryAsync();
            }
            b.InitialCatalog=name;
            var f=new DisposableTestDatabase(master,name,b.ConnectionString);
            try
            {
                await using var db=f.Db();
                await db.Database.EnsureCreatedAsync();
                await f.SeedAsync(db);
                return f;
            }
            catch
            {
                await f.DisposeAsync();
                throw;
            }
        }

        private async Task SeedAsync(AppDbContext db)
        {
            var now=DateTime.UtcNow;
            var org=new Organization
            {
                OrganizationCode="HTTP-ORG-"+Guid.NewGuid().ToString("N"),
                OrganizationName="Disposable SQL HTTP",
                IsActive=true,EffectiveFrom=Start,CreatedAt=now
            };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            OrgId=org.OrganizationId;
            var user=new User
            {
                OrganizationId=OrgId,EmployeeNo=Account,
                DisplayName="Real SQL HTTP Visitor",IsActive=true,CreatedAt=now
            };
            var role=new Role
            {
                RoleCode="visitor",RoleName="Visitor",
                IsActive=true,CreatedAt=now
            };
            var team=new Team
            {
                OrganizationId=OrgId,TeamCode="B4HT",TeamName="SQL HTTP Team",
                IsActive=true,EffectiveFrom=Start,CreatedAt=now
            };
            var center=new Center
            {
                OrganizationId=OrgId,CenterCode="B4HC",CenterName="SQL HTTP Center",
                IsActive=true,EffectiveFrom=Start
            };
            var location=new Location
            {
                OrganizationId=OrgId,LocationCode="B4HL",LocationName="HTTP Official Location",
                IsActive=true,ApprovalStatus="Approved",CreatedAt=now
            };
            var employment=new Employment
            {
                PersonId=1,OrganizationId=OrgId,EmployeeNo=Account,
                SourceType="DisposableSqlHttp"
            };
            db.AddRange(user,role,team,center,location,employment);
            await db.SaveChangesAsync();
            UserId=user.UserId;TeamId=team.TeamId;
            LocationId=location.LocationId;EmploymentId=employment.EmploymentId;
            user.TeamId=TeamId;
            db.UserRoles.Add(new UserRole
            {
                UserId=UserId,RoleId=role.RoleId,AssignedAt=now
            });
            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                UserId=UserId,RoleId=role.RoleId,EffectiveFrom=Start,CreatedAt=now
            });
            db.UserTeamAssignments.Add(new UserTeamAssignment
            {
                UserId=UserId,TeamId=TeamId,IsPrimary=true,
                EffectiveFrom=Start,CreatedAt=now
            });
            db.UserIdentityProfiles.Add(new UserIdentityProfile
            {
                UserId=UserId,EmploymentId=EmploymentId,UserCode=Account,
                UserType=UserTypes.Internal,CreatedAt=now
            });
            db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
            {
                EmploymentId=EmploymentId,EmploymentStatus=EmploymentStatuses.Active,
                EffectiveFrom=Start,SourceType="DisposableSqlHttp"
            });
            db.TeamMemberships.Add(new TeamMembership
            {
                EmploymentId=EmploymentId,TeamId=TeamId,
                IsPrimary=true,EffectiveFrom=Start
            });
            db.TeamCenterAssignments.Add(new TeamCenterAssignment
            {
                TeamId=TeamId,CenterId=center.CenterId,
                EffectiveFrom=Start,CreatedAt=now
            });
            var site=new DeploymentSite
            {
                CenterId=center.CenterId,SiteCode="B4HS",
                SiteName="SQL HTTP Official Site",
                IsActive=true,EffectiveFrom=Start,CreatedAt=now
            };
            db.DeploymentSites.Add(site);
            await db.SaveChangesAsync();
            SiteId=site.DeploymentSiteId;
            db.AddRange(
                new EmploymentDeploymentSiteAssignment
                {
                    EmploymentId=EmploymentId,DeploymentSiteId=SiteId,
                    IsPrimary=true,EffectiveFrom=Start,CreatedAt=now
                },
                new TeamDeploymentSiteAssignment
                {
                    TeamId=TeamId,DeploymentSiteId=SiteId,
                    EffectiveFrom=Start,CreatedAt=now
                },
                new DeploymentSiteLocationAssignment
                {
                    DeploymentSiteId=SiteId,LocationId=LocationId,
                    EffectiveFrom=Start,CreatedAt=now
                });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if(!_name.StartsWith("B4SqlHttp_",StringComparison.Ordinal))return;
            SqlConnection.ClearAllPools();
            await using var conn=new SqlConnection(_master);
            await conn.OpenAsync();
            await using var cmd=conn.CreateCommand();
            cmd.CommandText=$"IF DB_ID(N'{_name}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_name}]; END";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private sealed class RealSqlHttpFactory(string connection)
        : WebApplicationFactory<AuthController>
    {
        private int _databaseDisconnected;
        public void InjectLocalDatabaseFailure()=>Volatile.Write(ref _databaseDisconnected,1);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("B4DisposableSqlHttpOnly");
            builder.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
                new Dictionary<string,string?>
                {
                    ["Auth:Mode"]="Demo",
                    ["Auth:Issuer"]=LocalIssuer,
                    ["Auth:Audience"]=LocalAudience,
                    ["Auth:JwtKey"]=LocalJwtKey,
                    ["Auth:DemoPassword"]=LocalPassword,
                    ["Auth:LiveSessionGuard:Enabled"]="true",
                    ["PackageB:B3:Enabled"]="false",
                    ["ConnectionStrings:DefaultConnection"]=connection,
                    ["BackgroundJobs:WorkerEnabled"]="false",
                    ["BackgroundJobs:RecoverOnStartup"]="false",
                    ["Swagger:Enabled"]="false",
                }));
            builder.ConfigureTestServices(services=>
            {
                services.RemoveAll<AppDbContext>();
                services.AddScoped<AppDbContext>(_=>new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
                        Volatile.Read(ref _databaseDisconnected)==0
                            ? connection
                            : "Server=127.0.0.1,14336;Database=B4Unavailable;User ID=sa;Password=NotASecret;Connect Timeout=1;Encrypt=False",
                        sql=>sql.CommandTimeout(2)).Options));
                services.PostConfigure<JwtBearerOptions>("AppJwt",options=>
                {
                    options.TokenValidationParameters.ValidIssuer=LocalIssuer;
                    options.TokenValidationParameters.ValidAudience=LocalAudience;
                    options.TokenValidationParameters.IssuerSigningKey=
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LocalJwtKey));
                });
                foreach(var hosted in services.Where(x=>
                    x.ServiceType==typeof(IHostedService)&&
                    x.ImplementationType?.Name=="BackgroundJobHostedService").ToArray())
                    services.Remove(hosted);
            });
        }
        public HttpClient Client()=>CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false
        });
    }

    private static async Task<HttpClient> LoginAsync(
        RealSqlHttpFactory factory,DisposableTestDatabase database)
    {
        var client=factory.Client();
        using var response=await client.PostAsJsonAsync("/api/v1/auth/demo-login",
            new {Account=database.Account,Password=LocalPassword});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var parsed=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jwt=parsed.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jwt));
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",jwt);
        return client;
    }

    private static async Task<JsonDocument> GetContextAsync(
        HttpClient client, int teamId)
    {
        using var response=await client.GetAsync(Path+teamId);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertNoBusinessWriteAsync(DisposableTestDatabase f)
    {
        await using var db=f.Db();
        Assert.Empty(await db.VisitTrips.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SQL_HTTP_valid_JWT_role_membership_eligible_site_from_real_SQL()
    {
        await using var db=await DisposableTestDatabase.StartAsync();
        using var host=new RealSqlHttpFactory(db.Connection);
        using var client=await LoginAsync(host,db);
        using var result=await GetContextAsync(client,db.TeamId);
        Assert.True(result.RootElement.GetProperty("eligibleForTrip").GetBoolean());
        Assert.Equal(db.SiteId,
            result.RootElement.GetProperty("primaryDeploymentSiteId").GetInt32());
        await AssertNoBusinessWriteAsync(db);
    }

    [Fact]
    public async Task SQL_HTTP_site_disabled_after_JWT_auth_fails_closed_for_stale_site()
    {
        await using var db=await DisposableTestDatabase.StartAsync();
        using var host=new RealSqlHttpFactory(db.Connection);
        using var client=await LoginAsync(host,db);
        using(var before=await GetContextAsync(client,db.TeamId))
            Assert.True(before.RootElement.GetProperty("eligibleForTrip").GetBoolean());
        await using(var update=db.Db())
        {
            var site=await update.DeploymentSites.SingleAsync(x=>x.DeploymentSiteId==db.SiteId);
            site.IsActive=false;
            await update.SaveChangesAsync();
        }
        using var after=await GetContextAsync(client,db.TeamId);
        Assert.False(after.RootElement.GetProperty("eligibleForTrip").GetBoolean());
        await AssertNoBusinessWriteAsync(db);
    }

    [Fact]
    public async Task SQL_HTTP_role_revoked_after_JWT_issue_denies_old_token_403()
    {
        await using var db=await DisposableTestDatabase.StartAsync();
        using var host=new RealSqlHttpFactory(db.Connection);
        using var client=await LoginAsync(host,db);
        await using(var update=db.Db())
        {
            update.UserRoles.RemoveRange(await update.UserRoles
                .Where(x=>x.UserId==db.UserId).ToListAsync());
            await update.SaveChangesAsync();
        }
        using var response=await client.GetAsync(Path+db.TeamId);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_ROLE_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        await AssertNoBusinessWriteAsync(db);
    }

    [Fact]
    public async Task SQL_HTTP_team_revoked_after_JWT_issue_denies_old_token_403()
    {
        await using var db=await DisposableTestDatabase.StartAsync();
        using var host=new RealSqlHttpFactory(db.Connection);
        using var client=await LoginAsync(host,db);
        await using(var update=db.Db())
        {
            var assignment=await update.UserTeamAssignments
                .SingleAsync(x=>x.UserId==db.UserId);
            assignment.EffectiveTo=Start;
            await update.SaveChangesAsync();
        }
        using var response=await client.GetAsync(Path+db.TeamId);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_TEAM_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        await AssertNoBusinessWriteAsync(db);
    }

    [Fact]
    public async Task SQL_HTTP_HR_leave_after_JWT_issue_denies_old_token_403()
    {
        await using var db=await DisposableTestDatabase.StartAsync();
        using var host=new RealSqlHttpFactory(db.Connection);
        using var client=await LoginAsync(host,db);
        await using(var update=db.Db())
        {
            var status=await update.EmploymentStatusPeriods
                .SingleAsync(x=>x.EmploymentId==db.EmploymentId);
            status.EmploymentStatus=EmploymentStatuses.Leave;
            await update.SaveChangesAsync();
        }
        using var response=await client.GetAsync(Path+db.TeamId);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Contains("SESSION_ACCESS_REVOKED",
            await response.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        await AssertNoBusinessWriteAsync(db);
    }

    [Fact]
    public async Task SQL_HTTP_disconnected_SQL_500_generic_no_access_or_business_write()
    {
        await using var db=await DisposableTestDatabase.StartAsync();
        using var host=new RealSqlHttpFactory(db.Connection);
        using var client=await LoginAsync(host,db);
        host.InjectLocalDatabaseFailure();
        using var response=await client.GetAsync(Path+db.TeamId);
        Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();
        Assert.Contains("系統錯誤",body,StringComparison.Ordinal);
        Assert.DoesNotContain("Password",body,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException",body,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=",body,StringComparison.OrdinalIgnoreCase);
        using var health=await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK,health.StatusCode);
        await AssertNoBusinessWriteAsync(db);
    }
}
