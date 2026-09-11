using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180Ea1NotificationRuntimeTests
{
    [Fact]
    public void Catalog_has_exact_14_frozen_events_and_versioned_templates()
    {
        var rows = NotificationEventCatalog.All.OrderBy(x => x.EventCode).ToArray();
        Assert.Equal(14, rows.Length);
        Assert.Equal(new[]
        {
            "CorrectionApproved|Transaction|False|CorrectionApproved.v1",
            "CorrectionRequested|Transaction|False|CorrectionRequested.v1",
            "CorrectionReturned|Transaction|False|CorrectionReturned.v1",
            "DeploymentSiteChangeEffective|Reminder|True|DeploymentSiteChangeEffective.v1",
            "EmploymentAuthorizationExpiring|Reminder|True|EmploymentAuthorizationExpiring.v1",
            "ImportCompleted|System|False|ImportCompleted.v1",
            "ImportFailed|System|False|ImportFailed.v1",
            "LocationApproved|Transaction|False|LocationApproved.v1",
            "LocationReturned|Transaction|False|LocationReturned.v1",
            "LocationReviewRequested|Transaction|False|LocationReviewRequested.v1",
            "ProjectExpiring|Reminder|True|ProjectExpiring.v1",
            "TripApproved|Transaction|False|TripApproved.v1",
            "TripReturned|Transaction|False|TripReturned.v1",
            "TripSubmitted|Transaction|False|TripSubmitted.v1"
        }, rows.Select(x => $"{x.EventCode}|{x.Category}|{x.HonorsOptionalPreference}|{x.TemplateCode}").ToArray());
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

    [Fact]
    public void Typed_v1_template_payload_is_deterministic()
    {
        var payload = new NotificationTemplatePayloadV1("REF-1", "detail");
        Assert.Equal(
            "{\"version\":1,\"reference\":\"REF-1\",\"detail\":\"detail\"}",
            NotificationTemplatePayloadAuthority.Serialize("TripSubmitted.v1", payload));
    }

    [Fact]
    public void Arbitrary_payload_type_or_template_version_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NotificationTemplatePayloadAuthority.Serialize("TripSubmitted.v1", new UnsupportedPayload()));
        Assert.Throws<InvalidOperationException>(() =>
            NotificationTemplatePayloadAuthority.Serialize("TripSubmitted.v2", new NotificationTemplatePayloadV1("REF-1")));
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
