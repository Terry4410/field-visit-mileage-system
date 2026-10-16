using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Disposable SQL + real ASP.NET HTTP coverage for the normal Trip path.
/// This intentionally uses the full EF model created in an OptionCIsolated_*
/// database so controller, authentication, context reader, repository and
/// snapshot persistence are exercised together.
/// </summary>
public sealed class V180TripMileageHttpSqlE2ETests
{
    [Fact]
    public Task Trip_create_submit_and_leader_manual_fallback_preserve_location_identity_and_reject_reserved_location()
        => RunTripFlowAsync(oneStop: false);

    [Fact]
    public Task One_stop_trip_create_submit_calculate_and_manual_fallback_approve_preserves_snapshot()
        => RunTripFlowAsync(oneStop: true);

    [Fact]
    public Task Trip_return_correction_resubmit_and_reapprove_preserve_historical_submissions()
        => RunTripFlowAsync(oneStop: false, returnCorrection: true);

    [Fact]
    public Task Draft_route_preview_uses_provider_distance_and_persists_governance_attempt()
        => RunProviderPreviewAsync();

    private static async Task RunTripFlowAsync(bool oneStop, bool returnCorrection = false)
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_TRIP_HTTP_SQL_REQUIRED");
            return;
        }

        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
            || root.InitialCatalog != "master" || root.UserID != "sa"
            || root.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_TRIP_NONLOCAL_TARGET_DENIED");

        var name = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        await using (var admin = new SqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var cs = new SqlConnectionStringBuilder(root.ConnectionString)
            { InitialCatalog = name, Pooling = false }.ConnectionString;
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", cs);
                builder.UseSetting("Auth:Mode", "Demo");
                builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
                builder.UseSetting("OptionC:Candidate:Enabled", "true");
                builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
                builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
                builder.ConfigureTestServices(services =>
                    services.AddScoped<IV180RouteProvider, SuccessfulRouteProvider>());
            });

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                var fixture = await SeedAsync(db);
                // Add only the disposable reservation contract used by the
                // operational guard; no UAT/Production schema is touched.
                await db.Database.ExecuteSqlRawAsync("""
                    IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
                        CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
                    IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
                        CREATE TABLE dbo.LocationCreateReservations(LocationCreateReservationId bigint IDENTITY PRIMARY KEY,LocationId int NOT NULL,ReservationStatus nvarchar(30) NOT NULL,ChangeRequestId bigint NULL);
                    IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
                        INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'1.8.0-CREATE-LOWRISK',N'disposable trip guard',SYSUTCDATETIME(),N'E2E');
                    """);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT dbo.LocationCreateReservations(LocationId,ReservationStatus) VALUES ({0},N'Pending')",
                    fixture.Pending.LocationId);

                using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
                visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Visitor.UserId, fixture.Visitor.EmployeeNo!, "visitor", fixture.Team.TeamId));

                // A one-stop business visit still has the complete route
                // context (start/end deployment sites); it must not require a
                // fabricated second business stop.
                var request = Draft(fixture.Approved.LocationId, includeSecondStop: !oneStop);
                using var createdResponse = await visitor.PostAsJsonAsync("api/v1/trips", request);
                Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync());
                var created = await createdResponse.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_CREATE_RESPONSE_MISSING");
                Assert.Equal("Draft", created.Status);
                Assert.Contains(created.Stops, x => x.LocationId == fixture.Approved.LocationId);

                if (oneStop && !returnCorrection)
                {
                    var temporaryRequest = Draft(fixture.Approved.LocationId, includeSecondStop: false) with
                    {
                        VisitDate = new DateOnly(2026, 10, 16),
                        Stops = [new TripStopInput(null, null, null, "Temporary", "temporary client", "No. 99 Test Road", "visit", null, "12345678", "call before arrival")]
                    };
                    using var temporaryResponse = await visitor.PostAsJsonAsync("api/v1/trips", temporaryRequest);
                    Assert.True(temporaryResponse.IsSuccessStatusCode, await temporaryResponse.Content.ReadAsStringAsync());
                    var temporaryTrip = await temporaryResponse.Content.ReadFromJsonAsync<TripDto>()
                        ?? throw new InvalidOperationException("OPTIONC_TEMP_LOCATION_TRIP_RESPONSE_MISSING");
                    var temporaryId = temporaryTrip.Stops.Single().LocationId;
                    Assert.NotNull(temporaryId);
                    var savedTemporary = await db.Locations.SingleAsync(x => x.LocationId == temporaryId);
                    Assert.Equal("12345678", savedTemporary.TaxId);
                    Assert.Equal("call before arrival", savedTemporary.MasterNote);
                    var invalidTaxRequest = temporaryRequest with
                    {
                        VisitDate = new DateOnly(2026, 10, 17),
                        Stops = [new TripStopInput(null, null, null, "Temporary", "bad temporary", "No. 100 Test Road", "visit", null, new string('1', 21), "note")]
                    };
                    using var invalidTaxResponse = await visitor.PostAsJsonAsync("api/v1/trips", invalidTaxRequest);
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidTaxResponse.StatusCode);
                }

                // Direct LocationId input is still subject to the same server
                // reservation boundary as the Picker. It must not create a
                // Trip or Stop, even when the malformed reserved row is active.
                fixture.Pending.IsActive = true;
                fixture.Pending.ApprovalStatus = "Pending";
                await db.SaveChangesAsync();
                var before = await db.VisitTrips.CountAsync();
                using var blockedResponse = await visitor.PostAsJsonAsync(
                    "api/v1/trips", Draft(fixture.Pending.LocationId));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedResponse.StatusCode);
                Assert.Equal(before, await db.VisitTrips.CountAsync());
                Assert.Equal(0, await db.VisitTripStops.CountAsync(x => x.LocationId == fixture.Pending.LocationId));

                // Use an explicit request so the real optimistic-concurrency
                // If-Match contract is exercised by the HTTP pipeline.
                using var submit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit")
                {
                    Content = JsonContent.Create(new SubmitTripRequest(false))
                };
                submit.Headers.TryAddWithoutValidation("If-Match", created.RowVersion);
                using var submitted = await visitor.SendAsync(submit);
                Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
                var submittedTrip = await submitted.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_SUBMIT_RESPONSE_MISSING");
                Assert.Equal("Submitted", submittedTrip.Status);
                var mileageRepository = new TripRepository(db);
                var inRange = await mileageRepository.GetPendingMileageAsync(
                    [fixture.Team.TeamId], new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), null, CancellationToken.None);
                Assert.Contains(inRange, x => x.VisitTripId == created.VisitTripId);
                var outOfRange = await mileageRepository.GetPendingMileageAsync(
                    [fixture.Team.TeamId], new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 17), null, CancellationToken.None);
                Assert.DoesNotContain(outOfRange, x => x.VisitTripId == created.VisitTripId);

                var snapshotLocation = await db.VisitTripSnapshotStops
                    .Where(x => x.Snapshot.VisitTripId == created.VisitTripId)
                    .Select(x => x.LocationId).FirstAsync();
                Assert.Equal(fixture.Approved.LocationId, snapshotLocation);

                using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
                leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Leader.UserId, fixture.Leader.EmployeeNo!, "leader", fixture.Team.TeamId));
                using var calculated = await leader.PostAsJsonAsync(
                    "api/v1/leader/trips/calculate",
                    new MileageBatchRequest("Selected", null, null, [created.VisitTripId]));
                Assert.True(calculated.IsSuccessStatusCode, await calculated.Content.ReadAsStringAsync());
                var calculation = await calculated.Content.ReadFromJsonAsync<MileageBatchResult>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_CALCULATION_RESPONSE_MISSING");
                Assert.Equal(1, calculation.Success);

                var attemptCountBeforeStaleSelection = await db.RouteCalculationAttempts
                    .CountAsync(x => x.VisitTripId == created.VisitTripId);
                using var staleSelection = await leader.PostAsJsonAsync(
                    "api/v1/leader/trips/calculate",
                    new MileageBatchRequest("Selected", null, null, [created.VisitTripId, long.MaxValue]));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, staleSelection.StatusCode);
                Assert.Contains("MILEAGE_BATCH_SELECTION_STALE_OR_UNAUTHORIZED",
                    await staleSelection.Content.ReadAsStringAsync());
                Assert.Equal(attemptCountBeforeStaleSelection, await db.RouteCalculationAttempts
                    .CountAsync(x => x.VisitTripId == created.VisitTripId));

                var pendingApproval = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{created.VisitTripId}")
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_PENDING_APPROVAL_RESPONSE_MISSING");
                Assert.Equal("PendingApproval", pendingApproval.Status);

                if (returnCorrection)
                {
                    using var returnedResponse = await leader.PostAsJsonAsync(
                        $"api/v1/trips/{created.VisitTripId}/return",
                        new ReturnTripRequest("Please correct the visit purpose.", pendingApproval.RowVersion));
                    Assert.True(returnedResponse.IsSuccessStatusCode, await returnedResponse.Content.ReadAsStringAsync());
                    var returned = await returnedResponse.Content.ReadFromJsonAsync<TripDto>()
                        ?? throw new InvalidOperationException("OPTIONC_TRIP_RETURN_RESPONSE_MISSING");
                    Assert.Equal("Returned", returned.Status);
                    Assert.Equal("Please correct the visit purpose.", returned.ReturnReason);

                    using var correction = new HttpRequestMessage(HttpMethod.Put, $"api/v1/trips/{created.VisitTripId}")
                    {
                        Content = JsonContent.Create(Draft(fixture.Approved.LocationId, includeSecondStop: true) with
                        {
                            Notes = "Corrected after leader return"
                        })
                    };
                    correction.Headers.TryAddWithoutValidation("If-Match", returned.RowVersion);
                    using var correctedResponse = await visitor.SendAsync(correction);
                    Assert.True(correctedResponse.IsSuccessStatusCode, await correctedResponse.Content.ReadAsStringAsync());
                    var corrected = await correctedResponse.Content.ReadFromJsonAsync<TripDto>()
                        ?? throw new InvalidOperationException("OPTIONC_TRIP_CORRECTION_RESPONSE_MISSING");
                    // Returned trips remain Returned while corrected; Submit
                    // performs the canonical Returned -> Submitted Resubmit.
                    Assert.Equal("Returned", corrected.Status);

                    using var resubmit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit")
                    {
                        Content = JsonContent.Create(new SubmitTripRequest(false))
                    };
                    resubmit.Headers.TryAddWithoutValidation("If-Match", corrected.RowVersion);
                    using var resubmitted = await visitor.SendAsync(resubmit);
                    Assert.True(resubmitted.IsSuccessStatusCode, await resubmitted.Content.ReadAsStringAsync());
                    var resubmittedTrip = await resubmitted.Content.ReadFromJsonAsync<TripDto>()
                        ?? throw new InvalidOperationException("OPTIONC_TRIP_RESUBMIT_RESPONSE_MISSING");
                    Assert.Equal("Submitted", resubmittedTrip.Status);

                    using var recalculate = await leader.PostAsJsonAsync(
                        "api/v1/leader/trips/calculate",
                        new MileageBatchRequest("Selected", null, null, [created.VisitTripId]));
                    Assert.True(recalculate.IsSuccessStatusCode, await recalculate.Content.ReadAsStringAsync());
                    pendingApproval = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{created.VisitTripId}")
                        ?? throw new InvalidOperationException("OPTIONC_TRIP_RECALCULATED_RESPONSE_MISSING");
                    Assert.Equal("PendingApproval", pendingApproval.Status);
                    Assert.Equal(2, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted"));
                    Assert.Contains(await db.VisitTripStatusHistories.Where(x => x.VisitTripId == created.VisitTripId).Select(x => x.Action).ToListAsync(), x => x == "Return");
                    Assert.Contains(await db.VisitTripStatusHistories.Where(x => x.VisitTripId == created.VisitTripId).Select(x => x.Action).ToListAsync(), x => x == "Resubmit");
                }

                var approve = new ApproveTripRequest(8.5m, submittedTrip.RowVersion, "manual fallback", "ManualFallback");
                approve = approve with { RowVersion = pendingApproval.RowVersion };
                using var approved = await leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve);
                Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
                var approvedTrip = await approved.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_APPROVE_RESPONSE_MISSING");
                Assert.Equal("Approved", approvedTrip.Status);
                Assert.Equal(8.5m, approvedTrip.ApprovedDistanceKm);

                Assert.Equal(returnCorrection ? 2 : 1, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted"));
                // Submitted and Approved snapshots retain the exact submitted
                // stop identities; publication appends the approved snapshot
                // and never rewrites the historical Submitted snapshot.
                var expectedSnapshotStops = (returnCorrection ? 3 : 2) * (oneStop ? 1 : 2);
                Assert.Equal(expectedSnapshotStops, await db.VisitTripSnapshotStops.CountAsync(x => x.Snapshot.VisitTripId == created.VisitTripId && x.LocationId == fixture.Approved.LocationId));
                Assert.Equal(0, await db.VisitTripStops.CountAsync(x => x.LocationId == fixture.Pending.LocationId));
            }
        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task RunProviderPreviewAsync()
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_TRIP_HTTP_SQL_REQUIRED");
            return;
        }

        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
            || root.InitialCatalog != "master" || root.UserID != "sa"
            || root.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_TRIP_NONLOCAL_TARGET_DENIED");

        var name = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        await using (var admin = new SqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var cs = new SqlConnectionStringBuilder(root.ConnectionString)
            { InitialCatalog = name, Pooling = false }.ConnectionString;
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", cs);
                builder.UseSetting("Auth:Mode", "Demo");
                builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
                builder.UseSetting("OptionC:Candidate:Enabled", "true");
                builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
                builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
                builder.ConfigureTestServices(services =>
                    services.AddScoped<IV180RouteProvider, SuccessfulRouteProvider>());
            });

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var fixture = await SeedAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
                    CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
                IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
                    CREATE TABLE dbo.LocationCreateReservations(LocationCreateReservationId bigint IDENTITY PRIMARY KEY,LocationId int NOT NULL,ReservationStatus nvarchar(30) NOT NULL,ChangeRequestId bigint NULL);
                IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
                    INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'1.8.0-CREATE-LOWRISK',N'disposable trip guard',SYSUTCDATETIME(),N'E2E');
                """);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT dbo.LocationCreateReservations(LocationId,ReservationStatus) VALUES ({0},N'Pending')",
                fixture.Pending.LocationId);
            using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Visitor.UserId, fixture.Visitor.EmployeeNo!, "visitor", fixture.Team.TeamId));
            using var createdResponse = await visitor.PostAsJsonAsync(
                "api/v1/trips", Draft(fixture.Approved.LocationId, includeSecondStop: false));
            Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync());
            var created = await createdResponse.Content.ReadFromJsonAsync<TripDto>()
                ?? throw new InvalidOperationException("OPTIONC_TRIP_CREATE_RESPONSE_MISSING");

            using var preview = await visitor.PostAsync($"api/v1/trips/{created.VisitTripId}/route-preview", null);
            Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
            var result = await preview.Content.ReadFromJsonAsync<V180RouteOrchestrationResult>()
                ?? throw new InvalidOperationException("OPTIONC_ROUTE_PREVIEW_RESPONSE_MISSING");
            Assert.Equal("Succeeded", result.Status);
            Assert.Equal(12.5m, result.SuggestedDistanceKm);
            Assert.Equal(300, result.DurationSeconds);
            using var submit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit")
            {
                Content = JsonContent.Create(new SubmitTripRequest(false))
            };
            submit.Headers.TryAddWithoutValidation("If-Match", created.RowVersion);
            using var submittedResponse = await visitor.SendAsync(submit);
            Assert.True(submittedResponse.IsSuccessStatusCode, await submittedResponse.Content.ReadAsStringAsync());
            var submitted = await submittedResponse.Content.ReadFromJsonAsync<TripDto>()
                ?? throw new InvalidOperationException("OPTIONC_PROVIDER_SUBMIT_RESPONSE_MISSING");
            Assert.Equal("PendingApproval", submitted.Status);
            Assert.Equal(12.5m, submitted.SystemDistanceKm);
            Assert.Equal("GoogleMapsAPI", submitted.MileageSource);

            using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Leader.UserId, fixture.Leader.EmployeeNo!, "leader", fixture.Team.TeamId));
            var attempt = await db.RouteCalculationAttempts
                .Where(x => x.VisitTripId == created.VisitTripId && x.BasisType == "Draft")
                .SingleAsync();
            Assert.Equal("GoogleMapsRoutes-TestStub", attempt.Provider);
            Assert.Equal("Succeeded", attempt.Status);
            Assert.Equal("Calculated", (await db.MileageGovernanceEvents
                .SingleAsync(x => x.RouteCalculationAttemptId == attempt.RouteCalculationAttemptId && x.EventType == "Calculated")).EventType);

            var attemptsBeforeApproval = await db.RouteCalculationAttempts.CountAsync(x => x.VisitTripId == created.VisitTripId);
            Assert.Equal(1, attemptsBeforeApproval); // successful visitor route is reused; Leader does not recalculate
            var pendingApproval = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{created.VisitTripId}")
                ?? throw new InvalidOperationException("OPTIONC_PROVIDER_PENDING_APPROVAL_RESPONSE_MISSING");
            Assert.Equal("PendingApproval", pendingApproval.Status);

            using var approved = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(12.5m, pendingApproval.RowVersion,
                    "provider suggested approval", "ProviderSuggested", attempt.RouteCalculationAttemptId));
            Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
            var approvedTrip = await approved.Content.ReadFromJsonAsync<TripDto>()
                ?? throw new InvalidOperationException("OPTIONC_PROVIDER_APPROVED_RESPONSE_MISSING");
            Assert.Equal("Approved", approvedTrip.Status);
            Assert.Equal(12.5m, approvedTrip.ApprovedDistanceKm);
            Assert.Equal(37.5m, approvedTrip.ApprovedAmount);
            Assert.Equal(2, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId));
            Assert.Equal(1, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted"));
            Assert.Equal(1, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Approved"));
            Assert.Equal(1, await db.MileageGovernanceEvents.CountAsync(x => x.VisitTripId == created.VisitTripId && x.EventType == "Approved"));
            Assert.Equal(attemptsBeforeApproval, await db.RouteCalculationAttempts.CountAsync(x => x.VisitTripId == created.VisitTripId));
        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class SuccessfulRouteProvider : IV180RouteProvider
    {
        public string ProviderName => "GoogleMapsRoutes-TestStub";

        public Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct) =>
            Task.FromResult(new V180RouteProviderResult(
                true, 12.5m, 300, "encoded-test-polyline", null, null));
    }

    private static SaveTripRequest Draft(int locationId, bool includeSecondStop = true) => new(
        new DateOnly(2026, 10, 15), new TimeOnly(9, 0), new TimeOnly(10, 0),
        8.5m, "HTTP trip", "isolated", false,
        Stops(locationId, includeSecondStop),
        1, 1, 1, "Motorcycle");

    private static IReadOnlyList<TripStopInput> Stops(int locationId, bool includeSecondStop)
        => includeSecondStop
            ? new[] { new TripStopInput(locationId, null, null, "Location", "fixture", "address", "visit", null),
                     new TripStopInput(locationId, null, null, "Location", "fixture-2", "address-2", "visit", null) }
            : new[] { new TripStopInput(locationId, null, null, "Location", "fixture", "address", "visit", null) };

    private static async Task<(User Visitor, User Leader, Team Team, Location Approved, Location Pending)> SeedAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var org = new Organization { OrganizationCode = "E2E", OrganizationName = "E2E", IsActive = true, CreatedAt = now };
        var team = new Team { OrganizationId = 1, TeamCode = "E2E-T1", TeamName = "E2E Team", IsActive = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now };
        var visitorRole = new Role { RoleCode = "visitor", RoleName = "Visitor", IsActive = true, CreatedAt = now };
        var leaderRole = new Role { RoleCode = "leader", RoleName = "Leader", IsActive = true, CreatedAt = now };
        db.AddRange(org, visitorRole, leaderRole);
        await db.SaveChangesAsync();
        team.OrganizationId = org.OrganizationId;
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var visitor = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "E2E-V", DisplayName = "E2E Visitor", IsActive = true, CreatedAt = now };
        var leader = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "E2E-L", DisplayName = "E2E Leader", IsActive = true, CreatedAt = now };
        db.AddRange(visitor, leader);
        await db.SaveChangesAsync();

        var employment = new Employment { PersonId = visitor.UserId, OrganizationId = org.OrganizationId, EmployeeNo = visitor.EmployeeNo, LegacyUserId = visitor.UserId, HireDate = new DateOnly(2020, 1, 1), SourceType = "E2E" };
        var leaderEmployment = new Employment { PersonId = leader.UserId, OrganizationId = org.OrganizationId, EmployeeNo = leader.EmployeeNo, LegacyUserId = leader.UserId, HireDate = new DateOnly(2020, 1, 1), SourceType = "E2E" };
        db.AddRange(employment, leaderEmployment);
        await db.SaveChangesAsync();
        db.AddRange(
            new UserIdentityProfile { UserId = visitor.UserId, EmploymentId = employment.EmploymentId, UserType = UserTypes.Internal, UserCode = visitor.EmployeeNo!, IdentityProvider = "Demo", CreatedAt = now },
            new UserIdentityProfile { UserId = leader.UserId, EmploymentId = leaderEmployment.EmploymentId, UserType = UserTypes.Internal, UserCode = leader.EmployeeNo!, IdentityProvider = "Demo", CreatedAt = now },
            new EmploymentStatusPeriod { EmploymentId = employment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "E2E" },
            new EmploymentStatusPeriod { EmploymentId = leaderEmployment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "E2E" },
            new TeamMembership { EmploymentId = employment.EmploymentId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1) },
            new TeamMembership { EmploymentId = leaderEmployment.EmploymentId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1) },
            new UserEmploymentPeriod { UserId = visitor.UserId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "E2E", CreatedAt = now },
            new UserEmploymentPeriod { UserId = leader.UserId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "E2E", CreatedAt = now },
            new UserTeamAssignment { UserId = visitor.UserId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserTeamAssignment { UserId = leader.UserId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserTeamScope { UserId = visitor.UserId, TeamId = team.TeamId, IsPrimary = true, IsActive = true, AssignedAt = now },
            new UserTeamScope { UserId = leader.UserId, TeamId = team.TeamId, IsPrimary = true, IsActive = true, AssignedAt = now },
            new TeamLeaderAssignment { TeamId = team.TeamId, EmploymentId = leaderEmployment.EmploymentId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserRole { UserId = visitor.UserId, RoleId = visitorRole.RoleId, AssignedAt = now },
            new UserRole { UserId = leader.UserId, RoleId = leaderRole.RoleId, AssignedAt = now },
            new UserRoleAssignment { UserId = visitor.UserId, RoleId = visitorRole.RoleId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new UserRoleAssignment { UserId = leader.UserId, RoleId = leaderRole.RoleId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });

        var center = new Center { OrganizationId = org.OrganizationId, CenterCode = "E2E-C", CenterName = "E2E Center", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true };
        db.Centers.Add(center);
        await db.SaveChangesAsync();
        db.Add(new TeamCenterAssignment { TeamId = team.TeamId, CenterId = center.CenterId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });
        var site = new DeploymentSite { CenterId = center.CenterId, SiteCode = "E2E-S", SiteName = "E2E Site", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = now };
        db.DeploymentSites.Add(site);
        await db.SaveChangesAsync();
        db.AddRange(
            new TeamDeploymentSiteAssignment { TeamId = team.TeamId, DeploymentSiteId = site.DeploymentSiteId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new EmploymentDeploymentSiteAssignment { EmploymentId = employment.EmploymentId, DeploymentSiteId = site.DeploymentSiteId, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });

        var approved = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId, LocationName = "Approved", LocationType = "Customer", ApprovalStatus = "Approved", GeocodingStatus = "Succeeded", IsActive = true, CreatedByUserId = visitor.UserId, CreatedAt = now };
        var pending = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId, LocationName = "Pending", LocationType = "Customer", ApprovalStatus = "Pending", GeocodingStatus = "Pending", IsActive = false, CreatedByUserId = visitor.UserId, CreatedAt = now };
        db.AddRange(approved, pending);
        await db.SaveChangesAsync();
        db.AddRange(
            new DeploymentSiteLocationAssignment { DeploymentSiteId = site.DeploymentSiteId, LocationId = approved.LocationId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now },
            new VisitType { VisitTypeCode = "E2E", VisitTypeName = "E2E Visit", IsActive = true, CreatedAt = now },
            new MileageRateRule { OrganizationId = org.OrganizationId, RuleName = "E2E rate", VehicleType = "Motorcycle", RatePerKm = 3m, EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();
        return (visitor, leader, team, approved, pending);
    }

    private static string Token(int id, string employeeNo, string role, int teamId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, employeeNo),
            new Claim("employee_no", employeeNo),
            new Claim(ClaimTypes.Role, role),
            new Claim("organization_id", "1"),
            new Claim("team_id", teamId.ToString()),
            new Claim("team_name", "E2E Team"),
            new Claim("team_scopes", System.Text.Json.JsonSerializer.Serialize(
                new[] { new TeamScopeDto(teamId, "E2E Team", true) }))
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-characters-long"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "FieldVisit.UAT", "FieldVisit.UAT.Users", claims, expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }
}
