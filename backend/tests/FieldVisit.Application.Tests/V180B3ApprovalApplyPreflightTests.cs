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
        RequestedByUserId = 910,
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
    [InlineData("{\"LocationName\":\"New name\",\"City\":\"Taipei\",\"District\":\"Xinyi\",\"Address\":\"Old address\",\"PlusCode\":null,\"TaxId\":null,\"MasterNote\":null,\"IsActive\":true}")]
    [InlineData("{\"LocationName\":\"New name\",\"City\":\"Taipei\",\"District\":\"Xinyi\",\"Address\":\"Old address\",\"PlusCode\":null,\"TaxId\":null}")]
    [InlineData("{\"LocationName\":\"New name\",\"LocationName\":\"Injected\",\"City\":\"Taipei\",\"District\":\"Xinyi\",\"Address\":\"Old address\",\"PlusCode\":null,\"TaxId\":null,\"MasterNote\":null}")]
    [InlineData("{\"LocationName\":7,\"City\":\"Taipei\",\"District\":\"Xinyi\",\"Address\":\"Old address\",\"PlusCode\":null,\"TaxId\":null,\"MasterNote\":null}")]
    public void Extra_missing_duplicate_or_nonstring_fields_are_rejected(string json)
    {
        var row = Pending();
        row.ProposedJson = json;
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }

    [Fact]
    public void Whitespace_normalization_during_approval_fails_closed()
    {
        var row = Pending();
        row.ProposedJson = JsonSerializer.Serialize(
            Proposed with { LocationName = " New name " });
        Assert.Throws<InvalidOperationException>(() => Validate(row));
    }

    [Fact]
    public void Malformed_before_snapshot_json_fails_closed()
    {
        var row = Pending();
        row.BeforeJson = "{";
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
    [Fact]
    public void Independent_reviewer_with_fresh_version_passes_pure_decision_gate()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independently reviewed");
        Assert.Equal("Independently reviewed",
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 1, row.RequestPublicId, 20, review));
    }

    [Fact]
    public void Submitter_cannot_approve_own_request()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Self review");
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 1, row.RequestPublicId, row.RequestedByUserId, review));
    }

    [Fact]
    public void Stale_request_version_and_replayed_state_cannot_be_approved()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
            Guid.NewGuid(), "Independent review");
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 1, row.RequestPublicId, 20, review));
        row.Status = "Rejected";
        review = review with { RequestRowVersion =
            Convert.ToBase64String(row.RowVersion) };
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 1, row.RequestPublicId, 20, review));
    }

    [Fact]
    public void Missing_reviewer_and_cross_organization_fail_closed()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent review");
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 1, row.RequestPublicId, 0, review));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 2, row.RequestPublicId, 20, review));
    }

    [Fact]
    public void Empty_decision_key_and_blank_review_reason_fail_closed()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var version = Convert.ToBase64String(row.RowVersion);
        foreach (var review in new[]
        {
            new V180B3Review(version, Guid.Empty, "Reviewed"),
            new V180B3Review(version, Guid.NewGuid(), "   ")
        })
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                    row, 1, row.RequestPublicId, 20, review));
    }

    [Fact]
    public void Unknown_operation_cannot_pass_independent_approval_gate()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        row.OperationCode = "DeleteHistoricalTrip";
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Reviewed");
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequireIndependentApprovalDecision(
                row, 1, row.RequestPublicId, 20, review));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_team_ids_cannot_pass_approval_target_gate(int teamId)
    {
        var row = Pending();
        row.TeamId = teamId;
        Assert.Throws<UnauthorizedAccessException>(() => Validate(row));
    }

    [Fact]
    public void Empty_public_id_and_nonpositive_tenant_cannot_pass_review()
    {
        var row = Pending();
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ReviewTargetRules.RequireSupportedTarget(
                row, 1, Guid.Empty));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ReviewTargetRules.RequireSupportedTarget(
                row, 0, row.RequestPublicId));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ReviewTargetRules.RequireSupportedTarget(
                row, -1, row.RequestPublicId));
    }

    [Fact]
    public void Published_source_matching_owner_team_and_active_approval_passes()
    {
        var row = Pending();
        V180B3ApprovalApplyPreflight.RequirePublishedSourceEligibility(
            row, 9, 1, 7, 910, "Customer", "Approved", true);
    }

    [Theory]
    [InlineData(10, 1, 7, 910, "Customer", "Approved", true)]
    [InlineData(9, 2, 7, 910, "Customer", "Approved", true)]
    [InlineData(9, 1, 8, 910, "Customer", "Approved", true)]
    [InlineData(9, 1, 7, 911, "Customer", "Approved", true)]
    [InlineData(9, 1, 7, 910, "Official", "Approved", true)]
    [InlineData(9, 1, 7, 910, "Customer", "Pending", true)]
    [InlineData(9, 1, 7, 910, "Customer", "Approved", false)]
    public void Published_source_drift_or_unapproved_source_is_denied(
        int locationId, int? orgId, int? teamId, int? ownerId,
        string locationType, string approvalStatus, bool isActive)
    {
        var row = Pending();
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequirePublishedSourceEligibility(
                row, locationId, orgId, teamId, ownerId,
                locationType, approvalStatus, isActive));
    }

    [Fact]
    public void Missing_published_source_ownership_and_team_are_denied()
    {
        var row = Pending();
        row.TeamId = null;
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequirePublishedSourceEligibility(
                row, 9, 1, 7, 910, "Customer", "Approved", true));
        row = Pending();
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.RequirePublishedSourceEligibility(
                row, 9, 1, 7, null, "Customer", "Approved", true));
    }

    [Fact]
    public void Pure_approval_plan_preserves_both_rowversions_and_audit_snapshots()
    {
        var row = Pending();
        row.RowVersion = new byte[] { 2, 0, 0, 0, 0, 0, 0, 0 };
        var sourceVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        var plan = V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
            row, 1, row.RequestPublicId, 20, review,
            9, 1, 7, 910, "Customer", "Approved", true,
            sourceVersion, Before);
        Assert.Equal(row.RequestPublicId, plan.RequestPublicId);
        Assert.Equal(review.DecisionKey, plan.DecisionKey);
        Assert.Equal("Independent approval", plan.ReviewReason);
        Assert.Equal(Before, JsonSerializer.Deserialize<V180B3LocationFields>(
            plan.BeforeJson));
        Assert.Equal(Proposed, JsonSerializer.Deserialize<V180B3LocationFields>(
            plan.ProposedJson));
        Assert.Equal(sourceVersion, plan.ExpectedLocationRowVersion);
        Assert.Equal(row.RowVersion, plan.ExpectedRequestRowVersion);
        sourceVersion[0] = 99;
        row.RowVersion[0] = 99;
        Assert.Equal(0, plan.ExpectedLocationRowVersion[0]);
        Assert.Equal(2, plan.ExpectedRequestRowVersion[0]);
    }

    [Fact]
    public void Pure_approval_plan_denies_stale_source_even_for_valid_admin()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, Before));
    }

    [Fact]
    public void Pure_approval_plan_denies_source_owner_drift_even_with_valid_payload()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 911, "Customer", "Approved", true,
                new byte[8], Before));
    }

}
