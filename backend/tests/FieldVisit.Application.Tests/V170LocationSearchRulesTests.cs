using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationSearchRulesTests
{
    [Fact]
    public void Normalize_UsesDefaultPaging()
    {
        var result =
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    null,
                    null,
                    null,
                    null,
                    0,
                    0));

        Assert.Equal(1, result.Page);
        Assert.Equal(
            V170LocationSearchRules.DefaultPageSize,
            result.PageSize);
    }

    [Fact]
    public void Normalize_TrimsSearchFilters()
    {
        var result =
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    "  客戶A  ",
                    " 台北市 ",
                    " 內湖區 ",
                    null,
                    1,
                    20));

        Assert.Equal("客戶A", result.Query);
        Assert.Equal("台北市", result.City);
        Assert.Equal("內湖區", result.District);
    }

    [Fact]
    public void Normalize_ClampsPageSize()
    {
        var result =
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    null,
                    null,
                    null,
                    null,
                    1,
                    999));

        Assert.Equal(
            V170LocationSearchRules.MaxPageSize,
            result.PageSize);
    }

    [Fact]
    public void Normalize_RejectsInvalidProjectId()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    null,
                    null,
                    null,
                    0,
                    1,
                    20)));
    }

    [Fact]
    public void Normalize_RejectsOverlongQuery()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    new string('A', 201),
                    null,
                    null,
                    null,
                    1,
                    20)));
    }

    [Theory]
    [InlineData("visitor")]
    [InlineData("leader")]
    [InlineData("admin")]
    public void EnsurePickerRole_AllowsInternalPickerRoles(
        string role)
    {
        V170LocationSearchRules.EnsurePickerRole(
            new[] { role });
    }

    [Fact]
    public void EnsurePickerRole_RejectsSupervisorOnly()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            V170LocationSearchRules.EnsurePickerRole(
                new[] { "supervisor" }));
    }


    [Fact]
    public void Duplicate_rules_match_exact_and_small_text_variations_without_auto_deciding()
    {
        var source = new V170LocationDuplicateComparable(
            1, "台北 市政府", "台北市信義區市府路1號", "7Q2H+22", "12345678");
        var exact = new V170LocationDuplicateComparable(
            2, "台北市政府", "台北市信義區市府路1號", "7Q2H22", "12345678");
        var near = new V170LocationDuplicateComparable(
            3, "台北市政府A", "臺北市信義區市府路1號", null, null);
        var other = new V170LocationDuplicateComparable(
            4, "高雄服務中心", "高雄市前鎮區不同路99號", null, null);

        var exactReasons = V170LocationDuplicateRules.MatchReasons(source, exact);
        Assert.Contains("統一編號", exactReasons);
        Assert.Contains("名稱", exactReasons);
        Assert.Contains("地址", exactReasons);
        Assert.Contains("Plus Code", exactReasons);

        var nearReasons = V170LocationDuplicateRules.MatchReasons(source, near);
        Assert.Contains(nearReasons, x => x.StartsWith("名稱"));
        Assert.Contains(nearReasons, x => x.StartsWith("地址"));
        Assert.Empty(V170LocationDuplicateRules.MatchReasons(source, other));
    }

    [Fact]
    public void Duplicate_signature_normalizes_spacing_punctuation_and_tai_variant()
    {
        var a = new V170LocationDuplicateComparable(1, "台北 市政府", "臺北市-信義區", "7Q2H+22", "12-345");
        var b = new V170LocationDuplicateComparable(2, "臺北市政府", "台北市信義區", "7Q2H22", "12345");
        Assert.Equal(V170LocationDuplicateRules.Signature(a), V170LocationDuplicateRules.Signature(b));
    }
}
