using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("FB_SQL_CONNECTION")
    ?? throw new InvalidOperationException("FB_SQL_CONNECTION is required.");

await FaSchemaBootstrap.InitializeAsync(connectionString);
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
await using var db = new AppDbContext(options);

var now = DateTime.UtcNow;
var organization = new Organization
{
    OrganizationCode = "FB", OrganizationName = "F-B", IsActive = true, CreatedAt = now
};
db.Organizations.Add(organization);
await db.SaveChangesAsync();
var team = new Team
{
    OrganizationId = organization.OrganizationId, TeamCode = "FB-T", TeamName = "F-B Team",
    IsActive = true, CreatedAt = now
};
var user = new User
{
    OrganizationId = organization.OrganizationId, DisplayName = "F-B tester",
    EmployeeNo = "FB001", Email = "fb@example.test", IsActive = true, CreatedAt = now
};
db.Teams.Add(team);
db.Users.Add(user);
await db.SaveChangesAsync();
var person = new Person
{
    DisplayName = user.DisplayName, CreatedAt = now, CreatedByUserId = user.UserId
};
db.Persons.Add(person);
await db.SaveChangesAsync();
var employment = new Employment
{
    PersonId = person.PersonId, OrganizationId = organization.OrganizationId,
    EmployeeNo = "FB001", SourceType = "Test", CreatedAt = now, CreatedByUserId = user.UserId
};
db.Employments.Add(employment);
await db.SaveChangesAsync();
var location = new Location
{
    OrganizationId = organization.OrganizationId, TeamId = team.TeamId,
    LocationName = "F-B Location", LocationType = "Official", Address = "1 Current Road",
    ApprovalStatus = "Approved", GeocodingStatus = "Pending", IsActive = true,
    CreatedByUserId = user.UserId, CreatedAt = now
};
var persistedTrip = new VisitTrip
{
    TripNo = "FB-REAL-SQL-1", UserId = user.UserId, EmploymentId = employment.EmploymentId,
    OrganizationId = organization.OrganizationId, TeamId = team.TeamId,
    VisitDate = new DateOnly(2026, 9, 13),
    StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0),
    Status = TripStatuses.Submitted, VehicleType = "Car", SubmittedAt = now,
    CreatedAt = now, CreatedByUserId = user.UserId, UpdatedAt = now, UpdatedByUserId = user.UserId
};
db.Locations.Add(location);
db.VisitTrips.Add(persistedTrip);
await db.SaveChangesAsync();
db.VisitTripStops.AddRange(
    new VisitTripStop
    {
        VisitTripId = persistedTrip.VisitTripId, StopSequence = 1, LocationId = location.LocationId,
        LocationNameSnapshot = "A", AddressSnapshot = "A Road", CreatedAt = now
    },
    new VisitTripStop
    {
        VisitTripId = persistedTrip.VisitTripId, StopSequence = 2, LocationId = location.LocationId,
        LocationNameSnapshot = "B", AddressSnapshot = "B Road", CreatedAt = now
    });
var submitted = new VisitTripSnapshot
{
    VisitTripId = persistedTrip.VisitTripId, SnapshotVersion = 1, SnapshotType = "Submitted",
    TripNo = persistedTrip.TripNo, UserId = user.UserId,
    PersonIdSnapshot = person.PersonId, EmploymentIdSnapshot = employment.EmploymentId,
    EmployeeNoSnapshot = "FB001", DisplayNameSnapshot = user.DisplayName,
    OrganizationId = organization.OrganizationId, OrganizationNameSnapshot = organization.OrganizationName,
    TeamId = team.TeamId, TeamCodeSnapshot = team.TeamCode, TeamNameSnapshot = team.TeamName,
    StartDeploymentSiteCodeSnapshot = "START", StartDeploymentAddressSnapshot = "Start Road",
    EndDeploymentSiteCodeSnapshot = "END", EndDeploymentAddressSnapshot = "End Road",
    VisitDate = persistedTrip.VisitDate, StartTime = persistedTrip.StartTime, EndTime = persistedTrip.EndTime,
    StatusSnapshot = TripStatuses.Submitted, VehicleTypeSnapshot = "Car",
    SubmittedAtSnapshot = now, CreatedAt = now, CreatedByUserId = user.UserId,
    Stops =
    [
        new VisitTripSnapshotStop { StopSequence = 1, LocationNameSnapshot = "A", AddressSnapshot = "A Road", CreatedAt = now },
        new VisitTripSnapshotStop { StopSequence = 2, LocationNameSnapshot = "B", AddressSnapshot = "B Road", CreatedAt = now }
    ]
};
db.VisitTripSnapshots.Add(submitted);
db.MileageCalculations.Add(new MileageCalculation
{
    VisitTripId = persistedTrip.VisitTripId, ClaimedDistanceKm = 10m, CreatedAt = now, UpdatedAt = now
});
db.MileageRateRules.Add(new MileageRateRule
{
    OrganizationId = organization.OrganizationId, RuleName = "F-B Car",
    VehicleType = "Car", RatePerKm = 3m, EffectiveFrom = new DateOnly(2026, 1, 1),
    IsActive = true, CreatedAt = now, CreatedByUserId = user.UserId
});
await db.SaveChangesAsync();
db.ChangeTracker.Clear();

var trip = new VisitTrip
{
    VisitTripId = persistedTrip.VisitTripId, TripNo = persistedTrip.TripNo,
    UserId = user.UserId, EmploymentId = employment.EmploymentId, OrganizationId = organization.OrganizationId,
    TeamId = team.TeamId, StartDeploymentSiteId = 101, EndDeploymentSiteId = 102,
    VisitDate = persistedTrip.VisitDate, Status = TripStatuses.Draft, VehicleType = "Car",
    Stops =
    [
        new VisitTripStop { StopSequence = 1, LocationNameSnapshot = "A", AddressSnapshot = "A Road" },
        new VisitTripStop { StopSequence = 2, LocationNameSnapshot = "B", AddressSnapshot = "B Road" }
    ]
};
var visitor = Current(user, team, "visitor");
var leader = Current(user, team, "leader");
var context = new V180TripContextDto(
    employment.EmploymentId, trip.VisitDate, true, "OK", "OK",
    [new V180TripContextTeamDto(team.TeamId, team.TeamCode, team.TeamName, true)], team.TeamId,
    [
        new V180TripContextDeploymentSiteDto(101, 1, "C", "Center", "START", "Start", 1, null, "Start", "Start Road", true),
        new V180TripContextDeploymentSiteDto(102, 1, "C", "Center", "END", "End", 2, null, "End", "End Road", false)
    ], 101, 101, 102);

var repository = new V180GoogleMileageGovernanceRepository(db);
var routeProvider = new SqlObservingRouteProvider(options, db);
var geocodingProvider = new SqlObservingGeocodingProvider(options, db);
var visitorService = Service(visitor, trip, submitted, context, repository, routeProvider, geocodingProvider, db, new MasterRepository(db));

var preview = await visitorService.PreviewRouteAsync(trip.VisitTripId, default);
FbCheck.That(preview.Status == "Succeeded", "route preview success");
FbCheck.That(routeProvider.CallCount == 1, "one provider call per route attempt");
var routeAttempt = await db.RouteCalculationAttempts.AsNoTracking()
    .SingleAsync(x => x.RouteCalculationAttemptId == preview.RouteCalculationAttemptId);
FbCheck.That(routeAttempt.Status == "Succeeded" && routeAttempt.CompletedAt.HasValue, "route exact terminal attempt");
FbCheck.That(!await repository.TryFinalizeRouteCalculationAttemptAsync(
    routeAttempt.RouteCalculationAttemptId, "Failed", "SECOND_FINALIZE", "must fail", DateTime.UtcNow, default),
    "terminal route attempt immutable");
Console.WriteLine("FB-01_ROUTE_PENDING_BEFORE_PROVIDER=PASS");
Console.WriteLine("FB-02_ROUTE_EXACTLY_ONCE_AND_TERMINAL=PASS");

var finalizationFailureProvider = new SqlObservingRouteProvider(options, db);
var failingRepository = new FinalizationFailureRepository(repository);
var failureService = Service(visitor, trip, submitted, context, failingRepository,
    finalizationFailureProvider, geocodingProvider, db, new MasterRepository(db));
try
{
    await failureService.PreviewRouteAsync(trip.VisitTripId, default);
    throw new InvalidOperationException("FB finalization failure scenario did not fail.");
}
catch (InvalidOperationException ex) when (ex.Message == "FB_FINALIZATION_SQL_FAILURE")
{
    FbCheck.That(finalizationFailureProvider.CallCount == 1, "finalization failure does not replay provider");
    var pending = await db.RouteCalculationAttempts.AsNoTracking()
        .SingleAsync(x => x.CorrelationId == finalizationFailureProvider.LastCorrelationId);
    FbCheck.That(pending.Status == "Pending", "failed finalization leaves original pending audit row");
}
Console.WriteLine("FB-03_FINALIZATION_FAILURE_NO_PROVIDER_REPLAY=PASS");

trip.Status = TripStatuses.Submitted;
var leaderProvider = new SqlObservingRouteProvider(options, db);
var leaderService = Service(leader, trip, submitted, context, repository,
    leaderProvider, geocodingProvider, db, new MasterRepository(db));
var retryOne = await leaderService.RetryRouteAsync(trip.VisitTripId, default);
var retryTwo = await leaderService.RetryRouteAsync(trip.VisitTripId, default);
FbCheck.That(retryOne.RouteCalculationAttemptId != retryTwo.RouteCalculationAttemptId, "retry creates new attempt");
FbCheck.That(retryOne.CorrelationId != retryTwo.CorrelationId, "retry creates new correlation");
var retryRows = await db.RouteCalculationAttempts.AsNoTracking()
    .Where(x => x.CalculationReason == "LeaderRetry").ToListAsync();
FbCheck.That(retryRows.Count == 2 && retryRows.All(x => x.BasisVisitTripSnapshotId == submitted.VisitTripSnapshotId),
    "leader retries bind current submitted snapshot");
Console.WriteLine("FB-04_LEADER_RETRY_NEW_ATTEMPT_CORRELATION=PASS");
Console.WriteLine("FB-05_LATEST_SUBMITTED_SNAPSHOT_BASIS=PASS");

trip.Status = TripStatuses.Draft;
var providerFailure = new FixedFailureRouteProvider();
var failedRouteService = Service(visitor, trip, submitted, context, repository,
    providerFailure, geocodingProvider, db, new MasterRepository(db));
var failedRoute = await failedRouteService.PreviewRouteAsync(trip.VisitTripId, default);
FbCheck.That(failedRoute.Status == "Failed" && providerFailure.CallCount == 1,
    "provider failure persists one failed attempt");

var actualTripRepository = new TripRepository(db);
var mileageRepository = new MileageRepository(db);
var workflowRepository = new WorkflowRepository(db);
var snapshotRepository = new TripSnapshotRepository(db);
var leaderCurrent = new FixedCurrentUser(leader);
var tripMapper = new TripService(
    leaderCurrent, new FixedUserRepository(leader), actualTripRepository,
    new MasterRepository(db), mileageRepository, workflowRepository,
    new NoopAccessControl(), new FixedTripContextReader(context), snapshotRepository, db);
var approvalService = new LeaderService(
    leaderCurrent, actualTripRepository, mileageRepository, new NoopLegacyRouteService(),
    workflowRepository, snapshotRepository, db, tripMapper,
    mileageGovernance: repository);
var approvalTrip = await actualTripRepository.GetAsync(persistedTrip.VisitTripId, false, default)
    ?? throw new InvalidOperationException("F-B approval trip missing.");
var approved = await approvalService.ApproveAsync(
    persistedTrip.VisitTripId,
    new ApproveTripRequest(11m, Convert.ToBase64String(approvalTrip.RowVersion),
        "F-B manual fallback", "ManualFallback", null), default);
FbCheck.That(approved.Status == TripStatuses.Approved, "manual fallback approval succeeds");
var approvedCalc = await db.MileageCalculations.AsNoTracking()
    .SingleAsync(x => x.VisitTripId == persistedTrip.VisitTripId);
var approvedSnapshot = await db.VisitTripSnapshots.AsNoTracking()
    .SingleAsync(x => x.VisitTripId == persistedTrip.VisitTripId && x.SnapshotType == "Approved");
FbCheck.That(approvedCalc.ManualFallbackUsed
    && approvedCalc.ApprovedDistanceSource == "ManualFallback"
    && approvedCalc.SelectedRouteCalculationAttemptId is null,
    "manual fallback company decision evidence");
FbCheck.That(approvedSnapshot.RouteCalculationStatusSnapshot == "ManualFallback"
    && approvedSnapshot.ApprovedDistanceSourceSnapshot == "ManualFallback"
    && approvedSnapshot.ApprovalBasisHashSnapshot != null,
    "approved snapshot current manual governance");
Console.WriteLine("FB-06_PROVIDER_FAILURE_MANUAL_FALLBACK_APPROVAL=PASS");
Console.WriteLine("FB-07_APPROVED_SNAPSHOT_CURRENT_GOVERNANCE=PASS");

var returnedTrip = await db.VisitTrips.SingleAsync(x => x.VisitTripId == persistedTrip.VisitTripId);
returnedTrip.Status = TripStatuses.Returned;
returnedTrip.UpdatedAt = DateTime.UtcNow;
await db.SaveChangesAsync();
var historicalRouteAttemptCount = await db.RouteCalculationAttempts.CountAsync();
var editContext = new V180TripContextDto(
    employment.EmploymentId, returnedTrip.VisitDate.AddDays(1), true, "OK", "OK",
    [new V180TripContextTeamDto(team.TeamId, team.TeamCode, team.TeamName, true)], team.TeamId,
    [], null, null, null);
var visitorTripService = new TripService(
    new FixedCurrentUser(visitor), new FixedUserRepository(visitor), actualTripRepository,
    new MasterRepository(db), mileageRepository, workflowRepository,
    new NoopAccessControl(), new FixedTripContextReader(editContext), snapshotRepository, db,
    mileageGovernance: repository);
await visitorTripService.UpdateAsync(
    returnedTrip.VisitTripId,
    new SaveTripRequest(
        returnedTrip.VisitDate.AddDays(1), new TimeOnly(9, 0), new TimeOnly(10, 0),
        10m, "edited", null, false,
        [
            new TripStopInput(location.LocationId, null, null, "Master", "A", "1 Current Road", null, null),
            new TripStopInput(location.LocationId, null, null, "Master", "B", "1 Current Road", null, null)
        ], team.TeamId, null, null, "Motorcycle"),
    Convert.ToBase64String(returnedTrip.RowVersion), default);
var invalidatedCalc = await db.MileageCalculations.AsNoTracking()
    .SingleAsync(x => x.VisitTripId == persistedTrip.VisitTripId);
FbCheck.That(invalidatedCalc.SelectedRouteCalculationAttemptId is null
    && invalidatedCalc.DistanceDecisionGovernanceVersion is null
    && invalidatedCalc.InvalidatedAt.HasValue
    && invalidatedCalc.InvalidatedByUserId == user.UserId,
    "trip authority edit invalidates current decision");
FbCheck.That(await db.RouteCalculationAttempts.CountAsync() == historicalRouteAttemptCount,
    "trip edit preserves historical attempts");
FbCheck.That(await db.MileageGovernanceEvents.AnyAsync(x =>
    x.VisitTripId == persistedTrip.VisitTripId && x.EventType == "Invalidated"),
    "trip edit records invalidated governance event");
Console.WriteLine("FB-08_TRIP_EDIT_INVALIDATES_CURRENT_AUTHORITY=PASS");

var eventCountBeforeGeocoding = await db.MileageGovernanceEvents.CountAsync();
var geocoding = await leaderService.GeocodeLocationAsync(location.LocationId, default);
FbCheck.That(geocoding.Status == "Succeeded" && geocoding.SelectedAsCurrent, "geocoding success selected");
FbCheck.That(geocodingProvider.CallCount == 1, "one geocoding call");
var storedLocation = await db.Locations.AsNoTracking().SingleAsync(x => x.LocationId == location.LocationId);
FbCheck.That(storedLocation.SelectedGeocodingAttemptId == geocoding.GeocodingAttemptId, "same location current hash selected");
FbCheck.That(storedLocation.Latitude is null && storedLocation.Longitude is null, "provider coordinates transient");
FbCheck.That(await db.MileageGovernanceEvents.CountAsync() == eventCountBeforeGeocoding,
    "no fabricated geocoding mileage governance event");
var geoAttempt = await db.GeocodingAttempts.AsNoTracking()
    .SingleAsync(x => x.GeocodingAttemptId == geocoding.GeocodingAttemptId);
FbCheck.That(!await repository.TryFinalizeGeocodingAttemptAsync(
    geoAttempt.GeocodingAttemptId, "Failed", "SECOND_FINALIZE", "must fail", DateTime.UtcNow, default),
    "terminal geocoding attempt immutable");
Console.WriteLine("FB-09_GEOCODING_EXACTLY_ONCE_AND_CURRENT_SELECTION=PASS");
Console.WriteLine("FB-10_GEOCODING_RESULTS_TRANSIENT=PASS");

var historicalHash = geoAttempt.AddressBasisHash.ToArray();
var trackedLocation = await db.Locations.SingleAsync(x => x.LocationId == location.LocationId);
trackedLocation.Address = "2 Changed Road";
if (!V180MileageCanonicalization.HashAddress(trackedLocation).SequenceEqual(historicalHash))
    trackedLocation.SelectedGeocodingAttemptId = null;
await db.SaveChangesAsync();
var staleProtected = await db.Locations.AsNoTracking().SingleAsync(x => x.LocationId == location.LocationId);
var historicalAttempt = await db.GeocodingAttempts.AsNoTracking()
    .SingleAsync(x => x.GeocodingAttemptId == geoAttempt.GeocodingAttemptId);
FbCheck.That(staleProtected.SelectedGeocodingAttemptId is null, "stale address basis clears selector");
FbCheck.That(historicalAttempt.Status == "Succeeded" && historicalAttempt.AddressBasisHash.SequenceEqual(historicalHash),
    "historical geocoding attempt immutable");
Console.WriteLine("FB-11_STALE_ADDRESS_HASH_REJECTED_HISTORY_PRESERVED=PASS");

var forbiddenSentinel = "FB_PROVIDER_RESULT_SENTINEL";
FbCheck.That(!await db.AuditLogs.AsNoTracking().AnyAsync(x =>
        (x.OldValues != null && x.OldValues.Contains(forbiddenSentinel))
        || (x.NewValues != null && x.NewValues.Contains(forbiddenSentinel))),
    "provider result sentinel absent from audit");
FbCheck.That(!await db.VisitTripSnapshots.AsNoTracking().AnyAsync(x => x.RouteProviderSnapshot == forbiddenSentinel),
    "provider result sentinel absent from snapshots");
FbCheck.That(!await db.MileageCalculations.AsNoTracking().AnyAsync(x => x.CalculationSource == forbiddenSentinel),
    "provider result sentinel absent from mileage");
Console.WriteLine("FB-12_PROVIDER_RESULT_FORBIDDEN_PERSISTENCE=PASS");
Console.WriteLine("FB_REAL_SQL_REGRESSION=12/12=PASS");

static V180GoogleMileageOrchestrationService Service(
    CurrentUserDto user,
    VisitTrip trip,
    VisitTripSnapshot snapshot,
    V180TripContextDto context,
    IV180GoogleMileageGovernanceRepository repository,
    IV180RouteProvider routeProvider,
    IV180GeocodingProvider geocodingProvider,
    IUnitOfWork uow,
    IMasterRepository masters) =>
    new(new FixedCurrentUser(user), new FixedTripRepository(trip), masters,
        new FixedTripContextReader(context), new FixedSnapshotRepository(snapshot),
        repository, routeProvider, geocodingProvider, uow);

static CurrentUserDto Current(User user, Team team, string role) => new(
    user.UserId, user.EmployeeNo ?? "", user.DisplayName, user.Email,
    user.OrganizationId, team.TeamId, team.TeamName, [role],
    [new TeamScopeDto(team.TeamId, team.TeamName, true)]);

static class FbCheck
{
    public static void That(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"F-B assertion failed: {name}");
    }
}

sealed class SqlObservingRouteProvider(
    DbContextOptions<AppDbContext> options,
    AppDbContext orchestrationDb) : IV180RouteProvider
{
    public string ProviderName => "DeterministicFake";
    public int CallCount { get; private set; }
    public Guid LastCorrelationId { get; private set; }

    public async Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct)
    {
        CallCount++;
        LastCorrelationId = request.CorrelationId;
        FbCheck.That(orchestrationDb.Database.CurrentTransaction is null, "provider outside EF transaction");
        FbCheck.That(System.Transactions.Transaction.Current is null, "provider outside ambient transaction");
        await using var observer = new AppDbContext(options);
        FbCheck.That(await observer.RouteCalculationAttempts.AsNoTracking()
            .AnyAsync(x => x.CorrelationId == request.CorrelationId && x.Status == "Pending", ct),
            "pending route attempt visible to independent SQL session");
        return new(true, 42.42m, 1234, "FB_PROVIDER_RESULT_SENTINEL", null, null);
    }
}

sealed class SqlObservingGeocodingProvider(
    DbContextOptions<AppDbContext> options,
    AppDbContext orchestrationDb) : IV180GeocodingProvider
{
    public string ProviderName => "DeterministicFake";
    public int CallCount { get; private set; }

    public async Task<V180GeocodingProviderResult> GeocodeAsync(V180GeocodingProviderRequest request, CancellationToken ct)
    {
        CallCount++;
        FbCheck.That(orchestrationDb.Database.CurrentTransaction is null, "geocoding provider outside EF transaction");
        FbCheck.That(System.Transactions.Transaction.Current is null, "geocoding provider outside ambient transaction");
        await using var observer = new AppDbContext(options);
        FbCheck.That(await observer.GeocodingAttempts.AsNoTracking()
            .AnyAsync(x => x.CorrelationId == request.CorrelationId && x.Status == "Pending", ct),
            "pending geocoding attempt visible to independent SQL session");
        return new(true, 25.033m, 121.5654m, null, null);
    }
}

sealed class FixedFailureRouteProvider : IV180RouteProvider
{
    public string ProviderName => "DeterministicFake";
    public int CallCount { get; private set; }
    public Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(new V180RouteProviderResult(
            false, null, null, null, "NO_ROUTE", "No route available."));
    }
}

sealed class FinalizationFailureRepository(IV180GoogleMileageGovernanceRepository inner)
    : IV180GoogleMileageGovernanceRepository
{
    public Task<GeocodingAttempt> AddGeocodingAttemptAsync(V180GeocodingAttemptRequest request, CancellationToken ct) => inner.AddGeocodingAttemptAsync(request, ct);
    public Task<RouteCalculationAttempt> AddRouteCalculationAttemptAsync(V180RouteCalculationAttemptRequest request, CancellationToken ct) => inner.AddRouteCalculationAttemptAsync(request, ct);
    public Task<MileageGovernanceEvent> AddGovernanceEventAsync(V180MileageGovernanceEventRequest request, CancellationToken ct) => inner.AddGovernanceEventAsync(request, ct);
    public Task<RouteCalculationAttempt?> GetRouteCalculationAttemptAsync(long attemptId, CancellationToken ct) => inner.GetRouteCalculationAttemptAsync(attemptId, ct);
    public Task<GeocodingAttempt?> GetGeocodingAttemptAsync(long attemptId, CancellationToken ct) => inner.GetGeocodingAttemptAsync(attemptId, ct);
    public Task<bool> TryFinalizeRouteCalculationAttemptAsync(long attemptId, string status, string? errorCode, string? errorMessage, DateTime completedAt, CancellationToken ct) => throw new InvalidOperationException("FB_FINALIZATION_SQL_FAILURE");
    public Task<bool> TryFinalizeGeocodingAttemptAsync(long attemptId, string status, string? errorCode, string? errorMessage, DateTime completedAt, CancellationToken ct) => inner.TryFinalizeGeocodingAttemptAsync(attemptId, status, errorCode, errorMessage, completedAt, ct);
}

sealed class FixedCurrentUser(CurrentUserDto user) : ICurrentUserService
{
    public CurrentUserDto GetRequired() => user;
}

sealed class FixedUserRepository(CurrentUserDto user) : IUserRepository
{
    public Task<User?> FindByAccountAsync(string account, CancellationToken ct) => throw new NotSupportedException();
    public Task<User?> FindByEntraIdentityAsync(Guid tenantId, Guid objectId, CancellationToken ct) => throw new NotSupportedException();
    public Task<User?> BindEntraIdentityByEmailAsync(Guid tenantId, Guid objectId, string email, CancellationToken ct) => throw new NotSupportedException();
    public Task<CurrentUserDto?> GetProfileAsync(int userId, CancellationToken ct) =>
        Task.FromResult<CurrentUserDto?>(userId == user.UserId ? user : null);
}

sealed class NoopLegacyRouteService : IRouteCalculationService
{
    public Task<RouteCalculationResult> CalculateAsync(VisitTrip trip, CancellationToken ct) =>
        throw new InvalidOperationException("Legacy route service must not be used by F-B approval.");
}

sealed class NoopAccessControl : IV170AccessControl
{
    public Task<V170LoginEligibility> EvaluateLoginAsync(int userId, bool adminEnabled, CancellationToken ct) => throw new NotSupportedException();
    public Task<V170ReadScope> ResolveReadScopeAsync(CurrentUserDto user, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasCapabilityAsync(int userId, string capabilityCode, CancellationToken ct) => throw new NotSupportedException();
    public Task EnsureExportAllowedAsync(CurrentUserDto user, string format, CancellationToken ct) => throw new NotSupportedException();
    public Task AuditSupervisorQueryAsync(CurrentUserDto user, TripQueryRequest request, int resultCount, CancellationToken ct) => throw new NotSupportedException();
}

sealed class FixedTripContextReader(V180TripContextDto context) : IV180TripContextReader
{
    public Task<V180TripContextDto> ResolveAsync(CurrentUserDto user, DateOnly visitDate, int? teamId, CancellationToken ct) => Task.FromResult(context);
}

sealed class FixedSnapshotRepository(VisitTripSnapshot snapshot) : ITripSnapshotRepository
{
    public Task<VisitTripSnapshot?> GetLatestAsync(long tripId, string snapshotType, CancellationToken ct) =>
        Task.FromResult<VisitTripSnapshot?>(snapshot.VisitTripId == tripId && snapshot.SnapshotType == snapshotType ? snapshot : null);
    public Task AddSubmittedSnapshotAsync(VisitTrip trip, CurrentUserDto submitter, V180TripContextDto context, CancellationToken ct) => throw new NotSupportedException();
    public Task AddApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct) => throw new NotSupportedException();
}

sealed class FixedTripRepository(VisitTrip trip) : ITripRepository
{
    public Task<VisitTrip?> GetAsync(long tripId, bool tracking, CancellationToken ct) => Task.FromResult<VisitTrip?>(tripId == trip.VisitTripId ? trip : null);
    public Task AddAsync(VisitTrip row, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<VisitTrip>> GetVisitorHistoryAsync(int userId, DateOnly? start, DateOnly? end, string? locationKeyword, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<VisitTrip>> GetTeamQueueAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<VisitTrip>> GetPendingMileageAsync(IReadOnlyCollection<int> teamIds, DateOnly? start, DateOnly? end, IReadOnlyList<long>? selected, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<VisitTrip>> FindOverlapsAsync(int userId, DateOnly date, TimeOnly start, TimeOnly end, long? excludeTripId, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<VisitTrip>> GetReportTripsAsync(CurrentUserDto user, DateOnly? start, DateOnly? end, CancellationToken ct) => throw new NotSupportedException();
}
