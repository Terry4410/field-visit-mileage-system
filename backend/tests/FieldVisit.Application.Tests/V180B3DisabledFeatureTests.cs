using FieldVisit.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180B3DisabledFeatureTests
{
    [Fact]
    public async Task Canonical_B3_service_is_disabled_before_database_access()
    {
        // Option C deliberately does not register the B3 HTTP controller.
        // This test checks the service boundary without requiring UAT tables.
        var disabled = new V180B3ChangeRequestService(
            null!, null!, new ConfigurationBuilder().Build());
        Assert.False(disabled.Enabled);
        var input = new V180B3SubmitLocation(
            7, Convert.ToBase64String(new byte[8]), "reason",
            new V180B3LocationFields("Site", null, null, "Address", null, null, null));
        var review = new V180B3Review(
            Convert.ToBase64String(new byte[8]), Guid.NewGuid(), "reason");

        var submit = await Assert.ThrowsAsync<InvalidOperationException>(
            () => disabled.SubmitAsync(input, CancellationToken.None));
        Assert.Equal("B3_DISABLED", submit.Message);
        var approve = await Assert.ThrowsAsync<InvalidOperationException>(
            () => disabled.ApproveAsync(Guid.NewGuid(), review, CancellationToken.None));
        Assert.Equal("B3_DISABLED", approve.Message);
    }
}
