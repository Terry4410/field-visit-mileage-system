using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170ProjectDateRulesTests
{
    private static Project Build() => new()
    {
        ProjectId = 1,
        ProjectCode = "UAT-PROJECT-DATE",
        ProjectName = "專案期間測試",
        StartDate = new DateOnly(2026, 8, 10),
        EndDate = new DateOnly(2026, 8, 31),
        IsActive = true
    };

    [Theory]
    [InlineData(2026, 8, 9, false)]
    [InlineData(2026, 8, 10, true)]
    [InlineData(2026, 8, 31, true)]
    [InlineData(2026, 9, 1, false)]
    public void EffectivePeriod_IsInclusive(
        int year,int month,int day,bool expected)
    {
        Assert.Equal(
            expected,
            V170ProjectDateRules.IsAvailableOn(
                Build(),
                new DateOnly(year,month,day)));
    }

    [Fact]
    public void InactiveProject_IsUnavailable()
    {
        var project=Build();
        project.IsActive=false;

        Assert.False(
            V170ProjectDateRules.IsAvailableOn(
                project,
                new DateOnly(2026,8,20)));
    }

    [Fact]
    public void BeforeStart_ThrowsClearError()
    {
        var ex=Assert.Throws<InvalidOperationException>(
            ()=>V170ProjectDateRules.EnsureAvailableOn(
                Build(),
                new DateOnly(2026,8,9)));

        Assert.Contains("早於專案",ex.Message);
        Assert.Contains("2026-08-10",ex.Message);
    }

    [Fact]
    public void AfterEnd_ThrowsClearError()
    {
        var ex=Assert.Throws<InvalidOperationException>(
            ()=>V170ProjectDateRules.EnsureAvailableOn(
                Build(),
                new DateOnly(2026,9,1)));

        Assert.Contains("晚於專案",ex.Message);
        Assert.Contains("2026-08-31",ex.Message);
    }
}
