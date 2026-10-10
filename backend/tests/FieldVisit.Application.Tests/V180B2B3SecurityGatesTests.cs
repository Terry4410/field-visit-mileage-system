using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180B2B3SecurityGatesTests
{
    [Fact]
    public void Geocoding_does_not_publish_a_pending_location()
    {
        var location=new Location { ApprovalStatus="Pending", IsActive=true };
        V180LocationPublicationRules.PreserveReviewStateAfterGeocoding(location);
        Assert.Equal("Pending",location.ApprovalStatus);
        Assert.False(location.IsActive);
    }

    [Fact]
    public void Regeocoding_does_not_change_existing_approval_decision()
    {
        var location=new Location { ApprovalStatus="Approved", IsActive=true };
        V180LocationPublicationRules.PreserveReviewStateAfterGeocoding(location);
        Assert.Equal("Approved",location.ApprovalStatus);
        Assert.True(location.IsActive);
        location.IsActive=false;
        V180LocationPublicationRules.PreserveReviewStateAfterGeocoding(location);
        Assert.False(location.IsActive);
    }

    [Fact]
    public void Unattested_manager_grants_are_fail_closed()
    {
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B1ManagerGrantProvenance.RequireVerifiedManagerGrant());
    }
}
