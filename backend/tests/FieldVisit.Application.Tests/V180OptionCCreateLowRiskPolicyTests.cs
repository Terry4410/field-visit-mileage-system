using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCCreateLowRiskPolicyTests
{
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0, 1];

    [Theory]
    [InlineData("Name", "Address", 1, 1)]
    [InlineData("Name", "Address", 1, 3)]
    public void Create_always_classifies_as_high_risk(string name, string address, int org, int team)
    {
        Assert.Equal("High", V180OptionCCreateLowRiskPolicy.ClassifyCreate(name, address, org, team));
    }

    [Theory]
    [InlineData("", "Address", 1, 1)]
    [InlineData("Name", "", 1, 1)]
    [InlineData("Name", "Address", 0, 1)]
    [InlineData("Name", "Address", 1, 0)]
    public void Create_rejects_missing_identity(string name, string address, int org, int team)
        => Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.ClassifyCreate(name, address, org, team));

    [Fact]
    public void Published_note_only_is_low_but_address_or_tax_identity_is_high()
    {
        string Classify(string address, string tax) =>
            V180OptionCCreateLowRiskPolicy.ClassifyPublishedUpdate(
                "Taipei", "Xinyi", "Old address", null, "A",
                "Taipei", "Xinyi", address, null, tax,
                published: true, changedAnyField: true);
        Assert.Equal("Low", Classify("Old address", "A"));
        Assert.Equal("High", Classify("New address", "A"));
        Assert.Equal("High", Classify("Old address", "B"));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.ClassifyPublishedUpdate(
                "Taipei", "Xinyi", "Old address", null, "A",
                "Taipei", "Xinyi", "Old address", null, "A",
                published: true, changedAnyField: false));
    }

    [Fact]
    public void Create_must_use_pending_inactive_reservation_with_rowversion_and_no_before_image()
    {
        V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "High",
            true, false, "Pending", Version, false);
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "Low",
                true, false, "Pending", Version, false));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "High",
                true, true, "Pending", Version, false));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "High",
                true, false, "Approved", Version, false));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "High",
                true, false, "Pending", Version, true));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "High",
                false, false, "Pending", Version, false));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("CreateLocation", "High",
                true, false, "Pending", new byte[7], false));
    }

    [Theory]
    [InlineData("High")]
    [InlineData("Low")]
    public void Published_updates_require_active_approved_source_and_before_image(string risk)
    {
        V180OptionCCreateLowRiskPolicy.RequireSourceShape("UpdatePublishedLocation", risk,
            true, true, "Approved", Version, true);
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("UpdatePublishedLocation", risk,
                true, false, "Approved", Version, true));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("UpdatePublishedLocation", risk,
                true, true, "Pending", Version, true));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("UpdatePublishedLocation", risk,
                true, true, "Approved", Version, false));
    }

    [Fact]
    public void Self_review_and_missing_leader_review_fail_closed_for_both_risks()
    {
        V180OptionCCreateLowRiskPolicy.RequireIndependentLeaderDecision(1, 2, true);
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireIndependentLeaderDecision(1, 1, true));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireIndependentLeaderDecision(1, 2, false));
        V180OptionCCreateLowRiskPolicy.RequireAdminForHighRisk("Low", false);
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireAdminForHighRisk("High", false));
        V180OptionCCreateLowRiskPolicy.RequireAdminForHighRisk("High", true);
    }

    [Fact]
    public void Unsupported_operations_and_unknown_risks_are_denied()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireSourceShape("UpdateLocation", "Low",
                true, true, "Approved", Version, true));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCCreateLowRiskPolicy.RequireAdminForHighRisk("Unknown", true));
    }
}
