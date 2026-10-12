using System.IO.Compression;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Actual repository QueryTripsAsync -> ReportDocumentService.CreateExcelAsync
/// regression. EF InMemory for bounded historic read-source tests; separate
/// B3 SQL fixture covers SQL Server EF snapshot projection. Neither authorizes
/// B3 Approve/Apply nor proves every Production export/API is protected.
/// </summary>
public sealed class V180ApprovedSnapshotExportReadSourceTests
{
    private static CurrentUserDto Viewer(int org=1)=>new(
        910,"E910","Administrator",null,org,7,"Test Team",["admin"]);

    private static AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase("b3-snapshot-report-"+Guid.NewGuid().ToString("N"))
        .Options);

    private static async Task SeedAsync(AppDbContext db,bool snapshot=true)
    {
        db.Users.Add(new User {
            UserId=910,OrganizationId=1,TeamId=7,EmployeeNo="E910",
            DisplayName="Current Visitor",IsActive=true,
            CreatedAt=DateTime.UtcNow
        });
        db.Teams.Add(new Team {
            TeamId=7,OrganizationId=1,TeamCode="T7",
            TeamName="Current Team",IsActive=true
        });
        db.Locations.Add(new Location {
            LocationId=77,OrganizationId=1,TeamId=7,
            LocationCode="LIVE-NEW",LocationName="Mutated Live Location",
            Address="New Live Address",LocationType="Customer",
            ApprovalStatus="Approved",IsActive=true,
            CreatedAt=DateTime.UtcNow,CreatedByUserId=910
        });
        db.VisitTrips.Add(new VisitTrip {
            VisitTripId=1001,TripNo="CURRENT-LIVE-TRIP",
            UserId=910,OrganizationId=1,TeamId=7,
            VisitDate=new DateOnly(2026,10,10),
            Status=TripStatuses.Approved,
            CreatedByUserId=910,CreatedAt=DateTime.UtcNow,
            Stops=[new VisitTripStop {
                VisitTripStopId=501,VisitTripId=1001,StopSequence=1,
                LocationId=77,LocationNameSnapshot="Unapproved Live Stop",
                AddressSnapshot="Current working address",
                CreatedAt=DateTime.UtcNow
            }]
        });
        if(snapshot)
            db.VisitTripSnapshots.Add(Snapshot(9001,1,
                "Frozen Approved Location","Historic Approved Address",
                "Historic Approved Project",13.75m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static VisitTripSnapshot Snapshot(long id,int version,
        string name,string address,string project,decimal approvedDistance)=>
        new()
        {
            VisitTripSnapshotId=id,VisitTripId=1001,
            SnapshotVersion=version,
            SnapshotType="Approved",StatusSnapshot="Approved",
            TripNo="APPROVED-FROZEN-TRIP",UserId=910,
            EmployeeNoSnapshot="E910",DisplayNameSnapshot="Archived Visitor",
            OrganizationId=1,OrganizationNameSnapshot="Archived Org",
            TeamId=7,TeamNameSnapshot="Archived Team",
            VisitDate=new DateOnly(2026,10,9),
            ApprovedDistanceKmSnapshot=approvedDistance,
            ClaimedDistanceKmSnapshot=14.00m,
            SystemDistanceKmSnapshot=13.75m,
            RatePerKmSnapshot=2.00m,
            SubsidyAmountSnapshot=approvedDistance*2,
            CreatedAt=DateTime.UtcNow,
            Stops=[new VisitTripSnapshotStop
            {
                VisitTripSnapshotStopId=id+100,
                VisitTripSnapshotId=id,
                StopSequence=1,LocationId=77,
                LocationCodeSnapshot="ARCH-77",
                LocationNameSnapshot=name,AddressSnapshot=address,
                ProjectId=31,ProjectCodeSnapshot="HIST-P",
                ProjectNameSnapshot=project,
                VisitTypeId=20,VisitTypeCodeSnapshot="HIST-V",
                VisitTypeNameSnapshot="Historic Visit Type",
                CreatedAt=DateTime.UtcNow
            }]
        };

    private static async Task<PagedResult<TripQueryRowDto>> QueryAsync(
        AppDbContext db,CurrentUserDto? user=null,
        string? locationKeyword=null,bool exportAll=true)
    {
        var repo=new V160FinalRepository(db,new V170AccessControl(db));
        return await repo.QueryTripsAsync(
            user??Viewer(),
            new TripQueryRequest(
                StartDate:new DateOnly(2026,10,1),
                EndDate:new DateOnly(2026,10,31),
                LocationKeyword:locationKeyword,
                Status:TripStatuses.Approved),
            exportAll,CancellationToken.None);
    }

    [Fact]
    public async Task Approved_repository_query_retains_frozen_stop_and_financial_values()
    {
        await using var db=Db();
        await SeedAsync(db);
        var result=await QueryAsync(db);
        var item=Assert.Single(result.Items);
        Assert.True(item.IsSnapshot);
        Assert.Equal("APPROVED-FROZEN-TRIP",item.TripNo);
        Assert.Equal("Archived Visitor",item.VisitorName);
        Assert.Equal("Archived Team",item.TeamName);
        Assert.Equal("Frozen Approved Location",item.Route);
        Assert.Equal("Historic Approved Project",item.ProjectNames);
        Assert.Equal(13.75m,item.ApprovedDistanceKm);
        Assert.Equal(27.50m,item.SubsidyAmount);
        Assert.Equal(1,item.SnapshotVersion);
        var stop=Assert.Single(item.Stops);
        Assert.Equal("ARCH-77",stop.LocationCode);
        Assert.Equal("Frozen Approved Location",stop.LocationName);
        Assert.Equal("Historic Approved Address",stop.Address);
        Assert.Equal("Historic Approved Project",stop.ProjectName);
        Assert.DoesNotContain("Mutated Live Location",item.Route);
    }

    [Fact]
    public async Task Approved_repository_to_Excel_stop_sheet_ignores_live_location_changes()
    {
        await using var db=Db();
        await SeedAsync(db);
        var repo=new V160FinalRepository(db,new V170AccessControl(db));
        var request=new TripQueryRequest(
            StartDate:new DateOnly(2026,10,1),
            EndDate:new DateOnly(2026,10,31),
            Status:TripStatuses.Approved);
        var result=await repo.QueryTripsAsync(Viewer(),request,true,CancellationToken.None);
        var exported=await new ReportDocumentService(null!).CreateExcelAsync(
            result.Items,request,Viewer(),CancellationToken.None);
        Assert.EndsWith(".xlsx",exported.FileName);
        using var zip=new ZipArchive(new MemoryStream(exported.Content),
            ZipArchiveMode.Read);
        var stops=zip.GetEntry("xl/worksheets/sheet3.xml");
        Assert.NotNull(stops);
        using var sr=new StreamReader(stops!.Open(),Encoding.UTF8);
        var xml=await sr.ReadToEndAsync();
        Assert.Contains("Frozen Approved Location",xml);
        Assert.Contains("Historic Approved Address",xml);
        Assert.Contains("Historic Approved Project",xml);
        Assert.DoesNotContain("Mutated Live Location",xml);
        Assert.DoesNotContain("New Live Address",xml);
        Assert.DoesNotContain("Unapproved Live Stop",xml);
    }

    [Fact]
    public async Task Approved_repository_query_selects_latest_snapshot_not_live_or_older_version()
    {
        await using var db=Db();
        await SeedAsync(db);
        db.VisitTripSnapshots.Add(Snapshot(9002,2,
            "Latest Frozen Location","Latest Frozen Address",
            "Latest Frozen Project",18.25m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var item=Assert.Single((await QueryAsync(db)).Items);
        Assert.Equal(2,item.SnapshotVersion);
        Assert.Equal("Latest Frozen Location",item.Route);
        Assert.Equal("Latest Frozen Project",item.ProjectNames);
        Assert.Equal(18.25m,item.ApprovedDistanceKm);
        Assert.Equal("Latest Frozen Address",Assert.Single(item.Stops).Address);
    }

    [Fact]
    public async Task Approved_historical_location_search_cannot_use_new_live_name()
    {
        await using var db=Db();
        await SeedAsync(db);
        var frozen=await QueryAsync(db,locationKeyword:"Frozen Approved Location");
        Assert.Single(frozen.Items);
        var live=await QueryAsync(db,locationKeyword:"Mutated Live Location");
        Assert.Empty(live.Items);
    }

    [Fact]
    public async Task Approved_missing_snapshot_with_date_filter_returns_no_live_fallback()
    {
        await using var db=Db();
        await SeedAsync(db,snapshot:false);
        // Date-qualified Approved filter searches frozen VisitDate in a
        // Snapshot. With none it excludes the row; no live trip is returned.
        var result=await QueryAsync(db);
        Assert.Empty(result.Items);
        Assert.Equal(0,result.TotalCount);
    }

    [Fact]
    public async Task Approved_missing_snapshot_without_date_filter_throws_before_rendering()
    {
        await using var db=Db();
        await SeedAsync(db,snapshot:false);
        var repo=new V160FinalRepository(db,new V170AccessControl(db));
        // Without a date-qualified prefilter, the Approved trip reaches the
        // materialization path; absence of Snapshot is explicitly denied.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            repo.QueryTripsAsync(Viewer(),
                new TripQueryRequest(Status:TripStatuses.Approved),
                true,CancellationToken.None));
    }

    [Fact]
    public async Task Approved_query_respects_organization_scope_even_with_other_team_token()
    {
        await using var db=Db();
        await SeedAsync(db);
        var other=await QueryAsync(db,Viewer(org:2));
        Assert.Empty(other.Items);
        Assert.Equal(0,other.TotalCount);
    }
}
