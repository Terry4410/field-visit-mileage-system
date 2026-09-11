using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180Ea1NotificationRuntimeTests
{
    [Fact]
    public void Event_code_vocabulary_has_exact_14_frozen_identifiers_only()
    {
        Assert.Equal(new[]
        {
            "CorrectionApproved",
            "CorrectionRequested",
            "CorrectionReturned",
            "DeploymentSiteChangeEffective",
            "EmploymentAuthorizationExpiring",
            "ImportCompleted",
            "ImportFailed",
            "LocationApproved",
            "LocationReturned",
            "LocationReviewRequested",
            "ProjectExpiring",
            "TripApproved",
            "TripReturned",
            "TripSubmitted"
        }, NotificationEventCodes.All.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void No_duplicate_runtime_source_defines_type_preference_or_template_semantics()
    {
        var assembly = typeof(NotificationEventCodes).Assembly;
        Assert.Null(assembly.GetType("FieldVisit.Application.NotificationEventDefinition"));
        Assert.Null(assembly.GetType("FieldVisit.Application.NotificationEventCatalog"));
    }

    [Fact]
    public void Business_event_key_is_caller_supplied_canonical_and_never_clock_or_guid_generated()
    {
        const string key = "TRIP:123:SUBMITTED:v1";
        Assert.Equal(key, NotificationBusinessKeyAuthority.ValidateBusinessEventKey(key));
        Assert.Throws<ArgumentException>(() => NotificationBusinessKeyAuthority.ValidateBusinessEventKey(""));
        Assert.Throws<ArgumentException>(() => NotificationBusinessKeyAuthority.ValidateBusinessEventKey(" x "));
        Assert.Throws<ArgumentException>(() => NotificationBusinessKeyAuthority.ValidateBusinessEventKey(new string('x', 201)));
    }

    [Fact]
    public void Recipient_keys_are_only_stable_employment_or_user_identity_keys()
    {
        Assert.Equal("EMP:42", NotificationBusinessKeyAuthority.ForEmployment(42));
        Assert.Equal("USER:7", NotificationBusinessKeyAuthority.ForUser(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationBusinessKeyAuthority.ForEmployment(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationBusinessKeyAuthority.ForUser(0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("a@example")]
    public void Email_usability_rejects_null_blank_and_malformed_values(string? value)
        => Assert.Null(NotificationEmailAuthority.NormalizeUsable(value));

    [Fact]
    public void Email_usability_normalizes_only_valid_delivery_address()
        => Assert.Equal("user@example.invalid", NotificationEmailAuthority.NormalizeUsable(" User@Example.Invalid "));

    [Fact]
    public void Typed_v1_template_payload_is_deterministic_for_db_selected_v1_template()
    {
        var payload = new NotificationTemplatePayloadV1("REF-1", "detail");
        Assert.Equal(
            "{\"version\":1,\"reference\":\"REF-1\",\"detail\":\"detail\"}",
            NotificationTemplatePayloadAuthority.Serialize("RuntimeSelectedTemplate.v1", payload));
    }

    [Fact]
    public void Incompatible_db_template_version_is_payload_contract_mismatch_not_setting_drift()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NotificationTemplatePayloadAuthority.Serialize("TripSubmitted.v2", new NotificationTemplatePayloadV1("REF-1")));
        Assert.Contains("payload-contract/version mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setting drift", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Arbitrary_payload_type_is_rejected_even_when_version_number_matches()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NotificationTemplatePayloadAuthority.Serialize("TripSubmitted.v1", new UnsupportedPayload()));
        Assert.Contains("payload-contract/version mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_environment_is_bound_lazily_and_fails_closed_when_missing()
    {
        Assert.Equal("UAT", new NotificationRuntimeEnvironment(" UAT ").GetRequiredCode());
        Assert.Throws<InvalidOperationException>(() => new NotificationRuntimeEnvironment("").GetRequiredCode());
    }

    private sealed record UnsupportedPayload : INotificationTemplatePayload
    {
        public int Version => 1;
    }
}
