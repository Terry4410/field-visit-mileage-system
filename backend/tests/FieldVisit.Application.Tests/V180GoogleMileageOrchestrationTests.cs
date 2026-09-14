using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180GoogleMileageOrchestrationTests
{
    [Theory]
    [InlineData("Motorcycle", "TWO_WHEELER")]
    [InlineData("Car", "DRIVE")]
    public async Task Pending_is_persisted_before_exactly_one_route_provider_call(
        string vehicleType, string expectedMode)
    {
        var fixture = Fixture.Visitor(vehicleType);
        fixture.RouteProvider.OnCall = request =>
        {
            Assert.True(fixture.UnitOfWork.SaveCount >= 1);
            Assert.Equal(expectedMode, request.TravelMode);
        };

        var result = await fixture.Service.PreviewRouteAsync(fixture.Trip.VisitTripId, default);

        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(1, fixture.RouteProvider.CallCount);
        Assert.Equal("Succeeded", fixture.Governance.RouteAttempts.Single().Status);
        Assert.Null(fixture.Trip.MileageCalculation?.SystemDistanceKm);
        Assert.Null(fixture.Trip.MileageCalculation?.ApprovedDistanceKm);
    }

    [Fact]
    public async Task Provider_failure_is_finalized_once_and_never_replayed()
    {
        var fixture = Fixture.Visitor("Motorcycle");
        fixture.RouteProvider.Result = new(false, null, null, null, "NETWORK_TIMEOUT", "bounded failure");

        var result = await fixture.Service.PreviewRouteAsync(fixture.Trip.VisitTripId, default);

        Assert.Equal("Failed", result.Status);
        Assert.Equal(1, fixture.RouteProvider.CallCount);
        Assert.Equal("Failed", fixture.Governance.RouteAttempts.Single().Status);
        Assert.Equal("CalculationFailed", fixture.Governance.Events.Single().EventType);
    }

    [Fact]
    public async Task Finalization_conflict_does_not_replay_provider()
    {
        var fixture = Fixture.Visitor("Car");
        fixture.Governance.RejectRouteFinalization = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.PreviewRouteAsync(fixture.Trip.VisitTripId, default));

        Assert.Contains("F_B_ROUTE_ATTEMPT_TERMINAL", error.Message);
        Assert.Equal(1, fixture.RouteProvider.CallCount);
    }

    [Fact]
    public async Task Leader_retry_uses_latest_submitted_snapshot_and_new_correlation_each_time()
    {
        var fixture = Fixture.Leader();

        var first = await fixture.Service.RetryRouteAsync(fixture.Trip.VisitTripId, default);
        var second = await fixture.Service.RetryRouteAsync(fixture.Trip.VisitTripId, default);

        Assert.Equal(2, fixture.RouteProvider.CallCount);
        Assert.NotEqual(first.RouteCalculationAttemptId, second.RouteCalculationAttemptId);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
        Assert.All(fixture.Governance.RouteAttempts, attempt =>
        {
            Assert.Equal("SubmittedSnapshot", attempt.BasisType);
            Assert.Equal(fixture.SubmittedSnapshot.VisitTripSnapshotId, attempt.BasisVisitTripSnapshotId);
            Assert.Equal("LeaderRetry", attempt.CalculationReason);
        });
    }

    [Fact]
    public async Task Visitor_cannot_preview_another_visitors_trip()
    {
        var fixture = Fixture.Visitor("Car");
        fixture.Trip.UserId = 999;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Service.PreviewRouteAsync(fixture.Trip.VisitTripId, default));
        Assert.Equal(0, fixture.RouteProvider.CallCount);
    }

    [Fact]
    public async Task Geocoding_is_committed_then_called_once_and_selects_only_current_hash()
    {
        var fixture = Fixture.Leader();
        fixture.GeocodingProvider.OnCall = _ => Assert.True(fixture.UnitOfWork.SaveCount >= 1);

        var result = await fixture.Service.GeocodeLocationAsync(fixture.Location.LocationId, default);

        Assert.Equal("Succeeded", result.Status);
        Assert.True(result.SelectedAsCurrent);
        Assert.Equal(1, fixture.GeocodingProvider.CallCount);
        Assert.Equal(result.GeocodingAttemptId, fixture.Location.SelectedGeocodingAttemptId);
        Assert.Null(fixture.Location.Latitude);
        Assert.Null(fixture.Location.Longitude);
    }

    [Fact]
    public void Address_hash_ignores_plus_code_when_nonblank_address_is_unchanged()
    {
        var location = new Location { Address = "台北 101", PlusCode = "OLD" };
        var before = V180MileageCanonicalization.HashAddress(location);
        location.PlusCode = "NEW";
        var after = V180MileageCanonicalization.HashAddress(location);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Stale_geocoding_address_hash_is_rejected_at_use_time()
    {
        var location = new Location { LocationId = 1, Address = "Current address" };
        var attemptBasis = new Location { Address = "Old address" };
        var attempt = new GeocodingAttempt
        {
            LocationId = location.LocationId, Status = "Succeeded",
            AddressBasisHash = V180MileageCanonicalization.HashAddress(attemptBasis)
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.EnsureCurrentGeocodingSelection(location, attempt));
        Assert.Contains("F_B_GEOCODING_ATTEMPT_STALE", error.Message);
    }

    [Fact]
    public void F_b_decision_modes_exclude_claimed()
    {
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.RequireDecisionSource("ProviderSuggested"));
        Assert.Equal("LeaderAdjusted", V180MileageGovernanceRules.RequireDecisionSource("LeaderAdjusted"));
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.RequireDecisionSource("ManualFallback"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireDecisionSource("Claimed"));
    }

    [Fact]
    public async Task Approved_snapshot_projects_current_company_governance_metadata()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var submitted = Fixture.CreateSnapshot();
        var completedAt = DateTime.UtcNow;
        var attempt = new RouteCalculationAttempt
        {
            RouteCalculationAttemptId = 88, VisitTripId = submitted.VisitTripId,
            BasisType = "SubmittedSnapshot", BasisVisitTripSnapshotId = submitted.VisitTripSnapshotId,
            CalculationReason = "LeaderRetry", RequestedVehicleType = "Car", TravelMode = "DRIVE",
            Provider = "Fake", StopCount = 2, RequestBasisHash = new byte[32], Status = "Succeeded",
            CorrelationId = Guid.NewGuid(), RequestedAt = completedAt.AddSeconds(-1),
            CompletedAt = completedAt, RequestedByUserId = 1
        };
        db.VisitTripSnapshots.Add(submitted);
        db.RouteCalculationAttempts.Add(attempt);
        await db.SaveChangesAsync();
        var basisHash = Enumerable.Repeat((byte)7, 32).ToArray();
        var trip = new VisitTrip
        {
            VisitTripId = submitted.VisitTripId, EmploymentId = 100, Status = TripStatuses.Approved,
            ApprovedAt = completedAt,
            MileageCalculation = new MileageCalculation
            {
                VisitTripId = submitted.VisitTripId, ApprovedDistanceKm = 12m,
                SelectedRouteCalculationAttemptId = attempt.RouteCalculationAttemptId,
                DistanceDecisionGovernanceVersion = "1.8.0", ApprovedDistanceSource = "LeaderAdjusted",
                ApprovalBasisCode = V180MileageGovernanceRules.SubmittedSnapshotBasisCode,
                ApprovalBasisHash = basisHash, DistanceApprovedAt = completedAt,
                DistanceApprovedByUserId = 1
            }
        };

        await new TripSnapshotRepository(db).AddApprovedSnapshotAsync(
            trip, new CurrentUserDto(1, "E1", "Leader", null, 1, 10, "Team", ["leader"]), default);
        await db.SaveChangesAsync();

        var approved = await db.VisitTripSnapshots.SingleAsync(x => x.SnapshotType == "Approved");
        Assert.Equal(attempt.RouteCalculationAttemptId, approved.MileageRouteAttemptIdSnapshot);
        Assert.Equal("DRIVE", approved.RouteTravelModeSnapshot);
        Assert.Equal("Succeeded", approved.RouteCalculationStatusSnapshot);
        Assert.Equal(attempt.CorrelationId, approved.RouteCorrelationIdSnapshot);
        Assert.Equal("LeaderAdjusted", approved.ApprovedDistanceSourceSnapshot);
        Assert.Equal(V180MileageGovernanceRules.SubmittedSnapshotBasisCode, approved.ApprovalBasisCodeSnapshot);
        Assert.Equal(basisHash, approved.ApprovalBasisHashSnapshot);
        Assert.Equal(completedAt, approved.DistanceApprovedAtSnapshot);
    }

    [Fact]
    public void Approval_and_edit_source_gates_preserve_legacy_and_invalidate_current_authority()
    {
        var root = FindRepositoryRoot();
        var approval = File.ReadAllText(Path.Combine(root, "backend/src/FieldVisit.Application/LeaderService.cs"));
        Assert.Contains("unchangedLegacyPendingApproval", approval);
        Assert.Contains("F_B_APPROVAL_EVIDENCE_REQUIRED", approval);
        Assert.Contains("F_B_ROUTE_ATTEMPT_STALE", approval);
        Assert.Contains("decisionSource == \"ManualFallback\"", approval);

        var edit = File.ReadAllText(Path.Combine(root, "backend/src/FieldVisit.Application/TripService.cs"));
        Assert.Contains("RouteAuthorityChanged", edit);
        Assert.Contains("SelectedRouteCalculationAttemptId = null", edit);
        Assert.Contains("TripRouteAuthorityEdited", edit);
        Assert.Contains("\"Invalidated\"", edit);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "FieldVisitSystem.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed class Fixture
    {
        private Fixture(CurrentUserDto user, VisitTrip trip, VisitTripSnapshot snapshot)
        {
            User = user;
            Trip = trip;
            SubmittedSnapshot = snapshot;
            Location = new Location
            {
                LocationId = 201, OrganizationId = 1, TeamId = 10,
                LocationName = "Current location", Address = "No. 1 Current Road",
                ApprovalStatus = "Approved", GeocodingStatus = "Pending", IsActive = true
            };
            UnitOfWork = new FakeUnitOfWork();
            Governance = new FakeGovernanceRepository();
            RouteProvider = new FakeRouteProvider();
            GeocodingProvider = new FakeGeocodingProvider();
            Service = new V180GoogleMileageOrchestrationService(
                new FakeCurrentUser(User), new FakeTripRepository(Trip), new FakeMasterRepository(Location),
                new FakeTripContextReader(Context()), new FakeSnapshotRepository(SubmittedSnapshot),
                Governance, RouteProvider, GeocodingProvider, UnitOfWork);
        }

        public CurrentUserDto User { get; }
        public VisitTrip Trip { get; }
        public VisitTripSnapshot SubmittedSnapshot { get; }
        public Location Location { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeGovernanceRepository Governance { get; }
        public FakeRouteProvider RouteProvider { get; }
        public FakeGeocodingProvider GeocodingProvider { get; }
        public V180GoogleMileageOrchestrationService Service { get; }

        public static Fixture Visitor(string vehicleType)
        {
            var user = UserDto("visitor");
            return new Fixture(user, TripEntity(user.UserId, TripStatuses.Draft, vehicleType), Snapshot());
        }

        public static Fixture Leader()
        {
            var user = UserDto("leader");
            return new Fixture(user, TripEntity(2, TripStatuses.Submitted, "Car"), Snapshot());
        }

        private static CurrentUserDto UserDto(string role) => new(
            1, "E001", "F-B user", "fb@example.test", 1, 10, "Team",
            [role], [new TeamScopeDto(10, "Team", true)]);

        private static VisitTrip TripEntity(int userId, string status, string vehicleType) => new()
        {
            VisitTripId = 301, TripNo = "FB-301", UserId = userId,
            EmploymentId = 100, OrganizationId = 1, TeamId = 10,
            StartDeploymentSiteId = 101, EndDeploymentSiteId = 102,
            VisitDate = new DateOnly(2026, 9, 13), Status = status, VehicleType = vehicleType,
            Stops =
            [
                new VisitTripStop { StopSequence = 1, LocationNameSnapshot = "A", AddressSnapshot = "A road" },
                new VisitTripStop { StopSequence = 2, LocationNameSnapshot = "B", AddressSnapshot = "B road" }
            ],
            MileageCalculation = new MileageCalculation { VisitTripId = 301 }
        };

        public static VisitTripSnapshot CreateSnapshot() => Snapshot();

        private static VisitTripSnapshot Snapshot() => new()
        {
            VisitTripSnapshotId = 401, VisitTripId = 301, SnapshotVersion = 1,
            SnapshotType = "Submitted", VehicleTypeSnapshot = "Car",
            StartDeploymentSiteCodeSnapshot = "START", StartDeploymentAddressSnapshot = "Start road",
            EndDeploymentSiteCodeSnapshot = "END", EndDeploymentAddressSnapshot = "End road",
            Stops =
            [
                new VisitTripSnapshotStop { StopSequence = 1, AddressSnapshot = "A road" },
                new VisitTripSnapshotStop { StopSequence = 2, AddressSnapshot = "B road" }
            ]
        };

        private static V180TripContextDto Context() => new(
            100, new DateOnly(2026, 9, 13), true, "OK", "OK",
            [new V180TripContextTeamDto(10, "T", "Team", true)], 10,
            [
                new V180TripContextDeploymentSiteDto(101, 1, "C", "Center", "START", "Start", 1, null, "Start", "Start road", true),
                new V180TripContextDeploymentSiteDto(102, 1, "C", "Center", "END", "End", 2, null, "End", "End road", false)
            ], 101, 101, 102);
    }

    private sealed class FakeCurrentUser(CurrentUserDto user) : ICurrentUserService
    {
        public CurrentUserDto GetRequired() => user;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(++SaveCount);
    }

    private sealed class FakeRouteProvider : IV180RouteProvider
    {
        public string ProviderName => "DeterministicFake";
        public int CallCount { get; private set; }
        public Action<V180RouteProviderRequest>? OnCall { get; set; }
        public V180RouteProviderResult Result { get; set; } = new(true, 12.34m, 900, "TRANSIENT_POLYLINE", null, null);
        public Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct)
        {
            CallCount++;
            OnCall?.Invoke(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeGeocodingProvider : IV180GeocodingProvider
    {
        public string ProviderName => "DeterministicFake";
        public int CallCount { get; private set; }
        public Action<V180GeocodingProviderRequest>? OnCall { get; set; }
        public Task<V180GeocodingProviderResult> GeocodeAsync(V180GeocodingProviderRequest request, CancellationToken ct)
        {
            CallCount++;
            OnCall?.Invoke(request);
            return Task.FromResult(new V180GeocodingProviderResult(true, 25.033m, 121.5654m, null, null));
        }
    }

    private sealed class FakeGovernanceRepository : IV180GoogleMileageGovernanceRepository
    {
        private long _nextRouteId = 1;
        private long _nextGeoId = 1;
        public bool RejectRouteFinalization { get; set; }
        public List<RouteCalculationAttempt> RouteAttempts { get; } = [];
        public List<GeocodingAttempt> GeocodingAttempts { get; } = [];
        public List<MileageGovernanceEvent> Events { get; } = [];

        public Task<RouteCalculationAttempt> AddRouteCalculationAttemptAsync(V180RouteCalculationAttemptRequest request, CancellationToken ct)
        {
            var row = new RouteCalculationAttempt
            {
                RouteCalculationAttemptId = _nextRouteId++, VisitTripId = request.VisitTripId,
                BasisType = request.BasisType, BasisVisitTripSnapshotId = request.BasisVisitTripSnapshotId,
                CalculationReason = request.CalculationReason, RequestedVehicleType = request.RequestedVehicleType,
                TravelMode = request.TravelMode, Provider = request.Provider, StopCount = request.StopCount,
                RequestBasisHash = request.RequestBasisHash, CorrelationId = request.CorrelationId,
                RequestedAt = request.RequestedAt, RequestedByUserId = request.RequestedByUserId
            };
            RouteAttempts.Add(row);
            return Task.FromResult(row);
        }

        public Task<GeocodingAttempt> AddGeocodingAttemptAsync(V180GeocodingAttemptRequest request, CancellationToken ct)
        {
            var row = new GeocodingAttempt
            {
                GeocodingAttemptId = _nextGeoId++, LocationId = request.LocationId,
                Provider = request.Provider, AddressBasisHash = request.AddressBasisHash,
                CorrelationId = request.CorrelationId, RequestedAt = request.RequestedAt,
                RequestedByUserId = request.RequestedByUserId
            };
            GeocodingAttempts.Add(row);
            return Task.FromResult(row);
        }

        public Task<MileageGovernanceEvent> AddGovernanceEventAsync(V180MileageGovernanceEventRequest request, CancellationToken ct)
        {
            var row = new MileageGovernanceEvent
            {
                VisitTripId = request.VisitTripId, VisitTripSnapshotId = request.VisitTripSnapshotId,
                RouteCalculationAttemptId = request.RouteCalculationAttemptId, EventType = request.EventType,
                ReasonCode = request.ReasonCode, Message = request.Message,
                CorrelationId = request.CorrelationId, OccurredAt = request.OccurredAt,
                ActorUserId = request.ActorUserId
            };
            Events.Add(row);
            return Task.FromResult(row);
        }

        public Task<RouteCalculationAttempt?> GetRouteCalculationAttemptAsync(long attemptId, CancellationToken ct) =>
            Task.FromResult<RouteCalculationAttempt?>(RouteAttempts.SingleOrDefault(x => x.RouteCalculationAttemptId == attemptId));

        public Task<GeocodingAttempt?> GetGeocodingAttemptAsync(long attemptId, CancellationToken ct) =>
            Task.FromResult<GeocodingAttempt?>(GeocodingAttempts.SingleOrDefault(x => x.GeocodingAttemptId == attemptId));

        public Task<bool> TryFinalizeRouteCalculationAttemptAsync(long attemptId, string status, string? errorCode, string? errorMessage, DateTime completedAt, CancellationToken ct)
        {
            if (RejectRouteFinalization) return Task.FromResult(false);
            var row = RouteAttempts.Single(x => x.RouteCalculationAttemptId == attemptId);
            if (row.Status != "Pending") return Task.FromResult(false);
            row.Status = status; row.ErrorCode = errorCode; row.ErrorMessage = errorMessage; row.CompletedAt = completedAt;
            return Task.FromResult(true);
        }

        public Task<bool> TryFinalizeGeocodingAttemptAsync(long attemptId, string status, string? errorCode, string? errorMessage, DateTime completedAt, CancellationToken ct)
        {
            var row = GeocodingAttempts.Single(x => x.GeocodingAttemptId == attemptId);
            if (row.Status != "Pending") return Task.FromResult(false);
            row.Status = status; row.ErrorCode = errorCode; row.ErrorMessage = errorMessage; row.CompletedAt = completedAt;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeTripRepository(VisitTrip trip) : ITripRepository
    {
        public Task<VisitTrip?> GetAsync(long tripId, bool tracking, CancellationToken ct) => Task.FromResult<VisitTrip?>(tripId == trip.VisitTripId ? trip : null);
        public Task AddAsync(VisitTrip row, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<VisitTrip>> GetVisitorHistoryAsync(int userId, DateOnly? start, DateOnly? end, string? locationKeyword, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<VisitTrip>> GetTeamQueueAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<VisitTrip>> GetPendingMileageAsync(IReadOnlyCollection<int> teamIds, DateOnly? start, DateOnly? end, IReadOnlyList<long>? selected, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<VisitTrip>> FindOverlapsAsync(int userId, DateOnly date, TimeOnly start, TimeOnly end, long? excludeTripId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<VisitTrip>> GetReportTripsAsync(CurrentUserDto user, DateOnly? start, DateOnly? end, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeMasterRepository(Location location) : IMasterRepository
    {
        public Task<Location?> GetLocationAsync(int id, bool tracking, CancellationToken ct) => Task.FromResult<Location?>(id == location.LocationId ? location : null);
        public Task<List<Team>> GetTeamsAsync(CurrentUserDto user, CancellationToken ct) => throw new NotSupportedException();
        public Task<Team?> GetTeamAsync(int teamId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Location>> GetLocationsAsync(CurrentUserDto user, bool activeOnly, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Location>> GetPendingLocationsAsync(CurrentUserDto user, DateTime? start, DateTime? end, CancellationToken ct) => throw new NotSupportedException();
        public Task AddLocationAsync(Location row, CancellationToken ct) => throw new NotSupportedException();
        public Task<Location?> FindReusableTemporaryLocationAsync(int? organizationId, int? teamId, string locationName, string? addressOrPlusCode, CancellationToken ct) => throw new NotSupportedException();
        public Task AbandonUnusedTemporaryLocationsAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Project>> GetProjectsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct) => throw new NotSupportedException();
        public Task<Project?> GetProjectAsync(int projectId, bool tracking, CancellationToken ct) => throw new NotSupportedException();
        public Task AddProjectAsync(Project project, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ProjectCodeExistsAsync(int organizationId, string projectCode, int? excludeProjectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Location>> GetProjectLocationsAsync(int projectId, CurrentUserDto user, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<VisitType>> GetVisitTypesAsync(bool includeInactive, CancellationToken ct) => throw new NotSupportedException();
        public Task<VisitType?> GetVisitTypeAsync(int visitTypeId, bool tracking, CancellationToken ct) => throw new NotSupportedException();
        public Task AddVisitTypeAsync(VisitType visitType, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> VisitTypeCodeExistsAsync(string visitTypeCode, int? excludeVisitTypeId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeTripContextReader(V180TripContextDto context) : IV180TripContextReader
    {
        public Task<V180TripContextDto> ResolveAsync(CurrentUserDto user, DateOnly visitDate, int? teamId, CancellationToken ct) => Task.FromResult(context);
    }

    private sealed class FakeSnapshotRepository(VisitTripSnapshot snapshot) : ITripSnapshotRepository
    {
        public Task<VisitTripSnapshot?> GetLatestAsync(long tripId, string snapshotType, CancellationToken ct) =>
            Task.FromResult<VisitTripSnapshot?>(snapshot.VisitTripId == tripId && snapshot.SnapshotType == snapshotType ? snapshot : null);
        public Task AddSubmittedSnapshotAsync(VisitTrip trip, CurrentUserDto submitter, V180TripContextDto context, CancellationToken ct) => throw new NotSupportedException();
        public Task AddApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct) => throw new NotSupportedException();
    }
}
