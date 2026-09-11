using System.Text.Json;
using FieldVisit.Api;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180DbWork1MileageRateAuthorityTests
{
    private static readonly byte[] Version = [1,2,3,4,5,6,7,8];

    [Fact]
    public async Task ET_API_01_Create_omitted_EffectiveTo_is_accepted_for_DB_derivation()
    {
        await using var db=MemoryDb();
        var request=JsonSerializer.Deserialize<CreateMileageRateRequest>("""{"RuleName":"Omitted","VehicleType":"MOTORCYCLE","RatePerKm":2.5,"EffectiveFrom":"2026-01-01"}""")!;
        Assert.Null(request.EffectiveTo);

        var result=await Master(db).CreateRateAsync(request,default);

        Assert.Null(result.EffectiveTo);
        Assert.Single(db.MileageRateRules);
        Assert.Single(db.AuditLogs.Where(x=>x.Action=="MileageRateCreate"));
    }

    [Fact]
    public async Task ET_API_02_Create_explicit_null_EffectiveTo_is_accepted_for_DB_derivation()
    {
        await using var db=MemoryDb();
        var request=JsonSerializer.Deserialize<CreateMileageRateRequest>("""{"RuleName":"Null","VehicleType":"MOTORCYCLE","RatePerKm":2.5,"EffectiveFrom":"2026-07-01","EffectiveTo":null}""")!;
        Assert.Null(request.EffectiveTo);

        var result=await Master(db).CreateRateAsync(request,default);

        Assert.Null(result.EffectiveTo);
        Assert.Single(db.MileageRateRules);
        Assert.Single(db.AuditLogs.Where(x=>x.Action=="MileageRateCreate"));
    }

    [Fact]
    public async Task ET_API_03_Create_non_null_EffectiveTo_rejects_422_before_mutation_or_audit()
    {
        await using var db=MemoryDb();
        var request=new CreateMileageRateRequest("Caller","MOTORCYCLE",2.5m,new DateOnly(2026,1,1),new DateOnly(2099,12,31));

        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db).CreateRateAsync(request,default));

        Assert.Contains("MILEAGE_RATE_EFFECTIVE_TO_DB_AUTHORITY",ex.Message);
        Assert.Equal(422,ApiExceptionStatus.From(ex));
        Assert.Empty(db.MileageRateRules);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task ET_API_04_Update_non_null_EffectiveTo_rejects_without_entity_RowVersion_or_audit_change()
    {
        await using var db=await RateDbAsync();
        var before=await db.MileageRateRules.AsNoTracking().SingleAsync();
        var beforeVersion=before.RowVersion.ToArray();
        var request=new UpdateMileageRateRequest("Changed","MOTORCYCLE",9.9m,new DateOnly(2026,1,1),new DateOnly(2099,12,31),true);

        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db).UpdateRateAsync(1,request,default));

        Assert.Contains("MILEAGE_RATE_EFFECTIVE_TO_DB_AUTHORITY",ex.Message);
        Assert.Equal(422,ApiExceptionStatus.From(ex));
        var after=await db.MileageRateRules.AsNoTracking().SingleAsync();
        Assert.Equal(before.RuleName,after.RuleName);
        Assert.Equal(before.RatePerKm,after.RatePerKm);
        Assert.Equal(before.EffectiveFrom,after.EffectiveFrom);
        Assert.Equal(before.EffectiveTo,after.EffectiveTo);
        Assert.Equal(before.IsActive,after.IsActive);
        Assert.True(after.RowVersion.SequenceEqual(beforeVersion));
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task ET_API_05_Update_non_null_EffectiveTo_equal_to_canonical_date_still_rejects()
    {
        await using var db=await RateDbAsync();
        var before=await db.MileageRateRules.AsNoTracking().SingleAsync();
        var beforeVersion=before.RowVersion.ToArray();
        var request=new UpdateMileageRateRequest(before.RuleName,before.VehicleType,before.RatePerKm,before.EffectiveFrom,new DateOnly(2026,6,30),true);

        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Master(db).UpdateRateAsync(1,request,default));

        Assert.Contains("MILEAGE_RATE_EFFECTIVE_TO_DB_AUTHORITY",ex.Message);
        Assert.Equal(422,ApiExceptionStatus.From(ex));
        var after=await db.MileageRateRules.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026,6,30),after.EffectiveTo);
        Assert.True(after.RowVersion.SequenceEqual(beforeVersion));
        Assert.Empty(db.AuditLogs);
    }

    private static AppDbContext MemoryDb()=>new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<AppDbContext> RateDbAsync()
    {
        var db=MemoryDb();
        db.MileageRateRules.Add(new MileageRateRule
        {
            MileageRateRuleId=1,
            OrganizationId=1,
            RuleName="Existing",
            VehicleType="MOTORCYCLE",
            RatePerKm=2.5m,
            EffectiveFrom=new DateOnly(2026,1,1),
            EffectiveTo=new DateOnly(2026,6,30),
            IsActive=true,
            CreatedAt=DateTime.UtcNow,
            RowVersion=Version.ToArray()
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static MasterService Master(AppDbContext db)=>new(
        new FixedCurrentUser(new CurrentUserDto(99,"ADMIN","Admin",null,1,null,null,["admin"])),
        new MasterRepository(db),
        new MileageRepository(db),
        new NoopGeocoder(),
        new WorkflowRepository(db),
        new NoopVisitTypeCoordinator(),
        db);

    private sealed class FixedCurrentUser(CurrentUserDto user):ICurrentUserService
    {
        public CurrentUserDto GetRequired()=>user;
    }

    private sealed class NoopGeocoder:IGeocodingService
    {
        public Task<GeocodingResult> ResolveAsync(string? address,string? plusCode,CancellationToken ct)
            =>Task.FromResult(new GeocodingResult(false,null,null,"NOOP","NOOP"));
    }

    private sealed class NoopVisitTypeCoordinator:IVisitTypeMembershipCoordinator
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken,Task<T>> operation,CancellationToken ct)=>operation(ct);
    }
}
