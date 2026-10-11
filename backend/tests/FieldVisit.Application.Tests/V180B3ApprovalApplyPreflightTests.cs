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

    [Fact]
    public void Missing_or_malformed_request_rowversion_cannot_create_approval_plan()
    {
        var row = Pending();
        var review = new V180B3Review(
            Convert.ToBase64String(new byte[8]), Guid.NewGuid(),
            "Independent approval");
        foreach (var version in new[] { Array.Empty<byte>(), new byte[7], new byte[9] })
        {
            row.RowVersion = version;
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                    row, 1, row.RequestPublicId, 20, review,
                    9, 1, 7, 910, "Customer", "Approved", true,
                    new byte[8], Before));
        }
    }

    [Fact]
    public void Approval_plan_denies_self_review_and_rejected_request_without_side_effects()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 910, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
        row.Status = "Rejected";
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
    }

    [Fact]
    public void Approval_plan_denies_forged_before_snapshot_and_cross_tenant()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        row.BeforeJson = JsonSerializer.Serialize(
            Before with { LocationName = "Forged previous name" });
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
        row = Pending();
        row.RowVersion = new byte[8];
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 2, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
    }

    [Fact]
    public void Null_expected_source_version_fails_with_controlled_conflict()
    {
        var row = Pending();
        row.ExpectedEntityRowVersion = null;
        var error = Assert.Throws<UnauthorizedAccessException>(() => Validate(row));
        Assert.Equal("B3_REVIEW_TARGET_UNSUPPORTED", error.Message);
    }

    [Fact]
    public void Null_expected_source_version_cannot_create_immutable_approval_plan()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        row.ExpectedEntityRowVersion = null;
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        var error = Assert.Throws<UnauthorizedAccessException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
        Assert.Equal("B3_REVIEW_TARGET_UNSUPPORTED", error.Message);
    }

    [Theory]
    [InlineData("LocationName")]
    [InlineData("City")]
    [InlineData("District")]
    [InlineData("Address")]
    [InlineData("PlusCode")]
    [InlineData("TaxId")]
    [InlineData("MasterNote")]
    public void Approval_plan_rejects_each_individual_source_field_drift(string field)
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent review");
        var current = field switch
        {
            "LocationName" => Before with { LocationName = "Changed name" },
            "City" => Before with { City = "Taichung" },
            "District" => Before with { District = "Daan" },
            "Address" => Before with { Address = "Changed address" },
            "PlusCode" => Before with { PlusCode = "New code" },
            "TaxId" => Before with { TaxId = "12345678" },
            "MasterNote" => Before with { MasterNote = "Changed note" },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var error = Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], current));
        Assert.Equal("B3_APPROVAL_SOURCE_DRIFT", error.Message);
    }

    [Fact]
    public void Approval_plan_rejects_stale_request_version_and_empty_decision_key()
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var stale = new V180B3Review(
            Convert.ToBase64String(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
            Guid.NewGuid(), "Independent review");
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, stale,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
        var noKey = stale with
        {
            RequestRowVersion = Convert.ToBase64String(row.RowVersion),
            DecisionKey = Guid.Empty
        };
        Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, noKey,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[8], Before));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void Approval_plan_rejects_malformed_current_source_rowversion(int length)
    {
        var row = Pending();
        row.RowVersion = new byte[8];
        var review = new V180B3Review(
            Convert.ToBase64String(row.RowVersion), Guid.NewGuid(),
            "Independent approval");
        var error = Assert.Throws<InvalidOperationException>(() =>
            V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
                row, 1, row.RequestPublicId, 20, review,
                9, 1, 7, 910, "Customer", "Approved", true,
                new byte[length], Before));
        Assert.Equal("B3_SOURCE_ROWVERSION_CONFLICT", error.Message);
    }

    [Fact]
    public void Approval_plan_version_guard_accepts_only_exact_locked_versions()
    {
        var plan = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed), new byte[8], new byte[8]);
        V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
            plan, plan.RequestPublicId, 9, new byte[8], new byte[8]);
    }

    [Fact]
    public void Approval_plan_version_guard_rejects_request_source_or_identity_drift()
    {
        var plan = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed), new byte[8], new byte[8]);
        var stale = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        void Deny(Guid id, int locationId, byte[]? request, byte[]? source) =>
            Assert.Equal("B3_APPROVAL_PLAN_STALE",
                Assert.Throws<InvalidOperationException>(() =>
                    V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
                        plan, id, locationId, request, source)).Message);
        Deny(Guid.NewGuid(), 9, new byte[8], new byte[8]);
        Deny(plan.RequestPublicId, 10, new byte[8], new byte[8]);
        Deny(plan.RequestPublicId, 9, stale, new byte[8]);
        Deny(plan.RequestPublicId, 9, new byte[8], stale);
        Deny(plan.RequestPublicId, 9, null, new byte[8]);
        Deny(plan.RequestPublicId, 9, new byte[8], null);
        Deny(plan.RequestPublicId, 9, new byte[7], new byte[8]);
        Deny(plan.RequestPublicId, 9, new byte[8], new byte[9]);
    }

    [Fact]
    public void Approval_plan_version_guard_rejects_mutated_expected_versions()
    {
        var plan = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed), new byte[8], new byte[8]);
        plan.ExpectedRequestRowVersion[0] = 1;
        Assert.Equal("B3_APPROVAL_PLAN_STALE",
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
                    plan, plan.RequestPublicId, 9, new byte[8], new byte[8])).Message);
        plan.ExpectedRequestRowVersion[0] = 0;
        plan.ExpectedLocationRowVersion[0] = 1;
        Assert.Equal("B3_APPROVAL_PLAN_STALE",
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
                    plan, plan.RequestPublicId, 9, new byte[8], new byte[8])).Message);
    }

    [Fact]
    public void Approval_plan_boundary_rejects_tampered_decision_identity_and_reason()
    {
        var valid = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed), new byte[8], new byte[8]);
        void Deny(V180B3ApprovalApplyPlan candidate) =>
            Assert.Equal("B3_APPROVAL_PLAN_INVALID",
                Assert.Throws<InvalidOperationException>(() =>
                    V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
                        candidate, candidate.RequestPublicId, 9,
                        new byte[8], new byte[8])).Message);
        Deny(valid with { DecisionKey = Guid.Empty });
        Deny(valid with { ReviewerUserId = 0 });
        Deny(valid with { ReviewerUserId = -1 });
        Deny(valid with { ReviewReason = "" });
        Deny(valid with { ReviewReason = "  " });
        Deny(valid with { ReviewReason = " padded" });
        Deny(valid with { ReviewReason = "padded " });
        Deny(valid with { ReviewReason = "bad\0control" });
        Deny(valid with { ReviewReason = new string('X', 1001) });
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"LocationName\":\"A\",\"City\":null}")]
    public void Approval_plan_boundary_rejects_invalid_audit_snapshot_shape(string invalid)
    {
        var plan = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        foreach (var candidate in new[]
        {
            plan with { BeforeJson = invalid },
            plan with { ProposedJson = invalid }
        })
            Assert.Equal("B3_APPROVAL_PLAN_INVALID",
                Assert.Throws<InvalidOperationException>(() =>
                    V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
                        candidate, candidate.RequestPublicId, 9,
                        new byte[8], new byte[8])).Message);
    }

    [Fact]
    public void Approval_plan_boundary_rejects_noop_or_noncanonical_proposal()
    {
        var valid = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        foreach (var proposed in new[]
        {
            JsonSerializer.Serialize(Before),
            JsonSerializer.Serialize(Proposed with { LocationName = " New name " })
        })
        {
            var plan = valid with { ProposedJson = proposed };
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.RequirePlanVersionsStillCurrent(
                    plan, plan.RequestPublicId, 9, new byte[8], new byte[8]));
        }
    }

    [Fact]
    public void Approval_plan_locked_snapshot_guard_accepts_matching_source_and_request()
    {
        var request = Pending();
        request.RowVersion = new byte[8];
        var plan = new V180B3ApprovalApplyPlan(
            request.RequestPublicId, 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        V180B3ApprovalApplyPreflight.RequirePlanSnapshotsStillCurrent(
            plan, Before, new byte[8], request);
    }

    [Fact]
    public void Approval_plan_locked_snapshot_guard_rejects_source_request_and_version_drift()
    {
        var request = Pending();
        request.RowVersion = new byte[8];
        var plan = new V180B3ApprovalApplyPlan(
            request.RequestPublicId, 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        void Deny(V180B3ApprovalApplyPlan candidate,
            V180B3LocationFields source, byte[]? version, V180B3ChangeRequest row) =>
            Assert.Equal("B3_APPROVAL_PLAN_STALE",
                Assert.Throws<InvalidOperationException>(() =>
                    V180B3ApprovalApplyPreflight.RequirePlanSnapshotsStillCurrent(
                        candidate, source, version, row)).Message);
        Deny(plan, Before with { City = "Changed" }, new byte[8], request);
        Deny(plan, Before, null, request);
        Deny(plan, Before, new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, request);
        Deny(plan, Before, new byte[8], CloneRequest(request, status: "Rejected"));
        Deny(plan, Before, new byte[8], CloneRequest(request, proposedJson: JsonSerializer.Serialize(Before)));
        Deny(plan, Before, new byte[8], CloneRequest(request, beforeJson: JsonSerializer.Serialize(Before with { City = "Changed" })));
        Deny(plan, Before, new byte[8], CloneRequest(request, expectedVersion: new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
    }

    private static V180B3ChangeRequest CloneRequest(
        V180B3ChangeRequest row, string? status = null,
        string? proposedJson = null, string? beforeJson = null,
        byte[]? expectedVersion = null) => new()
    {
        RequestPublicId = row.RequestPublicId,
        OrganizationId = row.OrganizationId,
        TeamId = row.TeamId,
        EntityKind = row.EntityKind,
        OperationCode = row.OperationCode,
        RiskCode = row.RiskCode,
        RequestedByUserId = row.RequestedByUserId,
        EntityId = row.EntityId,
        Status = status ?? row.Status,
        RowVersion = row.RowVersion,
        ExpectedEntityRowVersion = expectedVersion ?? row.ExpectedEntityRowVersion,
        BeforeJson = beforeJson ?? row.BeforeJson,
        ProposedJson = proposedJson ?? row.ProposedJson
    };

    [Fact]
    public void Approval_plan_locked_request_guard_rejects_cross_scope_and_self_review()
    {
        var request = Pending();
        request.RowVersion = new byte[8];
        var plan = new V180B3ApprovalApplyPlan(
            request.RequestPublicId, 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        void Deny(Action<V180B3ChangeRequest> change)
        {
            var row = CloneRequest(request);
            change(row);
            Assert.Equal("B3_APPROVAL_PLAN_STALE",
                Assert.Throws<InvalidOperationException>(() =>
                    V180B3ApprovalApplyPreflight.RequirePlanSnapshotsStillCurrent(
                        plan, Before, new byte[8], row)).Message);
        }
        Deny(x => x.OrganizationId = 0);
        Deny(x => x.TeamId = null);
        Deny(x => x.EntityKind = "Team");
        Deny(x => x.OperationCode = "DeleteLocation");
        Deny(x => x.RiskCode = "Low");
        Deny(x => x.RequestedByUserId = 20);
        Deny(x => x.ReviewedByUserId = 20);
        Deny(x => x.ReviewReason = "Already reviewed");
        Deny(x => x.ReviewedAt = DateTime.UtcNow);
        Deny(x => x.AppliedAt = DateTime.UtcNow);
    }

    [Fact]
    public void Approval_locked_location_scope_guard_rejects_reassigned_or_unpublished_source()
    {
        var request = Pending();
        var plan = new V180B3ApprovalApplyPlan(
            request.RequestPublicId, 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        void Check(int id = 9, int? org = 1, int? team = 7,
            int? creator = 910, string? type = "Customer",
            string? approval = "Approved", bool active = true) =>
            V180B3ApprovalApplyPreflight.RequireLockedPublishedLocationScope(
                plan, request, id, org, team, creator, type, approval, active);
        Check();
        void Deny(Action action) =>
            Assert.Equal("B3_APPROVAL_SOURCE_SCOPE_DENIED",
                Assert.Throws<UnauthorizedAccessException>(action).Message);
        Deny(() => Check(id: 10));
        Deny(() => Check(org: 2));
        Deny(() => Check(team: 8));
        Deny(() => Check(creator: 20));
        Deny(() => Check(type: "Official"));
        Deny(() => Check(approval: "Pending"));
        Deny(() => Check(active: false));
        Deny(() => V180B3ApprovalApplyPreflight.RequireLockedPublishedLocationScope(
            plan with { RequestPublicId = Guid.NewGuid() }, request,
            9, 1, 7, 910, "Customer", "Approved", true));
    }

    [Fact]
    public void Approval_decision_key_guard_rejects_replay_and_empty_key()
    {
        var plan = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        V180B3ApprovalApplyPreflight.RequireUnusedDecisionKey(plan, false);
        Assert.Equal("B3_DECISION_KEY_REPLAY",
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.RequireUnusedDecisionKey(plan, true)).Message);
        Assert.Equal("B3_APPROVAL_PLAN_INVALID",
            Assert.Throws<InvalidOperationException>(() =>
                V180B3ApprovalApplyPreflight.RequireUnusedDecisionKey(
                    plan with { DecisionKey = Guid.Empty }, false)).Message);
    }

    [Fact]
    public void Approval_locked_decision_identity_rejects_reviewer_key_and_reason_drift()
    {
        var plan = new V180B3ApprovalApplyPlan(
            Guid.NewGuid(), 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        V180B3ApprovalApplyPreflight.RequireLockedDecisionIdentity(
            plan, 20, plan.DecisionKey, "Independent review");
        void Deny(int reviewer, Guid key, string? reason) =>
            Assert.Equal("B3_APPROVAL_DECISION_IDENTITY_DRIFT",
                Assert.Throws<UnauthorizedAccessException>(() =>
                    V180B3ApprovalApplyPreflight.RequireLockedDecisionIdentity(
                        plan, reviewer, key, reason)).Message);
        Deny(0, plan.DecisionKey, "Independent review");
        Deny(21, plan.DecisionKey, "Independent review");
        Deny(20, Guid.Empty, "Independent review");
        Deny(20, Guid.NewGuid(), "Independent review");
        Deny(20, plan.DecisionKey, "Different reason");
        Deny(20, plan.DecisionKey, null);
    }

    [Fact]
    public void Combined_locked_approval_boundary_requires_all_security_conditions()
    {
        var request = Pending();
        request.RowVersion = new byte[8];
        var plan = new V180B3ApprovalApplyPlan(
            request.RequestPublicId, 9, 20, Guid.NewGuid(), "Independent review",
            JsonSerializer.Serialize(Before), JsonSerializer.Serialize(Proposed),
            new byte[8], new byte[8]);
        void Check(V180B3ChangeRequest row, V180B3LocationFields source,
            int reviewer = 20, bool replay = false, int? team = 7) =>
            V180B3ApprovalApplyPreflight.RequireLockedApprovalBoundary(
                plan, row, source, new byte[8], 9, 1, team, 910,
                "Customer", "Approved", true, reviewer,
                plan.DecisionKey, "Independent review", replay);
        Check(request, Before);
        Assert.Throws<InvalidOperationException>(() =>
            Check(request, Before with { Address = "Drifted" }));
        Assert.Throws<UnauthorizedAccessException>(() =>
            Check(request, Before, team: 99));
        Assert.Throws<UnauthorizedAccessException>(() =>
            Check(request, Before, reviewer: 21));
        Assert.Equal("B3_DECISION_KEY_REPLAY",
            Assert.Throws<InvalidOperationException>(() =>
                Check(request, Before, replay: true)).Message);
    }

}
