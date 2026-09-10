using System.Text.Json;
using FieldVisit.Api;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.DA1b.Integration.Tests;

internal static class Program
{
    private static string Cs = "";
    private static int Passed;
    private static int Total;

    public static async Task<int> Main()
    {
        Cs = Environment.GetEnvironmentVariable("DA1B_CONNECTION_STRING") ?? "";
        if (string.IsNullOrWhiteSpace(Cs)) throw new InvalidOperationException("DA1B_CONNECTION_STRING is required; no silent skip is permitted.");
        await using (var probe = Db()) if (!await probe.Database.CanConnectAsync()) throw new InvalidOperationException("Cannot connect to D-A1b SQL Server database.");

        await Run("DA1B-IT-001 same-org authoritative Admin", AuthoritySameOrgAdmin);
        await Run("DA1B-IT-002 missing profile", ()=>AuthorityReject(profile:false));
        await Run("DA1B-IT-003 external identity", ()=>AuthorityReject(external:true));
        await Run("DA1B-IT-004 missing Employment", ()=>AuthorityReject(employment:false));
        await Run("DA1B-IT-005 inactive Employment", ()=>AuthorityReject(active:false));
        await Run("DA1B-IT-006 missing effective Admin role", ()=>AuthorityReject(adminRole:false));
        await Run("DA1B-IT-007 cross-org", ()=>AuthorityReject(locationOrg:2, locationTeam:20));
        await Run("DA1B-IT-008 shared/global", ()=>AuthorityReject(locationOrg:null, locationTeam:null));
        await Run("DA1B-IT-009 Visitor/Leader persona denial", AuthorityPersonaDenied);
        await Run("DA1B-IT-010 governance matching RowVersion", GovernanceMatching);
        await Run("DA1B-IT-011 governance stale RowVersion zero partial", GovernanceStale);
        await Run("DA1B-IT-012 malformed RowVersion zero partial", ()=>GovernanceInvalid("not-base64"));
        await Run("DA1B-IT-013 wrong-length RowVersion zero partial", ()=>GovernanceInvalid("AQ=="));
        await Run("DA1B-IT-014 duplicate self", ()=>DuplicateReject(1,"reason",TargetKind.Self));
        await Run("DA1B-IT-015 duplicate missing", ()=>DuplicateReject(999,"reason",TargetKind.Missing));
        await Run("DA1B-IT-016 duplicate cross-org", ()=>DuplicateReject(2,"reason",TargetKind.CrossOrg));
        await Run("DA1B-IT-017 duplicate shared/global", ()=>DuplicateReject(2,"reason",TargetKind.Shared));
        await Run("DA1B-IT-018 duplicate blank reason", ()=>DuplicateReject(2,"   ",TargetKind.Valid));
        await Run("DA1B-IT-019 null duplicate clears reason", DuplicateClear);
        await Run("DA1B-IT-020 valid same-org duplicate", DuplicateValid);
        await Run("DA1B-IT-021 EffectiveTo NULL blocks", ()=>Boundary(null,false));
        await Run("DA1B-IT-022 EffectiveTo Today blocks", ()=>Boundary(BusinessTime.Today,false));
        await Run("DA1B-IT-023 EffectiveTo Future blocks", ()=>Boundary(BusinessTime.Today.AddDays(1),false));
        await Run("DA1B-IT-024 historical EffectiveTo allows + metadata/audit", ()=>Boundary(BusinessTime.Today.AddDays(-1),true));
        await Run("DA1B-IT-025 already inactive matching RowVersion no-op", AlreadyInactiveNoOp);
        await Run("DA1B-IT-026 already inactive stale RowVersion", AlreadyInactiveStale);
        await Run("DA1B-IT-027 stale active deactivation zero mutation/audit", DeactivateStaleActive);
        await Run("DA1B-IT-028 audit failure rolls deactivation back", DeactivateAuditRollback);
        await Run("DA1B-IT-029 ordinary PUT Admin + legacy audit", OrdinaryAdmin);
        await Run("DA1B-IT-030 ordinary PUT Leader authorized", OrdinaryLeader);
        await Run("DA1B-IT-031 ordinary PUT Leader unauthorized", OrdinaryLeaderDenied);
        await Run("DA1B-IT-032 ordinary PUT cross-org", OrdinaryCrossOrg);
        await Run("DA1B-IT-033 ordinary PUT same IsActive", OrdinarySameActive);
        await Run("DA1B-IT-034 ordinary PUT true-to-false", ()=>OrdinaryToggle(true,false));
        await Run("DA1B-IT-035 ordinary PUT false-to-true", ()=>OrdinaryToggle(false,true));
        await Run("DA1B-IT-036 audit failure rolls ordinary PUT back", OrdinaryAuditRollback);
        await Run("DA1B-IT-037 Admin search scope", SearchAdminScope);
        await Run("DA1B-IT-038 Leader search scope", SearchLeaderScope);
        await Run("DA1B-IT-039 TaxId additive + legacy q", SearchTaxAndQ);
        await Run("DA1B-IT-040 active/city/district filters", SearchFilters);
        await Run("DA1B-IT-041 deterministic sort + pagination", SearchSortPagination);
        await Run("DA1B-IT-042 DbUpdateConcurrencyException -> 409", ()=>Status(new DbUpdateConcurrencyException(),409));
        await Run("DA1B-IT-043 ROWVERSION_CONFLICT InvalidOperation -> 409", ()=>Status(new InvalidOperationException("ROWVERSION_CONFLICT"),409));
        await Run("DA1B-IT-044 ordinary/malformed InvalidOperation -> 422", ()=>Status(new InvalidOperationException("ROWVERSION_INVALID"),422));
        await Run("DA1B-IT-045 UnauthorizedAccessException -> 403", ()=>Status(new UnauthorizedAccessException(),403));
        await Run("DA1B-IT-046 KeyNotFoundException -> 404", ()=>Status(new KeyNotFoundException(),404));

        Console.WriteLine($"DA1B_SQL_INTEGRATION={Passed}/{Total}=PASS");
        return 0;
    }

    private static async Task Run(string name, Func<Task> test)
    {
        Total++;
        try { await test(); Passed++; Console.WriteLine($"{name}=PASS"); }
        catch(Exception ex) { Console.Error.WriteLine($"{name}=FAIL: {ex.GetType().Name}: {ex.Message}"); throw; }
    }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Cs, x=>x.EnableRetryOnFailure(3,TimeSpan.FromSeconds(1),null)).Options);
    private static V180ManagedLocationGovernanceRepository Repo(AppDbContext db)=>new(db);
    private static CurrentUserDto Admin(int id=1,int org=1)=>new(id,$"E{id}",$"Admin {id}",null,org,null,null,new[]{"admin"});
    private static CurrentUserDto Leader(int id=2,int org=1,int team=10)=>new(id,$"E{id}",$"Leader {id}",null,org,team,$"Team {team}",new[]{"leader"},new[]{new TeamScopeDto(team,$"Team {team}",true)});
    private static CurrentUserDto Visitor(int id=3,int org=1,int team=10)=>new(id,$"E{id}",$"Visitor {id}",null,org,team,$"Team {team}",new[]{"visitor"},new[]{new TeamScopeDto(team,$"Team {team}",true)});

    private static async Task Reset()
    {
        await using var db=Db();
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.TR_DA1B_AuditFail',N'TR') IS NOT NULL DROP TRIGGER dbo.TR_DA1B_AuditFail;
DELETE dbo.AuditLogs; DELETE dbo.DeploymentSiteLocationAssignments; DELETE dbo.DeploymentSites;
DELETE dbo.EmploymentRoleAssignments; DELETE dbo.EmploymentStatusPeriods; DELETE dbo.UserIdentityProfiles;
DELETE dbo.Employments; DELETE dbo.Persons; UPDATE dbo.Locations SET DuplicateOfLocationId=NULL,DuplicateReason=NULL;
DELETE dbo.Locations; DELETE dbo.Users;");
    }

    private static async Task SeedAuthority(AppDbContext db,int userId=1,int orgId=1,bool profile=true,bool external=false,bool employment=true,bool active=true,bool adminRole=true)
    {
        var eid=1000L+userId; var pid=2000L+userId;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Users(UserId,OrganizationId,EmployeeNo,DisplayName,IsActive) VALUES({userId},{orgId},{"E"+userId},{"User "+userId},1)");
        if(employment)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Persons(PersonId,DisplayName,CreatedAt) VALUES({pid},{"Person "+userId},SYSUTCDATETIME())");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,SourceType,CreatedAt) VALUES({eid},{pid},{orgId},{"E"+userId},{"Test"},SYSUTCDATETIME())");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,EffectiveTo,SourceType,CreatedAt) VALUES({eid},{(active?"Active":"Leave")},{BusinessTime.Today.AddDays(-10)},{(DateOnly?)null},{"Test"},SYSUTCDATETIME())");
            if(adminRole) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.EmploymentRoleAssignments(EmploymentId,RoleId,EffectiveFrom,EffectiveTo,CreatedAt) VALUES({eid},1,{BusinessTime.Today.AddDays(-10)},{(DateOnly?)null},SYSUTCDATETIME())");
        }
        if(profile) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt) VALUES({userId},{eid},{(external?"External":"Internal")},{"U"+userId},{"Test"},SYSUTCDATETIME())");
    }

    private static async Task SeedLocation(AppDbContext db,int id,int? org=1,int? team=10,string name="Alpha",string city="Taipei",string district="Xinyi",string address="1 Test Rd",string plus="PLUS",bool active=true,string? tax=null)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.Locations(LocationId,OrganizationId,TeamId,LocationCode,LocationName,LocationType,City,District,Address,PlusCode,IsTemporary,ApprovalStatus,GeocodingStatus,IsActive,CreatedAt,UpdatedAt,TaxId)
VALUES({id},{org},{team},{"L"+id},{name},{"Official"},{city},{district},{address},{plus},0,{"Approved"},{"Success"},{active},SYSUTCDATETIME(),CONVERT(datetime2(3),'2000-01-01T00:00:00'),{tax})");
    }

    private static async Task SeedAssignment(AppDbContext db,int locationId,DateOnly? end)
    {
        var site=9000+locationId;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.DeploymentSites(DeploymentSiteId) VALUES({site})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.DeploymentSiteLocationAssignments(DeploymentSiteId,LocationId,EffectiveFrom,EffectiveTo) VALUES({site},{locationId},{BusinessTime.Today.AddDays(-20)},{end})");
    }

    private static async Task<string> Rv(AppDbContext db,int id)=>Convert.ToBase64String((await db.Locations.AsNoTracking().SingleAsync(x=>x.LocationId==id)).RowVersion);

    private static async Task<GovState> Gov(int id)
    {
        await using var c=new SqlConnection(Cs); await c.OpenAsync(); await using var cmd=new SqlCommand("SELECT TaxId,MasterNote,DuplicateOfLocationId,DuplicateReason,RowVersion FROM dbo.Locations WHERE LocationId=@id",c); cmd.Parameters.AddWithValue("@id",id);
        await using var r=await cmd.ExecuteReaderAsync(); if(!await r.ReadAsync())throw new Exception("Location missing");
        return new(r.IsDBNull(0)?null:r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetInt32(2),r.IsDBNull(3)?null:r.GetString(3),(byte[])r[4]);
    }

    private static async Task<LocationState> State(int id)
    {
        await using var c=new SqlConnection(Cs); await c.OpenAsync(); await using var cmd=new SqlCommand("SELECT IsActive,InactivatedAt,InactivatedByUserId,UpdatedAt,RowVersion,GeocodingStatus,LocationName,City,District,Address,PlusCode,TeamId FROM dbo.Locations WHERE LocationId=@id",c); cmd.Parameters.AddWithValue("@id",id);
        await using var r=await cmd.ExecuteReaderAsync(); if(!await r.ReadAsync())throw new Exception("Location missing");
        return new(r.GetBoolean(0),r.IsDBNull(1)?null:r.GetDateTime(1),r.IsDBNull(2)?null:r.GetInt32(2),r.IsDBNull(3)?null:r.GetDateTime(3),(byte[])r[4],r.GetString(5),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetString(10),r.IsDBNull(11)?null:r.GetInt32(11));
    }

    private static async Task<List<AuditState>> Audits(int id,string action)
    {
        await using var c=new SqlConnection(Cs); await c.OpenAsync(); await using var cmd=new SqlCommand("SELECT EntityType,EntityId,Action,UserId,NewValues,CorrelationId FROM dbo.AuditLogs WHERE EntityId=@id AND Action=@action ORDER BY AuditLogId",c); cmd.Parameters.AddWithValue("@id",id.ToString());cmd.Parameters.AddWithValue("@action",action);
        await using var r=await cmd.ExecuteReaderAsync();var a=new List<AuditState>();while(await r.ReadAsync())a.Add(new(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt32(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetGuid(5)));return a;
    }

    private static async Task AuthoritySameOrgAdmin(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);var result=await Repo(db).UpdateGovernanceAsync(Admin(),1,new(" 12345678 "," note ",null,null,await Rv(db,1)),default);Eq("12345678",result.TaxId);Eq("note",result.MasterNote);}
    private static async Task AuthorityReject(bool profile=true,bool external=false,bool employment=true,bool active=true,bool adminRole=true,int? locationOrg=1,int? locationTeam=10){await Reset();await using var db=Db();await SeedAuthority(db,profile:profile,external:external,employment:employment,active:active,adminRole:adminRole);await SeedLocation(db,1,locationOrg,locationTeam);var rv=await Rv(db,1);await Throws<UnauthorizedAccessException>(()=>Repo(db).UpdateGovernanceAsync(Admin(),1,new(null,null,null,null,rv),default));}
    private static async Task AuthorityPersonaDenied(){await Reset();await using var db=Db();await SeedAuthority(db,userId:3);await SeedLocation(db,1);var rv=await Rv(db,1);await Throws<UnauthorizedAccessException>(()=>Repo(db).UpdateGovernanceAsync(Visitor(),1,new(null,null,null,null,rv),default));await Throws<UnauthorizedAccessException>(()=>Repo(db).DeactivateManagedLocationAsync(Leader(3),1,rv,default));}
    private static async Task GovernanceMatching(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);var rv=await Rv(db,1);await Repo(db).UpdateGovernanceAsync(Admin(),1,new("TAX","Master",null,null,rv),default);var s=await Gov(1);Eq("TAX",s.TaxId);Eq("Master",s.MasterNote);True(!s.RowVersion.SequenceEqual(Convert.FromBase64String(rv)));}
    private static async Task GovernanceStale(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);var stale=await Rv(db,1);await Repo(db).UpdateGovernanceAsync(Admin(),1,new("ONE","First",null,null,stale),default);var before=await Gov(1);await Throws<DbUpdateConcurrencyException>(()=>Repo(db).UpdateGovernanceAsync(Admin(),1,new("TWO","Second",null,null,stale),default));GovEq(before,await Gov(1));}
    private static async Task GovernanceInvalid(string token){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);var before=await Gov(1);await Throws<InvalidOperationException>(()=>Repo(db).UpdateGovernanceAsync(Admin(),1,new("BAD","BAD",null,null,token),default));GovEq(before,await Gov(1));}

    private enum TargetKind{Self,Missing,CrossOrg,Shared,Valid}
    private static async Task DuplicateReject(int target,string reason,TargetKind kind){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);if(kind==TargetKind.CrossOrg)await SeedLocation(db,target,2,20);else if(kind==TargetKind.Shared)await SeedLocation(db,target,null,null);else if(kind==TargetKind.Valid)await SeedLocation(db,target);var before=await Gov(1);var rv=await Rv(db,1);await Throws<Exception>(()=>Repo(db).UpdateGovernanceAsync(Admin(),1,new("X","Y",target,reason,rv),default));GovEq(before,await Gov(1));}
    private static async Task DuplicateClear(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);await SeedLocation(db,2);await Repo(db).UpdateGovernanceAsync(Admin(),1,new(null,null,2,"reason",await Rv(db,1)),default);await Repo(db).UpdateGovernanceAsync(Admin(),1,new(null,null,null,"ignored",await Rv(db,1)),default);var s=await Gov(1);True(s.DuplicateId is null&&s.DuplicateReason is null);}
    private static async Task DuplicateValid(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);await SeedLocation(db,2);await Repo(db).UpdateGovernanceAsync(Admin(),1,new(null,null,2,"same place",await Rv(db,1)),default);var s=await Gov(1);Eq<int?>(2,s.DuplicateId);Eq("same place",s.DuplicateReason);}

    private static async Task Boundary(DateOnly? end,bool allowed){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);await SeedAssignment(db,1,end);var before=await State(1);var rv=Convert.ToBase64String(before.RowVersion);if(!allowed){await Throws<InvalidOperationException>(()=>Repo(db).DeactivateManagedLocationAsync(Admin(),1,rv,default));StateEq(before,await State(1));Eq(0,(await Audits(1,"LocationDeactivate")).Count);return;}await Repo(db).DeactivateManagedLocationAsync(Admin(),1,rv,default);var after=await State(1);True(!after.IsActive);True(after.InactivatedAt.HasValue);Eq<int?>(1,after.InactivatedByUserId);True(after.UpdatedAt!=before.UpdatedAt);True(!after.RowVersion.SequenceEqual(before.RowVersion));var audits=await Audits(1,"LocationDeactivate");Eq(1,audits.Count);AuditEq(audits[0],1,"LocationDeactivate",1);using var json=JsonDocument.Parse(audits[0].NewValues!);Eq(1,json.RootElement.GetProperty("locationId").GetInt32());}
    private static async Task AlreadyInactiveNoOp(){await Reset();string original;await using(var setup=Db()){await SeedAuthority(setup);await SeedLocation(setup,1);original=await Rv(setup,1);}await using(var requestA=Db()){await Repo(requestA).DeactivateManagedLocationAsync(Admin(),1,original,default);}var before=await State(1);var count=(await Audits(1,"LocationDeactivate")).Count;await using(var requestB=Db()){await Repo(requestB).DeactivateManagedLocationAsync(Admin(),1,Convert.ToBase64String(before.RowVersion),default);}StateEq(before,await State(1));Eq(count,(await Audits(1,"LocationDeactivate")).Count);}
    private static async Task AlreadyInactiveStale(){await Reset();string stale;await using(var setup=Db()){await SeedAuthority(setup);await SeedLocation(setup,1);stale=await Rv(setup,1);}await using(var requestA=Db()){await Repo(requestA).DeactivateManagedLocationAsync(Admin(),1,stale,default);}var before=await State(1);var count=(await Audits(1,"LocationDeactivate")).Count;await using(var requestB=Db()){await Throws<DbUpdateConcurrencyException>(()=>Repo(requestB).DeactivateManagedLocationAsync(Admin(),1,stale,default));}StateEq(before,await State(1));Eq(count,(await Audits(1,"LocationDeactivate")).Count);}
    private static async Task DeactivateStaleActive(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);var stale=await Rv(db,1);await Repo(db).UpdateGovernanceAsync(Admin(),1,new("T",null,null,null,stale),default);var before=await State(1);await Throws<DbUpdateConcurrencyException>(()=>Repo(db).DeactivateManagedLocationAsync(Admin(),1,stale,default));StateEq(before,await State(1));Eq(0,(await Audits(1,"LocationDeactivate")).Count);}
    private static async Task DeactivateAuditRollback(){await Reset();await using(var db=Db()){await SeedAuthority(db);await SeedLocation(db,1);await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.TR_DA1B_AuditFail ON dbo.AuditLogs INSTEAD OF INSERT AS THROW 59999, 'DA1B injected audit failure', 1;");var before=await State(1);await Throws<Exception>(()=>Repo(db).DeactivateManagedLocationAsync(Admin(),1,Convert.ToBase64String(before.RowVersion),default));}await using(var db=Db())await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.TR_DA1B_AuditFail");var after=await State(1);True(after.IsActive&&after.InactivatedAt is null&&after.InactivatedByUserId is null);Eq(0,(await Audits(1,"LocationDeactivate")).Count);}

    private static SaveManagedLocationRequest Edit(string name,bool active,string rv,int? team=10)=>new(team,name,"Official","Taipei","Xinyi","99 New Rd","NEWPLUS",active,rv);
    private static async Task OrdinaryAdmin(){await Reset();await using var db=Db();await SeedAuthority(db);await SeedLocation(db,1);var before=await State(1);await Repo(db).UpdateManagedLocationAsync(Admin(),1,Edit("Changed",true,Convert.ToBase64String(before.RowVersion)),default);var after=await State(1);Eq("Changed",after.Name);Eq("Pending",after.Geocoding);True(after.UpdatedAt!=before.UpdatedAt);var a=await Audits(1,"LocationUpdate");Eq(1,a.Count);AuditEq(a[0],1,"LocationUpdate",1);using var json=JsonDocument.Parse(a[0].NewValues!);True(json.RootElement.TryGetProperty("before",out _));True(json.RootElement.TryGetProperty("after",out _));}
    private static async Task OrdinaryLeader(){await Reset();await using var db=Db();await db.Database.ExecuteSqlRawAsync("INSERT dbo.Users(UserId,OrganizationId,DisplayName,IsActive) VALUES(2,1,N'Leader',1)");await SeedLocation(db,1);var rv=await Rv(db,1);await Repo(db).UpdateManagedLocationAsync(Leader(),1,Edit("Leader Edit",true,rv),default);Eq("Leader Edit",(await State(1)).Name);Eq(1,(await Audits(1,"LocationUpdate")).Count);}
    private static async Task OrdinaryLeaderDenied(){await Reset();await using var db=Db();await SeedLocation(db,1,1,11);var before=await State(1);await Throws<UnauthorizedAccessException>(()=>Repo(db).UpdateManagedLocationAsync(Leader(),1,Edit("No",true,Convert.ToBase64String(before.RowVersion),11),default));StateEq(before,await State(1));Eq(0,(await Audits(1,"LocationUpdate")).Count);}
    private static async Task OrdinaryCrossOrg(){await Reset();await using var db=Db();await SeedLocation(db,1,2,20);var before=await State(1);await Throws<UnauthorizedAccessException>(()=>Repo(db).UpdateManagedLocationAsync(Admin(),1,Edit("No",true,Convert.ToBase64String(before.RowVersion),null),default));StateEq(before,await State(1));}
    private static async Task OrdinarySameActive(){await Reset();await using var db=Db();await SeedLocation(db,1);var rv=await Rv(db,1);await Repo(db).UpdateManagedLocationAsync(Admin(),1,Edit("Allowed",true,rv),default);Eq("Allowed",(await State(1)).Name);}
    private static async Task OrdinaryToggle(bool initial,bool requested){await Reset();await using var db=Db();await SeedLocation(db,1,active:initial);var before=await State(1);await Throws<InvalidOperationException>(()=>Repo(db).UpdateManagedLocationAsync(Admin(),1,Edit("No",requested,Convert.ToBase64String(before.RowVersion)),default));StateEq(before,await State(1));Eq(0,(await Audits(1,"LocationUpdate")).Count);}
    private static async Task OrdinaryAuditRollback(){await Reset();await using(var db=Db()){await SeedLocation(db,1);await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.TR_DA1B_AuditFail ON dbo.AuditLogs INSTEAD OF INSERT AS THROW 59999, 'DA1B injected audit failure', 1;");var before=await State(1);await Throws<Exception>(()=>Repo(db).UpdateManagedLocationAsync(Admin(),1,Edit("Rollback",true,Convert.ToBase64String(before.RowVersion)),default));}await using(var db=Db())await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.TR_DA1B_AuditFail");var after=await State(1);Eq("Alpha",after.Name);Eq("Success",after.Geocoding);Eq(0,(await Audits(1,"LocationUpdate")).Count);}

    private static async Task SearchAdminScope(){await Reset();await using var db=Db();await SeedLocation(db,1,1,10);await SeedLocation(db,2,2,20);await SeedLocation(db,3,null,null);var r=await Repo(db).SearchManagedLocationsAsync(Admin(),new(PageSize:20),default);Seq(new[]{1,3},r.Items.Select(x=>x.LocationId).OrderBy(x=>x));}
    private static async Task SearchLeaderScope(){await Reset();await using var db=Db();await SeedLocation(db,1,1,10);await SeedLocation(db,2,1,11);await SeedLocation(db,3,null,null);var r=await Repo(db).SearchManagedLocationsAsync(Leader(),new(PageSize:20),default);Seq(new[]{1},r.Items.Select(x=>x.LocationId));}
    private static async Task SearchTaxAndQ(){await Reset();await using var db=Db();await SeedLocation(db,1,name:"NameNeedle");await SeedLocation(db,2,address:"AddressNeedle");await SeedLocation(db,3,plus:"PlusNeedle");await SeedLocation(db,4,tax:"87654321");foreach(var x in new[]{("NameNeedle",1),("AddressNeedle",2),("PlusNeedle",3),("L3",3),("87654321",4)}){var r=await Repo(db).SearchManagedLocationsAsync(Admin(),new(Q:x.Item1,PageSize:20),default);True(r.Items.Any(i=>i.LocationId==x.Item2));}}
    private static async Task SearchFilters(){await Reset();await using var db=Db();await SeedLocation(db,1,city:"Taipei",district:"Xinyi",active:true);await SeedLocation(db,2,city:"Taipei",district:"Daan",active:false);var r=await Repo(db).SearchManagedLocationsAsync(Admin(),new(City:"Taipei",District:"Xinyi",IsActive:true,PageSize:20),default);Seq(new[]{1},r.Items.Select(x=>x.LocationId));}
    private static async Task SearchSortPagination(){await Reset();await using var db=Db();for(var i=1;i<=21;i++)await SeedLocation(db,i,name:$"N{i:00}",city:"Taipei",district:"Xinyi");var p1=await Repo(db).SearchManagedLocationsAsync(Admin(),new(Page:1,PageSize:20),default);var p2=await Repo(db).SearchManagedLocationsAsync(Admin(),new(Page:2,PageSize:20),default);Eq(21,p1.TotalCount);Eq(20,p1.Items.Count);Eq(1,p2.Items.Count);Eq(21,p2.Items[0].LocationId);}

    private static Task Status(Exception ex,int expected){Eq(expected,ApiExceptionStatus.From(ex));return Task.CompletedTask;}
    private static async Task<T> Throws<T>(Func<Task> f) where T:Exception{try{await f();}catch(T ex){return ex;}throw new Exception($"Expected {typeof(T).Name}");}
    private static void True(bool b){if(!b)throw new Exception("Assertion failed");}
    private static void Eq<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected [{expected}] got [{actual}]");}
    private static void Seq<T>(IEnumerable<T> expected,IEnumerable<T> actual){if(!expected.SequenceEqual(actual))throw new Exception("Sequence mismatch");}
    private static void GovEq(GovState a,GovState b){Eq(a.TaxId,b.TaxId);Eq(a.MasterNote,b.MasterNote);Eq(a.DuplicateId,b.DuplicateId);Eq(a.DuplicateReason,b.DuplicateReason);True(a.RowVersion.SequenceEqual(b.RowVersion));}
    private static void StateEq(LocationState a,LocationState b){Eq(a.IsActive,b.IsActive);Eq(a.InactivatedAt,b.InactivatedAt);Eq(a.InactivatedByUserId,b.InactivatedByUserId);Eq(a.UpdatedAt,b.UpdatedAt);True(a.RowVersion.SequenceEqual(b.RowVersion));Eq(a.Geocoding,b.Geocoding);Eq(a.Name,b.Name);Eq(a.City,b.City);Eq(a.District,b.District);Eq(a.Address,b.Address);Eq(a.PlusCode,b.PlusCode);Eq(a.TeamId,b.TeamId);}
    private static void AuditEq(AuditState a,int id,string action,int user){Eq("Location",a.EntityType);Eq(id.ToString(),a.EntityId);Eq(action,a.Action);Eq<int?>(user,a.UserId);True(a.CorrelationId.HasValue);}

    private sealed record GovState(string? TaxId,string? MasterNote,int? DuplicateId,string? DuplicateReason,byte[] RowVersion);
    private sealed record LocationState(bool IsActive,DateTime? InactivatedAt,int? InactivatedByUserId,DateTime? UpdatedAt,byte[] RowVersion,string Geocoding,string Name,string? City,string? District,string? Address,string? PlusCode,int? TeamId);
    private sealed record AuditState(string EntityType,string? EntityId,string Action,int? UserId,string? NewValues,Guid? CorrelationId);
}
