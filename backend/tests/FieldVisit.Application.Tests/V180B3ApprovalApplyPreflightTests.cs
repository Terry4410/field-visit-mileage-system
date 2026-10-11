using System.Text.Json;
using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180B3ApprovalApplyPreflightTests
{
    private static readonly V180B3LocationFields Before =
        new("Old name", "Taipei", "Xinyi", "Old address", null, null, null);
    private static readonly V180B3LocationFields Proposed =
        Before with { LocationName = "New name" };

    private static V180B3ChangeRequest Pending() => new()
    {
        RequestPublicId = Guid.NewGuid(),
        OrganizationId = 1,
        TeamId = 7,
        EntityKind = "Location",
        EntityId = "9",
        OperationCode = "UpdatePublishedLocation",
        RiskCode = "High",
        Status = "Pending",
        ExpectedEntityRowVersion = new byte[8],
        BeforeJson = JsonSerializer.Serialize(Before),
        ProposedJson = JsonSerializer.Serialize(Proposed)
    };

    private static V180B3LocationFields Validate(
        V180B3ChangeRequest row, byte[]? version = null,
        V180B3LocationFields? current = null) =>
        V180B3ApprovalApplyPreflight.RequireValidatedProposal(
            row, 1, row.RequestPublicId, version ?? new byte[8],
            current ?? Before);

    [Fact]
    public void Valid_high_risk_pending_proposal_passes_pure_preflight()
    {
        Assert.Equal(Proposed, Validate(Pending()));
    }

    [Fact]
    public void Source_rowversion_drift_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Validate(Pending(), new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void Published_source_field_drift_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Validate(Pending(), current: Before with { Address = "Changed" }));
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Applied")]
    public void Nonpending_requests_are_never_applyable(string status)
    {
        var row = Pending();
        row.Status = status;
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }

    [Fact]
    public void Malformed_or_oversized_proposed_json_fails_closed()
    {
        var row = Pending();
        row.ProposedJson = "{invalid";
        Assert.Throws<InvalidOperationException>(() => Validate(row));
        row.ProposedJson = new string('x', 10001);
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }

    [Fact]
    public void Tampered_before_snapshot_fails_closed()
    {
        var row = Pending();
        row.BeforeJson = JsonSerializer.Serialize(
            Before with { LocationName = "Forged before" });
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }

    [Fact]
    public void Source_rowversion_missing_or_malformed_fails_closed()
    {
        var row = Pending();
        row.ExpectedEntityRowVersion = new byte[7];
        Assert.Throws<UnauthorizedAccessException>(() => Validate(row));
        row = Pending();
        Assert.Throws<InvalidOperationException>(() =>
            Validate(row, new byte[7]));
    }

    [Fact]
    public void Unsupported_operation_and_foreign_org_fail_closed()
    {
        var row = Pending();
        row.OperationCode = "UpdateRole";
        Assert.Throws<UnauthorizedAccessException>(() => Validate(row));
        row = Pending();
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequireValidatedProposal(
                row, 2, row.RequestPublicId, new byte[8], Before));
    }

    [Theory]
    [InlineData("{\\"LocationName\\":\\"New name\\",\\"City\\":\\"Taipei\\",\\"District\\":\\"Xinyi\\",\\"Address\\":\\"Old address\\",\\"PlusCode\\":null,\\"TaxId\\":null,\\"MasterNote\\":null,\\"IsActive\\":true}")]
    [InlineData("{\\"LocationName\\":\\"New name\\",\\"City\\":\\"Taipei\\",\\"District\\":\\"Xinyi\\",\\"Address\\":\\"Old address\\",\\"PlusCode\\":null,\\"TaxId\\":null}")]
    [InlineData("{\\"LocationName\\":\\"New name\\",\\"LocationName\\":\\"Injected\\",\\"City\\":\\"Taipei\\",\\"District\\":\\"Xinyi\\",\\"Address\\":\\"Old address\\",\\"PlusCode\\":null,\\"TaxId\\":null,\\"MasterNote\\":null}")]
    [InlineData("{\\"LocationName\\":7,\\"City\\":\\"Taipei\\",\\"District\\":\\"Xinyi\\",\\"Address\\":\\"Old address\\",\\"PlusCode\\":null,\\"TaxId\\":null,\\"MasterNote\\":null}")]
    public void Extra_missing_duplicate_or_nonstring_fields_are_rejected(string json)
    {
        var row = Pending();
        row.ProposedJson = json;
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }

    [Fact]
    public void Invalid_or_no_change_proposal_fails_closed()
    {
        var row = Pending();
        row.ProposedJson = JsonSerializer.Serialize(Before);
        Assert.Throws<InvalidOperationException>(() => Validate(row));
        row.ProposedJson = JsonSerializer.Serialize(
            Proposed with { LocationName = "" });
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }
}
