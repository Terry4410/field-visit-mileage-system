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

public sealed class FinalPreplanNotesSqlTests
{
    private const string Key="final-preplan-notes-isolated-jwt-key-over-32-characters";
    [Fact]
    public async Task Draft_notes_visible_to_three_authorized_roles_and_denied_outside_scope()
    {
        var raw=Environment.GetEnvironmentVariable("FV_FINAL_SQL")??throw new Exception("FV_FINAL_SQL_REQUIRED");
        var root=new SqlConnectionStringBuilder(raw){Pooling=false,ConnectTimeout=5};
        Assert.Contains(root.DataSource,new[]{"localhost,1433","127.0.0.1,1433"});
        Assert.Equal("master",root.InitialCatalog);
        var name="FinalNotes_"+Guid.NewGuid().ToString("N");
        await using var server=new SqlConnection(root.ConnectionString);await server.OpenAsync();
        await using(var cmd=server.CreateCommand()){cmd.CommandText=$"CREATE DATABASE [{name}]";await cmd.ExecuteNonQueryAsync();}
        root.InitialCatalog=name;
        try
        {
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(root.ConnectionString).Options);
            await db.Database.EnsureCreatedAsync();
            var now=DateTime.UtcNow;var from=BusinessTime.Today.AddDays(-10);
            var org=new Organization{OrganizationCode="NOTE",OrganizationName="Notes Synthetic",IsActive=true,CreatedAt=now};
            db.Add(org);await db.SaveChangesAsync();
            var team=new Team{OrganizationId=org.OrganizationId,TeamCode="NOTE-T",TeamName="Notes Team",IsActive=true,CreatedAt=now};
            var otherTeam=new Team{OrganizationId=org.OrganizationId,TeamCode="NOTE-X",TeamName="Other Team",IsActive=true,CreatedAt=now};
            var visitorRole=new Role{RoleCode="visitor",RoleName="Visitor",IsActive=true,CreatedAt=now};
            var leaderRole=new Role{RoleCode="leader",RoleName="Leader",IsActive=true,CreatedAt=now};
            var adminRole=new Role{RoleCode="admin",RoleName="Admin",IsActive=true,CreatedAt=now};
            db.AddRange(team,otherTeam,visitorRole,leaderRole,adminRole);await db.SaveChangesAsync();
            var visitor=new User{OrganizationId=org.OrganizationId,TeamId=team.TeamId,EmployeeNo="N-V",DisplayName="N Visitor",IsActive=true,CreatedAt=now};
            var leader=new User{OrganizationId=org.OrganizationId,TeamId=team.TeamId,EmployeeNo="N-L",DisplayName="N Leader",IsActive=true,CreatedAt=now};
            var admin=new User{OrganizationId=org.OrganizationId,TeamId=team.TeamId,EmployeeNo="N-A",DisplayName="N Admin",IsActive=true,CreatedAt=now};
            var outsider=new User{OrganizationId=org.OrganizationId,TeamId=otherTeam.TeamId,EmployeeNo="N-X",DisplayName="N Other Leader",IsActive=true,CreatedAt=now};
            var otherVisitor=new User{OrganizationId=org.OrganizationId,TeamId=team.TeamId,EmployeeNo="N-V2",DisplayName="N Other Visitor",IsActive=true,CreatedAt=now};
            db.AddRange(visitor,leader,admin,outsider,otherVisitor);await db.SaveChangesAsync();
            void Assign(User u,Role r,Team t)
            {
                db.AddRange(
                    new UserIdentityProfile{UserId=u.UserId,UserCode=u.EmployeeNo!,UserType="Internal",CreatedAt=now},
                    new UserEmploymentPeriod{UserId=u.UserId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=from,CreatedAt=now},
                    new UserRole{UserId=u.UserId,RoleId=r.RoleId,AssignedAt=now},
                    new UserRoleAssignment{UserId=u.UserId,RoleId=r.RoleId,EffectiveFrom=from,CreatedAt=now},
                    new UserTeamAssignment{UserId=u.UserId,TeamId=t.TeamId,IsPrimary=true,EffectiveFrom=from,CreatedAt=now});
            }
            Assign(visitor,visitorRole,team);Assign(leader,leaderRole,team);Assign(admin,adminRole,team);
            Assign(outsider,leaderRole,otherTeam);Assign(otherVisitor,visitorRole,team);
            await db.SaveChangesAsync();
            var draft=new VisitTrip{TripNo="N-PREPLAN-001",UserId=visitor.UserId,
                CreatedByUserId=visitor.UserId,OrganizationId=org.OrganizationId,TeamId=team.TeamId,
                VisitDate=BusinessTime.Today.AddDays(7),Status="Draft",Notes="UAT_PREPLAN_CROSS_ROLE_NOTE",
                CreatedAt=now};
            db.Add(draft);await db.SaveChangesAsync();
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{
                b.UseSetting("ConnectionStrings:DefaultConnection",root.ConnectionString);
                b.UseSetting("Auth:Mode","Demo");b.UseSetting("Auth:JwtKey",Key);
                b.UseSetting("BackgroundJobs:WorkerEnabled","false");b.UseSetting("BackgroundJobs:RecoverOnStartup","false");
            });
            using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            void Token(User u,string selected,Team t)
            {
                var claims=new[]{new Claim(ClaimTypes.NameIdentifier,u.UserId.ToString()),new Claim(ClaimTypes.Role,selected),
                    new Claim("employee_no",u.EmployeeNo!),new Claim("organization_id",org.OrganizationId.ToString()),
                    new Claim("team_id",t.TeamId.ToString())};
                client.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(
                    new JwtSecurityToken(issuer:"FieldVisit.UAT",audience:"FieldVisit.UAT.Users",claims:claims,
                        expires:DateTime.UtcNow.AddMinutes(10),
                        signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256))));
            }
            foreach(var (actor,role,scope) in new[]{(visitor,"visitor",team),(leader,"leader",team),(admin,"admin",team)})
            {
                Token(actor,role,scope);
                using var response=await client.GetAsync($"api/v1/trips/{draft.VisitTripId}");
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                var detail=await response.Content.ReadFromJsonAsync<TripDto>();
                Assert.Equal("Draft",detail?.Status);
                Assert.Equal("UAT_PREPLAN_CROSS_ROLE_NOTE",detail?.Notes);
                Console.WriteLine($"PREPLAN_NOTE_HTTP_SQL=PASS role={role} draft={draft.TripNo} note=VISIBLE");
            }
            foreach(var (actor,role,scope) in new[]{(outsider,"leader",otherTeam),(otherVisitor,"visitor",team)})
            {
                Token(actor,role,scope);
                using var response=await client.GetAsync($"api/v1/trips/{draft.VisitTripId}");
                Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
                Console.WriteLine($"PREPLAN_NOTE_DENY=PASS role={role} out_of_scope=403");
            }
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
