using FieldVisit.Api.Controllers;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180B3DisabledFeatureTests
{
    [Fact]
    public async Task All_B3_endpoints_are_closed_by_default_without_accessing_database()
    {
        var disabled=new V180B3ChangeRequestService(
            null!,null!,new ConfigurationBuilder().Build());
        Assert.False(disabled.Enabled);
        var controller=new V180B3ChangeRequestsController(disabled);
        var request=new V180B3SubmitLocation(
            7,Convert.ToBase64String(new byte[8]),"reason",
            new V180B3LocationFields("Site",null,null,"Address",null,null,null));
        var review=new V180B3Review(
            Convert.ToBase64String(new byte[8]),Guid.NewGuid(),"reason");
        var id=Guid.NewGuid();
        var responses=new IActionResult[]
        {
            await controller.Submit(request,CancellationToken.None),
            await controller.Mine(CancellationToken.None),
            await controller.Pending(CancellationToken.None),
            await controller.Reject(id,review,CancellationToken.None),
            await controller.Approve(id,review,CancellationToken.None)
        };
        Assert.All(responses,r=>Assert.Equal(503,Assert.IsType<ObjectResult>(r).StatusCode));
    }
}
