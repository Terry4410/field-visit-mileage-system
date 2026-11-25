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
using NPOI.XSSF.UserModel;

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
            // The Owner confirmed this master is shared company-wide; export is admin-only.
            const string visitExport="api/v1/admin/query-exports/visit-types.xlsx";
            db.AddRange(
                new VisitType{VisitTypeCode="G1_ACTIVE",VisitTypeName="Synthetic Active",Description="Global shared type",SortOrder=10,IsActive=true,CreatedAt=now},
                new VisitType{VisitTypeCode="G1_INACTIVE",VisitTypeName="Synthetic Inactive",SortOrder=20,IsActive=false,CreatedAt=now});
            await db.SaveChangesAsync();
            Token();
            using(var r=await client.GetAsync(visitExport))
            {
                Assert.Equal(HttpStatusCode.OK,r.StatusCode);
                Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",r.Content.Headers.ContentType?.MediaType);
                using var workbook=new XSSFWorkbook(new MemoryStream(await r.Content.ReadAsByteArrayAsync()));
                var sheet=workbook.GetSheet("查詢結果");
                Assert.Equal("代碼",sheet.GetRow(0).GetCell(0).StringCellValue);
                Assert.Equal(2,sheet.LastRowNum);
                Assert.Equal("G1_ACTIVE",sheet.GetRow(1).GetCell(0).StringCellValue);
                Assert.Equal("Synthetic Active",sheet.GetRow(1).GetCell(1).StringCellValue);
                Assert.Equal("Global shared type",sheet.GetRow(1).GetCell(2).StringCellValue);
                Assert.Equal("是",sheet.GetRow(1).GetCell(4).StringCellValue);
                Assert.Equal("G1_INACTIVE",sheet.GetRow(2).GetCell(0).StringCellValue);
                Assert.Equal("否",sheet.GetRow(2).GetCell(4).StringCellValue);
            }
            Token(claimedOrg:otherOrg.OrganizationId);
            using(var r=await client.GetAsync(visitExport))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            Token();user.IsActive=false;await db.SaveChangesAsync();
            using(var r=await client.GetAsync(visitExport))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            user.IsActive=true;await db.SaveChangesAsync();
            hrState.EmploymentStatus=EmploymentStatuses.Leave;await db.SaveChangesAsync();
            using(var r=await client.GetAsync(visitExport))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            hrState.EmploymentStatus=EmploymentStatuses.Active;await db.SaveChangesAsync();
            role.IsActive=false;await db.SaveChangesAsync();
            using(var r=await client.GetAsync(visitExport))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            role.IsActive=true;await db.SaveChangesAsync();
            foreach(var selected in new[]{"visitor","leader","hr","supervisor"})
            {
                role.RoleCode=selected;await db.SaveChangesAsync();Token(selected);
                using var denied=await client.GetAsync(visitExport);
                Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            }
            role.RoleCode="admin";await db.SaveChangesAsync();Token();
            client.DefaultRequestHeaders.Authorization=null;
            using(var r=await client.GetAsync(visitExport))Assert.Equal(HttpStatusCode.Unauthorized,r.StatusCode);
            Token();
            // Enabling admin SQL DML must not unlock operations for stale or forged admin scope.
            var unused = new VisitType { VisitTypeCode="G1_SAFE_DELETE", VisitTypeName="Unused synthetic type",
                SortOrder=30,IsActive=true,CreatedAt=now };
            var project = new Project { OrganizationId=org.OrganizationId,TeamId=team.TeamId,
                ProjectCode="G1_SCOPE",ProjectName="Synthetic scope",LocationMode="List",
                StartDate=BusinessTime.Today,IsActive=true,CreatedAt=now };
            db.AddRange(unused,project);await db.SaveChangesAsync();
            var safeDelete=$"api/v1/admin/visit-types/{unused.VisitTypeId}/permanent";
            var impact=$"api/v1/admin/visit-types/{unused.VisitTypeId}/delete-impact";
            var projectLocations=$"api/v1/admin/projects/{project.ProjectId}/locations";
            async Task DeniedAdminMaintenance()
            {
                using(var res=await client.GetAsync(impact)) Assert.Equal(HttpStatusCode.Forbidden,res.StatusCode);
                using(var res=await client.DeleteAsync(safeDelete)) Assert.Equal(HttpStatusCode.Forbidden,res.StatusCode);
                using(var body=new StringContent("{\"locationIds\":[]}",Encoding.UTF8,"application/json"))
                using(var res=await client.PutAsync(projectLocations,body)) Assert.Equal(HttpStatusCode.Forbidden,res.StatusCode);
                Assert.True(await db.VisitTypes.AsNoTracking().AnyAsync(x=>x.VisitTypeId==unused.VisitTypeId));
            }
            Token(claimedOrg:otherOrg.OrganizationId);await DeniedAdminMaintenance();
            Token();role.IsActive=false;await db.SaveChangesAsync();await DeniedAdminMaintenance();
            role.IsActive=true;await db.SaveChangesAsync();
            using(var res=await client.GetAsync(impact)) Assert.Equal(HttpStatusCode.OK,res.StatusCode);
            using(var body=new StringContent("{\"locationIds\":[]}",Encoding.UTF8,"application/json"))
            using(var res=await client.PutAsync(projectLocations,body)) Assert.Equal(HttpStatusCode.OK,res.StatusCode);
            using(var res=await client.DeleteAsync(safeDelete)) Assert.Equal(HttpStatusCode.NoContent,res.StatusCode);
            Assert.False(await db.VisitTypes.AsNoTracking().AnyAsync(x=>x.VisitTypeId==unused.VisitTypeId));
            // Re-check historical references at execution, not only at preview.
            var usedType=await db.VisitTypes.SingleAsync(x=>x.VisitTypeCode=="G1_ACTIVE");
            var trip=new VisitTrip{TripNo="G1_DELETE_HISTORY",UserId=user.UserId,OrganizationId=org.OrganizationId,
                TeamId=team.TeamId,VisitDate=BusinessTime.Today,Status="Draft",CreatedAt=now,CreatedByUserId=user.UserId};
            db.Add(trip);await db.SaveChangesAsync();
            db.Add(new VisitTripStop{VisitTripId=trip.VisitTripId,StopSequence=1,VisitTypeId=usedType.VisitTypeId,
                ProjectId=project.ProjectId,CreatedAt=now});
            var rate=new MileageRateRule{OrganizationId=org.OrganizationId,RuleName="Used rate",VehicleType="MOTORCYCLE",
                RatePerKm=2.5m,EffectiveFrom=BusinessTime.Today,IsActive=true,CreatedAt=now};
            var center=new Center{OrganizationId=org.OrganizationId,CenterCode="G1_USED_CENTER",CenterName="Used center",
                EffectiveFrom=BusinessTime.Today,IsActive=true};
            db.AddRange(rate,center);await db.SaveChangesAsync();
            db.Add(new MileageCalculation{VisitTripId=trip.VisitTripId,MileageRateRuleId=rate.MileageRateRuleId,
                ApprovedAmount=25m,CreatedAt=now});
            db.Add(new TeamCenterAssignment{TeamId=team.TeamId,CenterId=center.CenterId,
                EffectiveFrom=BusinessTime.Today,CreatedAt=now});
            await db.SaveChangesAsync();
            async Task Refused(string path)
            {
                using var response=await client.DeleteAsync(path);
                Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
            }
            await Refused($"api/v1/admin/visit-types/{usedType.VisitTypeId}/permanent");
            await Refused($"api/v1/admin/projects/{project.ProjectId}/permanent");
            await Refused($"api/v1/admin/mileage-rate-rules/{rate.MileageRateRuleId}/permanent");
            await Refused($"api/v1/admin/centers/{center.CenterId}/permanent");
            await Refused($"api/v1/admin/teams/{team.TeamId}/permanent");
            Assert.True(await db.VisitTripStops.AsNoTracking().AnyAsync(x=>x.VisitTripId==trip.VisitTripId && x.VisitTypeId==usedType.VisitTypeId && x.ProjectId==project.ProjectId));
            Assert.Equal(25m,await db.MileageCalculations.AsNoTracking().Where(x=>x.VisitTripId==trip.VisitTripId).Select(x=>x.ApprovedAmount).SingleAsync());
            var unusedCenter=new Center{OrganizationId=org.OrganizationId,CenterCode="G1_UNUSED_CENTER",CenterName="Unused center",EffectiveFrom=BusinessTime.Today,IsActive=true};
            var unusedProject=new Project{OrganizationId=org.OrganizationId,ProjectCode="G1_UNUSED_PROJECT",ProjectName="Unused project",LocationMode="List",IsActive=true,CreatedAt=now};
            var unusedRate=new MileageRateRule{OrganizationId=org.OrganizationId,RuleName="Unused rate",VehicleType="MOTORCYCLE",RatePerKm=3m,EffectiveFrom=BusinessTime.Today,IsActive=true,CreatedAt=now};
            var unusedTeam=new Team{OrganizationId=org.OrganizationId,TeamCode="G1_UNUSED_TEAM",TeamName="Unused team",IsActive=true,CreatedAt=now};
            db.AddRange(unusedCenter,unusedProject,unusedRate,unusedTeam);await db.SaveChangesAsync();
            foreach(var path in new[]{
                $"api/v1/admin/centers/{unusedCenter.CenterId}/permanent",
                $"api/v1/admin/projects/{unusedProject.ProjectId}/permanent",
                $"api/v1/admin/mileage-rate-rules/{unusedRate.MileageRateRuleId}/permanent",
                $"api/v1/admin/teams/{unusedTeam.TeamId}/permanent"})
            {using var response=await client.DeleteAsync(path);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);}
            Assert.False(await db.Projects.AsNoTracking().AnyAsync(x=>x.ProjectId==unusedProject.ProjectId));
            Assert.False(await db.Centers.AsNoTracking().AnyAsync(x=>x.CenterId==unusedCenter.CenterId));
            Assert.False(await db.Teams.AsNoTracking().AnyAsync(x=>x.TeamId==unusedTeam.TeamId));
            Assert.False(await db.MileageRateRules.AsNoTracking().AnyAsync(x=>x.MileageRateRuleId==unusedRate.MileageRateRuleId));
            var location=new Location{OrganizationId=org.OrganizationId,LocationCode="G1_SITE_LOCATION",LocationName="Synthetic location",IsActive=true,ApprovalStatus="Approved",CreatedAt=now};
            db.Add(location);await db.SaveChangesAsync();
            DeploymentSite Site(string code)=>new(){CenterId=center.CenterId,SiteCode=code,SiteName=code,EffectiveFrom=BusinessTime.Today,IsActive=true,CreatedAt=now};
            var usedSite=Site("G1_USED_SITE");var unusedSite=Site("G1_UNUSED_SITE");
            db.AddRange(usedSite,unusedSite);await db.SaveChangesAsync();
            foreach(var site in new[]{usedSite,unusedSite})
                db.Add(new DeploymentSiteLocationAssignment{DeploymentSiteId=site.DeploymentSiteId,LocationId=location.LocationId,EffectiveFrom=site.EffectiveFrom,ChangeReason="UAT Business Admin initial assignment",CreatedAt=now});
            await db.SaveChangesAsync();
            trip.StartDeploymentSiteId=usedSite.DeploymentSiteId;await db.SaveChangesAsync();
            await Refused($"api/v1/admin/deployment-sites/{usedSite.DeploymentSiteId}/permanent");
            using(var response=await client.DeleteAsync($"api/v1/admin/deployment-sites/{unusedSite.DeploymentSiteId}/permanent"))Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);
            Assert.True(await db.DeploymentSiteLocationAssignments.AsNoTracking().AnyAsync(x=>x.DeploymentSiteId==usedSite.DeploymentSiteId));
            Assert.False(await db.DeploymentSiteLocationAssignments.AsNoTracking().AnyAsync(x=>x.DeploymentSiteId==unusedSite.DeploymentSiteId));
            Assert.True(await db.Locations.AsNoTracking().AnyAsync(x=>x.LocationId==location.LocationId));
            async Task<(User User,Person Person,Employment Employment)> SeedPerson(string code)
            {
                var u=new User{OrganizationId=org.OrganizationId,EmployeeNo=code,DisplayName=code,IsActive=true,CreatedAt=now};
                db.Add(u);await db.SaveChangesAsync();
                var p=new Person{DisplayName=code,LegacyUserId=u.UserId,CreatedAt=now,CreatedByUserId=user.UserId};
                db.Add(p);await db.SaveChangesAsync();
                var e=new Employment{PersonId=p.PersonId,OrganizationId=org.OrganizationId,EmployeeNo=code,LegacyUserId=u.UserId,SourceType="Synthetic"};
                db.Add(e);await db.SaveChangesAsync();
                db.Add(new UserIdentityProfile{UserId=u.UserId,EmploymentId=e.EmploymentId,UserCode=code,UserType=UserTypes.Internal,CreatedAt=now});
                await db.SaveChangesAsync();return (u,p,e);
            }
            var unusedPerson=await SeedPerson("G1_UNUSED_PERSON");
            var usedPerson=await SeedPerson("G1_USED_PERSON");
            var sharedPerson=await SeedPerson("G1_SHARED_PERSON");
            db.Add(new AuditLog{UserId=usedPerson.User.UserId,EntityType="User",Action="SyntheticPriorUse",CreatedAt=now});
            db.Add(new Employment{PersonId=sharedPerson.Person.PersonId,OrganizationId=otherOrg.OrganizationId,EmployeeNo="G1_OTHER_EMP",SourceType="Synthetic"});
            await db.SaveChangesAsync();
            await Refused($"api/v1/admin/people/{usedPerson.User.UserId}/permanent");
            await Refused($"api/v1/admin/people/{sharedPerson.User.UserId}/permanent");
            using(var response=await client.DeleteAsync($"api/v1/admin/people/{unusedPerson.User.UserId}/permanent"))Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);
            Assert.True(await db.Users.AsNoTracking().AnyAsync(x=>x.UserId==usedPerson.User.UserId));
            Assert.Equal(2,await db.Employments.AsNoTracking().CountAsync(x=>x.PersonId==sharedPerson.Person.PersonId));
            Assert.False(await db.Users.AsNoTracking().AnyAsync(x=>x.UserId==unusedPerson.User.UserId));
            Assert.False(await db.Employments.AsNoTracking().AnyAsync(x=>x.EmploymentId==unusedPerson.Employment.EmploymentId));
            Assert.False(await db.Persons.AsNoTracking().AnyAsync(x=>x.PersonId==unusedPerson.Person.PersonId));
            Console.WriteLine("ADMIN_SAFE_DELETE_MASTER_HISTORY_HTTP_PASS used=DENIED unused=DELETED history=UNCHANGED sites=PASS people=PASS sharedEmployment=DENIED");
            // Remove disposable historical fixture before the independent export checks below.
            await db.MileageCalculations.Where(x=>x.VisitTripId==trip.VisitTripId).ExecuteDeleteAsync();
            await db.VisitTripStops.Where(x=>x.VisitTripId==trip.VisitTripId).ExecuteDeleteAsync();
            await db.VisitTrips.Where(x=>x.VisitTripId==trip.VisitTripId).ExecuteDeleteAsync();
            Console.WriteLine("ADMIN_MAINTENANCE_LIVE_SCOPE_HTTP_PASS forgedOrg=DENIED revokedRole=DENIED projectLocations=PASS safeDelete=PASS");
            // The old blanket date gate incorrectly rejected every Leader
            // historical download, even for an empty authorized result.
            // The real row-level team/organization negative tests live in
            // V180ThreeRoleHistoricalExportScopeTests.
            var historical = $"?startDate={BusinessTime.Today.AddDays(-15):yyyy-MM-dd}&endDate={BusinessTime.Today:yyyy-MM-dd}";
            foreach (var selected in new[]{"leader","visitor","admin"})
            {
                role.RoleCode=selected;await db.SaveChangesAsync();Token(selected);
                foreach (var format in new[]{"xlsx","pdf"})
                {
                    using var response=await client.GetAsync($"api/v1/query/trips/export.{format}{historical}");
                    Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                    var bytes=await response.Content.ReadAsByteArrayAsync();
                    Assert.True(bytes.Length>200);
                    if(format=="pdf")
                        Assert.Equal("%PDF-",Encoding.ASCII.GetString(bytes.AsSpan(0,5)));
                    else
                        Assert.Equal("PK",Encoding.ASCII.GetString(bytes.AsSpan(0,2)));
                }
            }
            role.RoleCode="admin";await db.SaveChangesAsync();Token();
            // Inner test-only filter revokes after workbook generation, before the outer G1 recheck.
            await using var raceFactory=factory.WithWebHostBuilder(b=>b.ConfigureServices(s=>s.Configure<MvcOptions>(o=>o.Filters.Add(new RevokeAfterAction(async()=>{
                await using var change=Db();await change.Users.Where(x=>x.UserId==user.UserId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsActive,false));
            })))));
            using var race=raceFactory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});race.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
            using(var r=await race.GetAsync("api/v1/admin/query-exports/teams.xlsx"))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            await using(var restore=Db())await restore.Users.Where(x=>x.UserId==user.UserId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsActive,true));
            using(var r=await race.GetAsync(visitExport))Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            Console.WriteLine("G1_REPORT_HTTP_SQL_PASS positive=PASS preAndPostRevocation=DENIED roleOrg=DENIED unapprovedRoles=DENIED visitTypeAdminWorkbook=PASS visitTypeOtherRoles=DENIED");
        }
        finally{await using var cmd=admin.CreateCommand();cmd.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";await cmd.ExecuteNonQueryAsync();}
    }
    sealed class RevokeAfterAction(Func<Task> revoke):IAsyncActionFilter,IOrderedFilter
    {
        public int Order=>100;
        public async Task OnActionExecutionAsync(ActionExecutingContext c,ActionExecutionDelegate next){await next();await revoke();}
    }
}
