using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>InMemory B4 negative coverage: shared Location is not a way to
/// enumerate other organizations' or teams' notes/audit history.</summary>
public sealed class V180LocationMaintenanceReadScopeTests
{
    private static CurrentUserDto Actor() => new(
        11,"V11","Visitor",null,1,7,"Team 7",
        new[]{"visitor"},new[]{new TeamScopeDto(7,"Team 7",true)});

    [Fact]
    public void Requested_team_must_be_in_current_token_read_scope()
    {
        V180LocationMaintenanceReadRules.RequireAllowedTeam(Actor(),7,false);
        V180LocationMaintenanceReadRules.RequireAllowedTeam(Actor(),null,false);
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationMaintenanceReadRules.RequireAllowedTeam(Actor(),8,false));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationMaintenanceReadRules.RequireAllowedTeam(Actor(),0,false));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180LocationMaintenanceReadRules.RequireAllowedTeam(
                Actor() with { OrganizationId=null },7,false));
    }

    [Fact]
    public async Task Shared_location_does_not_expose_other_organization_or_team_history()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b4-location-read-{Guid.NewGuid()}").Options);
        db.Teams.AddRange(
            new Team{TeamId=7,OrganizationId=1,TeamCode="T7",TeamName="Team 7",IsActive=true},
            new Team{TeamId=8,OrganizationId=1,TeamCode="T8",TeamName="Team 8",IsActive=true},
            new Team{TeamId=9,OrganizationId=2,TeamCode="T9",TeamName="Team 9",IsActive=true});
        db.Users.AddRange(
            new User{UserId=11,OrganizationId=1,TeamId=7,DisplayName="Owner"},
            new User{UserId=12,OrganizationId=1,TeamId=8,DisplayName="Other team"},
            new User{UserId=13,OrganizationId=2,TeamId=9,DisplayName="Other org"});
        db.Locations.AddRange(
            new Location{LocationId=10,OrganizationId=null,TeamId=null,
                LocationName="Shared",ApprovalStatus="Approved",IsActive=true},
            new Location{LocationId=20,OrganizationId=2,TeamId=9,
                LocationName="Cross-org",DuplicateOfLocationId=10},
            new Location{LocationId=21,OrganizationId=1,TeamId=8,
                LocationName="Other-team",DuplicateOfLocationId=10},
            new Location{LocationId=22,OrganizationId=1,TeamId=7,
                LocationName="Own-team",DuplicateOfLocationId=10});
        void Note(long id,int team,int location,int author,string text) =>
            db.TeamLocationNoteHistories.Add(new TeamLocationNoteHistory{
                TeamLocationNoteHistoryId=id,TeamLocationNoteId=id,
                TeamId=team,LocationId=location,ChangedByUserId=author,
                Action="Updated",NewNote=text,ChangedAt=DateTime.UtcNow});
        Note(1,7,10,11,"same-team");
        Note(2,8,10,12,"other-team");
        Note(3,9,10,13,"other-org");
        Note(4,9,20,13,"cross-org-duplicate");
        Note(5,8,21,12,"other-team-duplicate");
        Note(6,7,22,11,"own-team-duplicate");
        void Audit(long id,int author,int location,string oldValue) =>
            db.AuditLogs.Add(new AuditLog{AuditLogId=id,UserId=author,
                EntityType="Location",EntityId=location.ToString(),
                Action="LocationMaintenanceUpdate",
                OldValues=oldValue,CreatedAt=DateTime.UtcNow});
        Audit(1,11,10,"own-audit");
        Audit(2,12,10,"peer-audit");
        Audit(3,13,10,"cross-org-audit");
        Audit(4,13,20,"cross-org-duplicate-audit");
        Audit(5,11,22,"own-team-duplicate-audit");
        await db.SaveChangesAsync();db.ChangeTracker.Clear();

        var repository=new V170LocationRepository(db);
        var result=await repository.GetMaintenanceAsync(Actor(),10,null,CancellationToken.None);
        Assert.Equal(new long[]{1,6},
            result.Notes.Select(x=>x.HistoryId).OrderBy(x=>x).ToArray());
        Assert.Equal(new long[]{1,5},
            result.AddressAudit.Select(x=>x.AuditLogId).OrderBy(x=>x).ToArray());
        Assert.DoesNotContain(result.Notes,x=>x.Note.Contains("other-",StringComparison.Ordinal));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repository.GetMaintenanceAsync(Actor(),10,9,CancellationToken.None));
    }

    [Fact]
    public async Task Same_org_admin_may_see_only_same_org_shared_location_histories()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b4-location-admin-read-{Guid.NewGuid()}").Options);
        db.Teams.AddRange(
            new Team{TeamId=7,OrganizationId=1,TeamName="A"},
            new Team{TeamId=9,OrganizationId=2,TeamName="B"});
        db.Users.AddRange(
            new User{UserId=11,OrganizationId=1,DisplayName="A"},
            new User{UserId=13,OrganizationId=2,DisplayName="B"});
        db.Locations.Add(new Location{LocationId=10,LocationName="Shared",
            OrganizationId=null,IsActive=true,ApprovalStatus="Approved"});
        db.TeamLocationNoteHistories.AddRange(
            new TeamLocationNoteHistory{TeamLocationNoteHistoryId=1,
                TeamLocationNoteId=1,TeamId=7,LocationId=10,
                ChangedByUserId=11,NewNote="A"},
            new TeamLocationNoteHistory{TeamLocationNoteHistoryId=2,
                TeamLocationNoteId=2,TeamId=9,LocationId=10,
                ChangedByUserId=13,NewNote="B"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var admin=Actor() with { Roles=new[]{"admin"} };
        var result=await new V170LocationRepository(db)
            .GetMaintenanceAsync(admin,10,null,CancellationToken.None);
        Assert.Single(result.Notes);
        Assert.Equal(7,result.Notes[0].TeamId);
    }
}
