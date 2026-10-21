using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

// Synthetic EF-shaped fixture, never a clean baseline or UAT schema claim.
public sealed class G1ReportExportHttpSqlTests
{
    const string Key="g1-report-isolated-signing-key-at-least-32-characters";
    [Fact]
    public async Task G1_real_report_http_sql_revocation_policy_and_scope_denials()
    {
        var raw=Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(raw)) { if(Environment.GetEnvironmentVariable("E1_R1_REQUIRED")=="1")throw new Exception("G1_SQL_REQUIRED"); return; }
        var root=new SqlConnectionStringBuilder(raw){Pooling=false,ConnectTimeout=5};
        Assert.Contains(root.DataSource,new[]{"localhost,1433","127.0.0.1,1433"});Assert.Equal("master",root.InitialCatalog);
        var name="G1ReportIsolated_"+Guid.NewGuid().ToString("N");
        await using var admin=new SqlConnection(root.ConnectionString);await admin.OpenAsync();
        await using(var cmd=admin.CreateCommand()){cmd.CommandText=$"CREATE DATABASE [{name}]";await cmd.ExecuteNonQueryAsync();}
        root.InitialCatalog=name;
        AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(root.ConnectionString).Options);
        try
        {
            await using var db=Db();await db.Database.EnsureCreatedAsync();
            var now=DateTime.UtcNow;
            var org=new Organization{OrganizationCode="G1",OrganizationName="Synthetic",IsActive=true,CreatedAt=now};
            var otherOrg=new Organization{OrganizationCode="OUT",OrganizationName="Synthetic Other",IsActive=true,CreatedAt=now};
            db.AddRange(org,otherOrg);await db.SaveChangesAsync();
            var team=new Team{OrganizationId=org.OrganizationId,TeamCode="G1",TeamName="Synthetic Team",IsActive=true,CreatedAt=now};db.Add(team);await db.SaveChangesAsync();
            var user=new User{OrganizationId=org.OrganizationId,TeamId=team.TeamId,EmployeeNo="G1",DisplayName="Synthetic Admin",IsActive=true,CreatedAt=now};
            var role=new Role{RoleCode="admin",RoleName="Admin",IsActive=true,CreatedAt=now};db.AddRange(user,role);await db.SaveChangesAsync();
            db.Add(new UserRole{UserId=user.UserId,RoleId=role.RoleId,AssignedAt=now});await db.SaveChangesAsync();
            var config=new Action<IWebHostBuilder>(b=>{
                b.UseSetting("ConnectionStrings:DefaultConnection",root.ConnectionString);b.UseSetting("Auth:Mode","Demo");b.UseSetting("Auth:JwtKey",Key);
                b.UseSetting("BackgroundJobs:WorkerEnabled","false");b.UseSetting("BackgroundJobs:RecoverOnStartup","false");
            });
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(config);
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            void Token(string selected="admin",int? claimedOrg=null){
                var claims=new[]{new Claim(ClaimTypes.NameIdentifier,user.UserId.ToString()),new Claim(ClaimTypes.Role,selected),new Claim("employee_no","G1"),new Claim("organization_id",(claimedOrg??org.OrganizationId).ToString()),new Claim("team_id",team.TeamId.ToString())};
                client.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer:"FieldVisit.UAT",audience:"FieldVisit.UAT.Users",claims:claims,expires:DateTime.UtcNow.AddMinutes(10),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256))));
            }
            Token();using(var missingHr=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,missingHr.StatusCode);
            var hrState=new UserEmploymentPeriod{UserId=user.UserId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=new DateOnly(2020,1,1),SourceType="Synthetic",CreatedAt=now};
            db.Add(hrState);await db.SaveChangesAsync();
            using(var response=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            user.IsActive=false;await db.SaveChangesAsync();using(var r=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            user.IsActive=true;await db.SaveChangesAsync();
            hrState.EmploymentStatus=EmploymentStatuses.Leave;await db.SaveChangesAsync();using(var r=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            hrState.EmploymentStatus=EmploymentStatuses.Active;await db.SaveChangesAsync();
            var expiredRole=new UserRoleAssignment{UserId=user.UserId,RoleId=role.RoleId,EffectiveFrom=BusinessTime.Today.AddDays(-2),EffectiveTo=BusinessTime.Today.AddDays(-1),CreatedAt=now};
            db.Add(expiredRole);await db.SaveChangesAsync();using(var r=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            db.Remove(expiredRole);await db.SaveChangesAsync();
            role.IsActive=false;await db.SaveChangesAsync();using(var r=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            role.IsActive=true;await db.SaveChangesAsync();Token(claimedOrg:otherOrg.OrganizationId);using(var r=await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            foreach(var selected in new[]{"visitor","leader","hr"}){Token(selected);using var r=await client.GetAsync("api/v1/admin/query-exports/personnel-full.xlsx");Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);}
            // Visit types are global reference data and the workbook exposes only the
            // explicitly reviewed reference columns; live admin checks still bracket export.
            Token();using(var r=await client.GetAsync("api/v1/admin/query-exports/visit-types.xlsx"))
            {
                Assert.Equal(HttpStatusCode.OK,r.StatusCode);
                using var workbook=new NPOI.XSSF.UserModel.XSSFWorkbook(await r.Content.ReadAsStreamAsync());
                var sheets=Enumerable.Range(0,workbook.NumberOfSheets).Select(workbook.GetSheetAt).ToList();
                Assert.Contains(sheets, sheet => {
                    var row=sheet.GetRow(0);
                    return row is not null && Enumerable.Range(0,5).Select(i=>row.GetCell(i)?.StringCellValue)
                        .SequenceEqual(new[]{"代碼","名稱","說明","排序","啟用"});
                });
            }
            foreach(var selected in new[]{"visitor","leader","hr"}) { Token(selected);
                using var r=await client.GetAsync("api/v1/admin/query-exports/visit-types.xlsx");
                Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            }
            role.RoleCode="leader";await db.SaveChangesAsync();Token("leader");
            // Inconsistent cross-organization test data must not leak merely because
            // a trip has an allowed team id. This uses the real SQL query path.
            db.VisitTrips.AddRange(
                new VisitTrip { TripNo="SYN-LOCAL", UserId=user.UserId, OrganizationId=org.OrganizationId,
                    TeamId=team.TeamId, VisitDate=BusinessTime.Today, CreatedAt=now, CreatedByUserId=user.UserId },
                new VisitTrip { TripNo="SYN-OTHER", UserId=user.UserId, OrganizationId=otherOrg.OrganizationId,
                    TeamId=team.TeamId, VisitDate=BusinessTime.Today, CreatedAt=now, CreatedByUserId=user.UserId });
            await db.SaveChangesAsync();
            var actor=new CurrentUserDto(user.UserId,"G1","Synthetic",null,org.OrganizationId,team.TeamId,
                "Synthetic Team",new[]{"leader"},new[]{new TeamScopeDto(team.TeamId,"Synthetic Team",true)});
            var queryRepo=new V160FinalRepository(db,new V170AccessControl(db));
            var scoped=await queryRepo.QueryTripsAsync(actor,new TripQueryRequest(),false,default);
            Assert.Single(scoped.Items);
            Assert.Equal("SYN-LOCAL",scoped.Items[0].TripNo);
            var noOrg=actor with { OrganizationId=null };
            Assert.Empty((await queryRepo.QueryTripsAsync(noOrg,new TripQueryRequest(),false,default)).Items);
            using(var r=await client.GetAsync("api/v1/query/trips/export.xlsx")){
                Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
                Assert.Contains("REPORT_EXPORT_HISTORICAL_TEAM_POLICY_HOLD",await r.Content.ReadAsStringAsync());
            }
            role.RoleCode="admin";await db.SaveChangesAsync();Token();
            // Inner test-only filter revokes after workbook generation, before the outer G1 recheck.
            await using var raceFactory=factory.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.Configure<MvcOptions>(o=>o.Filters.Add(new RevokeAfterAction(async()=>{
                await using var change=Db();await change.Users.Where(x=>x.UserId==user.UserId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsActive,false));
            })))));
            using var race=raceFactory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});race.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
            using(var r=await race.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            Console.WriteLine("G1_REPORT_HTTP_SQL_PASS positive=PASS preAndPostRevocation=DENIED roleOrg=DENIED unapprovedRoles=DENIED visitTypeAdmin=PASS visitTypeOtherRoles=DENIED");
        }
        finally{await using var cmd=admin.CreateCommand();cmd.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";await cmd.ExecuteNonQueryAsync();}
    }
    sealed class RevokeAfterAction(Func<Task> revoke):IAsyncActionFilter,IOrderedFilter
    {
        public int Order=>100;
        public async Task OnActionExecutionAsync(ActionExecutingContext c,ActionExecutionDelegate next){await next();await revoke();}
    }
}
