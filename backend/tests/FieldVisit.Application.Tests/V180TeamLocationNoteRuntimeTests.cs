using System.Reflection;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TeamLocationNoteRuntimeTests
{
    private static readonly byte[] V1 = [1,2,3,4,5,6,7,8];
    private static readonly byte[] V2 = [2,3,4,5,6,7,8,9];
    private static readonly byte[] V3 = [3,4,5,6,7,8,9,10];
    private static string Token(byte[] value) => Convert.ToBase64String(value);

    [Fact]
    public void Ef_model_matches_nullable_tombstone_unique_row_and_rowversion_contract()
    {
        using var db = MemoryDb();
        var entity = db.Model.FindEntityType(typeof(V180TeamLocationNoteRow));
        Assert.NotNull(entity);
        Assert.Equal("TeamLocationNotes", entity!.GetTableName());
        Assert.True(entity.FindProperty(nameof(V180TeamLocationNoteRow.Note))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(V180TeamLocationNoteRow.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(V180TeamLocationNoteRow.TeamId), nameof(V180TeamLocationNoteRow.LocationId) }));
    }

    [Fact]
    public async Task NeverExisted_create_duplicate_and_visitor_read_follow_frozen_state_contract()
    {
        await using var db = await SeedAsync();
        var service = new V180TeamLocationNoteService(db);

        var never = await service.GetAsync(Actor(1,"visitor",1), 10, 100, default);
        Assert.Equal(V180TeamLocationNoteStates.NeverExisted, never.State);
        Assert.Null(never.TeamLocationNoteId);

        var created = await service.CreateAsync(Actor(2,"leader",1), 10,
            new(100,"  Call reception first  ","initial"), default);
        Assert.Equal(V180TeamLocationNoteStates.Active, created.State);
        Assert.Equal("Call reception first", created.Note);
        Assert.True(created.TeamLocationNoteId > 0);

        var visitor = await service.GetAsync(Actor(1,"visitor",1), 10, 100, default);
        Assert.Equal(created.TeamLocationNoteId, visitor.TeamLocationNoteId);
        Assert.Equal("Call reception first", visitor.Note);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.CreateAsync(
            Actor(2,"leader",1), 10, new(100,"second row"), default));

        var history = await db.TeamLocationNoteHistory.AsNoTracking().ToListAsync();
        Assert.Single(history);
        Assert.Equal("Created", history[0].Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_null_empty_or_whitespace(string? note)
    {
        await using var db = await SeedAsync();
        var service = new V180TeamLocationNoteService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            Actor(2,"leader",1), 10, new(101,note), default));
        Assert.Empty(await db.TeamLocationNotes.AsNoTracking().ToListAsync());
        Assert.Empty(await db.TeamLocationNoteHistory.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Update_clear_idempotent_clear_and_restore_keep_one_row_and_atomic_history()
    {
        await using var db = await SeedAsync();
        var service = new V180TeamLocationNoteService(db);
        var created = await service.CreateAsync(Actor(2,"leader",1), 10, new(101,"A"), default);
        var id = created.TeamLocationNoteId!.Value;
        var tracked = await db.TeamLocationNotes.Include(x=>x.History).SingleAsync(x=>x.TeamLocationNoteId==id);
        tracked.RowVersion = V1.ToArray();
        await db.SaveChangesAsync();

        var updated = await service.UpdateAsync(Actor(2,"leader",1), id,
            new("B","edit",Token(V1)), default);
        Assert.Equal(id, updated.TeamLocationNoteId);
        Assert.Equal("B", updated.Note);
        Assert.Equal(new[] { "Created", "Updated" },
            tracked.History.OrderBy(x=>x.TeamLocationNoteHistoryId).Select(x=>x.Action).ToArray());

        tracked.RowVersion = V2.ToArray();
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            Actor(2,"leader",1), id, new("SHOULD-NOT-WRITE",null,Token(V1)), default));
        Assert.Equal("B", tracked.Note);
        Assert.Equal(2, tracked.History.Count);

        var cleared = await service.ClearAsync(Actor(2,"leader",1), id,
            new("clear",Token(V2)), default);
        Assert.Equal(V180TeamLocationNoteStates.Cleared, cleared.State);
        Assert.Null(cleared.Note);
        Assert.Equal("Cleared", tracked.History.OrderBy(x=>x.TeamLocationNoteHistoryId).Last().Action);
        var historyCount = tracked.History.Count;
        var versionBeforeNoOp = Token(tracked.RowVersion);

        var noOp = await service.ClearAsync(Actor(2,"leader",1), id,
            new("must not duplicate",Token(V1)), default);
        Assert.Equal(V180TeamLocationNoteStates.Cleared, noOp.State);
        Assert.Equal(historyCount, tracked.History.Count);
        Assert.Equal(versionBeforeNoOp, Token(tracked.RowVersion));

        tracked.RowVersion = V3.ToArray();
        await db.SaveChangesAsync();
        var restored = await service.UpdateAsync(Actor(2,"leader",1), id,
            new("Restored text","restore",Token(V3)), default);
        Assert.Equal(id, restored.TeamLocationNoteId);
        Assert.Equal(V180TeamLocationNoteStates.Active, restored.State);
        Assert.Equal("Restored text", restored.Note);
        Assert.Equal("Updated", tracked.History.OrderBy(x=>x.TeamLocationNoteHistoryId).Last().Action);
        Assert.DoesNotContain(tracked.History, x=>x.Action=="Restored");
        Assert.Single(await db.TeamLocationNotes.AsNoTracking().Where(x=>x.TeamId==10&&x.LocationId==101).ToListAsync());
    }

    [Fact]
    public async Task Stale_clear_while_active_conflicts_without_partial_mutation()
    {
        await using var db = await SeedAsync();
        var service = new V180TeamLocationNoteService(db);
        var created = await service.CreateAsync(Actor(2,"leader",1), 10, new(101,"Active"), default);
        var row = await db.TeamLocationNotes.Include(x=>x.History).SingleAsync();
        row.RowVersion = V2.ToArray();
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearAsync(
            Actor(2,"leader",1), created.TeamLocationNoteId!.Value, new(null,Token(V1)), default));
        Assert.Equal("Active", row.Note);
        Assert.Single(row.History);
        Assert.Equal("Created", row.History[0].Action);
    }

    [Fact]
    public async Task Authoritative_today_scope_controls_visitor_leader_delegation_and_admin()
    {
        await using var db = await SeedAsync();
        var service = new V180TeamLocationNoteService(db);

        Assert.Equal(V180TeamLocationNoteStates.NeverExisted,
            (await service.GetAsync(Actor(1,"visitor",1,legacyTeamId:999),10,100,default)).State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            Actor(1,"visitor",1,legacyTeamId:11),11,100,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            Actor(1,"visitor",1,legacyTeamId:10),10,new(101,"visitor write"),default));

        var direct = await service.CreateAsync(Actor(2,"leader",1,legacyTeamId:999),10,new(101,"direct"),default);
        Assert.Equal(V180TeamLocationNoteStates.Active,direct.State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            Actor(2,"leader",1,legacyTeamId:11),11,102,default));

        var delegated = await service.CreateAsync(Actor(3,"leader",1,legacyTeamId:999),11,new(102,"delegated"),default);
        Assert.Equal(V180TeamLocationNoteStates.Active,delegated.State);

        Assert.Equal(V180TeamLocationNoteStates.Active,
            (await service.GetAsync(Actor(4,"admin",1),10,101,default)).State);
        await service.CreateAsync(Actor(4,"admin",1),11,new(100,"admin global"),default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            Actor(4,"admin",1),20,200,default));
        Assert.Equal(V180TeamLocationNoteStates.NeverExisted,
            (await service.GetAsync(Actor(5,"admin",2),20,200,default)).State);

        Assert.Equal(new[] { 10 },
            (await service.GetWritableTeamsAsync(Actor(2,"leader",1,legacyTeamId:999),default)).Select(x=>x.TeamId).ToArray());
        Assert.Equal(new[] { 11 },
            (await service.GetWritableTeamsAsync(Actor(3,"leader",1,legacyTeamId:999),default)).Select(x=>x.TeamId).ToArray());
        Assert.Equal(new[] { 10, 11 },
            (await service.GetWritableTeamsAsync(Actor(4,"admin",1),default)).Select(x=>x.TeamId).OrderBy(x=>x).ToArray());
    }

    [Fact]
    public void Routes_frontend_surface_and_historical_trip_snapshot_paths_remain_additive()
    {
        var controller = typeof(V180TeamLocationNotesController);
        var expected = new Dictionary<string,string>
        {
            ["Get"]="teams/{teamId:int}/location-notes",
            ["WritableTeams"]="team-location-notes/teams",
            ["Create"]="teams/{teamId:int}/location-notes",
            ["Update"]="team-location-notes/{teamLocationNoteId:long}",
            ["Clear"]="team-location-notes/{teamLocationNoteId:long}/clear"
        };
        foreach(var pair in expected)
            Assert.Equal(pair.Value, controller.GetMethod(pair.Key)!.GetCustomAttributes<HttpMethodAttribute>().Single().Template);
        Assert.DoesNotContain(controller.GetMethods(), m=>m.GetCustomAttributes<HttpMethodAttribute>().Any(a=>a.HttpMethods.Contains("DELETE")));

        var picker = Source("frontend/src/components/SmartLocationPicker.tsx");
        var page = Source("frontend/src/pages/TeamLocationNotesPage.tsx");
        var app = Source("frontend/src/App.tsx");
        Assert.Contains("TeamLocationNoteViewer",picker);
        Assert.Contains("/leader/location-notes",app);
        Assert.Contains("/admin/location-notes",app);
        Assert.Contains("SmartLocationPicker",page);
        Assert.DoesNotContain("TaxId",page);
        Assert.DoesNotContain("MasterNote",page);
        Assert.DoesNotContain("DuplicateOf",page);
        Assert.DoesNotContain("InactivatedAt",page);

        Assert.DoesNotContain("TeamLocationNote",Source("backend/src/FieldVisit.Application/TripService.cs"));
        Assert.DoesNotContain("TeamLocationNote",Source("backend/src/FieldVisit.Application/V160FinalService.cs"));
        Assert.DoesNotContain("TeamLocationNote",Source("backend/src/FieldVisit.Infrastructure/V160Repositories.cs"));

        var runtime = Source("backend/src/FieldVisit.Infrastructure/V180TeamLocationNoteRuntime.cs");
        Assert.Contains("sql.Number is 2601 or 2627",runtime);
        Assert.Contains("if (row.Note is null)\n            return Map(row);",runtime);
    }

    private static V180TeamLocationNoteDbContext MemoryDb() => new(
        new DbContextOptionsBuilder<V180TeamLocationNoteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<V180TeamLocationNoteDbContext> SeedAsync()
    {
        var db=MemoryDb();var today=BusinessTime.Today;var from=today.AddDays(-30);var to=today.AddDays(30);var now=DateTime.UtcNow;
        db.Teams.AddRange(
            new Team{TeamId=10,OrganizationId=1,TeamCode="T10",TeamName="Team 10",IsActive=true,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now},
            new Team{TeamId=11,OrganizationId=1,TeamCode="T11",TeamName="Team 11",IsActive=true,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now},
            new Team{TeamId=20,OrganizationId=2,TeamCode="T20",TeamName="Team 20",IsActive=true,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now});
        db.Locations.AddRange(
            new Location{LocationId=100,OrganizationId=null,TeamId=null,LocationCode="GLOBAL",LocationName="Shared",IsActive=true,ApprovalStatus="Approved",CreatedAt=now},
            new Location{LocationId=101,OrganizationId=1,TeamId=10,LocationCode="L101",LocationName="Team10",IsActive=true,ApprovalStatus="Approved",CreatedAt=now},
            new Location{LocationId=102,OrganizationId=1,TeamId=11,LocationCode="L102",LocationName="Team11",IsActive=true,ApprovalStatus="Approved",CreatedAt=now},
            new Location{LocationId=200,OrganizationId=2,TeamId=20,LocationCode="L200",LocationName="Org2",IsActive=true,ApprovalStatus="Approved",CreatedAt=now});
        db.Roles.AddRange(
            new Role{RoleId=1,RoleCode="visitor",RoleName="Visitor",IsActive=true,CreatedAt=now},
            new Role{RoleId=2,RoleCode="leader",RoleName="Leader",IsActive=true,CreatedAt=now},
            new Role{RoleId=3,RoleCode="admin",RoleName="Admin",IsActive=true,CreatedAt=now});
        AddActor(db,1,101,1,1,from,to,now);AddActor(db,2,102,1,2,from,to,now);AddActor(db,3,103,1,2,from,to,now);AddActor(db,4,105,1,3,from,to,now);AddActor(db,5,106,2,3,from,to,now);
        db.TeamMemberships.Add(new TeamMembership{TeamMembershipId=1,EmploymentId=101,TeamId=10,IsPrimary=true,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now});
        db.TeamLeaderAssignments.AddRange(
            new TeamLeaderAssignment{TeamLeaderAssignmentId=1,TeamId=10,EmploymentId=102,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now},
            new TeamLeaderAssignment{TeamLeaderAssignmentId=2,TeamId=11,EmploymentId=104,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now});
        db.TeamLeaderDelegations.Add(new TeamLeaderDelegation{TeamLeaderDelegationId=1,TeamLeaderAssignmentId=2,DelegateEmploymentId=103,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now});
        await db.SaveChangesAsync();return db;
    }

    private static void AddActor(V180TeamLocationNoteDbContext db,int userId,long employmentId,int org,int roleId,DateOnly from,DateOnly to,DateTime now)
    {
        db.UserIdentityProfiles.Add(new UserIdentityProfile{UserId=userId,EmploymentId=employmentId,UserType=UserTypes.Internal,UserCode=$"U{userId}",CreatedAt=now});
        db.Employments.Add(new Employment{EmploymentId=employmentId,PersonId=employmentId,OrganizationId=org,EmployeeNo=$"E{userId}",SourceType="Test",CreatedAt=now});
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod{EmploymentStatusPeriodId=employmentId,EmploymentId=employmentId,EmploymentStatus=EmploymentStatuses.Active,EffectiveFrom=from,EffectiveTo=to,SourceType="Test",CreatedAt=now});
        db.EmploymentRoleAssignments.Add(new EmploymentRoleAssignment{EmploymentRoleAssignmentId=employmentId,EmploymentId=employmentId,RoleId=roleId,EffectiveFrom=from,EffectiveTo=to,CreatedAt=now});
    }

    private static CurrentUserDto Actor(int userId,string role,int org,int? legacyTeamId=null) =>
        new(userId,$"E{userId}",$"User {userId}",null,org,legacyTeamId,legacyTeamId.HasValue?$"Legacy {legacyTeamId}":null,[role],
            legacyTeamId.HasValue?[new TeamScopeDto(legacyTeamId.Value,$"Legacy {legacyTeamId}",true)]:[]);

    private static string Source(string relative)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"backend","FieldVisitSystem.sln")))directory=directory.Parent;
        Assert.NotNull(directory);return File.ReadAllText(Path.Combine(directory!.FullName,relative));
    }
}
