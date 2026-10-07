using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180ProjectListAvailabilityTests
{
    [Fact]
    public async Task Visitor_project_list_keeps_active_past_and_future_projects_for_visit_date_filtering()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"project-list-{Guid.NewGuid()}")
            .Options;

        await using var db = new AppDbContext(options);
        var today = BusinessTime.Today;

        db.Projects.AddRange(
            new Project
            {
                ProjectId = 1,
                OrganizationId = 1,
                TeamId = 10,
                ProjectCode = "PAST",
                ProjectName = "A Past",
                StartDate = today.AddDays(-30),
                EndDate = today.AddDays(-1),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Project
            {
                ProjectId = 2,
                OrganizationId = 1,
                TeamId = 10,
                ProjectCode = "FUTURE",
                ProjectName = "B Future",
                StartDate = today.AddDays(1),
                EndDate = today.AddDays(30),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Project
            {
                ProjectId = 3,
                OrganizationId = 1,
                TeamId = 10,
                ProjectCode = "INACTIVE",
                ProjectName = "C Inactive",
                StartDate = today.AddDays(-1),
                EndDate = today.AddDays(1),
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            },
            new Project
            {
                ProjectId = 4,
                OrganizationId = 2,
                TeamId = 10,
                ProjectCode = "OTHER-ORG",
                ProjectName = "D Other Org",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var repository = new MasterRepository(db);
        var user = new CurrentUserDto(
            100,
            "pilotv01",
            "Pilot Visitor",
            "pilotv01@example.com",
            1,
            10,
            "北區第一組",
            new[] { "visitor" },
            new[] { new TeamScopeDto(10, "北區第一組", true) });

        var rows = await repository.GetProjectsAsync(
            user,
            includeInactive: false,
            CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, rows.Select(x => x.ProjectId).ToArray());
    }
}
