using System.Reflection;
using FieldVisit.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Source/metadata level API security contract only; NOT HTTP E2E.
/// Does not enable B3, change claims, create data or invoke approval.
/// </summary>
public sealed class V180B3ControllerAuthorizationContractTests
{
    [Fact]
    public void B3_controller_requires_authentication_and_keeps_expected_base_route()
    {
        var type=typeof(V180B3ChangeRequestsController);
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/v1/change-requests",
            type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData("Submit","locations","POST","visitor,leader")]
    [InlineData("Mine","mine","GET","visitor,leader,admin")]
    [InlineData("Pending","admin/pending","GET","admin")]
    [InlineData("Reject","admin/{id:guid}/reject","POST","admin")]
    [InlineData("Approve","admin/{id:guid}/approve","POST","admin")]
    public void B3_endpoint_role_and_route_contracts_must_not_drift(
        string action,string template,string verb,string roles)
    {
        var method=typeof(V180B3ChangeRequestsController).GetMethod(action);
        Assert.NotNull(method);
        Assert.Empty(method!.GetCustomAttributes<AllowAnonymousAttribute>());
        var authorization=Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(roles,authorization.Roles);
        var http=Assert.Single(method.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(template,http.Template);
        Assert.Contains(verb,http.HttpMethods);
    }
}
