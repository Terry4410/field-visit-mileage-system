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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class FinalAdminOfficialSqlTests
{
    private const string Key="final-official-admin-isolated-jwt-key-over-32-characters";
    [Fact]
    public async Task Center_and_site_create_update_deactivate_are_sql_persistent_and_role_bound()
    {
        var raw=Environment.GetEnvironmentVariable("FV_FINAL_SQL")??throw new Exception("FV_FINAL_SQL_REQUIRED");
        var root=new SqlConnectionStringBuilder(raw){Pooling=false,ConnectTimeout=5};
        Assert.Contains(root.DataSource,new[]{"localhost,1433","127.0.0.1,1433"});
        Assert.Equal("master",root.InitialCatalog);
        var name="FinalOfficial_"+Guid.NewGuid().ToString("N");
        await using var server=new SqlConnection(root.ConnectionString);await server.OpenAsync();
        await using(var cmd=server.CreateCommand()){cmd.CommandText=$"CREATE DATABASE [{name}]";await cmd.ExecuteNonQueryAsync();}
        root.InitialCatalog=name;
        try
        {
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(root.ConnectionString).Options);
            await db.Database.EnsureCreatedAsync();
            var now=DateTime.UtcNow;var from=BusinessTime.Today.AddDays(-10);
            var org=new Organization{OrganizationCode="OF",OrganizationName="Official Synthetic",IsActive=true,CreatedAt=now};
            var other=new Organization{OrganizationCode="OT",OrganizationName="Other Synthetic",IsActive=true,CreatedAt=now};
            db.AddRange(org,other);await db.SaveChangesAsync();
            var team=new Team{OrganizationId=org.OrganizationId,TeamCode="OF-T",TeamName="Official Team",IsActive=true,CreatedAt=now};
            var role=new Role{RoleCode="admin",RoleName="Admin",IsActive=true,CreatedAt=now};
            db.AddRange(team,role);await db.SaveChangesAsync();
            var user=new User{OrganizationId=org.OrganizationId,TeamId=team.TeamId,EmployeeNo="OF-001",DisplayName="Official Admin",IsActive=true,CreatedAt=now};
            db.Add(user);await db.SaveChangesAsync();
            db.AddRange(
                new UserIdentityProfile{UserId=user.UserId,UserCode="OF-001",UserType="Internal",CreatedAt=now},
                new UserEmploymentPeriod{UserId=user.UserId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=from,CreatedAt=now},
                new UserRole{UserId=user.UserId,RoleId=role.RoleId,AssignedAt=now},
                new UserRoleAssignment{UserId=user.UserId,RoleId=role.RoleId,EffectiveFrom=from,CreatedAt=now},
                new UserTeamAssignment{UserId=user.UserId,TeamId=team.TeamId,IsPrimary=true,EffectiveFrom=from,CreatedAt=now});
            var location=new Location{OrganizationId=org.OrganizationId,TeamId=team.TeamId,LocationCode="OF-L",
                LocationName="Official Location",LocationType="Official",ApprovalStatus="Approved",
                IsActive=true,CreatedAt=now};
            db.Add(location);await db.SaveChangesAsync();
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{
                b.UseSetting("ConnectionStrings:DefaultConnection",root.ConnectionString);
                b.UseSetting("Auth:Mode","Demo");b.UseSetting("Auth:JwtKey",Key);
                b.UseSetting("BackgroundJobs:WorkerEnabled","false");b.UseSetting("BackgroundJobs:RecoverOnStartup","false");
            });
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            void Token(string selected="admin",int? claimedOrg=null)
            {
                var claims=new[]{new Claim(ClaimTypes.NameIdentifier,user.UserId.ToString()),new Claim(ClaimTypes.Role,selected),
                    new Claim("employee_no","OF-001"),new Claim("organization_id",(claimedOrg??org.OrganizationId).ToString()),
                    new Claim("team_id",team.TeamId.ToString())};
                client.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(
                    new JwtSecurityToken(issuer:"FieldVisit.UAT",audience:"FieldVisit.UAT.Users",claims:claims,
                        expires:DateTime.UtcNow.AddMinutes(10),
                        signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256))));
            }
            Token();
            using var createCenter=await client.PostAsJsonAsync("api/v1/admin/master-data/centers",
                new V180CenterInput("OF-C","Original Center",from,null,true));
            Assert.Equal(HttpStatusCode.OK,createCenter.StatusCode);
            var center=await createCenter.Content.ReadFromJsonAsync<V180MasterDataRow>();Assert.NotNull(center);
            using var updateCenter=await client.PutAsJsonAsync($"api/v1/admin/master-data/centers/{center.Id}",
                new V180CenterInput("OF-C","Updated Center",from,null,true,center.RowVersion));
            Assert.Equal(HttpStatusCode.OK,updateCenter.StatusCode);
            var centerChanged=await updateCenter.Content.ReadFromJsonAsync<V180MasterDataRow>();Assert.NotNull(centerChanged);
            using(var inactivate=await client.PutAsJsonAsync($"api/v1/admin/master-data/centers/{center.Id}",
                new V180CenterInput("OF-C","Updated Center",from,null,false,centerChanged.RowVersion)))
                Assert.Equal(HttpStatusCode.OK,inactivate.StatusCode);
            Assert.False(await db.Centers.AsNoTracking().Where(x=>x.CenterCode=="OF-C").Select(x=>x.IsActive).SingleAsync());
            Console.WriteLine("CENTER_HTTP_SQL=PASS create/update/inactivate SQL_readback");
            using var siteCenterResponse=await client.PostAsJsonAsync("api/v1/admin/master-data/centers",
                new V180CenterInput("OF-C2","Site Center",from,null,true));
            Assert.Equal(HttpStatusCode.OK,siteCenterResponse.StatusCode);
            using var createSite=await client.PostAsJsonAsync("api/v1/admin/master-data/deployment-sites",
                new V180DeploymentSiteInput("OF-C2","OF-S","Original Site","OF-L",from,null,true));
            Assert.Equal(HttpStatusCode.OK,createSite.StatusCode);
            var site=await createSite.Content.ReadFromJsonAsync<V180MasterDataRow>();Assert.NotNull(site);
            using var updateSite=await client.PutAsJsonAsync($"api/v1/admin/master-data/deployment-sites/{site.Id}",
                new V180DeploymentSiteInput("OF-C2","OF-S","Updated Site","OF-L",from,null,true,site.RowVersion));
            Assert.Equal(HttpStatusCode.OK,updateSite.StatusCode);
            var siteChanged=await updateSite.Content.ReadFromJsonAsync<V180MasterDataRow>();Assert.NotNull(siteChanged);
            using(var inactivate=await client.PutAsJsonAsync($"api/v1/admin/master-data/deployment-sites/{site.Id}",
                new V180DeploymentSiteInput("OF-C2","OF-S","Updated Site","OF-L",from,null,false,siteChanged.RowVersion)))
                Assert.Equal(HttpStatusCode.OK,inactivate.StatusCode);
            Assert.False(await db.DeploymentSites.AsNoTracking().Where(x=>x.SiteCode=="OF-S").Select(x=>x.IsActive).SingleAsync());
            Assert.Equal(1,await db.DeploymentSiteLocationAssignments.AsNoTracking().CountAsync(x=>x.DeploymentSiteId==site.Id));
            Console.WriteLine("SITE_HTTP_SQL=PASS create/update/inactivate historical_location_assignment=1");
            Token("visitor");
            using(var denied=await client.PostAsJsonAsync("api/v1/admin/master-data/centers",
                new V180CenterInput("BAD","Denied",from,null,true)))
                Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            Token("admin",other.OrganizationId);
            using(var denied=await client.PostAsJsonAsync("api/v1/admin/master-data/centers",
                new V180CenterInput("OTHER","Denied",from,null,true)))
                Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            Assert.False(await db.Centers.AsNoTracking().AnyAsync(x=>x.CenterCode=="BAD"||x.CenterCode=="OTHER"));
            Console.WriteLine("OFFICIAL_DENIAL=PASS visitor=403 forged_org=403 no_cross_org_write");
        }
        finally
        {
            root.InitialCatalog="master";
            await using var cmd=server.CreateCommand();
            cmd.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
