using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
public sealed partial class V180TripMileageHttpSqlE2ETests
{
    [Fact]
    public Task Provider_failure_manual_fallback_requires_authoritative_route_evidence_and_preserves_location_identity()
        => RunTripFlowAsync(oneStop: false);

    [Fact]
    public Task One_stop_trip_provider_failure_manual_fallback_enforces_live_leader_authorization_and_concurrency()
        => RunTripFlowAsync(oneStop: true);

    [Fact]
    public Task Trip_return_correction_resubmit_and_reapprove_preserve_historical_submissions()
        => RunTripFlowAsync(oneStop: false, returnCorrection: true);

    [Fact]
    public Task Visitor_google_success_survives_same_route_edit_invalidates_changed_route_and_resubmits_with_immutable_history()
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
                    services.AddScoped<IV180RouteProvider, FailedRouteProvider>());
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

                using (var overlappingCheck = await visitor.PostAsJsonAsync(
                    "api/v1/trips/time-overlap-check",
                    new TimeOverlapRequest(created.VisitDate, new TimeOnly(9, 30), new TimeOnly(9, 45), null)))
                {
                    Assert.True(overlappingCheck.IsSuccessStatusCode, await overlappingCheck.Content.ReadAsStringAsync());
                    var overlap = await overlappingCheck.Content.ReadFromJsonAsync<TimeOverlapResult>()
                        ?? throw new InvalidOperationException("OPTIONC_OVERLAP_RESPONSE_MISSING");
                    Assert.True(overlap.HasOverlap);
                    Assert.Contains(overlap.OverlappingTrips, x => x.VisitTripId == created.VisitTripId);
                }
                using (var adjacentCheck = await visitor.PostAsJsonAsync(
                    "api/v1/trips/time-overlap-check",
                    new TimeOverlapRequest(created.VisitDate, new TimeOnly(10, 0), new TimeOnly(11, 0), null)))
                {
                    Assert.True(adjacentCheck.IsSuccessStatusCode, await adjacentCheck.Content.ReadAsStringAsync());
                    Assert.False((await adjacentCheck.Content.ReadFromJsonAsync<TimeOverlapResult>())?.HasOverlap);
                }

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
                using (var routeFailure = await visitor.PostAsync($"api/v1/trips/{created.VisitTripId}/route-preview", null))
                {
                    Assert.True(routeFailure.IsSuccessStatusCode, await routeFailure.Content.ReadAsStringAsync());
                    Assert.Equal("Failed", (await routeFailure.Content.ReadFromJsonAsync<V180RouteOrchestrationResult>())?.Status);
                }
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
                var leaderQueue = await leader.GetFromJsonAsync<List<TripDto>>("api/v1/leader/review-queue")
                    ?? throw new InvalidOperationException("OPTIONC_RETRY_QUEUE_MISSING");
                Assert.True(leaderQueue.Single(x => x.VisitTripId == created.VisitTripId).HasCurrentRouteFailureEvidence);
                var mileageJobCountBeforeOutOfRange = await db.BackgroundJobs.CountAsync(x => x.JobType == "Mileage");
                using (var outOfRangeJob = await leader.PostAsJsonAsync(
                    "api/v1/jobs/mileage",
                    new MileageBatchRequest("DateRange", new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 17), null)))
                {
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, outOfRangeJob.StatusCode);
                }
                Assert.Equal(mileageJobCountBeforeOutOfRange,
                    await db.BackgroundJobs.CountAsync(x => x.JobType == "Mileage"));
                using (var retryJob = await leader.PostAsJsonAsync(
                    "api/v1/jobs/mileage",
                    new MileageBatchRequest("Selected", null, null, [created.VisitTripId])))
                {
                    Assert.True(retryJob.IsSuccessStatusCode, await retryJob.Content.ReadAsStringAsync());
                    var createdJob = await retryJob.Content.ReadFromJsonAsync<BackgroundJobDto>()
                        ?? throw new InvalidOperationException("OPTIONC_RETRY_JOB_MISSING");
                    var persistedJob = await db.BackgroundJobs.SingleAsync(x => x.BackgroundJobId == createdJob.BackgroundJobId);
                    Assert.Contains(created.VisitTripId.ToString(), persistedJob.PayloadJson);
                    Assert.Contains("Selected", persistedJob.PayloadJson);

                    var attemptsBeforeRevokedWorker = await db.RouteCalculationAttempts
                        .CountAsync(x => x.VisitTripId == created.VisitTripId);
                    var leaderRoleId = await db.Roles.Where(r => r.RoleCode == "leader")
                        .Select(r => r.RoleId).SingleAsync();
                    var revokedLeaderAssignment = await db.UserRoleAssignments
                        .SingleAsync(x => x.UserId == fixture.Leader.UserId && x.RoleId == leaderRoleId);
                    revokedLeaderAssignment.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                    await db.SaveChangesAsync();
                    using var workerScopeA = factory.Services.CreateScope();
                    using var workerScopeB = factory.Services.CreateScope();
                    var workerA = workerScopeA.ServiceProvider.GetRequiredService<IBackgroundJobService>();
                    var workerB = workerScopeB.ServiceProvider.GetRequiredService<IBackgroundJobService>();
                    var claimResults = await Task.WhenAll(
                        workerA.ProcessNextAsync(CancellationToken.None),
                        workerB.ProcessNextAsync(CancellationToken.None));
                    Assert.Equal(1, claimResults.Count(x => x));
                    var processedJob = await db.BackgroundJobs.AsNoTracking()
                        .SingleAsync(x => x.BackgroundJobId == createdJob.BackgroundJobId);
                    Assert.Equal("Failed", processedJob.Status);
                    Assert.Contains("MILEAGE_JOB_LEADER_ROLE_REVOKED", processedJob.ErrorMessage);
                    Assert.Equal(attemptsBeforeRevokedWorker, await db.RouteCalculationAttempts
                        .CountAsync(x => x.VisitTripId == created.VisitTripId));
                    await db.UserRoleAssignments
                        .Where(x => x.UserRoleAssignmentId == revokedLeaderAssignment.UserRoleAssignmentId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, (DateOnly?)null));
                }
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

                var jobCountBeforeStaleRetry = await db.BackgroundJobs.CountAsync(x => x.JobType == "Mileage");
                using (var staleRetryJob = await leader.PostAsJsonAsync(
                    "api/v1/jobs/mileage",
                    new MileageBatchRequest("Selected", null, null, [created.VisitTripId])))
                {
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, staleRetryJob.StatusCode);
                }
                Assert.Equal(jobCountBeforeStaleRetry, await db.BackgroundJobs.CountAsync(x => x.JobType == "Mileage"));

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
                var leaderEmployment = await db.Employments.SingleAsync(x => x.PersonId == fixture.Leader.UserId);
                var activeStatus = await db.EmploymentStatusPeriods.SingleAsync(x => x.EmploymentId == leaderEmployment.EmploymentId && x.EmploymentStatus == "Active");
                activeStatus.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                await db.SaveChangesAsync();
                using (var revokedHr = await leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve))
                    Assert.Equal(HttpStatusCode.Forbidden, revokedHr.StatusCode);
                activeStatus.EffectiveTo = null;
                var leaderRoleIdForRevocation = await db.Roles
                    .Where(r => r.RoleCode == "leader")
                    .Select(r => r.RoleId)
                    .SingleAsync();
                var leaderRoleAssignment = await db.UserRoleAssignments
                    .SingleAsync(x => x.UserId == fixture.Leader.UserId && x.RoleId == leaderRoleIdForRevocation);
                var revokedRoleDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                await db.UserRoleAssignments
                    .Where(x => x.UserRoleAssignmentId == leaderRoleAssignment.UserRoleAssignmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, revokedRoleDate));
                db.ChangeTracker.Clear();
                using (var revokedRole = await leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve))
                    Assert.Equal(HttpStatusCode.Forbidden, revokedRole.StatusCode);
                await db.UserRoleAssignments
                    .Where(x => x.UserRoleAssignmentId == leaderRoleAssignment.UserRoleAssignmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, (DateOnly?)null));
                var leaderTeamAssignment = await db.UserTeamAssignments
                    .SingleAsync(x => x.UserId == fixture.Leader.UserId && x.TeamId == fixture.Team.TeamId);
                var revokedTeamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                await db.UserTeamAssignments
                    .Where(x => x.UserTeamAssignmentId == leaderTeamAssignment.UserTeamAssignmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, revokedTeamDate));
                db.ChangeTracker.Clear();
                using (var revokedTeam = await leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve))
                    Assert.Equal(HttpStatusCode.Forbidden, revokedTeam.StatusCode);
                await db.UserTeamAssignments
                    .Where(x => x.UserTeamAssignmentId == leaderTeamAssignment.UserTeamAssignmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, (DateOnly?)null));
                var pendingTripEntity = await db.VisitTrips.SingleAsync(x => x.VisitTripId == created.VisitTripId);
                await db.Entry(pendingTripEntity).ReloadAsync();
                var visitorOwnerId = pendingTripEntity.UserId;

                async Task AssertRevocationWinsApprovalRace(string revokeSql, string restoreSql, string label)
                {
                    await using var revokeTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                    await db.Database.ExecuteSqlRawAsync(revokeSql);
                    var connection = (SqlConnection)db.Database.GetDbConnection();
                    await db.Database.OpenConnectionAsync();
                    await using var spidCommand = new SqlCommand("SELECT @@SPID", connection);
                    spidCommand.Transaction = (SqlTransaction)revokeTransaction.GetDbTransaction();
                    var blockerSpid = Convert.ToInt32(await spidCommand.ExecuteScalarAsync());
                    var racedApproval = leader.PostAsJsonAsync(
                        $"api/v1/trips/{created.VisitTripId}/approve", approve);
                    await WaitUntilBlockedByAsync(root.ConnectionString, blockerSpid, racedApproval, label);
                    await revokeTransaction.CommitAsync();
                    await revokeTransaction.DisposeAsync();
                    using var racedResponse = await racedApproval.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.Equal(HttpStatusCode.Forbidden, racedResponse.StatusCode);
                    await db.Database.ExecuteSqlRawAsync(restoreSql);
                    await db.Database.CloseConnectionAsync();
                }

                await AssertRevocationWinsApprovalRace(
                    $"UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Leave' WHERE EmploymentId={leaderEmployment.EmploymentId} AND EmploymentStatus=N'Active'",
                    $"UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Active' WHERE EmploymentId={leaderEmployment.EmploymentId}", "HR");
                await AssertRevocationWinsApprovalRace(
                    $"UPDATE dbo.UserRoleAssignments SET EffectiveTo=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE UserId={fixture.Leader.UserId} AND RoleId=(SELECT RoleId FROM dbo.Roles WHERE RoleCode=N'leader') AND EffectiveTo IS NULL",
                    $"UPDATE dbo.UserRoleAssignments SET EffectiveTo=NULL WHERE UserId={fixture.Leader.UserId} AND RoleId=(SELECT RoleId FROM dbo.Roles WHERE RoleCode=N'leader')", "role");
                await AssertRevocationWinsApprovalRace(
                    $"UPDATE dbo.UserTeamAssignments SET EffectiveTo=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE UserId={fixture.Leader.UserId} AND TeamId={fixture.Team.TeamId} AND EffectiveTo IS NULL",
                    $"UPDATE dbo.UserTeamAssignments SET EffectiveTo=NULL WHERE UserId={fixture.Leader.UserId} AND TeamId={fixture.Team.TeamId}", "team");

                // The race helper deliberately closes/reopens its connection;
                // normalize the live authorization rows before the next
                // independent self-approval assertion so the assertion is not
                // coupled to a prior transaction's tracked state.
                await db.UserRoleAssignments
                    .Where(x => x.UserId == fixture.Leader.UserId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, (DateOnly?)null));
                await db.UserTeamAssignments
                    .Where(x => x.UserId == fixture.Leader.UserId && x.TeamId == fixture.Team.TeamId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, (DateOnly?)null));
                await db.EmploymentStatusPeriods
                    .Where(x => x.EmploymentId == leaderEmployment.EmploymentId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.EmploymentStatus, EmploymentStatuses.Active)
                        .SetProperty(x => x.EffectiveTo, (DateOnly?)null));
                db.ChangeTracker.Clear();
                await db.VisitTrips
                    .Where(x => x.VisitTripId == created.VisitTripId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, fixture.Leader.UserId));
                db.ChangeTracker.Clear();
                var selfApprovalTrip = await db.VisitTrips
                    .AsNoTracking()
                    .SingleAsync(x => x.VisitTripId == created.VisitTripId);
                var selfApprovalVersion = Convert.ToBase64String(selfApprovalTrip.RowVersion);
                using (var selfApproval = await leader.PostAsJsonAsync(
                    $"api/v1/trips/{created.VisitTripId}/approve",
                    approve with { RowVersion = selfApprovalVersion }))
                {
                    Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
                    var selfApprovalBody = await selfApproval.Content.ReadAsStringAsync();
                    // The authorization boundary may short-circuit before the
                    // domain exception formatter; the security contract is
                    // the non-success 403 and no state transition.
                    Assert.Contains("\"status\":403", selfApprovalBody);
                }
                await db.VisitTrips
                    .Where(x => x.VisitTripId == created.VisitTripId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, visitorOwnerId));
                db.ChangeTracker.Clear();
                var refreshedPending = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{created.VisitTripId}")
                    ?? throw new InvalidOperationException("OPTIONC_TRIP_AFTER_SELF_DENY_MISSING");
                approve = approve with { RowVersion = refreshedPending.RowVersion };
                using (var staleApproval = await leader.PostAsJsonAsync(
                    $"api/v1/trips/{created.VisitTripId}/approve",
                    approve with { RowVersion = "AA==" }))
                {
                    Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
                    Assert.Contains("ROWVERSION_CONFLICT", await staleApproval.Content.ReadAsStringAsync());
                }
                var concurrentApprovals = await Task.WhenAll(
                    leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve),
                    leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve));
                try
                {
                    Assert.Single(concurrentApprovals.Where(x => x.IsSuccessStatusCode));
                    var rejectedConcurrent = concurrentApprovals.Single(x => !x.IsSuccessStatusCode);
                    Assert.True(
                        rejectedConcurrent.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity,
                        $"Concurrent approval should be rejected as a conflict, received {(int)rejectedConcurrent.StatusCode}: {await rejectedConcurrent.Content.ReadAsStringAsync()}");
                    var approved = concurrentApprovals.Single(x => x.IsSuccessStatusCode);
                    var approvedTrip = await approved.Content.ReadFromJsonAsync<TripDto>()
                        ?? throw new InvalidOperationException("OPTIONC_TRIP_APPROVE_RESPONSE_MISSING");
                    Assert.Equal("Approved", approvedTrip.Status);
                    Assert.Equal(8.5m, approvedTrip.ApprovedDistanceKm);
                    var fallbackCalc = await db.MileageCalculations.AsNoTracking().SingleAsync(x => x.VisitTripId == created.VisitTripId);
                Assert.Equal("ManualFallback", fallbackCalc.ApprovedDistanceSource);
                Assert.True(fallbackCalc.ManualFallbackUsed);
                Assert.Single(await db.MileageGovernanceEvents.Where(x => x.VisitTripId == created.VisitTripId && x.EventType == "ManualFallback").ToListAsync());
                using var duplicateApproval = await leader.PostAsJsonAsync($"api/v1/trips/{created.VisitTripId}/approve", approve);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicateApproval.StatusCode);
                Assert.Equal(1, await db.ApprovalRecords.CountAsync(x => x.VisitTripId == created.VisitTripId && x.Action == "Approved"));
                }
                finally
                {
                    foreach (var response in concurrentApprovals) response.Dispose();
                }

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

            using (var editSameRoute = new HttpRequestMessage(HttpMethod.Put, $"api/v1/trips/{created.VisitTripId}")
            {
                Content = JsonContent.Create(Draft(fixture.Approved.LocationId, includeSecondStop: false) with { Notes = "same route edit" })
            })
            {
                editSameRoute.Headers.TryAddWithoutValidation("If-Match", created.RowVersion);
                using var response = await visitor.SendAsync(editSameRoute);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                var edited = await response.Content.ReadFromJsonAsync<TripDto>() ?? throw new InvalidOperationException("OPTIONC_SAME_ROUTE_EDIT_MISSING");
                Assert.Equal(12.5m, edited.SystemDistanceKm);
                Assert.Equal(result.RouteCalculationAttemptId, await db.MileageCalculations.Where(x => x.VisitTripId == created.VisitTripId).Select(x => x.SelectedRouteCalculationAttemptId).SingleAsync());
                created = edited;
            }

            var changedRoute = Draft(fixture.Approved.LocationId, includeSecondStop: false) with
            {
                Notes = "changed route edit",
                Stops = [new TripStopInput(fixture.Approved.LocationId, null, null, "Location", "fixture", "changed route address", "visit", null)]
            };
            using (var editChangedRoute = new HttpRequestMessage(HttpMethod.Put, $"api/v1/trips/{created.VisitTripId}")
            {
                Content = JsonContent.Create(changedRoute)
            })
            {
                editChangedRoute.Headers.TryAddWithoutValidation("If-Match", created.RowVersion);
                using var response = await visitor.SendAsync(editChangedRoute);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                created = await response.Content.ReadFromJsonAsync<TripDto>() ?? throw new InvalidOperationException("OPTIONC_CHANGED_ROUTE_EDIT_MISSING");
                Assert.Null(await db.MileageCalculations.Where(x => x.VisitTripId == created.VisitTripId).Select(x => x.SelectedRouteCalculationAttemptId).SingleAsync());
                Assert.Null((await db.MileageCalculations.SingleAsync(x => x.VisitTripId == created.VisitTripId)).SystemDistanceKm);
            }
            using (var changedPreview = await visitor.PostAsync($"api/v1/trips/{created.VisitTripId}/route-preview", null))
            {
                Assert.True(changedPreview.IsSuccessStatusCode, await changedPreview.Content.ReadAsStringAsync());
                Assert.Equal("Succeeded", (await changedPreview.Content.ReadFromJsonAsync<V180RouteOrchestrationResult>())?.Status);
            }

            using var submit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit")
            {
                Content = JsonContent.Create(new SubmitTripRequest(false))
            };
            var currentDraft = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{created.VisitTripId}")
                ?? throw new InvalidOperationException("OPTIONC_CHANGED_ROUTE_DRAFT_MISSING");
            submit.Headers.TryAddWithoutValidation("If-Match", currentDraft.RowVersion);
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
                .OrderByDescending(x => x.RouteCalculationAttemptId).FirstAsync();
            Assert.Equal("GoogleMapsRoutes-TestStub", attempt.Provider);
            Assert.Equal("Succeeded", attempt.Status);
            Assert.Equal("Calculated", (await db.MileageGovernanceEvents
                .SingleAsync(x => x.RouteCalculationAttemptId == attempt.RouteCalculationAttemptId && x.EventType == "Calculated")).EventType);

            var attemptsBeforeApproval = await db.RouteCalculationAttempts.CountAsync(x => x.VisitTripId == created.VisitTripId);
            Assert.Equal(2, attemptsBeforeApproval); // one stale attempt was invalidated by edit; only the current route is used
            var jobsBeforeApproval = await db.BackgroundJobItems.CountAsync(x => x.EntityType == "VisitTrip" && x.EntityId == created.VisitTripId.ToString());
            var pendingApproval = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{created.VisitTripId}")
                ?? throw new InvalidOperationException("OPTIONC_PROVIDER_PENDING_APPROVAL_RESPONSE_MISSING");
            Assert.Equal("PendingApproval", pendingApproval.Status);

            // A current successful result remains authoritative even when a
            // newer failure and newer stale success would hide it from a
            // latest-success-only lookup. Clear the convenience pointer to
            // prove fallback uses immutable provider evidence.
            var currentSubmitted = await db.VisitTripSnapshots.Include(x => x.Stops)
                .Where(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted")
                .OrderByDescending(x => x.SnapshotVersion).FirstAsync();
            var currentHash = V180MileageCanonicalization.HashRoute(
                V180MileageCanonicalization.BuildSubmittedSnapshotBasis(currentSubmitted));
            db.RouteCalculationAttempts.AddRange(
                new RouteCalculationAttempt
                {
                    VisitTripId = created.VisitTripId,
                    BasisType = "SubmittedSnapshot",
                    BasisVisitTripSnapshotId = currentSubmitted.VisitTripSnapshotId,
                    CalculationReason = "VisitorSubmit",
                    RequestedVehicleType = attempt.RequestedVehicleType,
                    TravelMode = attempt.TravelMode,
                    Provider = "GoogleMapsRoutes-TestStub",
                    StopCount = attempt.StopCount,
                    RequestBasisHash = currentHash,
                    Status = "Failed",
                    ErrorCode = "PROVIDER_UNAVAILABLE",
                    CorrelationId = Guid.NewGuid(),
                    RequestedAt = DateTime.UtcNow.AddSeconds(1),
                    CompletedAt = DateTime.UtcNow.AddSeconds(1),
                    RequestedByUserId = fixture.Visitor.UserId
                },
                new RouteCalculationAttempt
                {
                    VisitTripId = created.VisitTripId,
                    BasisType = "SubmittedSnapshot",
                    BasisVisitTripSnapshotId = currentSubmitted.VisitTripSnapshotId,
                    CalculationReason = "VisitorSubmit",
                    RequestedVehicleType = attempt.RequestedVehicleType,
                    TravelMode = attempt.TravelMode,
                    Provider = "GoogleMapsRoutes-TestStub",
                    StopCount = attempt.StopCount,
                    RequestBasisHash = SHA256.HashData("stale-route"u8.ToArray()),
                    Status = "Succeeded",
                    CorrelationId = Guid.NewGuid(),
                    RequestedAt = DateTime.UtcNow.AddSeconds(2),
                    CompletedAt = DateTime.UtcNow.AddSeconds(2),
                    RequestedByUserId = fixture.Visitor.UserId
                });
            var calculation = await db.MileageCalculations.SingleAsync(x => x.VisitTripId == created.VisitTripId);
            calculation.CalculationSource = null;
            calculation.SelectedRouteCalculationAttemptId = null;
            await db.SaveChangesAsync();
            attemptsBeforeApproval = await db.RouteCalculationAttempts.CountAsync(x => x.VisitTripId == created.VisitTripId);

            using (var fakeFallback = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(12.5m, pendingApproval.RowVersion, "client claims Google failed", "ManualFallback")))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, fakeFallback.StatusCode);
                Assert.Contains("F_B_MANUAL_FALLBACK_GOOGLE_SUCCESS_EXISTS", await fakeFallback.Content.ReadAsStringAsync());
            }

            using (var returnedResponse = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/return",
                new ReturnTripRequest("edit notes only", pendingApproval.RowVersion)))
            {
                Assert.True(returnedResponse.IsSuccessStatusCode, await returnedResponse.Content.ReadAsStringAsync());
                var returned = await returnedResponse.Content.ReadFromJsonAsync<TripDto>() ?? throw new InvalidOperationException("OPTIONC_PROVIDER_RETURN_MISSING");
                using var returnedEdit = new HttpRequestMessage(HttpMethod.Put, $"api/v1/trips/{created.VisitTripId}")
                {
                    Content = JsonContent.Create(Draft(fixture.Approved.LocationId, includeSecondStop: false) with
                    {
                        Notes = "returned edit; same route",
                        Stops = [new TripStopInput(fixture.Approved.LocationId, null, null, "Location", "fixture", "changed route address", "visit", null)]
                    })
                };
                returnedEdit.Headers.TryAddWithoutValidation("If-Match", returned.RowVersion);
                using var editedResponse = await visitor.SendAsync(returnedEdit);
                Assert.True(editedResponse.IsSuccessStatusCode, await editedResponse.Content.ReadAsStringAsync());
                var returnedEdited = await editedResponse.Content.ReadFromJsonAsync<TripDto>() ?? throw new InvalidOperationException("OPTIONC_PROVIDER_RETURN_EDIT_MISSING");
                Assert.Equal(12.5m, returnedEdited.SystemDistanceKm);
                using var resubmit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit")
                {
                    Content = JsonContent.Create(new SubmitTripRequest(false))
                };
                resubmit.Headers.TryAddWithoutValidation("If-Match", returnedEdited.RowVersion);
                using var resubmitted = await visitor.SendAsync(resubmit);
                Assert.True(resubmitted.IsSuccessStatusCode, await resubmitted.Content.ReadAsStringAsync());
                pendingApproval = await resubmitted.Content.ReadFromJsonAsync<TripDto>() ?? throw new InvalidOperationException("OPTIONC_PROVIDER_RESUBMIT_MISSING");
                Assert.Equal("PendingApproval", pendingApproval.Status);
                Assert.Equal(2, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted"));
                Assert.Equal("changed route edit", await db.VisitTripSnapshots.Where(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted").OrderBy(x => x.SnapshotVersion).Select(x => x.NotesSnapshot).FirstAsync());
            }

            using (var mismatchedProviderDistance = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(13.0m, pendingApproval.RowVersion, "provider source mismatch", "ProviderSuggested", attempt.RouteCalculationAttemptId)))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatchedProviderDistance.StatusCode);
                Assert.Contains("F_B_PROVIDER_DISTANCE_MISMATCH", await mismatchedProviderDistance.Content.ReadAsStringAsync());
            }
            using (var unchangedLeaderAdjusted = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(12.5m, pendingApproval.RowVersion, null, "LeaderAdjusted", attempt.RouteCalculationAttemptId,
                    "This reason is present but does not correspond to an adjustment.")))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, unchangedLeaderAdjusted.StatusCode);
                Assert.Contains("F_B_LEADER_ADJUSTMENT_NOT_DIFFERENT", await unchangedLeaderAdjusted.Content.ReadAsStringAsync());
            }
            using (var overlappingDraftResponse = await visitor.PostAsJsonAsync(
                "api/v1/trips",
                Draft(fixture.Approved.LocationId, includeSecondStop: true) with { Purpose = "overlap warning case" }))
            {
                Assert.True(overlappingDraftResponse.IsSuccessStatusCode, await overlappingDraftResponse.Content.ReadAsStringAsync());
                var overlappingDraft = await overlappingDraftResponse.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_OVERLAP_DRAFT_MISSING");
                Assert.True(overlappingDraft.HasTimeOverlapWarning);
                Assert.False(overlappingDraft.TimeOverlapConfirmed);
                using var forgedApproval = await leader.PostAsJsonAsync(
                    $"api/v1/trips/{created.VisitTripId}/approve",
                    new ApproveTripRequest(11.5m, pendingApproval.RowVersion, null, "LeaderAdjusted", attempt.RouteCalculationAttemptId,
                        "Travel time differs due to dispatch conditions."));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, forgedApproval.StatusCode);
                Assert.Contains("TRIP_OVERLAP_ACKNOWLEDGEMENT_REQUIRED", await forgedApproval.Content.ReadAsStringAsync());
            }
            using var approved = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(12.5m, pendingApproval.RowVersion,
                    "overlap confirmation remains separate", "LeaderAdjusted", attempt.RouteCalculationAttemptId,
                    OverlappingTripsAcknowledged: true));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, approved.StatusCode);
            Assert.Contains("F_B_LEADER_ADJUSTMENT_REASON_REQUIRED", await approved.Content.ReadAsStringAsync());
            using (var genericReason = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(11.5m, pendingApproval.RowVersion, null, "LeaderAdjusted",
                    attempt.RouteCalculationAttemptId, "aaaaaaaaaa", OverlappingTripsAcknowledged: true)))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, genericReason.StatusCode);
                Assert.Contains("F_B_LEADER_ADJUSTMENT_REASON_REQUIRED", await genericReason.Content.ReadAsStringAsync());
            }
            using var approvedWithReason = await leader.PostAsJsonAsync(
                $"api/v1/trips/{created.VisitTripId}/approve",
                new ApproveTripRequest(11.5m, pendingApproval.RowVersion,
                    null, "LeaderAdjusted", attempt.RouteCalculationAttemptId,
                    "Travel time differs due to dispatch conditions.", OverlappingTripsAcknowledged: true));
            Assert.True(approvedWithReason.IsSuccessStatusCode, await approvedWithReason.Content.ReadAsStringAsync());
            var approvedTrip = await approvedWithReason.Content.ReadFromJsonAsync<TripDto>()
                ?? throw new InvalidOperationException("OPTIONC_PROVIDER_APPROVED_RESPONSE_MISSING");
            Assert.Equal("Approved", approvedTrip.Status);
            Assert.Equal(11.5m, approvedTrip.ApprovedDistanceKm);
            Assert.Equal(34.5m, approvedTrip.ApprovedAmount);
            Assert.Equal(3, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId));
            Assert.Equal(2, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Submitted"));
            Assert.Equal(1, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == created.VisitTripId && x.SnapshotType == "Approved"));
            Assert.Equal(1, await db.MileageGovernanceEvents.CountAsync(x => x.VisitTripId == created.VisitTripId && x.EventType == "Approved"));
            var approvalAudit = await db.AuditLogs.SingleAsync(x => x.EntityId == created.VisitTripId.ToString() && x.Action == "TripApprove");
            Assert.Contains("LeaderAdjusted", approvalAudit.NewValues);
            Assert.Contains("Travel time differs due to dispatch conditions", approvalAudit.NewValues);
            Assert.Contains("OverlappingTripsAcknowledged\":true", approvalAudit.NewValues);
            Assert.Equal(attemptsBeforeApproval, await db.RouteCalculationAttempts.CountAsync(x => x.VisitTripId == created.VisitTripId));
            Assert.Equal(jobsBeforeApproval, await db.BackgroundJobItems.CountAsync(x => x.EntityType == "VisitTrip" && x.EntityId == created.VisitTripId.ToString()));

            async Task<(TripDto Trip, long AttemptId)> CreateBatchCandidate(DateOnly date, string purpose)
            {
                var draft = Draft(fixture.Approved.LocationId, includeSecondStop: false) with
                { VisitDate = date, Purpose = purpose };
                using var create = await visitor.PostAsJsonAsync("api/v1/trips", draft);
                Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
                var createdBatch = await create.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_BATCH_DRAFT_MISSING");
                using var preview = await visitor.PostAsync($"api/v1/trips/{createdBatch.VisitTripId}/route-preview", null);
                Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
                var previewResult = await preview.Content.ReadFromJsonAsync<V180RouteOrchestrationResult>()
                    ?? throw new InvalidOperationException("OPTIONC_BATCH_ROUTE_PREVIEW_MISSING");
                var currentDraft = await visitor.GetFromJsonAsync<TripDto>($"api/v1/trips/{createdBatch.VisitTripId}")
                    ?? throw new InvalidOperationException("OPTIONC_BATCH_REFRESH_MISSING");
                using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{createdBatch.VisitTripId}/submit")
                { Content = JsonContent.Create(new SubmitTripRequest(true)) };
                submitRequest.Headers.TryAddWithoutValidation("If-Match", currentDraft.RowVersion);
                using var submitResponse = await visitor.SendAsync(submitRequest);
                Assert.True(submitResponse.IsSuccessStatusCode, await submitResponse.Content.ReadAsStringAsync());
                var pending = await submitResponse.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_BATCH_SUBMIT_MISSING");
                Assert.Equal("PendingApproval", pending.Status);
                return (pending, previewResult.RouteCalculationAttemptId);
            }

            var normalCandidate = await CreateBatchCandidate(new DateOnly(2026, 10, 17), "normal non-overlap adjustment");
            using (var normalAdjusted = await leader.PostAsJsonAsync(
                $"api/v1/trips/{normalCandidate.Trip.VisitTripId}/approve",
                new ApproveTripRequest(11.5m, normalCandidate.Trip.RowVersion, null,
                    "LeaderAdjusted", normalCandidate.AttemptId, "Dispatch route conditions require this adjustment.")))
            {
                Assert.True(normalAdjusted.IsSuccessStatusCode, await normalAdjusted.Content.ReadAsStringAsync());
                var normalApproved = await normalAdjusted.Content.ReadFromJsonAsync<TripDto>()
                    ?? throw new InvalidOperationException("OPTIONC_NORMAL_LEADER_ADJUSTED_MISSING");
                Assert.Equal(11.5m, normalApproved.ApprovedDistanceKm);
                Assert.Equal(34.5m, normalApproved.ApprovedAmount);
                var normalAudit = await db.AuditLogs.SingleAsync(x =>
                    x.EntityId == normalCandidate.Trip.VisitTripId.ToString() && x.Action == "TripApprove");
                Assert.Contains("Dispatch route conditions require this adjustment", normalAudit.NewValues);
            }

            var batchOne = await CreateBatchCandidate(new DateOnly(2026, 10, 16), "batch adjustment one");
            var batchTwo = await CreateBatchCandidate(new DateOnly(2026, 10, 16), "batch adjustment two");
            using (var partialBatch = await leader.PostAsJsonAsync("api/v1/trips/batch-approve", new BatchApproveRequest([
                new BatchApproveItem(batchOne.Trip.VisitTripId, 11.5m, batchOne.Trip.RowVersion, "LeaderAdjusted", batchOne.AttemptId,
                    "aaaaaaaaaa", OverlappingTripsAcknowledged: true),
                new BatchApproveItem(batchTwo.Trip.VisitTripId, 13.5m, batchTwo.Trip.RowVersion, "LeaderAdjusted", batchTwo.AttemptId,
                    "Traffic conditions required this specific route adjustment.", OverlappingTripsAcknowledged: true)
            ])))
            {
                Assert.True(partialBatch.IsSuccessStatusCode, await partialBatch.Content.ReadAsStringAsync());
                var outcome = await partialBatch.Content.ReadFromJsonAsync<BatchApproveResult>()
                    ?? throw new InvalidOperationException("OPTIONC_BATCH_RESULT_MISSING");
                Assert.Equal(1, outcome.Success);
                Assert.Equal(1, outcome.Failed);
                Assert.Contains("F_B_LEADER_ADJUSTMENT_REASON_REQUIRED", string.Join(";", outcome.Errors));
            }
            using (var completeBatch = await leader.PostAsJsonAsync("api/v1/trips/batch-approve", new BatchApproveRequest([
                new BatchApproveItem(batchOne.Trip.VisitTripId, 11.5m, batchOne.Trip.RowVersion, "LeaderAdjusted", batchOne.AttemptId,
                    "Dispatch timing required this individual adjustment.", OverlappingTripsAcknowledged: true)
            ])))
            {
                Assert.True(completeBatch.IsSuccessStatusCode, await completeBatch.Content.ReadAsStringAsync());
                var outcome = await completeBatch.Content.ReadFromJsonAsync<BatchApproveResult>()
                    ?? throw new InvalidOperationException("OPTIONC_BATCH_RETRY_RESULT_MISSING");
                Assert.Equal(1, outcome.Success);
                Assert.Equal(0, outcome.Failed);
            }
            var batchAudits = await db.AuditLogs.Where(x =>
                    (x.EntityId == batchOne.Trip.VisitTripId.ToString() || x.EntityId == batchTwo.Trip.VisitTripId.ToString())
                    && x.Action == "TripApprove")
                .ToListAsync();
            Assert.Equal(2, batchAudits.Count);
            Assert.Contains(batchAudits, x => x.NewValues.Contains("Dispatch timing required this individual adjustment"));
            Assert.Contains(batchAudits, x => x.NewValues.Contains("Traffic conditions required this specific route adjustment"));
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

    private static async Task WaitUntilBlockedByAsync(
        string connectionString, int blockingSessionId,
        Task<HttpResponseMessage> request, string caseName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var monitor = new SqlConnection(builder.ConnectionString);
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !request.IsCompleted)
        {
            await using var command = new SqlCommand(
                "SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@spid AND session_id<>@@SPID",
                monitor);
            command.Parameters.AddWithValue("@spid", blockingSessionId);
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) > 0) return;
            await Task.Delay(50);
        }
        throw new InvalidOperationException($"TRIP_APPROVAL_REVOCATION_RACE_NOT_BLOCKED_{caseName}");
    }

    private sealed class FailedRouteProvider : IV180RouteProvider
    {
        public string ProviderName => "GoogleMapsRoutes-TestStub";

        public Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct) =>
            Task.FromResult(new V180RouteProviderResult(
                false, null, null, null, "PROVIDER_UNAVAILABLE", "isolated provider failure"));
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
