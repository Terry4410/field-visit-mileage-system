using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCLocationMaintenanceRiskTests
{
    [Theory]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "db-fieldvisit-pathb-uat", true, false, true)]
    [InlineData("PathBUatFormalLocations", "tcp:sql-fieldvisit-jpe-pathb-uat.database.windows.net,1433", "db-fieldvisit-pathb-uat", true, false, true)]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-uat.database.windows.net", "db-fieldvisit-pathb-uat", true, false, false)]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "master", true, false, false)]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-pathb-uat.database.windows.net.evil.example", "db-fieldvisit-pathb-uat", true, false, false)]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-pathb-uat.database.windows.net,1444", "db-fieldvisit-pathb-uat", true, false, false)]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "db-fieldvisit-pathb-uat", false, false, false)]
    [InlineData("PathBUatFormalLocations", "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "db-fieldvisit-pathb-uat", true, true, false)]
    [InlineData("IsolatedSqlTest", "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "db-fieldvisit-pathb-uat", true, false, false)]
    [InlineData(null, "sql-fieldvisit-jpe-pathb-uat.database.windows.net", "db-fieldvisit-pathb-uat", true, false, false)]
    public void PathB_formal_location_admission_matches_exact_tls_target(
        string? mode, string host, string database, bool encrypt, bool trust, bool expected)
    {
        var cs = $"Server={host};Initial Catalog={database};Encrypt={encrypt};TrustServerCertificate={trust}";
        Assert.Equal(expected, V180OptionCExecutionPolicy.MatchesPathBUatTarget(mode, host, database, cs));
    }

    [Fact]
    public void PathB_formal_location_admission_denies_connected_config_mismatch()
    {
        const string host = "sql-fieldvisit-jpe-pathb-uat.database.windows.net";
        const string db = "db-fieldvisit-pathb-uat";
        Assert.False(V180OptionCExecutionPolicy.MatchesPathBUatTarget(
            V180OptionCExecutionPolicy.PathBUatFormalLocationsMode, host, db,
            $"Server={host};Database=other;Encrypt=True;TrustServerCertificate=False"));
        Assert.False(V180OptionCExecutionPolicy.MatchesPathBUatTarget(
            V180OptionCExecutionPolicy.PathBUatFormalLocationsMode, host, db,
            $"Server=localhost;Database={db};Encrypt=True;TrustServerCertificate=False"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void OptionC_manual_transactions_do_not_change_normal_retry_strategy(string enabled)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?> {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=OptionCIsolated_Composition;Integrated Security=true;TrustServerCertificate=true",
                ["OptionC:Candidate:Enabled"] = enabled
            }).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var normal = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidate = first.ServiceProvider.GetRequiredKeyedService<AppDbContext>("OptionC");
        Assert.NotSame(normal,candidate);
        Assert.True(normal.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.False(candidate.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.Equal(normal.Database.GetConnectionString(),candidate.Database.GetConnectionString());
        Assert.Same(candidate,first.ServiceProvider.GetRequiredKeyedService<AppDbContext>("OptionC"));
        Assert.NotSame(candidate,second.ServiceProvider.GetRequiredKeyedService<AppDbContext>("OptionC"));
    }

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
    public void Published_note_only_change_requires_authorized_candidate_direct_save()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationMaintenanceRisk.RequireAuthorizedCandidateForPublishedLowRiskChange(
                isPublished: true, hasLowRiskChange: true));
        Assert.Equal("LOCATION_LOW_RISK_CHANGE_REQUIRES_AUTHORIZED_DIRECT_SAVE", ex.Message);
        V180OptionCLocationMaintenanceRisk.RequireAuthorizedCandidateForPublishedLowRiskChange(
            isPublished: false, hasLowRiskChange: true);
        V180OptionCLocationMaintenanceRisk.RequireAuthorizedCandidateForPublishedLowRiskChange(
            isPublished: true, hasLowRiskChange: false);
    }

    [Fact]
    public void Legacy_maintenance_gates_note_only_change_before_any_published_write()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "backend/src/FieldVisit.Infrastructure/V170LocationRepository.cs"));
        var lowGate = source.IndexOf("RequireAuthorizedCandidateForPublishedLowRiskChange(", StringComparison.Ordinal);
        var masterWrite = source.IndexOf("row.MasterNote=nextMasterNote", StringComparison.Ordinal);
        Assert.True(lowGate >= 0 && masterWrite > lowGate);
        Assert.Contains("row.MasterNote, nextMasterNote", source);
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

    [Theory]
    [InlineData("04595252")]
    [InlineData("10458570")]
    [InlineData("04595257")]
    [InlineData("10458574")]
    public void Blank_tax_id_accepts_current_Taiwan_checksum(string taxId)
        => Assert.Equal(taxId, V180OptionCLocationMaintenanceRisk.RequireFirstTaiwanTaxId(null, taxId));

    [Theory]
    [InlineData("00000000")]
    [InlineData("04595251")]
    [InlineData("1234567")]
    [InlineData("１２３４５６７８")]
    [InlineData("")]
    public void Blank_tax_id_rejects_invalid_format(string taxId)
        => Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationMaintenanceRisk.RequireFirstTaiwanTaxId(null, taxId));

    [Fact]
    public void Existing_tax_id_cannot_be_overwritten_or_cleared_by_first_fill()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationMaintenanceRisk.RequireFirstTaiwanTaxId("04595252", "10458570"));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationMaintenanceRisk.RequireFirstTaiwanTaxId("04595252", ""));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend/FieldVisitSystem.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
