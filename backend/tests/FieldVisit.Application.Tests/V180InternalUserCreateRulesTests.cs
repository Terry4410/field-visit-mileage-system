using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180InternalUserCreateRulesTests
{
    [Fact]
    public void Normalize_trims_and_normalizes_manual_people_create()
    {
        var r = V180InternalUserCreateRules.Normalize(new(
            " E100 ",
            " 王小明 ",
            " USER@EXAMPLE.COM ",
            new DateOnly(2026, 10, 8),
            null,
            " active ",
            new DateOnly(2026, 10, 8)));

        Assert.Equal("E100", r.EmployeeNo);
        Assert.Equal("王小明", r.DisplayName);
        Assert.Equal("user@example.com", r.Email);
        Assert.Equal(EmploymentStatuses.Active, r.InitialEmploymentStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    public void Invalid_status_is_rejected(string status)
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100",
                "Tester",
                null,
                null,
                null,
                status,
                new DateOnly(2026, 10, 8))));
    }

    [Fact]
    public void Terminated_requires_termination_date_and_date_order_is_checked()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100",
                "Tester",
                null,
                new DateOnly(2026, 10, 1),
                null,
                EmploymentStatuses.Terminated,
                new DateOnly(2026, 10, 8))));

        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100",
                "Tester",
                null,
                new DateOnly(2026, 10, 8),
                new DateOnly(2026, 10, 7),
                EmploymentStatuses.Terminated,
                new DateOnly(2026, 10, 8))));
    }

    [Theory]
    [InlineData(2025, 1, 15)]
    [InlineData(2035, 1, 15)]
    public void Ordinary_create_stores_active_from_hire_date_not_form_date(int year,int month,int day)
    {
        var hire = new DateOnly(year,month,day);
        var result = V180InternalUserCreateRules.Normalize(new(
            "E100","Tester","user@example.com",hire,null,
            EmploymentStatuses.Active,new DateOnly(2026,10,8)));
        Assert.Equal(hire,result.StatusEffectiveFrom);
        Assert.Equal(EmploymentStatuses.Active,result.InitialEmploymentStatus);
        Assert.Null(result.TerminationDate);
    }

    [Theory]
    [InlineData("Leave")]
    [InlineData("Terminated")]
    [InlineData("PreHire")]
    public void Ordinary_create_rejects_manually_selected_nonactive_states(string status)
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100","Tester","user@example.com",new DateOnly(2026,10,8),
                null,status,new DateOnly(2026,10,8))));
    }

    [Fact]
    public void Ordinary_create_requires_hire_date_and_email()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100","Tester","user@example.com",null,null,
                EmploymentStatuses.Active,new DateOnly(2026,10,8))));
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100","Tester",null,new DateOnly(2026,10,8),null,
                EmploymentStatuses.Active,new DateOnly(2026,10,8))));
    }

    [Fact]
    public void Ordinary_create_rejects_termination_date_even_with_active_status()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100","Tester","user@example.com",new DateOnly(2026,10,8),
                new DateOnly(2035,1,1),EmploymentStatuses.Active,new DateOnly(2026,10,8))));
    }

    [Fact]
    public void Invalid_email_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180InternalUserCreateRules.Normalize(new(
                "E100",
                "Tester",
                "not-an-email",
                new DateOnly(2026, 10, 8),
                null,
                EmploymentStatuses.Active,
                new DateOnly(2026, 10, 8))));
    }
}
