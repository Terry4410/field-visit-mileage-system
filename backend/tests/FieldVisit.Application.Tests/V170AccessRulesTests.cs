using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170AccessRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 13);

    [Fact]
    public void EffectiveRange_Includes_Boundaries()
    {
        Assert.True(V170AccessRules.IsEffective(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 13),
            Today));
    }

    [Fact]
    public void Legacy_User_Without_Hr_Record_Remains_Eligible()
    {
        Assert.True(V170AccessRules.IsEmploymentEligible(null));
    }

    [Theory]
    [InlineData(EmploymentStatuses.Active, true)]
    [InlineData(EmploymentStatuses.Leave, false)]
    [InlineData(EmploymentStatuses.Terminated, false)]
    [InlineData(EmploymentStatuses.PreHire, false)]
    public void Employment_Status_Drives_Internal_Eligibility(
        string status,
        bool expected)
    {
        Assert.Equal(
            expected,
            V170AccessRules.IsEmploymentEligible(status));
    }

    [Fact]
    public void External_User_Is_Blocked_After_Authorization_End()
    {
        Assert.False(V170AccessRules.IsSystemAccessAllowed(
            true,
            null,
            UserTypes.External,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 7, 31),
            Today));
    }

    [Fact]
    public void Supervisor_Is_Always_ReadOnly_For_Business_Mutations()
    {
        Assert.False(
            V170AccessRules.CanMutateBusinessData("supervisor"));

        Assert.True(
            V170AccessRules.RequiresExplicitExportCapability("supervisor"));
    }

    [Fact]
    public void Admin_Disable_Overrides_Other_Eligibility()
    {
        Assert.False(V170AccessRules.IsSystemAccessAllowed(
            false,
            EmploymentStatuses.Active,
            UserTypes.Internal,
            null,
            null,
            Today));
    }
}
