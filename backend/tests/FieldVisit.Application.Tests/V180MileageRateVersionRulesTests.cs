using FieldVisit.Application;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application.Tests;

public sealed class V180MileageRateVersionRulesTests
{
    [Theory]
    [InlineData("Motorcycle","Motorcycle")]
    [InlineData("MOTORCYCLE","Motorcycle")]
    [InlineData("Car","Car")]
    [InlineData("CAR","Car")]
    public void Vehicle_type_is_canonical(string input,string expected)
        => Assert.Equal(expected,V180MileageRateVersionRules.NormalizeVehicleType(input));

    [Fact]
    public void End_date_cannot_precede_start_date()
        => Assert.Throws<InvalidOperationException>(()=>V180MileageRateVersionRules.Validate("Test",3m,new DateOnly(2026,11,1),new DateOnly(2026,10,31)));

    [Fact]
    public void Same_vehicle_period_cannot_overlap()
    {
        var rows=new[]{new MileageRateRule{MileageRateRuleId=1,RuleName="Existing",VehicleType="Motorcycle",RatePerKm=3m,EffectiveFrom=new DateOnly(2026,1,1),EffectiveTo=new DateOnly(2026,10,31),IsActive=true}};
        Assert.Throws<InvalidOperationException>(()=>V180MileageRateVersionRules.EnsureNoOverlap(rows,null,new DateOnly(2026,10,31),new DateOnly(2026,12,31)));
    }

    [Fact]
    public void Adjacent_period_is_allowed()
    {
        var rows=new[]{new MileageRateRule{MileageRateRuleId=1,RuleName="Existing",VehicleType="Motorcycle",RatePerKm=3m,EffectiveFrom=new DateOnly(2026,1,1),EffectiveTo=new DateOnly(2026,10,31),IsActive=true}};
        V180MileageRateVersionRules.EnsureNoOverlap(rows,null,new DateOnly(2026,11,1),null);
    }
}
