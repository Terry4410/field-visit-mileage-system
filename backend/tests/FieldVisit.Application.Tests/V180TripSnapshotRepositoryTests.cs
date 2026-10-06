using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TripSnapshotRepositoryTests
{
    [Fact]
    public async Task Submitted_snapshot_uses_official_only_end_site()
    {
        await using var db = Db();
        db.Organizations.Add(new Organization
        {
            OrganizationId = 1,
            OrganizationCode = "ORG",
            OrganizationName = "Organization",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Employments.Add(new Employment
        {
            EmploymentId = 100,
            PersonId = 1000,
            OrganizationId = 1,
            EmployeeNo = "E001",
            SourceType = "UAT"
        });
        await db.SaveChangesAsync();

        var trip = new VisitTrip
        {
            VisitTripId = 301,
            TripNo = "UAT-301",
            UserId = 1,
            EmploymentId = 100,
            OrganizationId = 1,
            TeamId = 10,
            StartDeploymentSiteId = 101,
            EndDeploymentSiteId = 201,
            VisitDate = new DateOnly(2026, 10, 6),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            Status = TripStatuses.Submitted,
            VehicleType = "Motorcycle",
            SubmittedAt = DateTime.UtcNow,
            Stops =
            [
                new VisitTripStop
                {
                    StopSequence = 1,
                    LocationNameSnapshot = "Visit",
                    AddressSnapshot = "Visit road",
                    CreatedAt = DateTime.UtcNow
                }
            ],
            MileageCalculation = new MileageCalculation
            {
                VisitTripId = 301,
                ClaimedDistanceKm = 12.3m,
                CreatedAt = DateTime.UtcNow
            }
        };

        var context = new V180TripContextDto(
            100,
            new DateOnly(2026, 10, 6),
            true,
            "OK",
            "OK",
            [new V180TripContextTeamDto(10, "T10", "Team 10", true)],
            10,
            [
                Site(101, "START", "Start", "Start road", true),
                Site(102, "TEAM-END", "Team end", "Team end road", false)
            ],
            101,
            101,
            101,
            [
                Site(101, "START", "Start", "Start road", true),
                Site(102, "TEAM-END", "Team end", "Team end road", false),
                Site(201, "AUTO-S-431", "Official end", "Official end road", false)
            ]);

        var repository = new TripSnapshotRepository(db);
        await repository.AddSubmittedSnapshotAsync(
            trip,
            new CurrentUserDto(
                1, "E001", "Visitor", "visitor@example.test",
                1, 10, "Team 10", ["visitor"],
                [new TeamScopeDto(10, "Team 10", true)]),
            context,
            default);
        await db.SaveChangesAsync();

        var snapshot = await db.VisitTripSnapshots.SingleAsync();
        Assert.Equal("START", snapshot.StartDeploymentSiteCodeSnapshot);
        Assert.Equal("Start road", snapshot.StartDeploymentAddressSnapshot);
        Assert.Equal("AUTO-S-431", snapshot.EndDeploymentSiteCodeSnapshot);
        Assert.Equal("Official end road", snapshot.EndDeploymentAddressSnapshot);
        Assert.Equal(
            201,
            db.Entry(snapshot)
                .Property<int?>("EndDeploymentSiteIdSnapshot")
                .CurrentValue);
    }

    private static V180TripContextDeploymentSiteDto Site(
        int id,
        string code,
        string name,
        string address,
        bool primary) =>
        new(
            id,
            1,
            "C",
            "Center",
            code,
            name,
            1000 + id,
            null,
            name,
            address,
            primary);

    private static AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"v180-snapshot-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }
}
