using FieldVisit.Api.Controllers;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class UatAutomationCleanupStage007Tests
{
    [Fact]
    public void Cleanup_gate_requires_exact_uat_trip_purpose()
    {
        Assert.True(UatAutomationSafety.IsExactAutomationPurpose("UAT-AUTO-P2B-001", "UAT-AUTO-P2B-001"));
        Assert.False(UatAutomationSafety.IsExactAutomationPurpose("UAT-AUTO-P2B-001", "UAT-AUTO-P2B-002"));
        Assert.False(UatAutomationSafety.IsExactAutomationPurpose("NORMAL-P2B-001", "NORMAL-P2B-001"));
    }

    [Fact]
    public void Cleanup_gate_rejects_shared_background_job()
    {
        const string exact = "{\"mode\":\"Selected\",\"selectedTripIds\":[55]}";
        const string shared = "{\"mode\":\"Selected\",\"selectedTripIds\":[55,56]}";

        Assert.True(UatAutomationSafety.IsDedicatedMileageJob("Mileage", "Selected", exact, 55));
        Assert.False(UatAutomationSafety.IsDedicatedMileageJob("Mileage", "Selected", shared, 55));
        Assert.False(UatAutomationSafety.IsDedicatedMileageJob("Mileage", "AllPending", exact, 55));
    }
}
