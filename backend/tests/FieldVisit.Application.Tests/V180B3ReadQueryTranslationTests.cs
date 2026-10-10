using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Verifies SQL Server translatability and tenant/owner predicates without
/// connecting to a database or executing migration/DDL.
/// </summary>
public sealed class V180B3ReadQueryTranslationTests
{
    private static AppDbContext ModelOnly() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;TrustServerCertificate=True;")
            .Options);

    private static CurrentUserDto Actor(int id=71,int organization=3) =>
        new(id,"code","Tester",null,organization,7,"Team",
            new[]{"admin"},new[]{new TeamScopeDto(7,"Team",true)});

    [Fact]
    public void Mine_query_is_translatable_and_requires_requester_organization_and_known_operation()
    {
        using var db=ModelOnly();
        var sql=V180B3QueueScopeRules.ForRequester(
            db.ChangeRequests.AsNoTracking(),Actor()).ToQueryString();
        Assert.Contains("[OrganizationId]",sql);
        Assert.Contains("[RequestedByUserId]",sql);
        Assert.Contains("[EntityKind]",sql);
        Assert.Contains("[OperationCode]",sql);
        Assert.Contains("[RiskCode]",sql);
        Assert.Contains("[TeamId]",sql);
        Assert.Contains("[Status]",sql);
        Assert.Contains("UpdatePublishedLocation",sql);
        Assert.Contains("Location",sql);
        Assert.Contains("Rejected",sql);
    }

    [Fact]
    public void Admin_pending_query_has_tenant_and_operation_filters()
    {
        using var db=ModelOnly();
        var sql=V180B3QueueScopeRules.ForAdminPending(
            db.ChangeRequests.AsNoTracking(),Actor()).ToQueryString();
        Assert.Contains("[OrganizationId]",sql);
        Assert.Contains("[EntityKind]",sql);
        Assert.Contains("[OperationCode]",sql);
        Assert.Contains("[RiskCode]",sql);
        Assert.Contains("[TeamId]",sql);
        Assert.Contains("UpdatePublishedLocation",sql);
        Assert.Contains("Pending",sql);
        Assert.DoesNotContain("[RequestedByUserId] =",sql);
    }
}
