using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>Real SQL Server 2022 final decision gates on disposable OptionCIsolated databases.</summary>
public sealed class V180OptionCFinalSecurityGateSqlTests
{
    [Fact]
    public async Task Candidate_http_submission_approval_and_rejection_use_real_di_and_preserve_history()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        var locationBefore = await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=10");
        var tripRefsBefore = await h.Scalar("SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 ORDER BY VisitTripStopId FOR JSON PATH");
        var snapshotRefsBefore = await h.Scalar("SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 ORDER BY VisitTripSnapshotStopId FOR JSON PATH");
        var versionBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OptionC:Candidate:Enabled", "true");
            builder.UseSetting("ConnectionStrings:DefaultConnection", h.ConnectionString);
            builder.UseSetting("Auth:Mode", "Demo");
            builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
            builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
            builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
        });
        using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(1, "E1", "visitor"));
        var rowVersion = (byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=10", h.Db).ExecuteScalarAsync()
            ?? throw new InvalidOperationException("ROWVERSION_TEST_FIXTURE_MISSING"));
        var submit = await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/10/changes", new V180B3SubmitLocation(
            10, Convert.ToBase64String(rowVersion), "HTTP address correction",
            new V180B3LocationFields("Original 10", "Taipei", "Xinyi", "HTTP approved address", null, null, "HTTP note")));
        Assert.Equal(System.Net.HttpStatusCode.OK, submit.StatusCode);
        var submitted = await submit.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_SUBMIT_RESPONSE_MISSING");
        Assert.Equal("Pending", submitted.Status);
        await h.Exec("""
            UPDATE s SET GeocodeStatus=N'Succeeded',GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish',UpdatedAt=SYSUTCDATETIME()
            FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id;
            """, P("@id", submitted.RequestPublicId));
        var requestVersion = submitted.RowVersion;
        var approveKey = Guid.NewGuid();
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Authorization = new("Bearer", Token(2, "E2", "admin"));
        var approve = await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/approve", new V180B3Review(
            requestVersion, approveKey, "HTTP independent approval"));
        Assert.Equal(System.Net.HttpStatusCode.OK, approve.StatusCode);
        var applied = await approve.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_APPROVE_RESPONSE_MISSING");
        Assert.Equal("Applied", applied.Status);
        Assert.Equal("HTTP approved address", await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("10", await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("2", await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
        Assert.Equal("2", await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        Assert.Contains("Original address 10", await h.Scalar("SELECT PublishedJson FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND VersionNumber=1"));
        Assert.Equal(tripRefsBefore, await h.Scalar("SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 ORDER BY VisitTripStopId FOR JSON PATH"));
        Assert.Equal(snapshotRefsBefore, await h.Scalar("SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 ORDER BY VisitTripSnapshotStopId FOR JSON PATH"));
        Assert.Equal("1", versionBefore);

        var reconcile = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/reconcile?decisionKey={approveKey}");
        Assert.Equal(System.Net.HttpStatusCode.OK, reconcile.StatusCode);
        var reconcileBody = await reconcile.Content.ReadAsStringAsync();
        Assert.Contains("COMMITTED_EVIDENCE_REQUIRES_REVIEW", reconcileBody);
        using var outsider = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        outsider.DefaultRequestHeaders.Authorization = new("Bearer", Token(3, "E3", "admin"));
        var crossOrgReconcile = await outsider.GetAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/reconcile?decisionKey={approveKey}");
        Assert.NotEqual(System.Net.HttpStatusCode.OK, crossOrgReconcile.StatusCode);

        var secondVersion = (byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=11", h.Db).ExecuteScalarAsync()
            ?? throw new InvalidOperationException("ROWVERSION_TEST_FIXTURE_MISSING"));
        var submitReject = await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/11/changes", new V180B3SubmitLocation(
            11, Convert.ToBase64String(secondVersion), "HTTP reject correction",
            new V180B3LocationFields("Original 11", "Taipei", "Xinyi", "HTTP rejected address", null, null, "HTTP note")));
        Assert.Equal(System.Net.HttpStatusCode.OK, submitReject.StatusCode);
        var rejectedRequest = await submitReject.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_REJECT_SUBMIT_RESPONSE_MISSING");
        var reject = await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{rejectedRequest.RequestPublicId}/reject", new V180B3Review(
            rejectedRequest.RowVersion, Guid.NewGuid(), "HTTP independent rejection"));
        Assert.Equal(System.Net.HttpStatusCode.OK, reject.StatusCode);
        var rejected = await reject.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_REJECT_RESPONSE_MISSING");
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Abandoned", await h.StageStatus(rejected.RequestPublicId));
        Assert.Equal("Original address 11", await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=11"));
    }

    private static string Token(int userId, string employeeNo, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-characters-long"));
        var token = new JwtSecurityToken("FieldVisit.UAT", "FieldVisit.UAT.Users", new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, employeeNo),
            new Claim("employee_no", employeeNo), new Claim(ClaimTypes.Role, role)
        }, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Candidate_submission_persists_request_and_stage_without_publishing_location()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(h.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        await using var rowVersionCommand = new SqlCommand(
            "SELECT RowVersion FROM dbo.Locations WHERE LocationId=10", h.Db);
        var rowVersion = (byte[])(await rowVersionCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("ROWVERSION_TEST_FIXTURE_MISSING"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OptionC:Candidate:Enabled"] = "true"
        }).Build();
        var service = new V180OptionCCandidateService(db,
            new StubCurrentUser(new CurrentUserDto(1, "E1", "Visitor", null, 99, 99, null,
                new[] { "admin" }, new[] { new TeamScopeDto(99, "untrusted", true) })), config);
        var result = await service.SubmitAsync(new V180B3SubmitLocation(
            10, Convert.ToBase64String(rowVersion), "Address correction",
            new V180B3LocationFields("Original 10", "Taipei", "Xinyi",
                "New submitted address", null, null, "Low risk note")), CancellationToken.None);
        Assert.Equal("Pending", result.Status);
        Assert.Equal("PendingGeocode", await h.StageStatus(result.RequestPublicId));
        Assert.Equal("Original address 10", await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("1", await h.EventCount(result.RequestPublicId, "Submitted"));
    }

    [Fact]
    public async Task Live_authorization_adapter_reads_authoritative_hr_roles_and_team_inside_serializable_transaction()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(h.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var adapter = new V180OptionCLiveAuthorizationAdapter(db);
        var today = FieldVisit.Application.BusinessTime.Today;
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.AuthorizeDecisionAsync(
            1, 2, 1, 1, today, CancellationToken.None));

        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var evidence = await adapter.AuthorizeDecisionAsync(1, 2, 1, 1, today, CancellationToken.None);
            Assert.True(evidence.RequesterEligible);
            Assert.True(evidence.IndependentAdminEligible);
            Assert.True(evidence.OrganizationMatches);
            Assert.True(evidence.TeamMatches);
            await tx.RollbackAsync();
        }

        // A leave status is authoritative even though the SQL fixture's old
        // test-only actor row remains eligible.
        await h.Exec("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Leave' WHERE EmploymentId=101");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Active' WHERE EmploymentId=101");
        await h.Exec("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Leave' WHERE EmploymentId=102");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Active' WHERE EmploymentId=102");
        await h.Exec("UPDATE dbo.Employments SET TerminationDate=CONVERT(date,GETDATE()) WHERE EmploymentId=101");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.Employments SET TerminationDate=NULL WHERE EmploymentId=101");

        // A live Leader with a current role projection and dated appointment
        // is eligible on the same team, even with no Visitor role.
        await h.Exec("DELETE dbo.UserRoles WHERE UserId=1; INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(1,2,SYSUTCDATETIME()); UPDATE dbo.UserRoleAssignments SET RoleId=2 WHERE UserId=1; INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt) VALUES(1,101,'2000-01-01',SYSUTCDATETIME())");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var evidence = await adapter.AuthorizeDecisionAsync(1, 2, 1, 1, today, CancellationToken.None);
            Assert.True(evidence.RequesterEligible);
            await tx.RollbackAsync();
        }
        await h.Exec("DELETE dbo.TeamLeaderAssignments WHERE EmploymentId=101; DELETE dbo.UserRoles WHERE UserId=1; INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(1,1,SYSUTCDATETIME()); UPDATE dbo.UserRoleAssignments SET RoleId=1 WHERE UserId=1");

        // Organization and team reassignment are checked from current tables,
        // not claims or OptionCLiveApprovalActors.
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                3, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.UserTeamAssignments SET EffectiveTo=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE UserId=1");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.UserTeamAssignments SET EffectiveTo=NULL WHERE UserId=1");
        await h.Exec("UPDATE dbo.UserRoleAssignments SET EffectiveTo=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE UserId=1");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.UserRoleAssignments SET EffectiveTo=NULL WHERE UserId=1");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 3, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.UserTeamScopes SET TeamId=2 WHERE UserId=2; UPDATE dbo.UserTeamAssignments SET TeamId=2 WHERE UserId=2");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("UPDATE dbo.UserTeamScopes SET TeamId=1 WHERE UserId=2; UPDATE dbo.UserTeamAssignments SET TeamId=1 WHERE UserId=2");
        await h.Exec("DELETE dbo.UserIdentityProfiles WHERE UserId=1");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
        await h.Exec("INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt) VALUES(1,101,N'Internal',N'U1',N'Test',SYSUTCDATETIME())");

        // Serializable authorization reads retain locks through commit. A
        // concurrent HR revocation cannot commit between authorization and
        // publication on this connection.
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            _ = await adapter.AuthorizeDecisionAsync(1, 2, 1, 1, today, CancellationToken.None);
            var cs = new SqlConnectionStringBuilder(h.ConnectionString) { Pooling = false };
            await using var mutator = new SqlConnection(cs.ConnectionString);
            await mutator.OpenAsync();
            var spid = Convert.ToInt32(await new SqlCommand("SELECT @@SPID", mutator).ExecuteScalarAsync());
            var revoke = new SqlCommand("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Leave' WHERE EmploymentId=101", mutator);
            var revokeTask = revoke.ExecuteNonQueryAsync();
            await h.WaitForLockWait(spid, revokeTask);
            await tx.CommitAsync();
            Assert.Equal(1, await revokeTask.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        // Role revocation in the live effective assignment is fail-closed,
        // independently of stale JWT role claims or test-only actor flags.
        await h.Exec("UPDATE dbo.UserRoleAssignments SET EffectiveTo=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE UserId=2");
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.AuthorizeDecisionAsync(
                1, 2, 1, 1, today, CancellationToken.None));
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Execution_policy_requires_explicit_recovery_mode_local_host_and_isolated_database()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(h.Db);
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCExecutionPolicy.RequireIsolatedSqlTest(h.Db, "FutureUat"));
        var masterBuilder = new SqlConnectionStringBuilder(h.ConnectionString)
        { InitialCatalog = "master", Pooling = false };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            Assert.Throws<InvalidOperationException>(() =>
                V180OptionCExecutionPolicy.RequireIsolatedSqlTest(master));
        }
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCExecutionPolicy.RequireFutureUat(true, true, true));
        Assert.Throws<InvalidOperationException>(V180OptionCExecutionPolicy.RequireProduction);
    }

    [Fact]
    public async Task Reject_persists_terminal_request_stage_and_audit_without_publication_and_rolls_back_audit_failure()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        Assert.Equal("512", await h.Scalar("SELECT CONVERT(nvarchar(20),@@OPTIONS & 512)"));
        var locationBefore = await h.Scalar("""
            SELECT (SELECT LocationId,ApprovalStatus,IsActive,LocationName,Address,
              CONVERT(varchar(18),RowVersion,1) AS RowVersion FROM dbo.Locations
              WHERE LocationId=10 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """);
        var tripBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripStops WHERE LocationId=10");
        var snapshotBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10");
        var tripRefsBefore = await h.Scalar("SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 ORDER BY VisitTripStopId FOR JSON PATH");
        var snapshotRefsBefore = await h.Scalar("SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 ORDER BY VisitTripSnapshotStopId FOR JSON PATH");
        var versionBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10");
        var currentBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1");
        var request = await h.CreateRequest(10); await h.PrepareStage(request);
        // Reproduce the historical bug: ExecuteNonQuery loses row-count tokens
        // under NOCOUNT although SQL Server changed exactly one canonical row.
        await using (var tx=await h.Begin())
        {
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                  ReviewedByUserId=2,ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'Rowcount reproduction'
                WHERE RequestPublicId=@id AND Status=N'Pending';
                """,h.Db,tx);
            update.Parameters.AddWithValue("@id",request);
            Assert.Equal(-1,await update.ExecuteNonQueryAsync());
            await using var rowCount=new SqlCommand("SELECT CONVERT(nvarchar(20),@@ROWCOUNT)",h.Db,tx);
            Assert.Equal("1",Convert.ToString(await rowCount.ExecuteScalarAsync()));
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",await h.RequestStatus(request));
        var decision = Guid.NewGuid();
        await using (var tx = await h.Begin())
        {
            await Service().RejectAsync(h.Db, tx, request, 1, 2, decision, "Duplicate confirmed", CancellationToken.None);
            await tx.CommitAsync();
        }
        Assert.Equal("Rejected", await h.RequestStatus(request));
        Assert.Equal("2", await h.RequestValue(request,"ReviewedByUserId"));
        Assert.Equal("Abandoned", await h.StageStatus(request));
        Assert.Equal("1", await h.Scalar("""
            SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.RequestPublicId=@id AND e.EventType=N'Rejected'
              AND e.ActorUserId=2 AND e.DecisionKey=@key
              AND JSON_VALUE(e.DetailsJson,N'$.Reason')=N'Duplicate confirmed';
            """, P("@id",request),P("@key",decision)));
        Assert.Equal(locationBefore, await h.Scalar("""
            SELECT (SELECT LocationId,ApprovalStatus,IsActive,LocationName,Address,
              CONVERT(varchar(18),RowVersion,1) AS RowVersion FROM dbo.Locations
              WHERE LocationId=10 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """));
        Assert.Equal(tripBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripStops WHERE LocationId=10"));
        Assert.Equal(snapshotBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10"));
        Assert.Equal(tripRefsBefore, await h.Scalar("SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 ORDER BY VisitTripStopId FOR JSON PATH"));
        Assert.Equal(snapshotRefsBefore, await h.Scalar("SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 ORDER BY VisitTripSnapshotStopId FOR JSON PATH"));
        Assert.Equal(versionBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
        Assert.Equal(currentBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));

        // Same request replay and cross-request DecisionKey reuse are denied.
        await using (var tx = await h.Begin())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().RejectAsync(
                h.Db,tx,request,1,2,decision,"Replay",CancellationToken.None));
            await tx.RollbackAsync();
        }
        var second=await h.CreateRequest(11); await h.PrepareStage(second);
        await using (var tx = await h.Begin())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().RejectAsync(
                h.Db,tx,second,1,2,decision,"Reused key",CancellationToken.None));
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",await h.RequestStatus(second));
        Assert.Equal("ReadyForPublish",await h.StageStatus(second));

        // Inject actual audit-table failure after both canonical updates.
        var rollback=await h.CreateRequest(12); await h.PrepareStage(rollback);
        await h.Exec("""
            EXEC(N'CREATE TRIGGER dbo.TR_OptionC_TestRejectAuditFailure
              ON dbo.ChangeRequestEvents AFTER INSERT AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted WHERE EventType=N''Rejected'')
                  THROW 51991,N''OPTIONC_TEST_AUDIT_FAILURE'',1;
              END');
            """);
        await using (var tx = await h.Begin())
        {
            await Assert.ThrowsAsync<SqlException>(() => Service().RejectAsync(
                h.Db,tx,rollback,1,2,Guid.NewGuid(),"Audit failure",CancellationToken.None));
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",await h.RequestStatus(rollback));
        Assert.Equal("ReadyForPublish",await h.StageStatus(rollback));
        Assert.Equal("0",await h.EventCount(rollback,"Rejected"));
    }

    [Fact]
    public async Task Independent_connections_racing_approve_approve_approve_reject_and_reject_reject_commit_exactly_one()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var cases=new[]{("approve","approve",10),("approve","reject",11),("reject","reject",12)};
        foreach(var (firstDecision,secondDecision,location) in cases)
        {
            await h.ResetActors();
            var request=await h.CreateRequest(location); await h.PrepareStage(request);
            var gate=new GateProvider();
            var first=h.Decide(Service(gate),request,firstDecision,Guid.NewGuid(),null);
            var gateOrFirst=await Task.WhenAny(gate.Entered.Task,first);
            if(gateOrFirst==first){var ended=await first;throw new InvalidOperationException($"FIRST_DECISION_ENDED_BEFORE_GATE committed={ended.Committed} denied={ended.Denied}");}
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var ready=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second=h.Decide(Service(),request,secondDecision,Guid.NewGuid(),ready);
            var secondStart=await Task.WhenAny(ready.Task,second);
            if(secondStart==second){var ended=await second;throw new InvalidOperationException($"SECOND_DECISION_ENDED_BEFORE_SESSION committed={ended.Committed} denied={ended.Denied}");}
            var secondSpid=await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await h.WaitForLockWait(secondSpid,second);
            gate.Release.TrySetResult();
            var outcomes=await Task.WhenAll(first,second);
            Assert.Single(outcomes.Where(x=>x.Committed));
            Assert.Single(outcomes.Where(x=>x.Denied));
            var status=await h.RequestStatus(request);
            Assert.Contains(status,new[]{"Applied","Rejected"});
            Assert.Equal("1",await h.Scalar("""
                SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
                JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
                WHERE r.RequestPublicId=@id AND e.EventType IN(N'Approved',N'Rejected');
                """,P("@id",request)));
            Assert.Equal(status=="Applied"?"Published":"Abandoned",await h.StageStatus(request));
            Assert.Equal(status=="Applied"?"2":"1",await h.Scalar("""
                SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions
                WHERE LocationId=@location AND IsCurrent=1;
                """,P("@location",location)));
            Assert.Equal("1",await h.Scalar("""
                SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions
                WHERE LocationId=@location AND IsCurrent=1;
                """,P("@location",location)));
        }
    }

    [Fact]
    public async Task Reviewer_and_requester_hr_role_revocation_after_request_lock_is_fail_closed()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var cases=new[]
        {
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=0 WHERE UserId=2", // reviewer HR
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=1,IsIndependentAdminEligible=0 WHERE UserId=2", // reviewer role
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=0 WHERE UserId=1", // requester HR
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=1,IsRequesterEligible=0 WHERE UserId=1" // requester role
        };
        var location=13;
        foreach(var mutation in cases)
        {
            await h.ResetActors();
            var request=await h.CreateRequest(location++); await h.PrepareStage(request);
            var gate=new GateProvider(); var service=Service(gate);
            var running=h.Decide(service,request,"reject",Guid.NewGuid(),null);
            var gateOrRunning=await Task.WhenAny(gate.Entered.Task,running);
            if(gateOrRunning==running){var ended=await running;throw new InvalidOperationException($"REVOCATION_DECISION_ENDED_BEFORE_GATE committed={ended.Committed} denied={ended.Denied}");}
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await h.Exec(mutation); // committed on a separate SQL session before live evidence is read
            gate.Release.TrySetResult();
            var outcome=await running;
            Assert.True(outcome.Denied); Assert.False(outcome.Committed);
            Assert.Equal("Pending",await h.RequestStatus(request));
            Assert.Equal("ReadyForPublish",await h.StageStatus(request));
            Assert.Equal("0",await h.EventCount(request,"Rejected"));
        }
    }

    [Fact]
    public async Task Organization_and_team_mismatch_are_denied_without_request_or_stage_mutation()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        foreach(var (location,mutation) in new[]{
            (10,"UPDATE dbo.OptionCLiveApprovalActors SET OrganizationId=2 WHERE UserId=2"),
            (11,"UPDATE dbo.OptionCLiveApprovalActors SET TeamId=2 WHERE UserId=2")})
        {
            await h.ResetActors();
            var request=await h.CreateRequest(location); await h.PrepareStage(request);
            await h.Exec(mutation);
            await using var tx=await h.Begin();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Service().RejectAsync(
                h.Db,tx,request,1,2,Guid.NewGuid(),"Unauthorized",CancellationToken.None));
            await tx.RollbackAsync();
            Assert.Equal("Pending",await h.RequestStatus(request));
            Assert.Equal("ReadyForPublish",await h.StageStatus(request));
            Assert.Equal("0",await h.EventCount(request,"Rejected"));
        }
    }

    [Fact]
    public async Task Lost_commit_ack_is_reconciled_on_fresh_nonpooled_connection_without_retry()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var locationIdBefore=await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.Locations WHERE LocationId=10");
        var request=await h.CreateRequest(10); await h.PrepareStage(request);
        var key=Guid.NewGuid();
        var direct=new SqlConnectionStringBuilder(h.ConnectionString){Pooling=false,Encrypt=false};
        await using var proxy=new CommitAckLossProxy(direct.DataSource);
        var throughProxy=new SqlConnectionStringBuilder(direct.ConnectionString)
        { DataSource=$"127.0.0.1,{proxy.Port}",Pooling=false,ConnectTimeout=5 };
        var acknowledgementLost=false;
        await using(var c=new SqlConnection(throughProxy.ConnectionString))
        {
            await c.OpenAsync();
            await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
            await Service().ApproveAsync(c,tx,request,1,2,key,"Unknown commit fault injection",CancellationToken.None);
            proxy.ArmCommitAcknowledgementLoss();
            try { await tx.CommitAsync(); }
            catch(Exception ex) when(ex is SqlException or IOException or InvalidOperationException)
            { acknowledgementLost=proxy.CommitResponseDropped; }
        }
        Assert.True(acknowledgementLost,$"The proxy must forward the real SQL COMMIT then drop its acknowledgement. {proxy.Trace}");
        var observed=await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
            direct.ConnectionString,request,key,2,CancellationToken.None);
        Assert.Equal("COMMITTED_EVIDENCE_REQUIRES_REVIEW",observed);
        Assert.Equal("Applied",await h.RequestStatus(request));
        Assert.Equal("2",await h.RequestValue(request,"ReviewedByUserId"));
        Assert.Equal("Published",await h.StageStatus(request));
        Assert.Equal(locationIdBefore,await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("Changed 10",await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("2",await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        Assert.Equal("2",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
        Assert.Equal("1",await h.Scalar("""
            SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.RequestPublicId=@id AND e.EventType=N'Approved'
              AND e.DecisionKey=@key AND e.ActorUserId=2;
            """,P("@id",request),P("@key",key)));
        Assert.Equal("1",await h.Scalar("""
            SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.RequestPublicId=@id AND e.EventType=N'Applied' AND e.ActorUserId=2;
            """,P("@id",request)));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND VersionNumber=1"));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND VersionNumber=2 AND IsCurrent=1"));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripStops WHERE LocationId=10"));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10"));
        Assert.Equal("INDETERMINATE",await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
            direct.ConnectionString,request,key,3,CancellationToken.None));
        Assert.True(proxy.CommitRequestsForwarded>0,$"No encrypted application packet was forwarded. {proxy.Trace}");
    }

    [Fact]
    public async Task Reconciler_distinguishes_confirmed_rollback_from_indeterminate_evidence()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var request=await h.CreateRequest(10); await h.PrepareStage(request);
        var key=Guid.NewGuid();
        await using(var tx=await h.Begin())
        {
            await Service().ApproveAsync(h.Db,tx,request,1,2,key,"Explicit rollback evidence",CancellationToken.None);
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",await h.RequestStatus(request));
        Assert.Equal("",await h.RequestValue(request,"ReviewedByUserId"));
        Assert.Equal("0",await h.EventCount(request,"Approved"));
        Assert.Equal("0",await h.EventCount(request,"Applied"));
        Assert.Equal("ReadyForPublish",await h.StageStatus(request));
        Assert.Equal("NO_COMMIT_EVIDENCE_REQUIRES_REVIEW",
            await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
                h.ConnectionString,request,key,2,CancellationToken.None));
        Assert.Equal("NO_COMMIT_EVIDENCE_REQUIRES_REVIEW",
            await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
                h.ConnectionString,request,Guid.NewGuid(),2,CancellationToken.None));
    }

    private sealed class CommitAckLossProxy : IAsyncDisposable
    {
        private readonly System.Net.Sockets.TcpListener listener;
        private readonly CancellationTokenSource stop=new();
        private readonly Task loop;
        private System.Net.Sockets.TcpClient? activeClient;
        private int commitArmed;
        private int commitRequests;
        private int responseDropped;
        private int dropNext;
        private int closeScheduled;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> trace=new();
        private readonly TaskCompletionSource requestForwarded=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Trace=>string.Join(";",trace);
        internal int Port=>((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        internal int CommitRequestsForwarded=>Volatile.Read(ref commitRequests);
        internal bool CommitResponseDropped=>Volatile.Read(ref responseDropped)==1;
        private readonly string targetHost;
        private readonly int targetPort;
        internal CommitAckLossProxy(string target)
        {
            var comma=target.LastIndexOf(',');
            targetHost="127.0.0.1";targetPort=int.Parse(target[(comma+1)..]);
            listener=new(System.Net.IPAddress.Loopback,0);listener.Start();
            loop=RunAsync();
        }
        internal void ArmCommitAcknowledgementLoss()
        {
            if(Interlocked.Exchange(ref commitArmed,1)!=0)
                throw new InvalidOperationException("OPTIONC_COMMIT_ACK_FAULT_ALREADY_ARMED");
        }
        private async Task RunAsync()
        {
            try
            {
                using var client=await listener.AcceptTcpClientAsync(stop.Token);activeClient=client;
                using var server=new System.Net.Sockets.TcpClient();
                await server.ConnectAsync(targetHost,targetPort,stop.Token);
                var toServer=PumpAsync(client.GetStream(),server.GetStream(),true);
                var toClient=PumpAsync(server.GetStream(),client.GetStream(),false);
                await Task.WhenAny(toServer,toClient);
                client.Close();server.Close();
            }
            catch(OperationCanceledException) { }
            catch(System.Net.Sockets.SocketException) when(stop.IsCancellationRequested) { }
        }
        private async Task PumpAsync(System.Net.Sockets.NetworkStream input,System.Net.Sockets.NetworkStream output,bool clientToServer)
        {
            var buffer=new byte[8192];var packet=new List<byte>();
            while(!stop.IsCancellationRequested)
            {
                int n;try{n=await input.ReadAsync(buffer,stop.Token);}catch{return;} if(n==0)return;
                if(!clientToServer&&(Volatile.Read(ref dropNext)==1||Volatile.Read(ref commitArmed)==1))
                {
                    await requestForwarded.Task.WaitAsync(stop.Token);
                    Interlocked.Exchange(ref dropNext,0);
                    Interlocked.Exchange(ref responseDropped,1);trace.Enqueue("DROP:commit-ack-bytes");activeClient?.Close();return;
                }
                for(int i=0;i<n;i++)packet.Add(buffer[i]);
                while(packet.Count>=8)
                {
                    int length=(packet[2]<<8)|packet[3];if(length<8||packet.Count<length)break;
                    var current=packet.Take(length).ToArray();packet.RemoveRange(0,length);
                    if(clientToServer)
                    {
                        trace.Enqueue($"C:{current[0]:X2}:{Convert.ToHexString(current.Skip(8).Take(24).ToArray())}");
                        if(current[0]==0x17)Interlocked.Increment(ref commitRequests);
                    }
                    else trace.Enqueue($"S:{current[0]:X2}:{Convert.ToHexString(current.Skip(8).Take(24).ToArray())}");
                    if(clientToServer&&Interlocked.Exchange(ref commitArmed,0)==1)
                    {
                        // The test arms immediately before SqlTransaction.CommitAsync.
                        // Therefore this first complete TDS request is the real
                        // SQL COMMIT operation, regardless of SqlClient's packet type.
                        Interlocked.Increment(ref commitRequests);
                        Interlocked.Exchange(ref dropNext,1);
                    }
                }
                if(clientToServer)
                {
                    await output.WriteAsync(buffer.AsMemory(0,n),stop.Token);await output.FlushAsync(stop.Token);
                    if(Volatile.Read(ref commitArmed)==1 && Interlocked.Exchange(ref closeScheduled,1)==0)
                    {
                        requestForwarded.TrySetResult();
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(250);
                            Interlocked.Exchange(ref responseDropped,1);
                            trace.Enqueue("DROP:client-ack-socket-closed-after-forward");
                            activeClient?.Close();
                        });
                    }
                }
                else
                {
                    await output.WriteAsync(buffer.AsMemory(0,n),stop.Token);await output.FlushAsync(stop.Token);
                }
            }
        }
        public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();activeClient?.Close();try{await loop.WaitAsync(TimeSpan.FromSeconds(2));}catch{}stop.Dispose();}
    }

    private static FieldVisit.Infrastructure.V180OptionCAdminApprovalService Service(
        FieldVisit.Infrastructure.IV180OptionCLiveApprovalEvidenceProvider? provider=null) =>
        new(provider??new FieldVisit.Infrastructure.V180OptionCIsolatedApprovalEvidenceProvider());

    private sealed class GateProvider:FieldVisit.Infrastructure.IV180OptionCLiveApprovalEvidenceProvider
    {
        internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly FieldVisit.Infrastructure.V180OptionCIsolatedApprovalEvidenceProvider inner=new();
        public async Task<FieldVisit.Infrastructure.V180OptionCLiveApprovalEvidence> ReadAsync(
            SqlConnection c,SqlTransaction t,long id,int requester,int reviewer,int org,int? team,CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await inner.ReadAsync(c,t,id,requester,reviewer,org,team,ct);
        }
    }

    private sealed class Harness:IAsyncDisposable
    {
        private readonly SqlConnection root; private readonly string name;
        internal SqlConnection Db{get;}
        internal string ConnectionString{get;}
        private Harness(SqlConnection root,SqlConnection db,string name,string connectionString){this.root=root;Db=db;this.name=name;ConnectionString=connectionString;}

        internal static async Task<Harness?> TryCreateAsync()
        {
            var raw=Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(raw))
            {
                if(Environment.GetEnvironmentVariable("E1_R1_REQUIRED")=="1")
                    throw new InvalidOperationException("OPTIONC_FINAL_GATES_SQL_REQUIRED");
                return null;
            }
            var rb=new SqlConnectionStringBuilder(raw){Pooling=false,ConnectTimeout=5};
            if(rb.DataSource is not("localhost,1433" or "127.0.0.1,1433")||rb.InitialCatalog!="master"||rb.UserID!="sa"||rb.IntegratedSecurity)
                throw new InvalidOperationException("OPTIONC_FINAL_GATES_NONLOCAL_TARGET_DENIED");
            var root=new SqlConnection(rb.ConnectionString);await root.OpenAsync();
            var name="OptionCIsolated_"+Guid.NewGuid().ToString("N");await V180OptionCFinalSecurityGateSqlTests.Exec(root,$"CREATE DATABASE [{name}]");
            var dbb=new SqlConnectionStringBuilder(rb.ConnectionString){InitialCatalog=name,Pooling=false};
            var db=new SqlConnection(dbb.ConnectionString);
            try
            {
                await db.OpenAsync();await CreateBase(db);
                var repo=FindRoot();
                foreach(var p in new[]{"1800_011/Up.sql","1800_012/Up.sql","1800_014/Preflight.sql","1800_014/Up.sql","1800_014/Backfill.sql"})
                    await V180OptionCFinalSecurityGateSqlTests.Exec(db,File.ReadAllText(Path.Combine(repo,"docs","release","optionc-canonical-sql-draft",p.Replace('/',Path.DirectorySeparatorChar))));
                return new Harness(root,db,name,dbb.ConnectionString);
            }
            catch{await db.DisposeAsync();await V180OptionCFinalSecurityGateSqlTests.Exec(root,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");await root.DisposeAsync();throw;}
        }

        internal async Task<SqlTransaction> Begin()=> (SqlTransaction)await Db.BeginTransactionAsync(IsolationLevel.Serializable);
        internal async Task ResetActors()=>await Exec("UPDATE dbo.OptionCLiveApprovalActors SET OrganizationId=1,TeamId=1,IsActive=1,IsRequesterEligible=1,IsIndependentAdminEligible=CASE WHEN UserId=2 THEN 1 ELSE 0 END WHERE UserId IN(1,2)");
        internal async Task<Guid> CreateRequest(int location)
        {
            var id=Guid.NewGuid();
            await Exec("""
                INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                SELECT @id,OrganizationId,TeamId,N'Location',CONVERT(nvarchar(80),LocationId),N'UpdatePublishedLocation',N'High',RowVersion,N'{}',@json,1,SYSUTCDATETIME(),N'Pending' FROM dbo.Locations WHERE LocationId=@location;
                INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId)
                SELECT ChangeRequestId,N'Submitted',1,SYSUTCDATETIME(),NEWID() FROM dbo.ChangeRequests WHERE RequestPublicId=@id;
                """,P("@id",id),P("@location",location),
                P("@json",System.Text.Json.JsonSerializer.Serialize(new { LocationName=$"Changed {location}", City="Taipei", District="Xinyi", Address=$"New address {location}", PlusCode=(string?)null, TaxId=(string?)null, MasterNote=(string?)null })));
            return id;
        }
        internal async Task PrepareStage(Guid id)
        {
            await using(var tx=await Begin()){await FieldVisit.Infrastructure.V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(Db,tx,id,1,CancellationToken.None);await tx.CommitAsync();}
            await Exec("""
                UPDATE s SET GeocodeStatus=N'Succeeded',GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish',UpdatedAt=SYSUTCDATETIME()
                FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id;
                """,P("@id",id));
        }
        internal async Task<string> RequestStatus(Guid id)=>await Scalar("SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",P("@id",id));
        internal async Task<string> RequestValue(Guid id,string column)=>await Scalar($"SELECT CONVERT(nvarchar(200),{column}) FROM dbo.ChangeRequests WHERE RequestPublicId=@id",P("@id",id));
        internal async Task<string> StageStatus(Guid id)=>await Scalar("SELECT s.StageStatus FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",P("@id",id));
        internal async Task<string> EventCount(Guid id,string type)=>await Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId WHERE r.RequestPublicId=@id AND e.EventType=@type",P("@id",id),P("@type",type));
        internal async Task<string> Scalar(string sql,params SqlParameter[] ps)=>await ScalarOn(Db,sql,ps);
        internal async Task Exec(string sql,params SqlParameter[] ps)=>await ExecOn(Db,sql,ps);

        internal async Task<Outcome> Decide(FieldVisit.Infrastructure.V180OptionCAdminApprovalService service,Guid request,string kind,Guid key,TaskCompletionSource<int>? sessionReady)
        {
            var b=new SqlConnectionStringBuilder(ConnectionString){Pooling=false};
            await using var c=new SqlConnection(b.ConnectionString);await c.OpenAsync();
            await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
            if(sessionReady is not null)
            {
                await using var spid=new SqlCommand("SELECT @@SPID",c,tx);
                sessionReady.TrySetResult(Convert.ToInt32(await spid.ExecuteScalarAsync()));
            }
            try
            {
                if(kind=="approve")await service.ApproveAsync(c,tx,request,1,2,key,"Concurrent decision",CancellationToken.None);
                else await service.RejectAsync(c,tx,request,1,2,key,"Concurrent decision",CancellationToken.None);
                await tx.CommitAsync();return new(true,false);
            }
            catch(UnauthorizedAccessException){await tx.RollbackAsync(CancellationToken.None);return new(false,true);}
        }
        internal async Task WaitForLockWait(int spid,Task second)
        {
            for(var i=0;i<300;i++)
            {
                if(await Scalar("SELECT CONVERT(nvarchar(2),CASE WHEN EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE session_id=@id AND blocking_session_id<>0) THEN 1 ELSE 0 END)",P("@id",spid))=="1")return;
                if(second.IsCompleted)throw new InvalidOperationException("OPTIONC_AUTHORIZATION_CHANGE_FINISHED_WITHOUT_LOCK_WAIT");
                await Task.Delay(50);
            }
            var detail=await Scalar("SELECT COALESCE((SELECT CONCAT(status,N'|',COALESCE(wait_type,N''),N'|',blocking_session_id) FROM sys.dm_exec_requests WHERE session_id=@id),N'NO_ACTIVE_REQUEST')",P("@id",spid));
            throw new TimeoutException($"OPTIONC_CONCURRENT_DECISION_NEVER_BLOCKED spid={spid} detail={detail}");
        }
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await ExecOn(root,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");await root.DisposeAsync();}

        private static async Task CreateBase(SqlConnection c)
        {
            var locations=string.Join(",",Enumerable.Range(10,10).Select(i=>$"({i},1,1,1,N'Customer',N'Approved',1,N'Original {i}',N'Original address {i}')"));
            await ExecOn(c,$"""
                CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
                CREATE TABLE dbo.Organizations(OrganizationId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Teams(TeamId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamCode nvarchar(50) NOT NULL,TeamName nvarchar(200) NOT NULL,IsActive bit NOT NULL,EffectiveFrom date NULL,EffectiveTo date NULL,CreatedAt datetime2 NOT NULL,UpdatedAt datetime2 NULL);
                CREATE TABLE dbo.Users(UserId int NOT NULL PRIMARY KEY,OrganizationId int NULL,TeamId int NULL,EmployeeNo nvarchar(50) NULL,DisplayName nvarchar(200) NOT NULL,Email nvarchar(320) NULL,EntraObjectId uniqueidentifier NULL,IsActive bit NOT NULL,CreatedAt datetime2 NOT NULL,UpdatedAt datetime2 NULL);
                CREATE TABLE dbo.Roles(RoleId int NOT NULL PRIMARY KEY,RoleCode nvarchar(50) NOT NULL,RoleName nvarchar(100) NOT NULL,Description nvarchar(500) NULL,IsActive bit NOT NULL,CreatedAt datetime2 NOT NULL);
                CREATE TABLE dbo.UserRoles(UserRoleId int IDENTITY PRIMARY KEY,UserId int NOT NULL,RoleId int NOT NULL,AssignedAt datetime2 NOT NULL);
                CREATE TABLE dbo.UserIdentityProfiles(UserId int NOT NULL PRIMARY KEY,EmploymentId bigint NULL,UserType nvarchar(20) NOT NULL,UserCode nvarchar(100) NOT NULL,IdentityProvider nvarchar(30) NOT NULL,EntraTenantId uniqueidentifier NULL,EntraObjectId uniqueidentifier NULL,ExternalOrganization nvarchar(200) NULL,ExternalTitle nvarchar(200) NULL,AuthorizationFrom date NULL,AuthorizationTo date NULL,CreatedAt datetime2 NOT NULL,UpdatedAt datetime2 NULL);
                CREATE TABLE dbo.Employments(EmploymentId bigint NOT NULL PRIMARY KEY,PersonId bigint NOT NULL,OrganizationId int NOT NULL,EmployeeNo nvarchar(50) NULL,Email nvarchar(320) NULL,HireDate date NULL,TerminationDate date NULL,LegacyUserId int NULL,SourceType nvarchar(30) NOT NULL,SourceReference nvarchar(200) NULL,CreatedAt datetime2 NOT NULL,CreatedByUserId int NULL,UpdatedAt datetime2 NULL,UpdatedByUserId int NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.EmploymentStatusPeriods(EmploymentStatusPeriodId bigint IDENTITY PRIMARY KEY,EmploymentId bigint NOT NULL,EmploymentStatus nvarchar(30) NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,SourceType nvarchar(30) NOT NULL,SourceReference nvarchar(200) NULL,CreatedAt datetime2 NOT NULL,CreatedByUserId int NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.UserEmploymentPeriods(UserEmploymentPeriodId bigint IDENTITY PRIMARY KEY,UserId int NOT NULL,EmploymentStatus nvarchar(30) NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,SourceType nvarchar(30) NOT NULL,SourceReference nvarchar(200) NULL,CreatedAt datetime2 NOT NULL,UpdatedAt datetime2 NULL);
                CREATE TABLE dbo.UserRoleAssignments(UserRoleAssignmentId bigint IDENTITY PRIMARY KEY,UserId int NOT NULL,RoleId int NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2 NOT NULL);
                CREATE TABLE dbo.UserTeamScopes(UserTeamScopeId int IDENTITY PRIMARY KEY,UserId int NOT NULL,TeamId int NOT NULL,IsPrimary bit NOT NULL,IsActive bit NOT NULL,AssignedAt datetime2 NOT NULL,AssignedByUserId int NULL,EndedAt datetime2 NULL);
                CREATE TABLE dbo.UserTeamAssignments(UserTeamAssignmentId bigint IDENTITY PRIMARY KEY,UserId int NOT NULL,TeamId int NOT NULL,IsPrimary bit NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2 NOT NULL);
                CREATE TABLE dbo.TeamMemberships(TeamMembershipId bigint IDENTITY PRIMARY KEY,EmploymentId bigint NOT NULL,TeamId int NOT NULL,IsPrimary bit NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,ChangeReason nvarchar(500) NULL,AssignedByUserId int NULL,CreatedAt datetime2 NOT NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.TeamLeaderAssignments(TeamLeaderAssignmentId bigint IDENTITY PRIMARY KEY,TeamId int NOT NULL,EmploymentId bigint NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2 NOT NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.TeamLeaderDelegations(TeamLeaderDelegationId bigint IDENTITY PRIMARY KEY,TeamLeaderAssignmentId bigint NOT NULL,DelegateEmploymentId bigint NOT NULL,EffectiveFrom date NOT NULL,EffectiveTo date NOT NULL,Reason nvarchar(500) NULL,CreatedAt datetime2 NOT NULL,CreatedByUserId int NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.Locations(LocationId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NOT NULL,LocationCode nvarchar(80) NULL,CreatedByUserId int NOT NULL,LocationType nvarchar(20) NOT NULL,PostalCode nvarchar(20) NULL,City nvarchar(100) NULL,District nvarchar(100) NULL,Address nvarchar(500) NULL,PlusCode nvarchar(100) NULL,Latitude decimal(10,7) NULL,Longitude decimal(10,7) NULL,IsTemporary bit NOT NULL CONSTRAINT DF_TestLocations_IsTemporary DEFAULT(0),ApprovalStatus nvarchar(20) NOT NULL,GeocodingStatus nvarchar(30) NOT NULL CONSTRAINT DF_TestLocations_GeocodingStatus DEFAULT(N'Pending'),GeocodedAt datetime2 NULL,IsActive bit NOT NULL,LocationName nvarchar(200) NULL,CreatedAt datetime2 NOT NULL CONSTRAINT DF_TestLocations_CreatedAt DEFAULT(SYSUTCDATETIME()),UpdatedAt datetime2 NULL,SelectedGeocodingAttemptId bigint NULL,TaxId nvarchar(50) NULL,MasterNote nvarchar(1000) NULL,InactivatedAt datetime2 NULL,InactivatedByUserId int NULL,DuplicateOfLocationId int NULL,DuplicateReason nvarchar(500) NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.OptionCLiveApprovalActors(UserId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NULL,IsActive bit NOT NULL,IsRequesterEligible bit NOT NULL,IsIndependentAdminEligible bit NOT NULL);
                CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint NOT NULL PRIMARY KEY,LocationId int NOT NULL FOREIGN KEY REFERENCES dbo.Locations(LocationId));
                CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint NOT NULL PRIMARY KEY,LocationId int NOT NULL FOREIGN KEY REFERENCES dbo.Locations(LocationId));
                INSERT dbo.Organizations VALUES(1),(2);
                INSERT dbo.Teams(TeamId,OrganizationId,TeamCode,TeamName,IsActive,CreatedAt) VALUES(1,1,N'T1',N'Team 1',1,SYSUTCDATETIME()),(2,2,N'T2',N'Team 2',1,SYSUTCDATETIME());
                INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive,CreatedAt) VALUES(1,1,1,N'E1',N'Visitor',1,SYSUTCDATETIME()),(2,1,1,N'E2',N'Admin',1,SYSUTCDATETIME()),(3,2,2,N'E3',N'Other',1,SYSUTCDATETIME());
                INSERT dbo.Roles VALUES(1,N'visitor',N'Visitor',NULL,1,SYSUTCDATETIME()),(2,N'leader',N'Leader',NULL,1,SYSUTCDATETIME()),(3,N'admin',N'Admin',NULL,1,SYSUTCDATETIME());
                INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,HireDate,LegacyUserId,SourceType,CreatedAt) VALUES(101,101,1,N'E1','2000-01-01',1,N'Test',SYSUTCDATETIME()),(102,102,1,N'E2','2000-01-01',2,N'Test',SYSUTCDATETIME()),(103,103,2,N'E3','2000-01-01',3,N'Test',SYSUTCDATETIME());
                INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt) VALUES(1,101,N'Internal',N'U1',N'Test',SYSUTCDATETIME()),(2,102,N'Internal',N'U2',N'Test',SYSUTCDATETIME()),(3,103,N'Internal',N'U3',N'Test',SYSUTCDATETIME());
                INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,SourceType,CreatedAt) VALUES(101,N'Active','2000-01-01',N'Test',SYSUTCDATETIME()),(102,N'Active','2000-01-01',N'Test',SYSUTCDATETIME()),(103,N'Active','2000-01-01',N'Test',SYSUTCDATETIME());
                INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,CreatedAt) VALUES(1,1,'2000-01-01',SYSUTCDATETIME()),(2,3,'2000-01-01',SYSUTCDATETIME()),(3,3,'2000-01-01',SYSUTCDATETIME());
                INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(1,1,SYSUTCDATETIME()),(2,3,SYSUTCDATETIME()),(3,3,SYSUTCDATETIME());
                INSERT dbo.UserTeamScopes(UserId,TeamId,IsPrimary,IsActive,AssignedAt) VALUES(1,1,1,1,SYSUTCDATETIME()),(2,1,1,1,SYSUTCDATETIME());
                INSERT dbo.UserTeamAssignments(UserId,TeamId,IsPrimary,EffectiveFrom,CreatedAt) VALUES(1,1,1,'2000-01-01',SYSUTCDATETIME()),(2,1,1,'2000-01-01',SYSUTCDATETIME());
                INSERT dbo.TeamMemberships(EmploymentId,TeamId,IsPrimary,EffectiveFrom,CreatedAt) VALUES(101,1,1,'2000-01-01',SYSUTCDATETIME());
                INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,LocationType,ApprovalStatus,IsActive,LocationName,Address) VALUES {locations};
                INSERT dbo.OptionCLiveApprovalActors VALUES(1,1,1,1,1,0),(2,1,1,1,0,1),(3,2,2,1,0,1);
                INSERT dbo.VisitTripStops SELECT LocationId+1000,LocationId FROM dbo.Locations;INSERT dbo.VisitTripSnapshotStops SELECT LocationId+2000,LocationId FROM dbo.Locations;
                INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'isolated',SYSUTCDATETIME(),N'isolated');
                """);
        }
        private static string FindRoot(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d is not null){if(File.Exists(Path.Combine(d.FullName,"docs/release/optionc-canonical-sql-draft/1800_011/Up.sql")))return d.FullName;d=d.Parent;}throw new FileNotFoundException("OPTIONC_FINAL_GATE_REPO_ROOT_NOT_FOUND");}
    }

    private static async Task ExecOn(SqlConnection c,string sql,params SqlParameter[] ps){await using var cmd=new SqlCommand(sql,c){CommandTimeout=60};cmd.Parameters.AddRange(ps);await cmd.ExecuteNonQueryAsync();}
    private static async Task<string> ScalarOn(SqlConnection c,string sql,params SqlParameter[] ps){await using var cmd=new SqlCommand(sql,c){CommandTimeout=60};cmd.Parameters.AddRange(ps);return Convert.ToString(await cmd.ExecuteScalarAsync())??"";}
    private static async Task Exec(SqlConnection c,string sql,params SqlParameter[] ps)=>await ExecOn(c,sql,ps);
    private static SqlParameter P(string key,object value)=>new(key,value);
    private sealed record Outcome(bool Committed,bool Denied);

    private sealed class StubCurrentUser(CurrentUserDto user) : ICurrentUserService
    {
        public CurrentUserDto GetRequired() => user;
    }
}
