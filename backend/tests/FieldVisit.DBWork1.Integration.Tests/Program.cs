using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("DB_WORK1_SQL_CONNECTION")
    ?? throw new InvalidOperationException("DB_WORK1_SQL_CONNECTION is required.");

AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
var admin = new CurrentUserDto(99,"ADMIN","Admin",null,1,null,null,["admin"]);
MasterService Master(AppDbContext db,IVisitTypeMembershipCoordinator? coordinator=null) => new(
    new FixedCurrentUser(admin),new MasterRepository(db),new MileageRepository(db),new NoopGeocoder(),
    new WorkflowRepository(db),coordinator??new VisitTypeMembershipCoordinator(db),db);

// -----------------------------------------------------------------------------
// Real EF + production trigger proof. Use an isolated Organization series.
// -----------------------------------------------------------------------------
await using (var db = NewDb())
{
    var first = new MileageRateRule
    {
        OrganizationId = 501, RuleName = "DBW1 EF first", VehicleType = "MOTORCYCLE",
        RatePerKm = 2.50m, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = null,
        IsActive = true, CreatedAt = DateTime.UtcNow
    };
    db.MileageRateRules.Add(first);
    await db.SaveChangesAsync();
    if (first.MileageRateRuleId <= 0 || first.RowVersion is not { Length: 8 })
        throw new InvalidOperationException("EF INSERT did not retrieve identity/RowVersion with trigger enabled.");
    Console.WriteLine("DBW1_EF_TRIGGER_INSERT=PASS");

    var firstVersion = first.RowVersion.ToArray();
    var second = new MileageRateRule
    {
        OrganizationId = 501, RuleName = "DBW1 EF second", VehicleType = "MOTORCYCLE",
        RatePerKm = 2.75m, EffectiveFrom = new DateOnly(2026, 7, 1), EffectiveTo = null,
        IsActive = true, CreatedAt = DateTime.UtcNow
    };
    db.MileageRateRules.Add(second);
    await db.SaveChangesAsync();
    if (second.RowVersion is not { Length: 8 })
        throw new InvalidOperationException("EF second INSERT did not retrieve RowVersion.");

    await db.Entry(first).ReloadAsync();
    if (first.EffectiveTo != new DateOnly(2026, 6, 30))
        throw new InvalidOperationException($"DB EffectiveTo derivation after INSERT is wrong: {first.EffectiveTo}.");

    var secondVersion = second.RowVersion.ToArray();
    second.EffectiveFrom = new DateOnly(2026, 9, 1);
    second.EffectiveTo = null;
    second.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    if (second.RowVersion is not { Length: 8 } || second.RowVersion.SequenceEqual(secondVersion))
        throw new InvalidOperationException("EF UPDATE did not retrieve a fresh RowVersion with trigger enabled.");
    Console.WriteLine("DBW1_EF_TRIGGER_UPDATE=PASS");
    Console.WriteLine("DBW1_EF_ROWVERSION=PASS");

    await db.Entry(first).ReloadAsync();
    await db.Entry(second).ReloadAsync();
    if (first.EffectiveTo != new DateOnly(2026, 8, 31) || second.EffectiveTo is not null)
        throw new InvalidOperationException($"DB-derived EffectiveTo lost authority after EF UPDATE: first={first.EffectiveTo}, second={second.EffectiveTo}.");
    if (first.RowVersion.SequenceEqual(firstVersion))
        throw new InvalidOperationException("Trigger-derived first-row EffectiveTo update did not advance RowVersion.");
    Console.WriteLine("DBW1_EF_DERIVED_EFFECTIVETO=PASS");
}

// -----------------------------------------------------------------------------
// D-B1 real SQL Project lifecycle through production service/repository.
// -----------------------------------------------------------------------------
await using (var db = NewDb())
{
    var service=Master(db);
    var created=await service.CreateProjectAsync(new(10,"SQL-P1","SQL Project",null,"List",new DateOnly(2026,1,1),null),default);
    if(!created.IsActive||string.IsNullOrWhiteSpace(created.RowVersion)) throw new InvalidOperationException("Project create lifecycle/RowVersion failed.");
    var updated=await service.UpdateProjectAsync(created.ProjectId,new(10,"SQL-P1","SQL Project Updated",null,"List",new DateOnly(2026,1,1),null,created.RowVersion),default);
    if(!updated.IsActive||updated.ProjectName!="SQL Project Updated") throw new InvalidOperationException("Project ordinary update changed lifecycle or failed.");
    var deactivated=await service.DeactivateProjectAsync(created.ProjectId,updated.RowVersion,default);
    if(deactivated.IsActive||deactivated.InactivatedAt is null||deactivated.InactivatedByUserId!=99) throw new InvalidOperationException("Project deactivate metadata failed.");
    var reactivated=await service.ReactivateProjectAsync(created.ProjectId,new(deactivated.RowVersion),default);
    if(!reactivated.IsActive||reactivated.InactivatedAt is not null||reactivated.InactivatedByUserId is not null) throw new InvalidOperationException("Project reactivate metadata failed.");
    var staleFailed=false;
    try { await service.DeactivateProjectAsync(created.ProjectId,created.RowVersion,default); }
    catch(InvalidOperationException ex) when(ex.Message.Contains("ROWVERSION_CONFLICT",StringComparison.OrdinalIgnoreCase)){staleFailed=true;}
    if(!staleFailed) throw new InvalidOperationException("Project stale RowVersion was not rejected on real SQL.");
    Console.WriteLine("DB1_PROJECT_SQL_INTEGRATION=PASS");
}

// -----------------------------------------------------------------------------
// D-B2 real SQL VisitType lifecycle/order through production coordinator.
// -----------------------------------------------------------------------------
int vtA,vtB;
await ResetVisitTypesAsync();
await using (var db = NewDb())
{
    var service=Master(db);
    var a=await service.CreateVisitTypeAsync(new("SQL-A","SQL A",null),default);
    var b=await service.CreateVisitTypeAsync(new("SQL-B","SQL B",null),default);
    vtA=a.VisitTypeId; vtB=b.VisitTypeId;
    if(a.SortOrder!=10||b.SortOrder!=20||!a.IsActive||!b.IsActive) throw new InvalidOperationException("VisitType MAX+10 create failed.");
    var reordered=await service.ReorderVisitTypesAsync(new([vtB,vtA]),default);
    if(!reordered.Select(x=>x.VisitTypeId).SequenceEqual([vtB,vtA])||!reordered.Select(x=>x.SortOrder).SequenceEqual([10,20])) throw new InvalidOperationException("VisitType complete reorder failed.");
    var currentA=reordered.Single(x=>x.VisitTypeId==vtA);
    var off=await service.DeactivateVisitTypeAsync(vtA,currentA.RowVersion,default);
    if(off.IsActive||off.InactivatedAt is null) throw new InvalidOperationException("VisitType deactivate failed.");
    var on=await service.ReactivateVisitTypeAsync(vtA,new(off.RowVersion),default);
    if(!on.IsActive||on.InactivatedAt is not null) throw new InvalidOperationException("VisitType reactivate failed.");
    Console.WriteLine("DB2_VISITTYPE_SQL_INTEGRATION=PASS");
}

// -----------------------------------------------------------------------------
// Four deterministic VisitType concurrency families. BlockingCoordinator only
// holds the production coordinator after it has acquired FieldVisit.VisitTypeOrder.
// The competing service uses the production coordinator unchanged.
// -----------------------------------------------------------------------------
await VisitTypeRaceAsync("REORDER_REORDER",async(a,b,ids,block)=>
{
    var first=a.ReorderVisitTypesAsync(new([ids.B,ids.A]),default);
    await block.WaitUntilEnteredAsync();
    var second=b.ReorderVisitTypesAsync(new([ids.A,ids.B]),default);
    await EnsureBlockedAsync(second,"reorder vs reorder");
    block.Release();
    await Task.WhenAll(first,second);
});

await VisitTypeRaceAsync("REORDER_CREATE",async(a,b,ids,block)=>
{
    var first=a.ReorderVisitTypesAsync(new([ids.B,ids.A]),default);
    await block.WaitUntilEnteredAsync();
    var second=b.CreateVisitTypeAsync(new("R-C","Race Create",null),default);
    await EnsureBlockedAsync(second,"reorder vs create");
    block.Release();
    await Task.WhenAll(first,second);
});

await VisitTypeRaceAsync("REORDER_DEACTIVATE",async(a,b,ids,block)=>
{
    var staleToken=ids.ARowVersion;
    var first=a.ReorderVisitTypesAsync(new([ids.B,ids.A]),default);
    await block.WaitUntilEnteredAsync();
    var second=b.DeactivateVisitTypeAsync(ids.A,staleToken,default);
    await EnsureBlockedAsync(second,"reorder vs deactivate");
    block.Release();
    await first;
    var rejected=false;
    try { await second; }
    catch(InvalidOperationException ex) when(ex.Message.Contains("ROWVERSION_CONFLICT",StringComparison.OrdinalIgnoreCase)){rejected=true;}
    if(!rejected) throw new InvalidOperationException("reorder vs deactivate stale lifecycle writer did not roll back.");
});

await VisitTypeRaceAsync("REORDER_REACTIVATE",async(a,b,ids,block)=>
{
    if(ids.C is null||ids.CRowVersion is null) throw new InvalidOperationException("Race fixture missing inactive VisitType.");
    var first=a.ReorderVisitTypesAsync(new([ids.B,ids.A]),default);
    await block.WaitUntilEnteredAsync();
    var second=b.ReactivateVisitTypeAsync(ids.C.Value,new(ids.CRowVersion),default);
    await EnsureBlockedAsync(second,"reorder vs reactivate");
    block.Release();
    await Task.WhenAll(first,second);
},includeInactive:true);

Console.WriteLine("DB2_VISITTYPE_CONCURRENCY=4/4");

async Task ResetVisitTypesAsync()
{
    await using var db=NewDb();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.VisitTypes; DELETE FROM dbo.AuditLogs WHERE EntityType=N'VisitType';");
}

async Task VisitTypeRaceAsync(string name,Func<MasterService,MasterService,VisitTypeIds,BlockingCoordinator,Task> race,bool includeInactive=false)
{
    await ResetVisitTypesAsync();
    int aId,bId; string aVersion; int? cId=null; string? cVersion=null;
    await using(var seedDb=NewDb())
    {
        var seed=Master(seedDb);
        var a=await seed.CreateVisitTypeAsync(new($"{name}-A","A",null),default);
        var b=await seed.CreateVisitTypeAsync(new($"{name}-B","B",null),default);
        aId=a.VisitTypeId;bId=b.VisitTypeId;aVersion=a.RowVersion;
        if(includeInactive)
        {
            var c=await seed.CreateVisitTypeAsync(new($"{name}-C","C",null),default);
            var off=await seed.DeactivateVisitTypeAsync(c.VisitTypeId,c.RowVersion,default);
            cId=off.VisitTypeId;cVersion=off.RowVersion;
        }
    }

    await using var dbA=NewDb();
    await using var dbB=NewDb();
    var blocking=new BlockingCoordinator(new VisitTypeMembershipCoordinator(dbA));
    var serviceA=Master(dbA,blocking);
    var serviceB=Master(dbB,new VisitTypeMembershipCoordinator(dbB));
    await race(serviceA,serviceB,new VisitTypeIds(aId,bId,aVersion,cId,cVersion),blocking);

    await using var verify=NewDb();
    var active=await verify.VisitTypes.AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.SortOrder).ThenBy(x=>x.VisitTypeId).ToListAsync();
    if(active.Count<2||active.Select(x=>x.SortOrder).Distinct().Count()!=active.Count)
        throw new InvalidOperationException($"{name}: partial/duplicate active ordering detected.");
    Console.WriteLine($"DB2_{name}=PASS");
}

static async Task EnsureBlockedAsync(Task task,string name)
{
    await Task.Delay(250);
    if(task.IsCompleted) throw new InvalidOperationException($"{name}: competing writer did not wait for the global set lock.");
}

sealed record VisitTypeIds(int A,int B,string ARowVersion,int? C,string? CRowVersion);

sealed class FixedCurrentUser(CurrentUserDto user):ICurrentUserService
{
    public CurrentUserDto GetRequired()=>user;
}

sealed class NoopGeocoder:IGeocodingService
{
    public Task<GeocodingResult> ResolveAsync(string? address,string? plusCode,CancellationToken ct)=>Task.FromResult(new GeocodingResult(false,null,null,"NOOP","NOOP"));
}

sealed class BlockingCoordinator(IVisitTypeMembershipCoordinator inner):IVisitTypeMembershipCoordinator
{
    private readonly TaskCompletionSource _entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitUntilEnteredAsync()=>_entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    public void Release()=>_release.TrySetResult();
    public Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> operation,CancellationToken ct)=>inner.ExecuteAsync(async innerCt=>
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(innerCt);
        return await operation(innerCt);
    },ct);
}
