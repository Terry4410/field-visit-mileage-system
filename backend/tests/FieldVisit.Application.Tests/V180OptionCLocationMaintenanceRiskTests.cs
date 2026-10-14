using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCLocationMaintenanceRiskTests
{
    [Theory]
    [InlineData("Taipei", "Taipei", "Old Rd", "Old Rd", "A", "A", false)]
    [InlineData("Taipei", "New Taipei", "Old Rd", "Old Rd", "A", "A", true)]
    [InlineData("Taipei", "Taipei", "Old Rd", "New Rd", "A", "A", true)]
    [InlineData("Taipei", "Taipei", "Old Rd", "Old Rd", "A", "B", true)]
    [InlineData("Taipei", "Taipei", "Old Rd", "Old Rd", null, "Plus", true)]
    public void Published_address_and_identity_fields_are_classified(
        string city, string proposedCity, string address, string proposedAddress,
        string? taxId, string? proposedTaxId, bool expected)
    {
        Assert.Equal(expected, V180OptionCLocationMaintenanceRisk.HasHighRiskChange(
            city, "District", address, "Plus", taxId,
            proposedCity, "District", proposedAddress, "Plus", proposedTaxId));
    }

    [Fact]
    public void Published_high_risk_change_is_explicitly_rejected_but_draft_is_not()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationMaintenanceRisk.RequireApprovalForPublishedHighRiskChange(true, true));
        V180OptionCLocationMaintenanceRisk.RequireApprovalForPublishedHighRiskChange(false, true);
        V180OptionCLocationMaintenanceRisk.RequireApprovalForPublishedHighRiskChange(true, false);
    }

    [Fact]
    public void Source_contract_keeps_low_risk_master_note_out_of_high_risk_classifier()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root,
            "backend/src/FieldVisit.Infrastructure/V170LocationRepository.cs"));
        var gate = source.IndexOf("RequireApprovalForPublishedHighRiskChange", StringComparison.Ordinal);
        var writes = source.IndexOf("row.MasterNote=nextMasterNote", StringComparison.Ordinal);
        Assert.True(gate >= 0 && writes > gate);
        Assert.Contains("nextCity,nextDistrict,nextAddress,nextPlus,nextTax", source);
    }

    [Fact]
    public void Other_confirmed_published_write_paths_keep_explicit_guards()
    {
        var root = FindRepoRoot();
        var locationRepo = File.ReadAllText(Path.Combine(root,
            "backend/src/FieldVisit.Infrastructure/V170LocationRepository.cs"));
        var background = File.ReadAllText(Path.Combine(root,
            "backend/src/FieldVisit.Infrastructure/BackgroundJobService.cs"));
        var imports = File.ReadAllText(Path.Combine(root,
            "backend/src/FieldVisit.Infrastructure/WorkbookImportService.cs"));
        Assert.Contains("RequireApprovalForPublishedHighRiskChange(\n                published,hasHighRiskChange:true)", locationRepo);
        Assert.Contains("PUBLISHED_LOCATION_REQUIRES_OPTIONC_APPROVAL", background);
        Assert.Contains("row.ApprovalStatus == \"Approved\" || row.IsActive", imports);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend/FieldVisitSystem.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
