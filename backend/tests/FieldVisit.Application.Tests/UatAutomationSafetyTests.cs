using FieldVisit.Api.Controllers;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class UatAutomationSafetyTests
{
    [Theory]
    [InlineData("Demo", "1.7.2-uat-candidate", true)]
    [InlineData("demo", "2.0.0-UAT-CANDIDATE", true)]
    [InlineData("Entra", "1.7.2-uat-candidate", false)]
    [InlineData("Demo", "1.7.2", false)]
    [InlineData("Demo", "", false)]
    public void Environment_gate_requires_demo_and_uat_candidate(
        string authMode,
        string version,
        bool expected)
    {
        Assert.Equal(
            expected,
            UatAutomationSafety.IsEligibleEnvironment(authMode, version));
    }

    [Theory]
    [InlineData("UAT-AUTO-123", "UAT-AUTO-123", true)]
    [InlineData("UAT-AUTO-123", "UAT-AUTO-456", false)]
    [InlineData("NORMAL-123", "NORMAL-123", false)]
    [InlineData("UAT-AUTO-123", "NORMAL-123", false)]
    public void Purpose_gate_requires_exact_uat_auto_marker(
        string actual,
        string expected,
        bool allowed)
    {
        Assert.Equal(
            allowed,
            UatAutomationSafety.IsExactAutomationPurpose(actual, expected));
    }

    [Fact]
    public void Automation_key_must_match_exactly()
    {
        Assert.True(UatAutomationSafety.KeyMatches("secret-123", "secret-123"));
        Assert.False(UatAutomationSafety.KeyMatches("secret-123", "secret-124"));
        Assert.False(UatAutomationSafety.KeyMatches("", "secret-123"));
    }

    [Fact]
    public void Dedicated_mileage_job_accepts_only_single_selected_trip()
    {
        const string payload = "{\"mode\":\"Selected\",\"startDate\":null,\"endDate\":null,\"selectedTripIds\":[321]}";

        Assert.True(
            UatAutomationSafety.IsDedicatedMileageJob(
                "Mileage",
                "Selected",
                payload,
                321));

        Assert.False(
            UatAutomationSafety.IsDedicatedMileageJob(
                "Mileage",
                "Selected",
                payload,
                999));
    }

    [Theory]
    [InlineData("Mileage", "AllPending", "{\"selectedTripIds\":[321]}")]
    [InlineData("Geocoding", "Selected", "{\"selectedTripIds\":[321]}")]
    [InlineData("Mileage", "Selected", "{\"selectedTripIds\":[321,322]}")]
    [InlineData("Mileage", "Selected", "not-json")]
    public void Dedicated_mileage_job_rejects_broad_or_invalid_jobs(
        string jobType,
        string mode,
        string payload)
    {
        Assert.False(
            UatAutomationSafety.IsDedicatedMileageJob(
                jobType,
                mode,
                payload,
                321));
    }
}
