using System.Reflection;
using FieldVisit.Api;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180DbWork1ProjectVisitTypeTests
{
    private static readonly byte[] Version = [1,2,3,4,5,6,7,8];
    private static readonly string Token = Convert.ToBase64String(Version);
    private static readonly string StaleToken = Convert.ToBase64String([8,7,6,5,4,3,2,1]);
    private static readonly DateOnly VisitDate = new(2026,9,10);

    [Fact]
    public async Task DB1_01_Project_create_is_active_by_default()
    {
        await using var db = await ProjectDbAsync();
        var service = Master(db, Admin());
        var created = await service.CreateProjectAsync(new(10," P2 "," Project Two ",null,"List",VisitDate,null), default);
        Assert.True(created.IsActive);
        Assert.True((await db.Projects.SingleAsync(x=>x.ProjectCode=="P2")).IsActive);
    }

    [Fact]
    public async Task DB1_02_Project_ordinary_update_preserves_lifecycle_state()
    {
        await using var db = await ProjectDbAsync(active:false);
        var service = Master(db, Admin());
        await service.UpdateProjectAsync(1,new(10,"P1","Renamed",null,"List",VisitDate,null,Token),default);
        var row=await db.Projects.SingleAsync(x=>x.ProjectId==1);
        Assert.False(row.IsActive);
        Assert.Equal("Renamed",row.ProjectName);
    }

    [Fact]
    public async Task DB1_03_Project_update_requires_matching_RowVersion_and_maps_conflict_to_409()
    {
        await using var db = await ProjectDbAsync();
        var service=Master(db,Admin());
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>service.UpdateProjectAsync(1,new(10,"P1","X",null,"List",VisitDate,null,StaleToken),default));
        Assert.Equal(409,ApiExceptionStatus.From(ex));
        Assert.Empty(db.AuditLogs);
        Assert.Equal("Project 1",(await db.Projects.SingleAsync()).ProjectName);
    }

    [Fact]
    public async Task DB1_04_Project_deactivate_sets_lifecycle_metadata()
    {
        await using var db=await ProjectDbAsync();
        var result=await Master(db,Admin()).DeactivateProjectAsync(1,Token,default);
        Assert.False(result.IsActive);
        var row=await db.Projects.SingleAsync();
        Assert.NotNull(row.InactivatedAt);
        Assert.Equal(99,row.InactivatedByUserId);
        Assert.Single(db.AuditLogs.Where(x=>x.Action=="ProjectDeactivate"));
    }

    [Fact]
    public async Task DB1_05_Project_stale_deactivate_has_zero_success_audit_or_mutation()
    {
        await using var db=await ProjectDbAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).DeactivateProjectAsync(1,StaleToken,default));
        Assert.True((await db.Projects.SingleAsync()).IsActive);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task DB1_06_Project_reactivate_clears_lifecycle_metadata()
    {
        await using var db=await ProjectDbAsync(active:false,inactivated:true);
        var result=await Master(db,Admin()).ReactivateProjectAsync(1,new(Token),default);
        Assert.True(result.IsActive);
        var row=await db.Projects.SingleAsync();
        Assert.Null(row.InactivatedAt);
        Assert.Null(row.InactivatedByUserId);
    }

    [Fact]
    public void DB1_07_Project_exposes_dedicated_lifecycle_without_DELETE_bypass()
    {
        var methods=typeof(MasterController).GetMethods();
        Assert.Contains(methods,m=>Route(m)=="projects/{projectId:int}/deactivate");
        Assert.Contains(methods,m=>Route(m)=="projects/{projectId:int}/reactivate");
        Assert.DoesNotContain(methods,m=>m.GetCustomAttributes<HttpMethodAttribute>().Any(a=>a.HttpMethods.Contains("DELETE")&&a.Template=="projects/{projectId:int}"));
    }

    [Fact]
    public async Task DB1_08_Project_code_is_unique_within_organization()
    {
        await using var db=await ProjectDbAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).CreateProjectAsync(new(10,"P1","Duplicate",null,"List",VisitDate,null),default));
    }

    [Fact]
    public async Task DB1_09_Project_team_must_belong_to_admin_organization()
    {
        await using var db=await ProjectDbAsync();
        db.Teams.Add(new Team{TeamId=20,OrganizationId=2,TeamCode="T2",TeamName="Other",IsActive=true,RowVersion=Version.ToArray()});
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).CreateProjectAsync(new(20,"P3","Wrong Org",null,"List",VisitDate,null),default));
    }

    [Fact]
    public async Task DB1_10_Submit_allows_NULL_ProjectId()
    {
        await using var db=await TripDbAsync(projects:[]);
        var trip=await SeedTripAsync(db,TripStatuses.Draft,[null,null]);
        var result=await Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default);
        Assert.Equal(TripStatuses.Submitted,result.Status);
    }

    [Fact]
    public async Task DB1_11_Submit_accepts_same_Project_on_multiple_stops()
    {
        await using var db=await TripDbAsync(projects:[ProjectRow(1)]);
        var trip=await SeedTripAsync(db,TripStatuses.Draft,[1,1]);
        Assert.Equal(TripStatuses.Submitted,(await Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default)).Status);
    }

    [Fact]
    public async Task DB1_12_Submit_validates_multiple_distinct_Projects()
    {
        await using var db=await TripDbAsync(projects:[ProjectRow(1),ProjectRow(2)]);
        var trip=await SeedTripAsync(db,TripStatuses.Draft,[1,2]);
        Assert.Equal(TripStatuses.Submitted,(await Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default)).Status);
    }

    [Fact]
    public async Task DB1_13_Project_deactivated_after_draft_blocks_Submit_with_zero_side_effects()
    {
        await using var db=await TripDbAsync(projects:[ProjectRow(1,active:false)]);
        var trip=await SeedTripAsync(db,TripStatuses.Draft,[1,1]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default));
        await AssertSubmitUnchanged(db,trip.VisitTripId,TripStatuses.Draft);
    }

    [Fact]
    public async Task DB1_14_Project_date_changed_after_draft_blocks_Submit_with_zero_side_effects()
    {
        await using var db=await TripDbAsync(projects:[ProjectRow(1,start:VisitDate.AddDays(1))]);
        var trip=await SeedTripAsync(db,TripStatuses.Draft,[1,1]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default));
        await AssertSubmitUnchanged(db,trip.VisitTripId,TripStatuses.Draft);
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task DB1_15_Project_org_or_team_scope_change_after_draft_blocks_Submit(bool wrongOrg,bool wrongTeam)
    {
        var project=ProjectRow(1,organizationId:wrongOrg?2:1,teamId:wrongTeam?20:10);
        await using var db=await TripDbAsync(projects:[project]);
        var trip=await SeedTripAsync(db,TripStatuses.Draft,[1,1]);
        await Assert.ThrowsAnyAsync<Exception>(()=>Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default));
        await AssertSubmitUnchanged(db,trip.VisitTripId,TripStatuses.Draft);
    }

    [Fact]
    public async Task DB1_16_Returned_Resubmit_runs_same_Project_revalidation()
    {
        await using var db=await TripDbAsync(projects:[ProjectRow(1,active:false)]);
        var trip=await SeedTripAsync(db,TripStatuses.Returned,[1,1]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Trip(db,Visitor()).SubmitAsync(trip.VisitTripId,new(false),Token,default));
        await AssertSubmitUnchanged(db,trip.VisitTripId,TripStatuses.Returned);
    }

    [Fact]
    public async Task DB1_17_Historical_Project_snapshot_is_unchanged_after_master_edit()
    {
        await using var db=await ProjectDbAsync();
        db.VisitTripSnapshots.Add(Snapshot(projectId:1,projectCode:"P1-OLD",projectName:"Frozen Project",visitTypeId:null,visitTypeCode:null,visitTypeName:null));
        await db.SaveChangesAsync();
        await Master(db,Admin()).UpdateProjectAsync(1,new(10,"P1","Current Project Renamed",null,"List",VisitDate,null,Token),default);
        var frozen=await db.VisitTripSnapshotStops.AsNoTracking().SingleAsync();
        Assert.Equal("P1-OLD",frozen.ProjectCodeSnapshot);
        Assert.Equal("Frozen Project",frozen.ProjectNameSnapshot);
    }

    [Fact]
    public void DB2_01_VisitType_is_GLOBAL_without_OrganizationId()
        => Assert.Null(typeof(VisitType).GetProperty("OrganizationId",BindingFlags.Public|BindingFlags.Instance));

    [Fact]
    public async Task DB2_02_VisitType_create_is_active_and_MAX_plus_10()
    {
        await using var db=await VisitTypeDbAsync();
        var coordinator=new CountingCoordinator();
        var result=await Master(db,Admin(),coordinator).CreateVisitTypeAsync(new("C","Third",null),default);
        Assert.True(result.IsActive);
        Assert.Equal(30,result.SortOrder);
        Assert.Equal(1,coordinator.Calls);
    }

    [Fact]
    public async Task DB2_03_VisitType_ordinary_update_preserves_SortOrder_and_IsActive()
    {
        await using var db=await VisitTypeDbAsync(firstActive:false);
        await Master(db,Admin()).UpdateVisitTypeAsync(1,new("A","Renamed",null,Token),default);
        var row=await db.VisitTypes.SingleAsync(x=>x.VisitTypeId==1);
        Assert.False(row.IsActive);
        Assert.Equal(10,row.SortOrder);
    }

    [Fact]
    public async Task DB2_04_VisitType_stale_update_maps_409_and_has_zero_audit()
    {
        await using var db=await VisitTypeDbAsync();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).UpdateVisitTypeAsync(1,new("A","X",null,StaleToken),default));
        Assert.Equal(409,ApiExceptionStatus.From(ex));
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task DB2_05_VisitType_deactivate_uses_membership_coordinator_and_sets_metadata()
    {
        await using var db=await VisitTypeDbAsync();
        var coordinator=new CountingCoordinator();
        var result=await Master(db,Admin(),coordinator).DeactivateVisitTypeAsync(1,Token,default);
        Assert.False(result.IsActive);
        Assert.Equal(1,coordinator.Calls);
        var row=await db.VisitTypes.SingleAsync(x=>x.VisitTypeId==1);
        Assert.NotNull(row.InactivatedAt);
        Assert.Equal(99,row.InactivatedByUserId);
    }

    [Fact]
    public async Task DB2_06_VisitType_stale_deactivate_has_zero_audit_or_mutation()
    {
        await using var db=await VisitTypeDbAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).DeactivateVisitTypeAsync(1,StaleToken,default));
        Assert.True((await db.VisitTypes.SingleAsync(x=>x.VisitTypeId==1)).IsActive);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task DB2_07_VisitType_reactivate_clears_metadata_under_same_coordinator()
    {
        await using var db=await VisitTypeDbAsync(firstActive:false,firstInactivated:true);
        var coordinator=new CountingCoordinator();
        var result=await Master(db,Admin(),coordinator).ReactivateVisitTypeAsync(1,new(Token),default);
        Assert.True(result.IsActive);
        Assert.Equal(1,coordinator.Calls);
        var row=await db.VisitTypes.SingleAsync(x=>x.VisitTypeId==1);
        Assert.Null(row.InactivatedAt);
        Assert.Null(row.InactivatedByUserId);
    }

    [Fact]
    public async Task DB2_08_VisitType_code_uniqueness_is_GLOBAL()
    {
        await using var db=await VisitTypeDbAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).CreateVisitTypeAsync(new("A","Duplicate",null),default));
    }

    [Fact]
    public async Task DB2_09_Reorder_accepts_complete_active_expectedOrder_and_compacts_to_10_steps()
    {
        await using var db=await VisitTypeDbAsync();
        var coordinator=new CountingCoordinator();
        var result=await Master(db,Admin(),coordinator).ReorderVisitTypesAsync(new([2,1]),default);
        Assert.Equal([2,1],result.Select(x=>x.VisitTypeId));
        Assert.Equal([10,20],result.Select(x=>x.SortOrder));
        Assert.Equal(1,coordinator.Calls);
    }

    [Fact]
    public async Task DB2_10_Reorder_rejects_duplicate_membership()
    {
        await using var db=await VisitTypeDbAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).ReorderVisitTypesAsync(new([1,1]),default));
        Assert.Empty(db.AuditLogs);
    }

    [Theory]
    [MemberData(nameof(InvalidExpectedOrders))]
    public async Task DB2_11_Reorder_rejects_missing_unknown_extra_inactive_or_incomplete_membership(int[] expected)
    {
        await using var db=await VisitTypeDbAsync(includeInactive:true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db,Admin()).ReorderVisitTypesAsync(new(expected),default));
        Assert.Empty(db.AuditLogs);
    }

    public static IEnumerable<object[]> InvalidExpectedOrders()=>new[]
    {
        new object[]{Array.Empty<int>()},
        new object[]{new[]{1,1}},
        new object[]{new[]{1,99}},
        new object[]{new[]{1,2,99}},
        new object[]{new[]{1,2,3}}
    };

    [Fact]
    public async Task DB2_12_Create_Deactivate_Reactivate_Reorder_all_enter_membership_coordinator()
    {
        await using var db=await VisitTypeDbAsync();
        var coordinator=new CountingCoordinator();
        var service=Master(db,Admin(),coordinator);
        var created=await service.CreateVisitTypeAsync(new("C","Third",null),default);
        var row=await db.VisitTypes.SingleAsync(x=>x.VisitTypeId==created.VisitTypeId); row.RowVersion=Version.ToArray(); await db.SaveChangesAsync();
        await service.DeactivateVisitTypeAsync(created.VisitTypeId,Token,default);
        await service.ReactivateVisitTypeAsync(created.VisitTypeId,new(Token),default);
        await service.ReorderVisitTypesAsync(new([1,2,created.VisitTypeId]),default);
        Assert.Equal(4,coordinator.Calls);
    }

    [Fact]
    public void DB2_13_Production_coordinator_declares_exact_global_lock_contract()
    {
        var source=Source("backend/src/FieldVisit.Infrastructure/VisitTypeMembershipCoordinator.cs");
        Assert.Contains("FieldVisit.VisitTypeOrder",source);
        Assert.Contains("Exclusive",source);
        Assert.Contains("Transaction",source);
        Assert.Contains("@LockTimeout=10000",source);
        Assert.Contains("UPDLOCK,HOLDLOCK",source);
    }

    [Fact]
    public void DB2_14_VisitType_exposes_dedicated_lifecycle_and_reorder_without_DELETE_bypass()
    {
        var methods=typeof(MasterController).GetMethods();
        Assert.Contains(methods,m=>Route(m)=="visit-types/{visitTypeId:int}/deactivate");
        Assert.Contains(methods,m=>Route(m)=="visit-types/{visitTypeId:int}/reactivate");
        Assert.Contains(methods,m=>Route(m)=="visit-types/reorder");
        Assert.DoesNotContain(methods,m=>m.GetCustomAttributes<HttpMethodAttribute>().Any(a=>a.HttpMethods.Contains("DELETE")&&a.Template=="visit-types/{visitTypeId:int}"));
    }

    [Fact]
    public async Task DB2_15_Historical_VisitType_snapshot_is_unchanged_after_master_edit()
    {
        await using var db=await VisitTypeDbAsync();
        db.VisitTripSnapshots.Add(Snapshot(projectId:null,projectCode:null,projectName:null,visitTypeId:1,visitTypeCode:"A-OLD",visitTypeName:"Frozen Type"));
        await db.SaveChangesAsync();
        await Master(db,Admin()).UpdateVisitTypeAsync(1,new("A","Current Type Renamed",null,Token),default);
        var frozen=await db.VisitTripSnapshotStops.AsNoTracking().SingleAsync();
        Assert.Equal("A-OLD",frozen.VisitTypeCodeSnapshot);
        Assert.Equal("Frozen Type",frozen.VisitTypeNameSnapshot);
    }

    [Fact]
    public async Task DB2_16_Non_admin_cannot_mutate_VisitType()
    {
        await using var db=await VisitTypeDbAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Master(db,Visitor()).UpdateVisitTypeAsync(1,new("A","X",null,Token),default));
    }

    private static CurrentUserDto Admin()=>new(99,"ADMIN","Admin",null,1,null,null,["admin"]);
    private static CurrentUserDto Visitor()=>new(7,"V7","Visitor",null,1,10,"Team 10",["visitor"],[new TeamScopeDto(10,"Team 10",true)]);
    private static AppDbContext MemoryDb()=>new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MasterService Master(AppDbContext db,CurrentUserDto user,CountingCoordinator? coordinator=null)=>
        new(new FixedCurrentUser(user),new MasterRepository(db),new MileageRepository(db),new NoopGeocoder(),new WorkflowRepository(db),coordinator??new CountingCoordinator(),db);

    private static TripService Trip(AppDbContext db,CurrentUserDto user)=>new(
        new FixedCurrentUser(user),new UserRepository(db),new TripRepository(db),new MasterRepository(db),new MileageRepository(db),new WorkflowRepository(db),new NoopAccess(),new ForbiddenTripContext(),new NoopSnapshots(),db);

    private static async Task<AppDbContext> ProjectDbAsync(bool active=true,bool inactivated=false)
    {
        var db=MemoryDb();
        db.Organizations.Add(new Organization{OrganizationId=1,OrganizationCode="O1",OrganizationName="Org",IsActive=true,RowVersion=Version.ToArray()});
        db.Teams.Add(new Team{TeamId=10,OrganizationId=1,TeamCode="T10",TeamName="Team 10",IsActive=true,RowVersion=Version.ToArray()});
        db.Projects.Add(ProjectRow(1,active:active,inactivated:inactivated));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<AppDbContext> VisitTypeDbAsync(bool firstActive=true,bool firstInactivated=false,bool includeInactive=false)
    {
        var db=MemoryDb();
        db.VisitTypes.AddRange(
            new VisitType{VisitTypeId=1,VisitTypeCode="A",VisitTypeName="First",SortOrder=10,IsActive=firstActive,InactivatedAt=firstInactivated?DateTime.UtcNow:null,InactivatedByUserId=firstInactivated?99:null,CreatedAt=DateTime.UtcNow,RowVersion=Version.ToArray()},
            new VisitType{VisitTypeId=2,VisitTypeCode="B",VisitTypeName="Second",SortOrder=20,IsActive=true,CreatedAt=DateTime.UtcNow,RowVersion=Version.ToArray()});
        if(includeInactive) db.VisitTypes.Add(new VisitType{VisitTypeId=3,VisitTypeCode="Z",VisitTypeName="Inactive",SortOrder=30,IsActive=false,CreatedAt=DateTime.UtcNow,RowVersion=Version.ToArray()});
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<AppDbContext> TripDbAsync(Project[] projects)
    {
        var db=MemoryDb();
        db.Teams.Add(new Team{TeamId=10,OrganizationId=1,TeamCode="T10",TeamName="Team 10",IsActive=true,RowVersion=Version.ToArray()});
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();
        return db;
    }

    private static Project ProjectRow(int id,bool active=true,int organizationId=1,int? teamId=10,DateOnly? start=null,bool inactivated=false)=>new()
    {
        ProjectId=id,OrganizationId=organizationId,TeamId=teamId,ProjectCode=$"P{id}",ProjectName=$"Project {id}",LocationMode="List",
        StartDate=start??VisitDate.AddDays(-5),EndDate=VisitDate.AddDays(5),IsActive=active,CreatedAt=DateTime.UtcNow,
        InactivatedAt=inactivated?DateTime.UtcNow:null,InactivatedByUserId=inactivated?99:null,RowVersion=Version.ToArray()
    };

    private static async Task<VisitTrip> SeedTripAsync(AppDbContext db,string status,int?[] projectIds)
    {
        var trip=new VisitTrip{VisitTripId=100,TripNo="T100",UserId=7,OrganizationId=1,TeamId=10,VisitDate=VisitDate,StartTime=new(9,0),EndTime=new(10,0),Status=status,CreatedAt=DateTime.UtcNow,CreatedByUserId=7,RowVersion=Version.ToArray()};
        var seq=1;
        foreach(var projectId in projectIds) trip.Stops.Add(new VisitTripStop{VisitTripStopId=seq,VisitTripId=100,StopSequence=seq++,ProjectId=projectId,LocationNameSnapshot="Stop",CreatedAt=DateTime.UtcNow,VisitTrip=trip});
        db.VisitTrips.Add(trip);
        db.MileageCalculations.Add(new MileageCalculation{MileageCalculationId=1,VisitTripId=100,ClaimedDistanceKm=5,CreatedAt=DateTime.UtcNow,VisitTrip=trip});
        await db.SaveChangesAsync();
        return trip;
    }

    private static async Task AssertSubmitUnchanged(AppDbContext db,long tripId,string expectedStatus)
    {
        Assert.Equal(expectedStatus,(await db.VisitTrips.SingleAsync(x=>x.VisitTripId==tripId)).Status);
        Assert.Empty(db.VisitTripStatusHistories);
        Assert.Empty(db.AuditLogs);
        Assert.Empty(db.VisitTripSnapshots);
    }

    private static VisitTripSnapshot Snapshot(int? projectId,string? projectCode,string? projectName,int? visitTypeId,string? visitTypeCode,string? visitTypeName)
    {
        var snapshot=new VisitTripSnapshot{VisitTripSnapshotId=1,VisitTripId=900,SnapshotVersion=1,SnapshotType="Approved",TripNo="OLD",UserId=7,EmployeeNoSnapshot="V7",DisplayNameSnapshot="Visitor",OrganizationId=1,OrganizationNameSnapshot="Org",VisitDate=VisitDate,StatusSnapshot="Approved",CreatedAt=DateTime.UtcNow};
        snapshot.Stops.Add(new VisitTripSnapshotStop{VisitTripSnapshotStopId=1,VisitTripSnapshotId=1,StopSequence=1,LocationNameSnapshot="Frozen Stop",ProjectId=projectId,ProjectCodeSnapshot=projectCode,ProjectNameSnapshot=projectName,VisitTypeId=visitTypeId,VisitTypeCodeSnapshot=visitTypeCode,VisitTypeNameSnapshot=visitTypeName,CreatedAt=DateTime.UtcNow,Snapshot=snapshot});
        return snapshot;
    }

    private static string? Route(MethodInfo method)=>method.GetCustomAttributes<HttpMethodAttribute>().Select(x=>x.Template).FirstOrDefault(x=>x is not null);
    private static string Source(string relative)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,relative))) directory=directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName,relative));
    }

    private sealed class FixedCurrentUser(CurrentUserDto user):ICurrentUserService { public CurrentUserDto GetRequired()=>user; }
    private sealed class CountingCoordinator:IVisitTypeMembershipCoordinator
    {
        public int Calls{get;private set;}
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> operation,CancellationToken ct){Calls++;return await operation(ct);}
    }
    private sealed class NoopGeocoder:IGeocodingService { public Task<GeocodingResult> ResolveAsync(string? address,string? plusCode,CancellationToken ct)=>Task.FromResult(new GeocodingResult(false,null,null,"NOOP","NOOP")); }
    private sealed class NoopAccess:IV170AccessControl
    {
        public Task<V170LoginEligibility> EvaluateLoginAsync(int userId,bool adminEnabled,CancellationToken ct)=>Task.FromResult(new V170LoginEligibility(true,"Internal","Active",null));
        public Task<V170ReadScope> ResolveReadScopeAsync(CurrentUserDto user,CancellationToken ct)=>Task.FromResult(new V170ReadScope(false,user.TeamIds));
        public Task<bool> HasCapabilityAsync(int userId,string capabilityCode,CancellationToken ct)=>Task.FromResult(false);
        public Task EnsureExportAllowedAsync(CurrentUserDto user,string format,CancellationToken ct)=>Task.CompletedTask;
        public Task AuditSupervisorQueryAsync(CurrentUserDto user,TripQueryRequest request,int resultCount,CancellationToken ct)=>Task.CompletedTask;
    }
    private sealed class ForbiddenTripContext:IV180TripContextReader { public Task<V180TripContextDto> ResolveAsync(CurrentUserDto user,DateOnly visitDate,int? teamId,CancellationToken ct)=>throw new InvalidOperationException("legacy test should not call v1.8 trip context"); }
    private sealed class NoopSnapshots:ITripSnapshotRepository
    {
        public Task AddSubmittedSnapshotAsync(VisitTrip trip,CurrentUserDto submitter,V180TripContextDto context,CancellationToken ct)=>Task.CompletedTask;
        public Task AddApprovedSnapshotAsync(VisitTrip trip,CurrentUserDto approver,CancellationToken ct)=>Task.CompletedTask;
        public Task<VisitTripSnapshot?> GetLatestAsync(long tripId,string snapshotType,CancellationToken ct)=>Task.FromResult<VisitTripSnapshot?>(null);
    }
}
