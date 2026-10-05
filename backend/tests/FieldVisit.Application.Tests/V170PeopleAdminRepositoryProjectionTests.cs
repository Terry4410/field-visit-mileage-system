using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170PeopleAdminRepositoryProjectionTests
{
    [Fact]
    public void Organization_scope_projection_avoids_schema_absent_effective_columns()
    {
        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=FieldVisitModelOnly;User Id=model;Password=model;TrustServerCertificate=True")
                .Options;

        using var db =
            new AppDbContext(options);

        var scopeOrgIds =
            new[] { 1 };

        var sql =
            db.Organizations
                .AsNoTracking()
                .Where(
                    x =>
                        scopeOrgIds.Contains(
                            x.OrganizationId))
                .Select(
                    x => new
                    {
                        x.OrganizationId,
                        x.OrganizationName
                    })
                .ToQueryString();

        Assert.Contains(
            "[OrganizationId]",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "[OrganizationName]",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[EffectiveFrom]",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[EffectiveTo]",
            sql,
            StringComparison.Ordinal);
    }
}
