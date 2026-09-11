using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180DbWork2MileageRuntimeTests
{
    [Theory]
    [InlineData("Motorcycle", "MOTORCYCLE")]
    [InlineData("MOTORCYCLE", "MOTORCYCLE")]
    [InlineData("Car", "CAR")]
    [InlineData("CAR", "CAR")]
    [InlineData(null, "MOTORCYCLE")]
    [InlineData("", "MOTORCYCLE")]
    public void VehicleType_NormalizesLegacyAndCanonicalValues(string? input, string expected)
    {
        Assert.Equal(expected, DbWork2MileageRateRules.NormalizeVehicleType(input));
    }

    [Fact]
    public void VehicleType_RejectsUnknownValue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DbWork2MileageRateRules.NormalizeVehicleType("Scooter"));
    }

    [Fact]
    public void RowVersion_AcceptsEightByteBase64()
    {
        Assert.Equal(8, DbWork2MileageRateRules.RequireRowVersion("AAAAAAAAAAE=").Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQ==")]
    public void RowVersion_RejectsMissingMalformedOrWrongLength(string? value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DbWork2MileageRateRules.RequireRowVersion(value));
    }

    [Fact]
    public void RowVersion_RejectsStaleVersionWithConflictCode()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DbWork2MileageRateRules.EnsureRowVersion(
                Convert.FromBase64String("AAAAAAAAAAI="),
                "AAAAAAAAAAE="));
        Assert.Contains("ROWVERSION_CONFLICT", ex.Message);
    }

    [Fact]
    public void EffectiveTo_RemainsDatabaseAuthority()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DbWork2MileageRateRules.RejectCallerEffectiveTo(new DateOnly(2026, 12, 31)));
        Assert.Contains("MILEAGE_RATE_EFFECTIVE_TO_DB_AUTHORITY", ex.Message);
    }

    [Fact]
    public void SameDateCorrection_PreservesBaseFinalizedSnapshotRate()
    {
        var visitDate = new DateOnly(2026, 9, 1);
        Assert.True(V160CorrectionFinancialRules.ShouldPreserveSnapshotRate(visitDate, visitDate));
        Assert.Equal(2.75m, V160CorrectionFinancialRules.RequireSnapshotRate(2.75m));
        Assert.Equal(27.50m, V160CorrectionFinancialRules.CalculateSubsidy(10m, 2.75m));
    }
}
