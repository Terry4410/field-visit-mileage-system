using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationPromotionRulesTests
{
    private static Location Ready() => new()
    {
        LocationId = 56,
        LocationName = "南投縣政府",
        IsTemporary = true,
        IsActive = true,
        ApprovalStatus = "Approved",
        GeocodingStatus = "Completed",
        Latitude = 23.913m,
        Longitude = 120.684m
    };

    [Fact]
    public void ReadyTemporaryLocation_CanPromote()
        => Assert.True(V170LocationPromotionRules.CanPromote(Ready()));

    [Fact]
    public void FormalLocation_CannotPromoteAgain()
    {
        var row = Ready();
        row.IsTemporary = false;
        var ex = Assert.Throws<InvalidOperationException>(
            () => V170LocationPromotionRules.EnsureCanPromote(row));
        Assert.Contains("正式地點", ex.Message);
    }

    [Fact]
    public void PendingLocation_CannotPromote()
    {
        var row = Ready();
        row.ApprovalStatus = "Pending";
        var ex = Assert.Throws<InvalidOperationException>(
            () => V170LocationPromotionRules.EnsureCanPromote(row));
        Assert.Contains("完成核准", ex.Message);
    }

    [Fact]
    public void UngeocodedLocation_CannotPromote()
    {
        var row = Ready();
        row.GeocodingStatus = "Pending";
        row.Latitude = null;
        row.Longitude = null;
        var ex = Assert.Throws<InvalidOperationException>(
            () => V170LocationPromotionRules.EnsureCanPromote(row));
        Assert.Contains("完成地理解析", ex.Message);
    }
}
