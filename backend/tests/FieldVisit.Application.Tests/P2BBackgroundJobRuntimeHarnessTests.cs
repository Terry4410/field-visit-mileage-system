using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class P2BBackgroundJobRuntimeHarnessTests
{
    [Fact]
    public async Task Mixed_failure_is_durable_and_later_trip_continues_with_fresh_tracking()
    {
        var connectionString = Environment.GetEnvironmentVariable("P2B_TEST_SQL_CONNECTION");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "P2B_TEST_SQL_CONNECTION is required for the ephemeral SQL Server runtime harness.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(3),
                    errorNumbersToAdd: null))
            .Options;

        await PrepareEphemeralDatabaseAsync(options);

        const int teamId = 18002;
        const int requestedByUserId = 18001;
        var now = DateTime.UtcNow;
        var firstDate = DateOnly.FromDateTime(now.Date.AddDays(1));
        var secondDate = firstDate.AddDays(1);

        long failedTripId;
        long succeededTripId;
        Guid jobId;

        await using (var seed = new AppDbContext(options))
        {
            var failedTrip = BuildSubmittedTrip(
                "P2B-BG-FAIL-FIRST",
                teamId,
                requestedByUserId,
                firstDate,
                now);
            var succeededTrip = BuildSubmittedTrip(
                "P2B-BG-SUCCEED-SECOND",
                teamId,
                requestedByUserId,
                secondDate,
                now.AddSeconds(1));

            seed.VisitTrips.AddRange(failedTrip, succeededTrip);
            await seed.SaveChangesAsync();

            failedTripId = failedTrip.VisitTripId;
            succeededTripId = succeededTrip.VisitTripId;

            jobId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(
                new MileageBatchRequest(
                    "Selected",
                    null,
                    null,
                    new[] { failedTripId, succeededTripId }),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            seed.BackgroundJobs.Add(new BackgroundJob
            {
                BackgroundJobId = jobId,
                JobType = "Mileage",
                Status = "Waiting",
                Mode = "Selected",
                OrganizationId = 1,
                TeamScopeJson = JsonSerializer.Serialize(new[] { teamId }),
                RequestedByUserId = requestedByUserId,
                PayloadJson = payload,
                CreatedAt = now
            });
            await seed.SaveChangesAsync();
        }

        var fakeRoute = new FailFirstSucceedSecondRouteService(
            failedTripId,
            succeededTripId);
        var fakeGeocoding = new NeverCalledGeocodingService();

        await using (var execution = new AppDbContext(options))
        {
            var service = new BackgroundJobService(
                execution,
                fakeRoute,
                fakeGeocoding);

            var processed = await service.ProcessNextAsync(CancellationToken.None);
            Assert.True(processed);
        }

        await using (var verify = new AppDbContext(options))
        {
            var job = await verify.BackgroundJobs
                .AsNoTracking()
                .SingleAsync(x => x.BackgroundJobId == jobId);

            Assert.Equal("PartiallySucceeded", job.Status);
            Assert.Equal(2, job.TotalCount);
            Assert.Equal(1, job.SuccessCount);
            Assert.Equal(1, job.FailedCount);
            Assert.NotEqual("Waiting", job.Status);
            Assert.NotEqual("Processing", job.Status);
            Assert.NotNull(job.CompletedAt);

            var items = await verify.BackgroundJobItems
                .AsNoTracking()
                .Where(x => x.BackgroundJobId == jobId)
                .OrderBy(x => x.BackgroundJobItemId)
                .ToListAsync();

            Assert.Equal(2, items.Count);

            var failedItem = items.Single(x => x.EntityId == failedTripId.ToString());
            Assert.Equal("Failed", failedItem.Status);
            Assert.NotNull(failedItem.CompletedAt);
            Assert.Equal("MILEAGE_JOB_FAILED", failedItem.ErrorCode);

            var succeededItem = items.Single(x => x.EntityId == succeededTripId.ToString());
            Assert.Equal("Succeeded", succeededItem.Status);
            Assert.NotNull(succeededItem.CompletedAt);

            var failedTrip = await verify.VisitTrips
                .AsNoTracking()
                .SingleAsync(x => x.VisitTripId == failedTripId);
            Assert.Equal(TripStatuses.Submitted, failedTrip.Status);
            Assert.NotEqual(TripStatuses.PendingApproval, failedTrip.Status);

            var succeededTrip = await verify.VisitTrips
                .AsNoTracking()
                .Include(x => x.MileageCalculation)
                .SingleAsync(x => x.VisitTripId == succeededTripId);
            Assert.Equal(TripStatuses.PendingApproval, succeededTrip.Status);
            Assert.NotNull(succeededTrip.MileageCalculation);
            Assert.NotNull(succeededTrip.MileageCalculation!.SystemDistanceKm);

            var failedEventExists = await verify.MileageGovernanceEvents
                .AsNoTracking()
                .AnyAsync(
                    x => x.VisitTripId == failedTripId
                        && x.EventType == "CalculationFailed");
            Assert.True(failedEventExists);
        }

        Assert.Equal(
            new[] { failedTripId, succeededTripId },
            fakeRoute.ProcessedTripIds);

        await using var cleanup = new AppDbContext(options);
        await cleanup.Database.EnsureDeletedAsync();
    }

    private static VisitTrip BuildSubmittedTrip(
        string tripNo,
        int teamId,
        int userId,
        DateOnly visitDate,
        DateTime createdAt)
    {
        var trip = new VisitTrip
        {
            TripNo = tripNo,
            UserId = userId,
            OrganizationId = 1,
            TeamId = teamId,
            VisitDate = visitDate,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Status = TripStatuses.Submitted,
            VehicleType = "Motorcycle",
            Purpose = "P2B background runtime harness",
            SubmittedAt = createdAt,
            CreatedAt = createdAt,
            CreatedByUserId = userId,
            UpdatedAt = createdAt,
            UpdatedByUserId = userId
        };

        trip.Stops =
        [
            new VisitTripStop
            {
                StopSequence = 1,
                LocationNameSnapshot = $"{tripNo}-STOP-1",
                AddressSnapshot = "Harness address 1",
                VisitPurpose = "Harness",
                CreatedAt = createdAt
            },
            new VisitTripStop
            {
                StopSequence = 2,
                LocationNameSnapshot = $"{tripNo}-STOP-2",
                AddressSnapshot = "Harness address 2",
                VisitPurpose = "Harness",
                CreatedAt = createdAt
            }
        ];

        trip.MileageCalculation = new MileageCalculation
        {
            ClaimedDistanceKm = 10m,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

        return trip;
    }

    private static async Task PrepareEphemeralDatabaseAsync(
        DbContextOptions<AppDbContext> options)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await using var db = new AppDbContext(options);
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException(
            "Ephemeral SQL Server did not become ready for the P2B runtime harness.",
            lastError);
    }

    private sealed class FailFirstSucceedSecondRouteService(
        long failedTripId,
        long succeededTripId) : IRouteCalculationService
    {
        public List<long> ProcessedTripIds { get; } = [];

        public Task<RouteCalculationResult> CalculateAsync(
            VisitTrip trip,
            CancellationToken ct)
        {
            ProcessedTripIds.Add(trip.VisitTripId);

            if (trip.VisitTripId == failedTripId)
            {
                return Task.FromResult(
                    new RouteCalculationResult(
                        false,
                        null,
                        "HARNESS_FORCED_FAILURE",
                        "Forced first-trip route failure."));
            }

            Assert.Equal(succeededTripId, trip.VisitTripId);
            return Task.FromResult(
                new RouteCalculationResult(
                    true,
                    12.34m,
                    null,
                    null));
        }
    }

    private sealed class NeverCalledGeocodingService : IGeocodingService
    {
        public Task<GeocodingResult> ResolveAsync(
            string? address,
            string? plusCode,
            CancellationToken ct) =>
            throw new InvalidOperationException(
                "Geocoding must not be called by the P2B mileage runtime harness.");
    }
}
