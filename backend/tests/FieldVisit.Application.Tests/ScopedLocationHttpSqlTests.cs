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
            var loc=new Location{OrganizationId=org.OrganizationId,LocationCode="SHARED",LocationName="Synthetic shared location",LocationType="Customer",ApprovalStatus="Approved",IsActive=true,CreatedAt=now};
            db.Add(loc);await db.SaveChangesAsync();
            var na=new TeamLocationNote{TeamId=a.TeamId,LocationId=loc.LocationId,Note="SECRET_A",CreatedAt=now,CreatedByUserId=user.UserId};
            var nb=new TeamLocationNote{TeamId=b.TeamId,LocationId=loc.LocationId,Note="VISIBLE_B",CreatedAt=now,CreatedByUserId=user.UserId};
            db.AddRange(na,nb);await db.SaveChangesAsync();
            db.AddRange(new TeamLocationNoteHistory{TeamLocationNoteId=na.TeamLocationNoteId,TeamId=a.TeamId,LocationId=loc.LocationId,Action="Create",NewNote=na.Note,ChangedAt=now,ChangedByUserId=user.UserId},
                new TeamLocationNoteHistory{TeamLocationNoteId=nb.TeamLocationNoteId,TeamId=b.TeamId,LocationId=loc.LocationId,Action="Create",NewNote=nb.Note,ChangedAt=now.AddMinutes(-1),ChangedByUserId=user.UserId});
            await db.SaveChangesAsync();
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(w=>{
                w.UseSetting("ConnectionStrings:DefaultConnection",root.ConnectionString);w.UseSetting("Auth:Mode","Demo");w.UseSetting("Auth:JwtKey",Key);
                w.UseSetting("BackgroundJobs:WorkerEnabled","false");w.UseSetting("BackgroundJobs:RecoverOnStartup","false");
            });
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            void Token(params Team[] teams)
            {
                var claims=new[]{new Claim(ClaimTypes.NameIdentifier,user.UserId.ToString()),new Claim(ClaimTypes.Role,"visitor"),
                    new Claim("employee_no","SCOPED"),new Claim("organization_id",org.OrganizationId.ToString()),new Claim("team_id",b.TeamId.ToString()),
                    new Claim("team_scopes",JsonSerializer.Serialize(teams.Select(t=>new TeamScopeDto(t.TeamId,t.TeamName,t.TeamId==b.TeamId))))};
                client.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer:"FieldVisit.UAT",audience:"FieldVisit.UAT.Users",claims:claims,expires:DateTime.UtcNow.AddMinutes(10),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256))));
            }
            const string list="api/v1/scoped-location-export/locations";
            const string excel=list+".xlsx";
            var maintenance=$"api/v1/locations/{loc.LocationId}/maintenance";
            async Task Denied(){foreach(var path in new[]{list,excel,maintenance}){using var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);}}
            async Task Visible(string note)
            {
                using(var response=await client.GetAsync(list)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains(note,await response.Content.ReadAsStringAsync());}
                using(var response=await client.GetAsync(excel)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var stream=new MemoryStream(await response.Content.ReadAsByteArrayAsync());using var book=new XSSFWorkbook(stream);Assert.Equal(note,book.GetSheetAt(0).GetRow(1).GetCell(7).StringCellValue);}
                using(var response=await client.GetAsync(maintenance)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains(note,await response.Content.ReadAsStringAsync());}
            }
            Token(a,b);await Visible("SECRET_A");
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
            Console.WriteLine("SCOPED_LOCATION_HTTP_SQL_PASS current=ALLOW staleScope=DENY staleAssignment=DENY freshRemaining=ALLOW revokedSearch=EMPTY revokedRole=DENY postGenerationRevocation=DENY");
        }
        finally{await using var cmd=admin.CreateCommand();cmd.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";await cmd.ExecuteNonQueryAsync();}
    }
    sealed class RevokeAfterAction(Func<Task> revoke):IAsyncActionFilter,IOrderedFilter
    {
        public int Order=>100;
        public async Task OnActionExecutionAsync(ActionExecutingContext c,ActionExecutionDelegate next){await next();await revoke();}
    }
}
