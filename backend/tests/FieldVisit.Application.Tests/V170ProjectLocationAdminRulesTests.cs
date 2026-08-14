using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170ProjectLocationAdminRulesTests
{
    [Fact]
    public void NormalizeSearch_UsesSearchFirstForBlankQuery()
    {
        var result =
            V170ProjectLocationAdminRules
                .NormalizeSearch(
                    " ",
                    1,
                    20);

        Assert.Null(result.Query);
    }

    [Fact]
    public void NormalizeSearch_TrimsQuery()
    {
        var result =
            V170ProjectLocationAdminRules
                .NormalizeSearch(
                    " 中寮 ",
                    1,
                    20);

        Assert.Equal(
            "中寮",
            result.Query);
    }

    [Fact]
    public void NormalizeSearch_ClampsPageSize()
    {
        var result =
            V170ProjectLocationAdminRules
                .NormalizeSearch(
                    "A",
                    1,
                    999);

        Assert.Equal(
            V170ProjectLocationAdminRules
                .MaxPageSize,
            result.PageSize);
    }

    [Fact]
    public void NormalizeSearch_RejectsOverlongQuery()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170ProjectLocationAdminRules
                .NormalizeSearch(
                    new string('A', 201),
                    1,
                    20));
    }

    [Fact]
    public void NormalizeLocationIds_RemovesDuplicates()
    {
        var ids =
            V170ProjectLocationAdminRules
                .NormalizeLocationIds(
                    new V170SaveProjectLocationsRequest(
                        new[] { 10, 20, 20, 30 }));

        Assert.Equal(
            new[] { 10, 20, 30 },
            ids);
    }

    [Fact]
    public void NormalizeLocationIds_RejectsInvalidId()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170ProjectLocationAdminRules
                .NormalizeLocationIds(
                    new V170SaveProjectLocationsRequest(
                        new[] { 0, 20 })));
    }
}
