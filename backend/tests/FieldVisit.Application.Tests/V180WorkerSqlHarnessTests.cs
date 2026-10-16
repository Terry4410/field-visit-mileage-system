using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace FieldVisit.Application.Tests;

// Actual DI/services/HTTP/SQL; only the external network provider is controlled.
// EF EnsureCreated baseline is explicit: this is NOT canonical/UAT schema proof.
public sealed partial class V180TripMileageHttpSqlE2ETests
{
    private readonly ITestOutputHelper harnessOutput;
    public V180TripMileageHttpSqlE2ETests(ITestOutputHelper output) => harnessOutput = output;

    [Theory]
    [InlineData("own-earliest", 0, true)]
    [InlineData("other-earliest", 1, true)]
    [InlineData("both-before-persistence", 0, true)]
    [InlineData("failure-vs-success", 1, false)]
    [InlineData("one-applies-before-second-completes", 0, true)]
    public async Task Worker_sql_controlled_interleavings(string scenario, int first, bool aSucceeds)
    {
        await WithWorkerHarness(async h =>
        {
            await h.Enqueue();
            await h.Enqueue();
            var history = await h.History();
            h.Control.Armed = true;
            using var scopeA = h.Factory.Services.CreateScope();
            using var scopeB = h.Factory.Services.CreateScope();
            var a = scopeA.ServiceProvider.GetRequiredService<IBackgroundJobService>().ProcessNextAsync(h.Token);
            var ga = await h.Control.Arrived(0, h.Token);
            var b = scopeB.ServiceProvider.GetRequiredService<IBackgroundJobService>().ProcessNextAsync(h.Token);
            var gb = await h.Control.Arrived(1, h.Token);
            var gates = new[] { ga, gb };
            ga.Success = aSucceeds;
            gb.Distance = 99.75m; // detect an incorrect competing overwrite
            gates[first].ProviderRelease.TrySetResult();
            await gates[first].AtFinal.Task.WaitAsync(h.Token);
            if (scenario == "one-applies-before-second-completes")
            {
                ga.FinalRelease.TrySetResult();
                Assert.True(await a.WaitAsync(h.Token));
                gb.ProviderRelease.TrySetResult();
                await gb.AtFinal.Task.WaitAsync(h.Token);
            }
            else
            {
                gates[1 - first].ProviderRelease.TrySetResult();
                await gates[1 - first].AtFinal.Task.WaitAsync(h.Token);
            }
            // Serial release is deterministic and also avoids making the test
            // itself depend on SQL deadlock victim scheduling.
            ga.FinalRelease.TrySetResult();
            Assert.True(await a.WaitAsync(h.Token));
            gb.FinalRelease.TrySetResult();
            Assert.True(await b.WaitAsync(h.Token));
            await using var db = h.NewDb();
            var attempts = await db.RouteCalculationAttempts.Where(x => x.VisitTripId == h.TripId).ToListAsync(h.Token);
            Assert.Equal(3, attempts.Count); // initial failure plus both immutable outcomes
            Assert.Equal(aSucceeds ? 2 : 1, attempts.Count(x => x.Status == "Succeeded"));
            Assert.DoesNotContain(attempts, x => x.Status == "Pending");
            var workerAttempts = attempts.OrderBy(x => x.RouteCalculationAttemptId).Skip(1).ToArray();
            Assert.True(workerAttempts[first].CompletedAt <= workerAttempts[1 - first].CompletedAt);
            var applications = await db.VisitTripStatusHistories.CountAsync(x => x.VisitTripId == h.TripId && x.Action == "GoogleMapsMileageCalculated", h.Token);
            Assert.Equal(scenario is "one-applies-before-second-completes" or "failure-vs-success" ? 1 : 0, applications);
            var items = await db.BackgroundJobItems.OrderBy(x => x.BackgroundJobItemId).ToListAsync(h.Token);
            Assert.Equal(2, items.Count);
            Assert.All(items, x => Assert.Contains(x.Status, new[] { "Failed", "Succeeded" }));
            if (applications == 0)
            {
                Assert.All(items, x => Assert.Equal("MILEAGE_SUPERSEDED_BY_PROVIDER_SUCCESS", x.ErrorCode));
                Assert.Null(await db.MileageCalculations.Where(x => x.VisitTripId == h.TripId).Select(x => x.SystemDistanceKm).SingleAsync(h.Token));
            }
            else Assert.Equal(scenario == "failure-vs-success" ? 99.75m : 12.5m, await db.MileageCalculations.Where(x => x.VisitTripId == h.TripId).Select(x => x.SystemDistanceKm).SingleAsync(h.Token));
            await h.AssertCountersAndNoApprovals(db);
            Assert.Equal(history, await h.History());
            using var retry = await h.Leader.PostAsJsonAsync("api/v1/jobs/mileage", new MileageBatchRequest("Selected", null, null, [h.TripId]), h.Token);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, retry.StatusCode);
            harnessOutput.WriteLine($"SQL_WORKER_INTERLEAVING={scenario}; applications={applications}; attempts={attempts.Count}; history=UNCHANGED; retry=DENIED");
            if (applications == 0) harnessOutput.WriteLine("LIVENESS_HOLD=ANY_OTHER_SUCCESS_REJECTS_BOTH; no attempt distance is durably available for reconciliation");
        });
    }

    [Theory]
    [InlineData("route")]
    [InlineData("HR")]
    [InlineData("Role")]
    [InlineData("Team")]
    public async Task Worker_sql_provider_inflight_changes_fail_closed(string change)
    {
        await WithWorkerHarness(async h =>
        {
            await h.Enqueue();
            h.Control.Armed = true;
            using var scope = h.Factory.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>().ProcessNextAsync(h.Token);
            var gate = await h.Control.Arrived(0, h.Token);
            var history = await h.History();
            await using (var db = h.NewDb())
            {
                if (change == "route")
                {
                    // Simulate the committed result of return/edit/resubmit:
                    // append a new Submitted identity; never mutate history.
                    var original = await db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops).SingleAsync(x => x.VisitTripId == h.TripId, h.Token);
                    var copy = new VisitTripSnapshot();
                    db.Entry(copy).CurrentValues.SetValues(original);
                    copy.VisitTripSnapshotId = 0;
                    copy.SnapshotVersion++;
                    copy.StartDeploymentAddressSnapshot = "synthetic-changed-route";
                    foreach (var originalStop in original.Stops)
                    {
                        var stop = new VisitTripSnapshotStop();
                        db.Entry(stop).CurrentValues.SetValues(originalStop);
                        stop.VisitTripSnapshotStopId = 0;
                        stop.VisitTripSnapshotId = 0;
                        stop.Snapshot = copy;
                        copy.Stops.Add(stop);
                    }
                    db.VisitTripSnapshots.Add(copy);
                    await db.SaveChangesAsync(h.Token);
                }
                else if (change == "HR")
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE p SET EmploymentStatus=N'Leave' FROM dbo.EmploymentStatusPeriods p JOIN dbo.Employments e ON e.EmploymentId=p.EmploymentId WHERE e.LegacyUserId={h.LeaderId}", h.Token);
                else if (change == "Role")
                    await db.UserRoleAssignments.Where(x => x.UserId == h.LeaderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, new DateOnly(2020, 1, 2)), h.Token);
                else
                    await db.UserTeamAssignments.Where(x => x.UserId == h.LeaderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveTo, new DateOnly(2020, 1, 2)), h.Token);
            }
            gate.ProviderRelease.TrySetResult();
            await gate.AtFinal.Task.WaitAsync(h.Token);
            gate.FinalRelease.TrySetResult();
            Assert.True(await worker.WaitAsync(h.Token));
            await using var final = h.NewDb();
            Assert.Null(await final.MileageCalculations.Where(x => x.VisitTripId == h.TripId).Select(x => x.SystemDistanceKm).SingleAsync(h.Token));
            Assert.Equal(0, await final.VisitTripStatusHistories.CountAsync(x => x.VisitTripId == h.TripId && x.Action == "GoogleMapsMileageCalculated", h.Token));
            Assert.Equal("Failed", (await final.BackgroundJobItems.SingleAsync(h.Token)).Status);
            Assert.Equal(1, await final.RouteCalculationAttempts.CountAsync(x => x.VisitTripId == h.TripId && x.Status == "Succeeded", h.Token));
            // Existing snapshot bytes remain identical even when a new snapshot is appended.
            Assert.StartsWith(history[..^1], (await h.History()));
            await h.AssertCountersAndNoApprovals(final);
            harnessOutput.WriteLine($"SQL_PROVIDER_INFLIGHT={change}; application=0; immutable_attempt=Succeeded; item=Failed");
        });
    }

    [Theory]
    [InlineData(1205)]
    [InlineData(1222)]
    public async Task Worker_sql_real_fault_rolls_back_and_recovers(int number)
    {
        await WithWorkerHarness(async h =>
        {
            await h.Enqueue();
            var history = await h.History();
            h.Control.Armed = true;
            h.Control.FaultNumber = number;
            await using var blocker = new SqlConnection(h.ConnectionString);
            await blocker.OpenAsync(h.Token);
            await using var held = (SqlTransaction)await blocker.BeginTransactionAsync(h.Token);
            await using (var hold = new SqlCommand("UPDATE dbo.WorkerFaultProbe WITH (ROWLOCK) SET Value=Value+1 WHERE Id=2", blocker, held))
                await hold.ExecuteNonQueryAsync(h.Token);
            using var scope = h.Factory.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>().ProcessNextAsync(h.Token);
            var gate = await h.Control.Arrived(0, h.Token);
            gate.Success = false; // durable failure permits a safe subsequent retry
            gate.ProviderRelease.TrySetResult();
            await gate.AtFinal.Task.WaitAsync(h.Token);
            gate.FinalRelease.TrySetResult();
            await h.Control.FaultPrepared.Task.WaitAsync(h.Token);
            if (number == 1205)
            {
                // The application owns row 1 and requests row 2; the blocker
                // owns row 2 and requests row 1. Explicit victim priority.
                await using var cycle = new SqlCommand("SET DEADLOCK_PRIORITY HIGH; UPDATE dbo.WorkerFaultProbe WITH (ROWLOCK) SET Value=Value+1 WHERE Id=1", blocker, held) { CommandTimeout = 20 };
                await cycle.ExecuteNonQueryAsync(h.Token);
            }
            Assert.True(await worker.WaitAsync(h.Token));
            await held.RollbackAsync(h.Token);
            Assert.Equal(number, h.Control.ObservedFault);
            await using (var db = h.NewDb())
            {
                var item = await db.BackgroundJobItems.SingleAsync(h.Token);
                Assert.Equal("Failed", item.Status);
                Assert.Equal(number == 1205 ? "SQL_DEADLOCK_1205" : "SQL_LOCK_TIMEOUT_1222", item.ErrorCode);
                Assert.Equal(item.ErrorCode, item.ErrorMessage);
                Assert.Null(await db.MileageCalculations.Where(x => x.VisitTripId == h.TripId).Select(x => x.SystemDistanceKm).SingleAsync(h.Token));
                Assert.Equal("Submitted", (await db.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == h.TripId, h.Token)).Status);
                Assert.Equal(0, await db.VisitTripStatusHistories.CountAsync(x => x.VisitTripId == h.TripId && x.Action == "GoogleMapsFailedManualFallback", h.Token));
                await h.AssertCountersAndNoApprovals(db);
                await using var check = new SqlConnection(h.ConnectionString);
                await check.OpenAsync(h.Token);
                await using var query = new SqlCommand("SELECT SUM(Value) FROM dbo.WorkerFaultProbe", check);
                Assert.Equal(0, Convert.ToInt32(await query.ExecuteScalarAsync(h.Token)));
            }
            Assert.Equal(history, await h.History());
            using var status = await h.Leader.GetAsync($"api/v1/jobs/{h.JobIds[0]}", h.Token);
            Assert.True(status.IsSuccessStatusCode, await status.Content.ReadAsStringAsync(h.Token));
            Assert.DoesNotContain("WorkerFaultProbe", await status.Content.ReadAsStringAsync(h.Token));
            h.Control.Armed = false;
            h.Control.FaultNumber = 0;
            await h.Enqueue();
            using var recoveryScope = h.Factory.Services.CreateScope();
            Assert.True(await recoveryScope.ServiceProvider.GetRequiredService<IBackgroundJobService>().ProcessNextAsync(h.Token));
            await using var recovered = h.NewDb();
            await h.AssertCountersAndNoApprovals(recovered);
            Assert.Equal(2, await recovered.BackgroundJobs.CountAsync(h.Token));
            var recoveredJob = await recovered.BackgroundJobs.AsNoTracking().SingleAsync(x => x.BackgroundJobId == h.JobIds[1], h.Token);
            Assert.Equal("Succeeded", recoveredJob.Status);
            Assert.Equal(1, recoveredJob.SuccessCount);
            Assert.Equal(1, await recovered.VisitTripStatusHistories.CountAsync(x => x.VisitTripId == h.TripId && x.Action == "GoogleMapsFailedManualFallback", h.Token));
            Assert.DoesNotContain(await recovered.BackgroundJobs.ToListAsync(h.Token), x => x.Status is "Waiting" or "Processing");
            harnessOutput.WriteLine($"REAL_SQL_ERROR={number}; rollback=PASS; durable_worker_error=SAFE_CODE; recovery=PASS; HTTP409=NOT_APPLICABLE_BACKGROUND_WORKER");
        });
    }

    [Theory]
    [InlineData("SQL_1205 SELECT secret FROM Employees token=synthetic-token")]
    [InlineData("PROVIDER_CUSTOM_TOKEN_ABC alice@example.invalid national-id-123 customer-contract-99")]
    public async Task Worker_sql_provider_sensitive_payload_never_reaches_storage_or_http(string payload)
    {
        await WithWorkerHarness(async h =>
        {
            await h.Enqueue();
            h.Control.Armed = true;
            using var scope = h.Factory.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>().ProcessNextAsync(h.Token);
            var gate = await h.Control.Arrived(0, h.Token);
            gate.Success = false;
            gate.Payload = payload;
            gate.ProviderRelease.TrySetResult();
            await gate.AtFinal.Task.WaitAsync(h.Token);
            gate.FinalRelease.TrySetResult();
            Assert.True(await worker.WaitAsync(h.Token));
            await using var db = h.NewDb();
            var attempts = await db.RouteCalculationAttempts.Where(x => x.VisitTripId == h.TripId).ToListAsync(h.Token);
            var items = await db.BackgroundJobItems.ToListAsync(h.Token);
            var events = await db.MileageGovernanceEvents.Where(x => x.VisitTripId == h.TripId).ToListAsync(h.Token);
            var jobs = await db.BackgroundJobs.ToListAsync(h.Token);
            var persisted = JsonSerializer.Serialize(new { attempts, items, events, jobs });
            Assert.DoesNotContain(payload, persisted);
            foreach (var secret in payload.Split(' ')) Assert.DoesNotContain(secret, persisted);
            var last = attempts.OrderBy(x => x.RouteCalculationAttemptId).Last();
            Assert.Equal("PROVIDER_FAILURE", last.ErrorCode);
            Assert.Equal("PROVIDER_FAILURE", last.ErrorMessage);
            Assert.Equal("PROVIDER_FAILURE", events.Single(x => x.RouteCalculationAttemptId == last.RouteCalculationAttemptId).Message);
            using var response = await h.Leader.GetAsync($"api/v1/jobs/{h.JobIds[0]}", h.Token);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(h.Token));
            var body = await response.Content.ReadAsStringAsync(h.Token);
            foreach (var secret in payload.Split(' ')) Assert.DoesNotContain(secret, body);
            await h.AssertCountersAndNoApprovals(db);
            harnessOutput.WriteLine("SENSITIVE_PROVIDER_HTTP_SQL=PASS; provider=CONTROLLED_STUB; attempts/items/events/jobs/response=NO_RAW_PAYLOAD");
        });
    }

    private async Task WithWorkerHarness(Func<WorkerHarness, Task> run)
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1") throw new InvalidOperationException("WORKER_SQL_REQUIRED");
            harnessOutput.WriteLine("HOLD=DISPOSABLE_SQL_NOT_CONFIGURED");
            return;
        }
        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433") || root.InitialCatalog != "master" || root.UserID != "sa" || root.IntegratedSecurity)
            throw new InvalidOperationException("WORKER_SQL_NONLOCAL_TARGET_DENIED");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var name = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        await using var admin = new SqlConnection(root.ConnectionString);
        await admin.OpenAsync(timeout.Token);
        await using (var create = new SqlCommand($"CREATE DATABASE [{name}]", admin)) await create.ExecuteNonQueryAsync(timeout.Token);
        var control = new WorkerControl();
        try
        {
            var cs = new SqlConnectionStringBuilder(root.ConnectionString) { InitialCatalog = name }.ConnectionString;
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = cs,
                    ["Auth:Mode"] = "Demo", ["Auth:JwtKey"] = "test-key-that-is-at-least-32-characters-long",
                    ["OptionC:Candidate:Enabled"] = "true", ["Providers:Route"] = "Google",
                    ["GoogleMaps:ApiKey"] = "synthetic-unused-controlled-provider",
                    ["BackgroundJobs:WorkerEnabled"] = "false", ["BackgroundJobs:RecoverOnStartup"] = "false"
                };
                foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped<IV180RouteProvider>(sp => new WorkerProvider(control, sp.GetRequiredService<AppDbContext>()));
                    services.AddDbContext<AppDbContext>(options => options.UseSqlServer(cs).AddInterceptors(new WorkerInterceptor(control), new WorkerFaultInterceptor(control)));
                });
            });
            using var seedScope = factory.Services.CreateScope();
            var runtimeConfiguration = seedScope.ServiceProvider.GetRequiredService<IConfiguration>();
            Assert.Equal("Google", runtimeConfiguration["Providers:Route"]);
            Assert.Equal("false", runtimeConfiguration["BackgroundJobs:WorkerEnabled"]);
            Assert.IsType<WorkerProvider>(seedScope.ServiceProvider.GetRequiredService<IV180RouteProvider>());
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync(timeout.Token);
            var fixture = await SeedAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500),AppliedAt datetime2 NOT NULL,AppliedBy nvarchar(200));
                CREATE TABLE dbo.LocationCreateReservations(LocationCreateReservationId bigint IDENTITY PRIMARY KEY,LocationId int NOT NULL,ReservationStatus nvarchar(30) NOT NULL,ChangeRequestId bigint NULL);
                INSERT dbo.SchemaVersions VALUES(N'1.8.0-CREATE-LOWRISK',N'disposable worker test',SYSUTCDATETIME(),N'test');
                CREATE TABLE dbo.WorkerFaultProbe(Id int PRIMARY KEY,Value int NOT NULL);
                INSERT dbo.WorkerFaultProbe VALUES(1,0),(2,0);
                """, timeout.Token);
            using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Visitor.UserId, fixture.Visitor.EmployeeNo!, "visitor", fixture.Team.TeamId));
            leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(fixture.Leader.UserId, fixture.Leader.EmployeeNo!, "leader", fixture.Team.TeamId));
            using var createdResponse = await visitor.PostAsJsonAsync("api/v1/trips", Draft(fixture.Approved.LocationId, false), timeout.Token);
            Assert.True(createdResponse.IsSuccessStatusCode, await createdResponse.Content.ReadAsStringAsync(timeout.Token));
            var created = (await createdResponse.Content.ReadFromJsonAsync<TripDto>(timeout.Token))!;
            using var preview = await visitor.PostAsync($"api/v1/trips/{created.VisitTripId}/route-preview", null, timeout.Token);
            Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync(timeout.Token));
            using var submit = new HttpRequestMessage(HttpMethod.Post, $"api/v1/trips/{created.VisitTripId}/submit") { Content = JsonContent.Create(new SubmitTripRequest(false)) };
            submit.Headers.TryAddWithoutValidation("If-Match", created.RowVersion);
            using var submitted = await visitor.SendAsync(submit, timeout.Token);
            Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync(timeout.Token));
            await run(new WorkerHarness(factory, control, cs, leader, created.VisitTripId, fixture.Leader.UserId, timeout.Token));
        }
        finally
        {
            control.ReleaseAll();
            // Fresh connection/token ensures cleanup even if the scenario times out.
            await using var cleanup = new SqlConnection(root.ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", cleanup) { CommandTimeout = 30 };
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class WorkerHarness(WebApplicationFactory<Program> factory, WorkerControl control, string cs, HttpClient leader, long tripId, int leaderId, CancellationToken token)
    {
        public WebApplicationFactory<Program> Factory => factory;
        public WorkerControl Control => control;
        public string ConnectionString => cs;
        public HttpClient Leader => leader;
        public long TripId => tripId;
        public int LeaderId => leaderId;
        public CancellationToken Token => token;
        public List<Guid> JobIds { get; } = [];
        public AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
        public async Task Enqueue()
        {
            using var response = await leader.PostAsJsonAsync("api/v1/jobs/mileage", new MileageBatchRequest("Selected", null, null, [tripId]), token);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(token));
            JobIds.Add((await response.Content.ReadFromJsonAsync<BackgroundJobDto>(token))!.BackgroundJobId);
        }
        public async Task<string> History()
        {
            await using var db = NewDb();
            return JsonSerializer.Serialize(await db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops).Where(x => x.VisitTripId == tripId).OrderBy(x => x.SnapshotVersion)
                .Select(x => new { x.VisitTripSnapshotId, x.SnapshotVersion, x.VehicleTypeSnapshot, x.StartDeploymentAddressSnapshot, Stops = x.Stops.OrderBy(s => s.StopSequence).Select(s => new { s.VisitTripSnapshotStopId, s.AddressSnapshot, s.LocationId }).ToArray() }).ToArrayAsync(token));
        }
        public async Task AssertCountersAndNoApprovals(AppDbContext db)
        {
            foreach (var job in await db.BackgroundJobs.AsNoTracking().ToListAsync(token))
            {
                var items = await db.BackgroundJobItems.Where(x => x.BackgroundJobId == job.BackgroundJobId).ToListAsync(token);
                Assert.Equal(items.Count(x => x.Status == "Succeeded"), job.SuccessCount);
                Assert.Equal(items.Count(x => x.Status == "Failed"), job.FailedCount);
                Assert.Equal(job.SuccessCount + job.FailedCount, job.TotalCount);
                Assert.Equal(job.FailedCount > 0 ? "Failed" : "Succeeded", job.Status);
            }
            Assert.Equal(0, await db.ApprovalRecords.CountAsync(x => x.VisitTripId == tripId, token));
            Assert.Equal(0, await db.MileageGovernanceEvents.CountAsync(x => x.VisitTripId == tripId && (x.EventType == "Approved" || x.EventType == "ManualFallback"), token));
            Assert.Equal(0, await db.VisitTripSnapshots.CountAsync(x => x.VisitTripId == tripId && x.SnapshotType == "Approved", token));
            var governance = await db.MileageGovernanceEvents.Where(x => x.VisitTripId == tripId && x.RouteCalculationAttemptId != null).ToListAsync(token);
            Assert.All(governance.GroupBy(x => new { x.RouteCalculationAttemptId, x.EventType }), group => Assert.Single(group));
            Assert.Equal(JobIds.Count, await db.AuditLogs.CountAsync(x => x.EntityType == "BackgroundJob" && x.Action == "MileageJobEnqueued", token));
        }
    }

    private sealed class WorkerGate
    {
        public TaskCompletionSource ProviderRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AtFinal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinalRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Success { get; set; } = true;
        public decimal Distance { get; set; } = 12.5m;
        public string Payload { get; set; } = "synthetic provider failure";
    }
    private sealed class WorkerControl
    {
        public volatile bool Armed;
        public int FaultNumber;
        public int ObservedFault;
        public TaskCompletionSource FaultPrepared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<Guid, WorkerGate> Contexts { get; } = new();
        public TaskCompletionSource<WorkerGate>[] Slots { get; } = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        private int next = -1;
        public WorkerGate Enter(Guid context)
        {
            var gate = new WorkerGate();
            Contexts[context] = gate;
            Slots[Interlocked.Increment(ref next)].TrySetResult(gate);
            return gate;
        }
        public Task<WorkerGate> Arrived(int index, CancellationToken ct) => Slots[index].Task.WaitAsync(ct);
        public void ReleaseAll() { foreach (var gate in Contexts.Values) { gate.ProviderRelease.TrySetResult(); gate.FinalRelease.TrySetResult(); } }
    }
    private sealed class WorkerProvider(WorkerControl control, AppDbContext db) : IV180RouteProvider
    {
        public string ProviderName => "GoogleMapsRoutes-ControlledSqlStub";
        public async Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct)
        {
            if (!control.Armed) return new(false, null, null, null, "PROVIDER_UNAVAILABLE", "synthetic initial failure");
            var gate = control.Enter(db.ContextId.InstanceId);
            await gate.ProviderRelease.Task.WaitAsync(ct);
            return gate.Success ? new(true, gate.Distance, 300, "synthetic-polyline", null, null) : new(false, null, null, null, gate.Payload, gate.Payload);
        }
    }
    private sealed class WorkerInterceptor(WorkerControl control) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (!command.CommandText.Contains("WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal) || eventData.Context is null || !control.Contexts.TryGetValue(eventData.Context.ContextId.InstanceId, out var gate)) return result;
            gate.AtFinal.TrySetResult();
            await gate.FinalRelease.Task.WaitAsync(ct);
            return result;
        }
    }
    private sealed class WorkerFaultInterceptor(WorkerControl control) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        {
            var db = eventData.Context;
            if (control.FaultNumber == 0 || db?.Database.CurrentTransaction is null || !control.Contexts.ContainsKey(db.ContextId.InstanceId)) return result;
            // Fault after actual Trip/Mileage/StatusHistory writes have reached
            // SQL, before their owning application transaction commits.
            var connection = (SqlConnection)db.Database.GetDbConnection();
            var transaction = (SqlTransaction)db.Database.CurrentTransaction.GetDbTransaction();
            await using var prepare = new SqlCommand("SET DEADLOCK_PRIORITY LOW; SET LOCK_TIMEOUT 1500; UPDATE dbo.WorkerFaultProbe WITH (ROWLOCK) SET Value=Value+1 WHERE Id=1", connection, transaction);
            if (control.FaultNumber == 1205) prepare.CommandText = prepare.CommandText.Replace("1500", "20000");
            await prepare.ExecuteNonQueryAsync(ct);
            control.FaultPrepared.TrySetResult();
            try
            {
                await using var collide = new SqlCommand("UPDATE dbo.WorkerFaultProbe WITH (ROWLOCK) SET Value=Value+1 WHERE Id=2", connection, transaction) { CommandTimeout = 25 };
                await collide.ExecuteNonQueryAsync(ct);
                throw new InvalidOperationException("EXPECTED_SQL_FAULT_NOT_RAISED");
            }
            catch (SqlException ex) { control.ObservedFault = ex.Number; throw; }
        }
    }
}
