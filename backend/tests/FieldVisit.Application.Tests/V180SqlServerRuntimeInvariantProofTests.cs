using System.Diagnostics;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180SqlServerRuntimeInvariantProofTests
{
    private const string StructuralResource = "FieldVisit.TeamSiteCoverageInvariant";

    [Fact]
    public async Task Employment_status_and_employment_site_writers_preserve_invariant_under_contention()
    {
        if (!RuntimeRequired(out var root)) return;
        var fixture = await CreateFixtureAsync(root, includeTeamSite: true);
        await using var read = fixture.Context();
        var status = await read.EmploymentStatusPeriods.SingleAsync();
        var expectedVersion = Convert.ToBase64String(status.RowVersion);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statusTask = Task.Run(async () =>
        {
            await start.Task;
            await using var db = fixture.Context();
            var repo = new V180MasterDataAdminRepository(db);
            return await CaptureAsync(() => repo.SaveEmploymentStatusAsync(
                fixture.Admin,
                fixture.StatusId,
                new V180EmploymentStatusInput(
                    "E01",
                    EmploymentStatuses.Leave,
                    new DateOnly(2020, 1, 1),
                    null,
                    expectedVersion),
                default));
        });
        var siteTask = Task.Run(async () =>
        {
            await start.Task;
            await using var db = fixture.Context();
            var repo = new V180MasterDataAdminRepository(db);
            return await CaptureAsync(() => repo.SaveEmploymentSiteAsync(
                fixture.Admin,
                null,
                new V180EmploymentSiteInput(
                    "E01",
                    "S01",
                    true,
                    new DateOnly(2021, 1, 1),
                    null),
                default));
        });

        start.SetResult();
        var results = await Task.WhenAll(statusTask, siteTask);
        Assert.Single(results.Where(x => x is null));
        Assert.Single(results.Where(x => x is not null));
        Assert.Contains(
            results.Where(x => x is not null).Select(x => x!.Message),
            x => x is "EMPLOYMENT_STATUS_CHANGE_BREAKS_EMPLOYMENT_SITE"
                or "EMPLOYMENT_SITE_WITHOUT_ACTIVE_EMPLOYMENT");

        await using var verify = fixture.Context();
        var employmentSiteExists =
            await verify.EmploymentDeploymentSiteAssignments.AnyAsync();
        var activeStatusExists =
            await verify.EmploymentStatusPeriods.AnyAsync(x =>
                x.EmploymentId == fixture.EmploymentId
                && x.EmploymentStatus == EmploymentStatuses.Active
                && x.EffectiveFrom <= new DateOnly(2021, 1, 1)
                && !x.EffectiveTo.HasValue);

        Assert.False(employmentSiteExists && !activeStatusExists);
        Assert.Single(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Team_site_and_employment_site_writers_preserve_invariant_under_contention()
    {
        if (!RuntimeRequired(out var root)) return;
        var fixture = await CreateFixtureAsync(root, includeTeamSite: true);
        await using var read = fixture.Context();
        var teamSite = await read.TeamDeploymentSiteAssignments.SingleAsync();
        var expectedVersion = Convert.ToBase64String(teamSite.RowVersion);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var teamSiteTask = Task.Run(async () =>
        {
            await start.Task;
            await using var db = fixture.Context();
            var repo = new V180MasterDataAdminRepository(db);
            return await CaptureAsync(() => repo.SaveTeamSiteAsync(
                fixture.Admin,
                fixture.TeamSiteId,
                new V180TeamSiteInput(
                    "T01",
                    "S01",
                    new DateOnly(2020, 1, 1),
                    new DateOnly(2020, 12, 31),
                    expectedVersion),
                default));
        });
        var employmentSiteTask = Task.Run(async () =>
        {
            await start.Task;
            await using var db = fixture.Context();
            var repo = new V180MasterDataAdminRepository(db);
            return await CaptureAsync(() => repo.SaveEmploymentSiteAsync(
                fixture.Admin,
                null,
                new V180EmploymentSiteInput(
                    "E01",
                    "S01",
                    true,
                    new DateOnly(2021, 1, 1),
                    null),
                default));
        });

        start.SetResult();
        var results = await Task.WhenAll(teamSiteTask, employmentSiteTask);
        Assert.Single(results.Where(x => x is null));
        Assert.Single(results.Where(x => x is not null));
        Assert.Contains(
            results.Where(x => x is not null).Select(x => x!.Message),
            x => x is "TEAM_SITE_CHANGE_HAS_DEPENDENCIES"
                or "EMPLOYMENT_SITE_WITHOUT_TEAM_SITE_COVERAGE");

        await using var verify = fixture.Context();
        var siteAssignment =
            await verify.TeamDeploymentSiteAssignments.SingleAsync();
        var employmentSiteExists =
            await verify.EmploymentDeploymentSiteAssignments.AnyAsync();

        if (employmentSiteExists)
            Assert.Null(siteAssignment.EffectiveTo);
        else
            Assert.Equal(
                new DateOnly(2020, 12, 31),
                siteAssignment.EffectiveTo);

        Assert.Single(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Employment_lock_timeout_fails_closed_without_partial_audit_or_entity_commit()
    {
        if (!RuntimeRequired(out var root)) return;
        var fixture = await CreateFixtureAsync(root, includeTeamSite: true);
        await using var read = fixture.Context();
        var status = await read.EmploymentStatusPeriods.SingleAsync();
        var expectedVersion = Convert.ToBase64String(status.RowVersion);

        await using var blocker = new SqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync();
        await using var blockerTx = (SqlTransaction)await blocker.BeginTransactionAsync();
        var lockResult = await AcquireAppLockAsync(
            blocker,
            blockerTx,
            $"FieldVisit.E1.EmploymentStatusSite:{fixture.EmploymentId}",
            0);
        Assert.True(lockResult >= 0);

        var stopwatch = Stopwatch.StartNew();
        Exception? failure;
        await using (var db = fixture.Context())
        {
            var repo = new V180MasterDataAdminRepository(db);
            failure = await CaptureAsync(() => repo.SaveEmploymentStatusAsync(
                fixture.Admin,
                fixture.StatusId,
                new V180EmploymentStatusInput(
                    "E01",
                    EmploymentStatuses.Leave,
                    new DateOnly(2020, 1, 1),
                    null,
                    expectedVersion),
                default));
        }
        stopwatch.Stop();

        Assert.NotNull(failure);
        Assert.Equal(
            "EMPLOYMENT_STATUS_SITE_INVARIANT_LOCK_FAILED",
            failure!.Message);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20));

        await blockerTx.RollbackAsync();

        await using var verify = fixture.Context();
        Assert.Equal(
            EmploymentStatuses.Active,
            (await verify.EmploymentStatusPeriods.SingleAsync()).EmploymentStatus);
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Employment_site_acquires_structural_lock_before_employment_lock()
    {
        if (!RuntimeRequired(out var root)) return;
        var fixture = await CreateFixtureAsync(root, includeTeamSite: true);

        await using var structural = new SqlConnection(fixture.ConnectionString);
        await structural.OpenAsync();
        await using var structuralTx =
            (SqlTransaction)await structural.BeginTransactionAsync();
        Assert.True(await AcquireAppLockAsync(
            structural,
            structuralTx,
            StructuralResource,
            0) >= 0);

        var writer = Task.Run(async () =>
        {
            await using var db = fixture.Context();
            var repo = new V180MasterDataAdminRepository(db);
            return await CaptureAsync(() => repo.SaveEmploymentSiteAsync(
                fixture.Admin,
                null,
                new V180EmploymentSiteInput(
                    "E01",
                    "S01",
                    true,
                    new DateOnly(2021, 1, 1),
                    null),
                default));
        });

        await Task.Delay(750);
        Assert.False(writer.IsCompleted);

        await using var employment = new SqlConnection(fixture.ConnectionString);
        await employment.OpenAsync();
        await using var employmentTx =
            (SqlTransaction)await employment.BeginTransactionAsync();
        var employmentLock = await AcquireAppLockAsync(
            employment,
            employmentTx,
            $"FieldVisit.E1.EmploymentStatusSite:{fixture.EmploymentId}",
            0);
        Assert.True(
            employmentLock >= 0,
            "Employment lock was held while the structural lock was still blocked; lock order is not global -> employment.");
        await employmentTx.RollbackAsync();

        await structuralTx.RollbackAsync();
        var writerFailure = await writer;
        Assert.Null(writerFailure);

        await using var verify = fixture.Context();
        Assert.Single(await verify.EmploymentDeploymentSiteAssignments.ToListAsync());
    }

    [Fact]
    public async Task Retry_enabled_execution_strategy_owns_transaction_and_commits_entity_with_audit()
    {
        if (!RuntimeRequired(out var root)) return;
        var fixture = await CreateFixtureAsync(root, includeTeamSite: false);

        await using (var db = fixture.Context())
        {
            Assert.Null(db.Database.CurrentTransaction);
            var repo = new V180MasterDataAdminRepository(db);
            var result = await repo.SaveCenterAsync(
                fixture.Admin,
                null,
                new V180CenterInput(
                    "C02",
                    "Center 02",
                    new DateOnly(2020, 1, 1),
                    null,
                    true),
                default);
            Assert.Equal("C02", result.Key);
            Assert.Null(db.Database.CurrentTransaction);
        }

        await using var verify = fixture.Context();
        Assert.True(await verify.Centers.AnyAsync(x => x.CenterCode == "C02"));
        Assert.Single(await verify.AuditLogs.Where(x => x.EntityType == "Center").ToListAsync());
    }

    private static async Task<RuntimeFixture> CreateFixtureAsync(
        string rootConnectionString,
        bool includeTeamSite)
    {
        await WaitForSqlServerAsync(rootConnectionString);
        var root = new SqlConnectionStringBuilder(rootConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        var databaseName = $"FieldVisitE1R1_{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(root.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var target = new SqlConnectionStringBuilder(rootConnectionString)
        {
            InitialCatalog = databaseName,
            Pooling = false
        };

        var fixture = new RuntimeFixture(target.ConnectionString);
        await using var db = fixture.Context();
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var organization = new Organization
        {
            OrganizationCode = $"R1-{Guid.NewGuid():N}",
            OrganizationName = "E1-R1 Runtime",
            IsActive = true,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            CreatedAt = now
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var team = new Team
        {
            OrganizationId = organization.OrganizationId,
            TeamCode = "T01",
            TeamName = "Team 01",
            IsActive = true,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            CreatedAt = now
        };
        var adminUser = new User
        {
            OrganizationId = organization.OrganizationId,
            DisplayName = "Runtime Admin",
            Email = $"runtime-{Guid.NewGuid():N}@example.test",
            IsActive = true,
            CreatedAt = now
        };
        var location = new FieldVisit.Domain.Entities.Location
        {
            OrganizationId = organization.OrganizationId,
            LocationCode = "L01",
            LocationName = "Runtime Location",
            ApprovalStatus = "Approved",
            GeocodingStatus = "Succeeded",
            IsActive = true,
            CreatedAt = now
        };
        var center = new Center
        {
            OrganizationId = organization.OrganizationId,
            CenterCode = "C01",
            CenterName = "Center 01",
            EffectiveFrom = new DateOnly(2020, 1, 1),
            IsActive = true
        };
        var employment = new Employment
        {
            PersonId = 1,
            OrganizationId = organization.OrganizationId,
            EmployeeNo = "E01",
            SourceType = "Runtime"
        };
        db.AddRange(team, adminUser, location, center, employment);
        await db.SaveChangesAsync();

        adminUser.TeamId = team.TeamId;
        var status = new EmploymentStatusPeriod
        {
            EmploymentId = employment.EmploymentId,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            SourceType = "Runtime"
        };
        var membership = new TeamMembership
        {
            EmploymentId = employment.EmploymentId,
            TeamId = team.TeamId,
            IsPrimary = true,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            AssignedByUserId = adminUser.UserId
        };
        var teamCenter = new TeamCenterAssignment
        {
            TeamId = team.TeamId,
            CenterId = center.CenterId,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            CreatedAt = now,
            CreatedByUserId = adminUser.UserId
        };
        var site = new DeploymentSite
        {
            CenterId = center.CenterId,
            SiteCode = "S01",
            SiteName = "Site 01",
            EffectiveFrom = new DateOnly(2020, 1, 1),
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = adminUser.UserId
        };
        db.AddRange(status, membership, teamCenter, site);
        await db.SaveChangesAsync();

        var siteLocation = new DeploymentSiteLocationAssignment
        {
            DeploymentSiteId = site.DeploymentSiteId,
            LocationId = location.LocationId,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            CreatedAt = now,
            CreatedByUserId = adminUser.UserId
        };
        db.DeploymentSiteLocationAssignments.Add(siteLocation);

        TeamDeploymentSiteAssignment? teamSite = null;
        if (includeTeamSite)
        {
            teamSite = new TeamDeploymentSiteAssignment
            {
                TeamId = team.TeamId,
                DeploymentSiteId = site.DeploymentSiteId,
                EffectiveFrom = new DateOnly(2020, 1, 1),
                CreatedAt = now,
                CreatedByUserId = adminUser.UserId
            };
            db.TeamDeploymentSiteAssignments.Add(teamSite);
        }
        await db.SaveChangesAsync();

        fixture.Admin = new CurrentUserDto(
            adminUser.UserId,
            "RUNTIME-ADMIN",
            adminUser.DisplayName,
            adminUser.Email,
            organization.OrganizationId,
            team.TeamId,
            null,
            ["admin"]);
        fixture.EmploymentId = employment.EmploymentId;
        fixture.StatusId = status.EmploymentStatusPeriodId;
        fixture.TeamSiteId = teamSite?.TeamDeploymentSiteAssignmentId;
        return fixture;
    }

    private static async Task WaitForSqlServerAsync(string connectionString)
    {
        Exception? last = null;
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            Pooling = false,
            ConnectTimeout = 2
        };

        for (var attempt = 1; attempt <= 60; attempt++)
        {
            try
            {
                await using var connection =
                    new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }

        throw new InvalidOperationException(
            "E1_R1_SQL_SERVER_UNAVAILABLE",
            last);
    }

    private static async Task<int> AcquireAppLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string resource,
        int timeoutMs)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        command.Parameters.AddWithValue("@resource", resource);
        command.Parameters.AddWithValue("@timeout", timeoutMs);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static bool RuntimeRequired(out string connectionString)
    {
        connectionString =
            Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION")
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(connectionString))
            return true;

        if (string.Equals(
                Environment.GetEnvironmentVariable("E1_R1_REQUIRED"),
                "1",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "E1_R1_SQL_CONNECTION_REQUIRED");

        return false;
    }

    private sealed class RuntimeFixture(string connectionString)
    {
        public string ConnectionString { get; } = connectionString;
        public CurrentUserDto Admin { get; set; } = null!;
        public long EmploymentId { get; set; }
        public long StatusId { get; set; }
        public long? TeamSiteId { get; set; }

        public AppDbContext Context()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(
                    ConnectionString,
                    sql => sql.EnableRetryOnFailure(
                        3,
                        TimeSpan.FromSeconds(1),
                        null))
                .Options;
            return new AppDbContext(options);
        }
    }
}
