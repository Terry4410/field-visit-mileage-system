using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using NPOI.XSSF.UserModel;
using Xunit;

namespace FieldVisit.Application.Tests;

// Isolated SQL only. Covers the actual shared-location HTTP read and workbook,
// not just a route string or a mock authorization function.
public sealed class ScopedLocationHttpSqlTests
{
    const string Key="scope-note-isolated-signing-key-at-least-32-characters";
    [Fact]
    public async Task Scoped_location_list_excel_and_maintenance_reject_stale_team_scope()
    {
        var raw=Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(raw)){if(Environment.GetEnvironmentVariable("E1_R1_REQUIRED")=="1")throw new Exception("SCOPED_LOCATION_SQL_REQUIRED");return;}
        var root=new SqlConnectionStringBuilder(raw){Pooling=false,ConnectTimeout=5};
        Assert.Contains(root.DataSource,new[]{"localhost,1433","127.0.0.1,1433"});
        Assert.Equal("master",root.InitialCatalog);
        var name="ScopedNoteIsolated_"+Guid.NewGuid().ToString("N");
        await using var admin=new SqlConnection(root.ConnectionString);await admin.OpenAsync();
        await using(var cmd=admin.CreateCommand()){cmd.CommandText=$"CREATE DATABASE [{name}]";await cmd.ExecuteNonQueryAsync();}
        root.InitialCatalog=name;
        AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(root.ConnectionString).Options);
        try
        {
            await using var db=Db();await db.Database.EnsureCreatedAsync();
            var now=DateTime.UtcNow;var from=BusinessTime.Today.AddDays(-10);
            var org=new Organization{OrganizationCode="SCOPE",OrganizationName="Synthetic",IsActive=true,CreatedAt=now};
            db.Add(org);await db.SaveChangesAsync();
            var a=new Team{OrganizationId=org.OrganizationId,TeamCode="A",TeamName="Synthetic A",IsActive=true,CreatedAt=now};
            var b=new Team{OrganizationId=org.OrganizationId,TeamCode="B",TeamName="Synthetic B",IsActive=true,CreatedAt=now};
            db.AddRange(a,b);await db.SaveChangesAsync();
            var user=new User{OrganizationId=org.OrganizationId,TeamId=b.TeamId,EmployeeNo="SCOPED",DisplayName="Synthetic Visitor",IsActive=true,CreatedAt=now};
            var role=new Role{RoleCode="visitor",RoleName="Visitor",IsActive=true,CreatedAt=now};
            db.AddRange(user,role);await db.SaveChangesAsync();
            db.Add(new UserRole{UserId=user.UserId,RoleId=role.RoleId,AssignedAt=now});
            db.Add(new UserRoleAssignment{UserId=user.UserId,RoleId=role.RoleId,EffectiveFrom=from,CreatedAt=now});
            db.Add(new UserEmploymentPeriod{UserId=user.UserId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=from,SourceType="Synthetic",CreatedAt=now});
            var aa=new UserTeamAssignment{UserId=user.UserId,TeamId=a.TeamId,EffectiveFrom=from,CreatedAt=now};
            var ab=new UserTeamAssignment{UserId=user.UserId,TeamId=b.TeamId,IsPrimary=true,EffectiveFrom=from,CreatedAt=now};
            var sa=new UserTeamScope{UserId=user.UserId,TeamId=a.TeamId,IsActive=true,AssignedAt=now};
            var sb=new UserTeamScope{UserId=user.UserId,TeamId=b.TeamId,IsPrimary=true,IsActive=true,AssignedAt=now};
            db.AddRange(aa,ab,sa,sb);
            var leader=new User{OrganizationId=org.OrganizationId,TeamId=b.TeamId,EmployeeNo="LEADER",DisplayName="Synthetic Leader",IsActive=true,CreatedAt=now};
            var leaderRole=new Role{RoleCode="leader",RoleName="Leader",IsActive=true,CreatedAt=now};
            var person=new Person{DisplayName="Synthetic Leader",CreatedAt=now};
            db.AddRange(leader,leaderRole,person);await db.SaveChangesAsync();
            var employment=new Employment{PersonId=person.PersonId,OrganizationId=org.OrganizationId,EmployeeNo="LEADER",LegacyUserId=leader.UserId,SourceType="Synthetic"};
            db.Add(employment);await db.SaveChangesAsync();
            db.Add(new UserIdentityProfile{UserId=leader.UserId,EmploymentId=employment.EmploymentId,UserType=UserTypes.Internal,UserCode="LEADER",IdentityProvider="Demo",CreatedAt=now});
            db.Add(new EmploymentStatusPeriod{EmploymentId=employment.EmploymentId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=from,SourceType="Synthetic"});
            db.Add(new UserRole{UserId=leader.UserId,RoleId=leaderRole.RoleId,AssignedAt=now});
            db.Add(new UserRoleAssignment{UserId=leader.UserId,RoleId=leaderRole.RoleId,EffectiveFrom=from,CreatedAt=now});
            var leaderAssignment=new UserTeamAssignment{UserId=leader.UserId,TeamId=b.TeamId,IsPrimary=true,EffectiveFrom=from,CreatedAt=now};
            var leaderScope=new UserTeamScope{UserId=leader.UserId,TeamId=b.TeamId,IsPrimary=true,IsActive=true,AssignedAt=now};
            db.AddRange(leaderAssignment,leaderScope);
            db.Add(new TeamLeaderAssignment{TeamId=b.TeamId,EmploymentId=employment.EmploymentId,EffectiveFrom=from,CreatedAt=now});
            var loc=new Location{OrganizationId=org.OrganizationId,LocationCode="SHARED",LocationName="Synthetic shared location",LocationType="Customer",ApprovalStatus="Approved",IsActive=true,CreatedAt=now};
            var leaderLocation=new Location{OrganizationId=org.OrganizationId,TeamId=b.TeamId,LocationCode="LEADER-B",LocationName="Leader team location",LocationType="Customer",ApprovalStatus="Approved",IsActive=true,CreatedAt=now};
            db.AddRange(loc,leaderLocation);await db.SaveChangesAsync();
            var na=new TeamLocationNote{TeamId=a.TeamId,LocationId=loc.LocationId,Note="SECRET_A",CreatedAt=now,CreatedByUserId=user.UserId};
            var nb=new TeamLocationNote{TeamId=b.TeamId,LocationId=loc.LocationId,Note="VISIBLE_B",CreatedAt=now,CreatedByUserId=user.UserId};
            db.AddRange(na,nb);await db.SaveChangesAsync();
            var leaderNote=new TeamLocationNote{TeamId=b.TeamId,LocationId=leaderLocation.LocationId,Note="LEADER_B",CreatedAt=now,CreatedByUserId=leader.UserId};
            db.Add(leaderNote);await db.SaveChangesAsync();
            db.AddRange(new TeamLocationNoteHistory{TeamLocationNoteId=na.TeamLocationNoteId,TeamId=a.TeamId,LocationId=loc.LocationId,Action="Create",NewNote=na.Note,ChangedAt=now,ChangedByUserId=user.UserId},
                new TeamLocationNoteHistory{TeamLocationNoteId=nb.TeamLocationNoteId,TeamId=b.TeamId,LocationId=loc.LocationId,Action="Create",NewNote=nb.Note,ChangedAt=now.AddMinutes(-1),ChangedByUserId=user.UserId},
                new TeamLocationNoteHistory{TeamLocationNoteId=leaderNote.TeamLocationNoteId,TeamId=b.TeamId,LocationId=leaderLocation.LocationId,Action="Create",NewNote=leaderNote.Note,ChangedAt=now,ChangedByUserId=leader.UserId});
            await db.SaveChangesAsync();
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(w=>{
                w.UseSetting("ConnectionStrings:DefaultConnection",root.ConnectionString);w.UseSetting("Auth:Mode","Demo");w.UseSetting("Auth:JwtKey",Key);
                w.UseSetting("BackgroundJobs:WorkerEnabled","false");w.UseSetting("BackgroundJobs:RecoverOnStartup","false");
            });
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            void TokenFor(HttpClient target,User actor,string roleCode,params Team[] teams)
            {
                var claims=new[]{new Claim(ClaimTypes.NameIdentifier,actor.UserId.ToString()),new Claim(ClaimTypes.Role,roleCode),
                    new Claim("employee_no",actor.EmployeeNo!),new Claim("organization_id",org.OrganizationId.ToString()),new Claim("team_id",b.TeamId.ToString()),
                    new Claim("team_scopes",JsonSerializer.Serialize(teams.Select(t=>new TeamScopeDto(t.TeamId,t.TeamName,t.TeamId==b.TeamId))))};
                target.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer:"FieldVisit.UAT",audience:"FieldVisit.UAT.Users",claims:claims,expires:DateTime.UtcNow.AddMinutes(10),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256))));
            }
            void Token(params Team[] teams)=>TokenFor(client,user,"visitor",teams);
            const string list="api/v1/scoped-location-export/locations";
            const string excel=list+".xlsx";
            var maintenance=$"api/v1/locations/{loc.LocationId}/maintenance";
            async Task Denied(){foreach(var path in new[]{list,excel,maintenance}){using var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);}}
            async Task Visible(string note)
            {
                using(var response=await client.GetAsync(list)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains(note,await response.Content.ReadAsStringAsync());}
                using(var response=await client.GetAsync(excel)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var stream=new MemoryStream(await response.Content.ReadAsByteArrayAsync());using var book=new XSSFWorkbook(stream);Assert.Contains(note,Enumerable.Range(1,book.GetSheetAt(0).LastRowNum).Select(i=>book.GetSheetAt(0).GetRow(i).GetCell(7).StringCellValue));}
                using(var response=await client.GetAsync(maintenance)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains(note,await response.Content.ReadAsStringAsync());}
            }
            Token(a,b);await Visible("SECRET_A");
            using(var filtered=await client.GetAsync(excel+"?teamId="+b.TeamId+"&status=Approved"))
            {
                Assert.Equal(HttpStatusCode.OK,filtered.StatusCode);using var stream=new MemoryStream(await filtered.Content.ReadAsByteArrayAsync());using var book=new XSSFWorkbook(stream);
                var sheet=book.GetSheetAt(0);Assert.Equal(1,sheet.LastRowNum);Assert.Equal("LEADER-B",sheet.GetRow(1).GetCell(0).StringCellValue);
            }
            // Scope projection and effective dates are independently checked.
            sa.IsActive=false;await db.SaveChangesAsync();await Denied();
            aa.EffectiveTo=BusinessTime.Today.AddDays(-1);await db.SaveChangesAsync();await Denied();
            Token(b);await Visible("VISIBLE_B");
            using(var response=await client.GetAsync(maintenance)){Assert.DoesNotContain("SECRET_A",await response.Content.ReadAsStringAsync());}
            using(var response=await client.GetAsync(list+"?q=SECRET_A")){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(0,json.RootElement.GetArrayLength());}
            role.IsActive=false;await db.SaveChangesAsync();await Denied();
            role.IsActive=true;await db.SaveChangesAsync();
            // Revoke after the inner action generates bytes, before outer guard returns.
            await using var raceFactory=factory.WithWebHostBuilder(w=>w.ConfigureServices(s=>s.Configure<MvcOptions>(o=>o.Filters.Add(new RevokeAfterAction(async()=>{
                await using var change=Db();await change.UserTeamScopes.Where(x=>x.UserTeamScopeId==sb.UserTeamScopeId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsActive,false));
            })))));
            using var race=raceFactory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});race.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
            using(var response=await race.GetAsync(excel))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            using var leaderClient=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            TokenFor(leaderClient,leader,"leader",b);
            var leaderMaintenance=$"api/v1/locations/{leaderLocation.LocationId}/maintenance";
            foreach(var path in new[]{list,excel,leaderMaintenance})
            {
                using var response=await leaderClient.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                if(path==excel){using var stream=new MemoryStream(await response.Content.ReadAsByteArrayAsync());using var book=new XSSFWorkbook(stream);Assert.Contains("LEADER_B",Enumerable.Range(1,book.GetSheetAt(0).LastRowNum).Select(i=>book.GetSheetAt(0).GetRow(i).GetCell(7).StringCellValue));}
                else Assert.Contains("LEADER_B",await response.Content.ReadAsStringAsync());
            }
            TokenFor(leaderClient,leader,"leader",a,b);
            foreach(var path in new[]{list,excel,leaderMaintenance}){using var response=await leaderClient.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);}
            TokenFor(leaderClient,leader,"leader",b);
            leaderScope.IsActive=false;await db.SaveChangesAsync();
            foreach(var path in new[]{list,excel,leaderMaintenance}){using var response=await leaderClient.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);}
            leaderScope.IsActive=true;await db.SaveChangesAsync();
            leaderAssignment.EffectiveTo=BusinessTime.Today.AddDays(-1);await db.SaveChangesAsync();
            foreach(var path in new[]{list,excel,leaderMaintenance}){using var response=await leaderClient.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);}
            Console.WriteLine("SCOPED_LOCATION_HTTP_SQL_PASS visitor=ALLOW_REVOKE_DENY leader=ALLOW_STALE_SCOPE_DENY_SCOPE_REVOKE_DENY_ASSIGNMENT_REVOKE_DENY search=SCOPED postGenerationRevocation=DENY");
        }
        finally{await using var cmd=admin.CreateCommand();cmd.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";await cmd.ExecuteNonQueryAsync();}
    }
    sealed class RevokeAfterAction(Func<Task> revoke):IAsyncActionFilter,IOrderedFilter
    {
        public int Order=>100;
        public async Task OnActionExecutionAsync(ActionExecutingContext c,ActionExecutionDelegate next){await next();await revoke();}
    }
}
