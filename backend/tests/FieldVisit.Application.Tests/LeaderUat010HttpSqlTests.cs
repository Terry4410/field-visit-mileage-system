using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

// Protected EF model on disposable SQL: DOES NOT certify actual UAT metadata parity.
public sealed class LeaderUat010HttpSqlTests
{
    const string Key="leader-rc-isolated-key-at-least-32-characters";
    [Fact]
    public async Task Leader_draft_real_http_sql_and_negative_gates_without_011_020()
    {
        var root=Environment.GetEnvironmentVariable("LEADER_RC_SQL_CONNECTION");
        if(string.IsNullOrEmpty(root)) { if(Environment.GetEnvironmentVariable("LEADER_RC_REQUIRED")=="1") throw new Exception("SQL_REQUIRED"); return; }
        var source=new SqlConnectionStringBuilder(root);
        Assert.Contains(source.DataSource,new[]{"localhost,1433","127.0.0.1,1433"});
        Assert.Equal("master",source.InitialCatalog);
        var name="LeaderRcIsolated_"+Guid.NewGuid().ToString("N");
        await using(var c=new SqlConnection(root)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=$"CREATE DATABASE [{name}]";await cmd.ExecuteNonQueryAsync();}
        source.InitialCatalog=name;source.Pooling=false;var cs=source.ConnectionString;
        AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
        var fixturePath=Environment.GetEnvironmentVariable("LEADER_RC_BROWSER_FIXTURE_PATH");
        var retainForBrowser=false;
        try{
        await using var seed=Db();await seed.Database.EnsureCreatedAsync();
        // This is explicitly a protected-model fixture, not a reconstructed true migration chain.
        await using(var c=new SqlConnection(cs)){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sys.tables WHERE name IN ('ChangeRequests','ChangeRequestEvents','LocationPublishedVersions','LocationPublicationStages')";Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));}
        var now=DateTime.UtcNow;var since=new DateOnly(2020,1,1);
        var org=new Organization{OrganizationCode="RC",OrganizationName="Synthetic RC",IsActive=true,CreatedAt=now};
        var otherOrg=new Organization{OrganizationCode="OUT",OrganizationName="Other synthetic org",IsActive=true,CreatedAt=now};seed.AddRange(org,otherOrg);await seed.SaveChangesAsync();
        var team=new Team{OrganizationId=org.OrganizationId,TeamCode="A",TeamName="Synthetic A",IsActive=true,CreatedAt=now};
        var otherTeam=new Team{OrganizationId=org.OrganizationId,TeamCode="B",TeamName="Synthetic B",IsActive=true,CreatedAt=now};
        var foreignTeam=new Team{OrganizationId=otherOrg.OrganizationId,TeamCode="C",TeamName="Synthetic C",IsActive=true,CreatedAt=now};
        var leader=new User{OrganizationId=org.OrganizationId,DisplayName="Synthetic Leader",EmployeeNo="LRC",IsActive=true,CreatedAt=now};
        var creator=new User{OrganizationId=org.OrganizationId,DisplayName="Synthetic Visitor",EmployeeNo="VRC",IsActive=true,CreatedAt=now};
        var role=new Role{RoleCode="leader",RoleName="Leader",IsActive=true,CreatedAt=now};seed.AddRange(team,otherTeam,foreignTeam,leader,creator,role);await seed.SaveChangesAsync();
        var emp=new Employment{OrganizationId=org.OrganizationId,PersonId=1,EmployeeNo="LRC",HireDate=since,LegacyUserId=leader.UserId,SourceType="Synthetic"};seed.Add(emp);await seed.SaveChangesAsync();
        var status=new EmploymentStatusPeriod{EmploymentId=emp.EmploymentId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=since,SourceType="Synthetic"};
        var appointment=new TeamLeaderAssignment{TeamId=team.TeamId,EmploymentId=emp.EmploymentId,EffectiveFrom=since,CreatedAt=now};
        var grant=new UserTeamScope{UserId=leader.UserId,TeamId=team.TeamId,IsActive=true,IsPrimary=true,AssignedAt=now};
        var assignedRole=new UserRoleAssignment{UserId=leader.UserId,RoleId=role.RoleId,EffectiveFrom=since,CreatedAt=now};
        var membership=new UserTeamAssignment{UserId=leader.UserId,TeamId=team.TeamId,IsPrimary=true,EffectiveFrom=since,CreatedAt=now};
        seed.AddRange(new UserIdentityProfile{UserId=leader.UserId,EmploymentId=emp.EmploymentId,UserType=UserTypes.Internal,UserCode="LRC",IdentityProvider="Demo",CreatedAt=now},status,appointment,grant,assignedRole,membership,new UserRole{UserId=leader.UserId,RoleId=role.RoleId,AssignedAt=now});await seed.SaveChangesAsync();
        var visitorRole=new Role{RoleCode="visitor",RoleName="Visitor",IsActive=true,CreatedAt=now};seed.Add(visitorRole);await seed.SaveChangesAsync();
        var visitorRoleAssignment=new UserRoleAssignment{UserId=creator.UserId,RoleId=visitorRole.RoleId,EffectiveFrom=since,CreatedAt=now};
        var visitorMembership=new UserTeamAssignment{UserId=creator.UserId,TeamId=team.TeamId,IsPrimary=true,EffectiveFrom=since,CreatedAt=now};
        var visitorStatus=new UserEmploymentPeriod{UserId=creator.UserId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=since,SourceType="Synthetic",CreatedAt=now};
        seed.AddRange(visitorRoleAssignment,visitorMembership,visitorStatus,new UserRole{UserId=creator.UserId,RoleId=visitorRole.RoleId,AssignedAt=now});await seed.SaveChangesAsync();
        Location Row(string label,int orgId,int teamId,bool published=false)=>new(){OrganizationId=orgId,TeamId=teamId,LocationCode=label,LocationName=label,LocationType="Customer",Address=label+" Test Road",ApprovalStatus=published?"Approved":"Pending",IsActive=published,GeocodingStatus="Pending",CreatedByUserId=creator.UserId,CreatedAt=now};
        var draft=Row("OtherOwnerDraft",org.OrganizationId,team.TeamId);var published=Row("Published",org.OrganizationId,team.TeamId,true);var outside=Row("OtherTeamDraft",org.OrganizationId,otherTeam.TeamId);var foreign=Row("OtherOrgDraft",otherOrg.OrganizationId,foreignTeam.TeamId);seed.AddRange(draft,published,outside,foreign);await seed.SaveChangesAsync();
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{b.UseSetting("ConnectionStrings:DefaultConnection",cs);b.UseSetting("Auth:JwtKey",Key);b.UseSetting("Auth:Mode","Demo");b.UseSetting("BackgroundJobs:WorkerEnabled","false");b.UseSetting("BackgroundJobs:RecoverOnStartup","false");});
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        string Token(string currentRole="leader",int? tokenOrg=null,int? tokenTeam=null,int? tokenUser=null){var claims=new[]{new Claim(ClaimTypes.NameIdentifier,(tokenUser??leader.UserId).ToString()),new Claim(ClaimTypes.Name,"Synthetic Leader"),new Claim("employee_no","LRC"),new Claim(ClaimTypes.Role,currentRole),new Claim("organization_id",(tokenOrg??org.OrganizationId).ToString()),new Claim("team_id",(tokenTeam??team.TeamId).ToString())};return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("FieldVisit.UAT","FieldVisit.UAT.Users",claims,expires:DateTime.UtcNow.AddMinutes(20),signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256)));}
        void Login(string r="leader",int? o=null,int? t=null)=>client.DefaultRequestHeaders.Authorization=new("Bearer",Token(r,o,t));Login();
        SaveManagedLocationRequest Input(int? teamId=null,string? rv=null,string label="EightFields")=>new(teamId??team.TeamId,label,"Customer","SyntheticCity","SyntheticDistrict","SyntheticRoad 88","TEST+CODE",false,rv,"12345678","Synthetic note");
        async Task<ManagedLocationDto> Success(HttpResponseMessage response){Assert.True(response.IsSuccessStatusCode,$"HTTP {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");return (await response.Content.ReadFromJsonAsync<ManagedLocationDto>())!;}
        async Task Denied(HttpResponseMessage response,HttpStatusCode expected){Assert.Equal(expected,response.StatusCode);await using var verify=Db();Assert.Equal(0,await verify.Locations.CountAsync(x=>x.LocationName=="ForbiddenAttempt"));}
        var created=await Success(await client.PostAsJsonAsync("/api/v1/managed-locations",Input()));Assert.Equal("Pending",created.ApprovalStatus);Assert.False(created.IsActive);Assert.Equal("12345678",created.TaxId);Assert.Equal("Synthetic note",created.MasterNote);Assert.Equal("SyntheticCity",created.City);Assert.Equal("TEST+CODE",created.PlusCode);
        var changed=await Success(await client.PutAsJsonAsync($"/api/v1/managed-locations/{draft.LocationId}",Input(rv:Convert.ToBase64String(draft.RowVersion),label:"ChangedOtherOwner")));Assert.False(changed.IsActive);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{draft.LocationId}",Input(label:"ForbiddenAttempt")),HttpStatusCode.UnprocessableEntity);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{draft.LocationId}",Input(rv:Convert.ToBase64String(draft.RowVersion),label:"ForbiddenAttempt")),HttpStatusCode.Conflict);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{published.LocationId}",Input(rv:Convert.ToBase64String(published.RowVersion),label:"ForbiddenAttempt")),HttpStatusCode.UnprocessableEntity);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{outside.LocationId}",Input(rv:Convert.ToBase64String(outside.RowVersion),label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{foreign.LocationId}",Input(rv:Convert.ToBase64String(foreign.RowVersion),label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(otherTeam.TeamId,label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt") with{IsActive=true}),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt") with{LocationType="Official"}),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt") with{TaxId=new string('X',21)}),HttpStatusCode.UnprocessableEntity);
        var list=await client.GetFromJsonAsync<List<ManagedLocationDto>>("/api/v1/managed-locations");Assert.DoesNotContain(list!,x=>x.LocationId==outside.LocationId||x.LocationId==foreign.LocationId);
        Login("visitor");await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);Login();
        // Historical Visitor contract: own current-team drafts only; no published/foreign/other-owner writes.
        client.DefaultRequestHeaders.Authorization=new("Bearer",Token("visitor",tokenUser:creator.UserId));
        var visitorTeams=await client.GetFromJsonAsync<List<VisitorTeamDto>>("/api/v1/managed-locations/visitor-teams");Assert.Single(visitorTeams!);Assert.Equal(team.TeamId,visitorTeams![0].TeamId);Assert.True(visitorTeams[0].IsPrimary);
        var visitorCreated=await Success(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"Visitor Created")));Assert.Equal(creator.UserId,visitorCreated.CreatedByUserId);
        var visitorEdited=await Success(await client.PutAsJsonAsync($"/api/v1/managed-locations/{visitorCreated.LocationId}",Input(rv:visitorCreated.RowVersion,label:"Visitor Edited")));Assert.Equal(creator.UserId,visitorEdited.CreatedByUserId);
        var search=await client.GetFromJsonAsync<PagedResult<ManagedLocationDto>>("/api/v1/managed-locations/search?q=Visitor&includeInactive=true");Assert.Contains(search!.Items,x=>x.LocationId==visitorCreated.LocationId);
        var visitorList=await client.GetFromJsonAsync<List<ManagedLocationDto>>("/api/v1/managed-locations?includeInactive=true");Assert.Contains(visitorList!,x=>x.LocationId==published.LocationId);Assert.DoesNotContain(visitorList!,x=>x.LocationId==outside.LocationId||x.LocationId==foreign.LocationId);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{created.LocationId}",Input(rv:created.RowVersion,label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{published.LocationId}",Input(rv:Convert.ToBase64String(published.RowVersion),label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{visitorCreated.LocationId}",Input(rv:visitorCreated.RowVersion,label:"ForbiddenAttempt")),HttpStatusCode.Conflict);
        await Denied(await client.PutAsJsonAsync($"/api/v1/managed-locations/{visitorCreated.LocationId}",Input(label:"ForbiddenAttempt")),HttpStatusCode.Conflict);
        foreach(var teamId in new[]{otherTeam.TeamId,foreignTeam.TeamId})await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(teamId,label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt") with{IsActive=true}),HttpStatusCode.Forbidden);
        visitorRoleAssignment.EffectiveTo=BusinessTime.Today.AddDays(-1);await seed.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/managed-locations/visitor-teams")).StatusCode);visitorRoleAssignment.EffectiveTo=null;await seed.SaveChangesAsync();
        visitorStatus.EmploymentStatus=EmploymentStatuses.Leave;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);visitorStatus.EmploymentStatus=EmploymentStatuses.Active;await seed.SaveChangesAsync();
        visitorMembership.EffectiveTo=BusinessTime.Today.AddDays(-1);await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);visitorMembership.EffectiveTo=null;await seed.SaveChangesAsync();
        creator.IsActive=false;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);creator.IsActive=true;await seed.SaveChangesAsync();
        Login("supervisor");await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization=null;await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Unauthorized);
        Login("admin");var adminCreated=await Success(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(otherTeam.TeamId,label:"Admin Created")));Assert.Equal(otherTeam.TeamId,adminCreated.TeamId);
        await Success(await client.PutAsJsonAsync($"/api/v1/managed-locations/{adminCreated.LocationId}",Input(otherTeam.TeamId,adminCreated.RowVersion,"Admin Edited")));
        Login();
        status.EmploymentStatus=EmploymentStatuses.Leave;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);status.EmploymentStatus=EmploymentStatuses.Active;await seed.SaveChangesAsync();
        leader.IsActive=false;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);leader.IsActive=true;await seed.SaveChangesAsync();
        emp.TerminationDate=BusinessTime.Today;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);emp.TerminationDate=null;await seed.SaveChangesAsync();
        assignedRole.EffectiveTo=BusinessTime.Today.AddDays(-1);await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/managed-locations")).StatusCode);assignedRole.EffectiveTo=null;await seed.SaveChangesAsync();
        grant.IsActive=false;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);grant.IsActive=true;await seed.SaveChangesAsync();
        membership.EffectiveTo=BusinessTime.Today.AddDays(-1);await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);membership.EffectiveTo=null;await seed.SaveChangesAsync();
        appointment.EffectiveTo=BusinessTime.Today.AddDays(-1);await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);appointment.EffectiveTo=null;await seed.SaveChangesAsync();
        team.IsActive=false;await seed.SaveChangesAsync();await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);team.IsActive=true;await seed.SaveChangesAsync();
        Login(o:otherOrg.OrganizationId);await Denied(await client.PostAsJsonAsync("/api/v1/managed-locations",Input(label:"ForbiddenAttempt")),HttpStatusCode.Forbidden);Login();
        // Old alternate endpoints must not turn the new draft wave into published writes.
        await Denied(await client.PutAsJsonAsync($"/api/v1/locations/{published.LocationId}/maintenance",new{city="Changed",address="Changed",rowVersion=Convert.ToBase64String(published.RowVersion)}),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsJsonAsync("/api/v1/jobs/geocoding",new{mode="AllPending"}),HttpStatusCode.Forbidden);
        await Denied(await client.PostAsync($"/api/v1/imports/{Guid.NewGuid()}/confirm",null),HttpStatusCode.Forbidden);
        await using var final=Db();Assert.Equal("Published",(await final.Locations.SingleAsync(x=>x.LocationId==published.LocationId)).LocationName);Assert.Equal(6,await final.AuditLogs.CountAsync(x=>x.Action=="LocationCreate"||x.Action=="LocationUpdate"));
        if (!string.IsNullOrEmpty(fixturePath))
        {
            await File.WriteAllTextAsync(fixturePath,JsonSerializer.Serialize(new{connectionString=cs,token=Token(),visitorToken=Token("visitor",tokenUser:creator.UserId),visitorUserId=creator.UserId,teamId=team.TeamId,database=name}));
            retainForBrowser=true;
        }
        Console.WriteLine("LEADER_RC_REAL_HTTP_SQL_PASS create=8fields otherOwnerDraft=PASS stale=409 published=422 scopes=403 liveHRRoleTeamAppointment=403 actualUatParity=HOLD fixture=PROTECTED_EF_MODEL_ONLY no011to020=true");
        }finally{if(!retainForBrowser){SqlConnection.ClearAllPools();await using var c=new SqlConnection(root);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";await cmd.ExecuteNonQueryAsync();}}
    }

    [Fact]
    public async Task Browser_write_sql_probe_when_requested()
    {
        var p=Environment.GetEnvironmentVariable("LEADER_RC_VERIFY_BROWSER_FIXTURE");
        if(string.IsNullOrEmpty(p))return;
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(p));
        var cs=json.RootElement.GetProperty("connectionString").GetString()!;
        var b=new SqlConnectionStringBuilder(cs);Assert.StartsWith("LeaderRcIsolated_",b.InitialCatalog);Assert.Contains(b.DataSource,new[]{"localhost,1433","127.0.0.1,1433"});
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
        var row=await db.Locations.SingleAsync(x=>x.LocationName=="Browser RC Edited");
        Assert.Equal("Browser City",row.City);Assert.Equal("Browser District",row.District);Assert.Equal("Browser Road 18",row.Address);Assert.Equal("TEST+BROWSER",row.PlusCode);Assert.Equal("87654321",row.TaxId);Assert.Equal("Browser note",row.MasterNote);Assert.False(row.IsActive);Assert.Equal("Pending",row.ApprovalStatus);
        Assert.True(await db.AuditLogs.AnyAsync(x=>x.Action=="LocationUpdate"&&x.EntityId==row.LocationId.ToString()));
        var visitorRow=await db.Locations.SingleAsync(x=>x.LocationName=="Visitor Browser Edited");
        Assert.Equal(json.RootElement.GetProperty("visitorUserId").GetInt32(),visitorRow.CreatedByUserId);Assert.False(visitorRow.IsActive);Assert.Equal("Pending",visitorRow.ApprovalStatus);Assert.True(await db.AuditLogs.AnyAsync(x=>x.Action=="LocationUpdate"&&x.EntityId==visitorRow.LocationId.ToString()));
        Console.WriteLine("LEADER_RC_BROWSER_API_SQL_PASS pendingInactive=true eightFields=true audit=true actualUatParity=HOLD");
    }
}
