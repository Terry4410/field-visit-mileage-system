using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180ManagedLocationDeletionRulesTests
{
    private static bool Can(params int[] n) =>
        V180ManagedLocationDeletionRules.CanPermanentlyDelete(
            n[0],n[1],n[2],n[3],n[4],n[5],n[6],n[7],n[8],n[9],n[10]);

    [Fact]
    public void Only_truly_unreferenced_location_may_be_permanently_deleted()
        => Assert.True(Can(new int[11]));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void Every_current_or_historical_reference_blocks_permanent_deletion(int i)
    {
        var counts=new int[11];
        counts[i]=1;
        Assert.False(Can(counts));
    }

    [Fact]
    public void Mixed_histories_and_invalid_negative_counts_fail_closed()
    {
        var counts=new int[11];counts[5]=4;counts[9]=3;
        Assert.False(Can(counts));
        counts=new int[11];counts[8]=-1;
        Assert.False(Can(counts));
    }
}
