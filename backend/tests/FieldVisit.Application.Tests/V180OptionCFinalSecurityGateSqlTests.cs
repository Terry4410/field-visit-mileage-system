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
    public async Task CreateLocation_http_reserves_pending_identity_stage_and_rejects_duplicate()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        var root = FindRoot();
        foreach (var relative in new[] {
            "leader-review/LeaderReviewDetails.disposable.sql",
            "create-lowrisk/CreateLowRiskContract.disposable.sql" })
            await h.Exec(File.ReadAllText(Path.Combine(root, "docs", "release",
                "optionc-canonical-sql-draft",
                relative.Replace('/', Path.DirectorySeparatorChar))));
        // Independent live-appointed Leader for the complete CREATE HTTP flow.
        await h.Exec("""
            INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive,CreatedAt)
                VALUES(4,1,1,N'E4',N'Independent Leader',1,SYSUTCDATETIME());
            INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt,UpdatedAt)
                VALUES(4,104,N'Internal',N'E4',N'Demo',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,LegacyUserId,HireDate,SourceType,CreatedAt)
                VALUES(104,104,1,N'E4',4,'2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,SourceType,CreatedAt)
                VALUES(104,N'Active','2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(4,2,SYSUTCDATETIME());
            INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,CreatedAt)
                VALUES(4,2,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.UserTeamScopes(UserId,TeamId,IsPrimary,IsActive,AssignedAt)
                VALUES(4,1,1,1,SYSUTCDATETIME());
            INSERT dbo.UserTeamAssignments(UserId,TeamId,IsPrimary,EffectiveFrom,CreatedAt)
                VALUES(4,1,1,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt)
                VALUES(1,104,'2000-01-01',SYSUTCDATETIME());
            """);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.UseSetting("OptionC:Candidate:Enabled", "true");
            builder.UseSetting("ConnectionStrings:DefaultConnection", h.ConnectionString);
            builder.UseSetting("Auth:Mode", "Demo");
            builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
            builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
            builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
        });
        using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(1, "E1", "visitor"));
        // Legacy HTTP Create cannot bypass canonical reservation on
        // an OptionCIsolated database with the additive Create contract.
        // It must not leave a new inactive Location, request or audit.
        var direct = new SaveManagedLocationRequest(1, "Legacy HTTP bypass denied",
            "Customer", "Taipei", "Xinyi", "Never commit direct creation",
            null, false, null);
        // The legacy controller needs normal JWT team/org scope; absence of
        // a JWT claim alone would be an invalid proof of the canonical guard.
        visitor.DefaultRequestHeaders.Authorization =
            new("Bearer", Token(1, "E1", "visitor", 1, 1));
        using var blockedLegacyCreate = await visitor.PostAsJsonAsync(
            "api/v1/managed-locations", direct);
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity,
            blockedLegacyCreate.StatusCode);
        Assert.Contains("LOCATION_LEGACY_CREATE_REQUIRES_CANONICAL_REVIEW",
            await blockedLegacyCreate.Content.ReadAsStringAsync());
        Assert.Equal("0", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.Locations
            WHERE LocationName=N'Legacy HTTP bypass denied'
            """));
        Assert.Equal("0", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.ChangeRequests
            WHERE ProposedJson LIKE N'%Legacy HTTP bypass denied%'
            """));
        var payload = new V180OptionCCreateLocation(1, "New customer candidate",
            new V180B3LocationFields("Candidate HTTP identity", "Taipei", "Xinyi",
                "New pending candidate address", null, null, "Pending only"));
        using var response = await visitor.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations", payload);
        Assert.True(response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_HTTP_VIEW_MISSING");
        Assert.Equal("High", created.RiskCode);
        Assert.Equal("CreateLocation", created.OperationCode);
        Assert.Equal("Pending", created.Status);
        var locationId = int.Parse(created.EntityId);
        Assert.True(locationId > 19);
        Assert.Equal("0", await h.Scalar(
            "SELECT CONVERT(nvarchar(1),IsActive) FROM dbo.Locations WHERE LocationId=@id",
            P("@id", locationId)));
        Assert.Equal("Pending", await h.Scalar(
            "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=@id",
            P("@id", locationId)));
        // Real legacy HTTP UPDATE/DELETE must not mutate a canonical reserved
        // identity. Test as valid org-scoped admin to exclude auth-only denials.
        using var legacyAdmin = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        legacyAdmin.DefaultRequestHeaders.Authorization =
            new("Bearer", Token(2, "E2", "admin", 1, 1));
        var sourceVersion = await h.Scalar("""
            SELECT CONVERT(varchar(32), RowVersion, 2)
            FROM dbo.Locations WHERE LocationId=@id
            """, P("@id", locationId));
        var editLegacy = direct with {
            LocationName = "Illegal reserved update",
            RowVersion = Convert.ToBase64String(Convert.FromHexString(sourceVersion))
        };
        using var blockedUpdate = await legacyAdmin.PutAsJsonAsync(
            $"api/v1/managed-locations/{locationId}", editLegacy);
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, blockedUpdate.StatusCode);
        Assert.Contains("LOCATION_CANONICAL_CREATE_RESERVATION_REQUIRES_REVIEW",
            await blockedUpdate.Content.ReadAsStringAsync());
        using var blockedImpact = await legacyAdmin.GetAsync(
            $"api/v1/managed-locations/{locationId}/delete-impact");
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, blockedImpact.StatusCode);
        using var blockedDelete = await legacyAdmin.DeleteAsync(
            $"api/v1/managed-locations/{locationId}/permanent");
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, blockedDelete.StatusCode);
        Assert.Equal(sourceVersion, await h.Scalar("""
            SELECT CONVERT(varchar(32), RowVersion, 2)
            FROM dbo.Locations WHERE LocationId=@id
            """, P("@id", locationId)));
        Assert.Equal("Pending", await h.Scalar(
            "SELECT ReservationStatus FROM dbo.LocationCreateReservations WHERE LocationId=@id",
            P("@id", locationId)));
        Assert.Equal("PendingGeocode", await h.StageStatus(created.RequestPublicId));
        // Actual EF query path shared with BackgroundJobService must exclude
        // reserved CreateLocation even though it looks Pending and inactive.
        // Unreserved legacy drafts remain eligible; the candidate table must
        // not be queried on schemas where it is absent.
        await h.Exec("""
            INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,
                LocationType,ApprovalStatus,GeocodingStatus,IsActive,LocationName,Address)
            VALUES(900,1,1,1,N'Customer',N'Pending',N'Pending',0,
                N'Legitimate legacy draft',N'Legacy address');
            """);
        await using (var geocodeDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(h.ConnectionString).Options))
        {
            var allowedIds = await BackgroundJobService.PendingDraftGeocodingCandidates(
                geocodeDb, 1, hasCreateReservations: true)
                .Select(x => x.LocationId).ToListAsync();
            Assert.DoesNotContain(locationId, allowedIds);
            Assert.Contains(900, allowedIds);
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(
                () => V180OptionCCreateReservationGuard.RequireUnreservedDraftAsync(
                    geocodeDb, locationId, CancellationToken.None));
            Assert.Equal("LOCATION_CANONICAL_CREATE_RESERVATION_REQUIRES_REVIEW",
                blocked.Message);
            await V180OptionCCreateReservationGuard.RequireUnreservedDraftAsync(
                geocodeDb, 900, CancellationToken.None);
        }

        Assert.Equal("1", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.ChangeRequests r
            JOIN dbo.LocationCreateReservations cr ON cr.ChangeRequestId=r.ChangeRequestId
            JOIN dbo.LocationPublicationStages s ON s.ChangeRequestId=r.ChangeRequestId
            WHERE r.RequestPublicId=@id AND r.OperationCode=N'CreateLocation'
              AND r.RiskCode=N'High' AND r.BeforeJson IS NULL
              AND r.Status=N'Pending' AND cr.ReservationStatus=N'Pending'
              AND s.StageStatus=N'PendingGeocode'
              AND cr.LocationId=s.LocationId AND r.EntityId=CONVERT(nvarchar(80),cr.LocationId)
              AND cr.ReservedLocationRowVersion=s.ExpectedLocationRowVersion
            """, P("@id", created.RequestPublicId)));
        Assert.Equal("1", await h.EventCount(created.RequestPublicId, "Submitted"));
        Assert.Equal("0", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE LocationId=@id",
            P("@id", locationId)));
        // Legacy Picker endpoints require the normal org/team JWT claims.
        // The isolated Option C write path above intentionally ignores claims
        // and instead authorizes directly against live HR/role/team tables.
        visitor.DefaultRequestHeaders.Authorization =
            new("Bearer", Token(1, "E1", "visitor", 1, 1));
        // The actual Visitor Picker/search API must not list the reserved
        // inactive Location before independent publication.
        using var pendingPicker = await visitor.GetAsync(
            "api/v1/locations/search?pageSize=100");
        Assert.True(pendingPicker.IsSuccessStatusCode,
            await pendingPicker.Content.ReadAsStringAsync());
        var pendingResults = await pendingPicker.Content.ReadFromJsonAsync<V170LocationSearchResult>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_PICKER_VIEW_MISSING");
        Assert.DoesNotContain(pendingResults.Items, x => x.LocationId == locationId);
        using var pendingFavorite = await visitor.PostAsync(
            $"api/v1/locations/{locationId}/favorite", null);
        Assert.False(pendingFavorite.IsSuccessStatusCode);
        using var again = await visitor.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations", payload);
        Assert.False(again.IsSuccessStatusCode);
        Assert.Equal("1", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.Locations
            WHERE OrganizationId=1 AND TeamId=1 AND ApprovalStatus=N'Pending'
              AND LocationName=N'Candidate HTTP identity'
              AND Address=N'New pending candidate address'
            """));
        // Two real HTTP clients race to reserve the same Location identity.
        // Exactly one request may commit; the loser must not leave a second
        // Pending Location, orphan Reservation/Stage, or extra Submitted audit.
        using var peerVisitor = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        peerVisitor.DefaultRequestHeaders.Authorization =
            new("Bearer", Token(1, "E1", "visitor", 1, 1));
        var racePayload = new V180OptionCCreateLocation(1,
            "Concurrent canonical create same identity",
            new V180B3LocationFields("Race customer pending", "Taipei", "Xinyi",
                "Unique concurrent race address", null, null, null));
        var concurrent = await Task.WhenAll(
            visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations", racePayload),
            peerVisitor.PostAsJsonAsync("api/v1/optionc-candidate/locations", racePayload));
        using var firstConcurrent = concurrent[0];
        using var secondConcurrent = concurrent[1];
        Assert.Single(concurrent.Where(x => x.IsSuccessStatusCode));
        Assert.Equal("1", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.Locations WHERE OrganizationId=1 AND TeamId=1
              AND LocationName=N'Race customer pending'
              AND Address=N'Unique concurrent race address'
              AND ApprovalStatus=N'Pending' AND IsActive=0
            """));
        Assert.Equal("1", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.ChangeRequests r
            JOIN dbo.Locations l ON r.EntityId=CONVERT(nvarchar(80),l.LocationId)
            JOIN dbo.LocationCreateReservations cr ON cr.ChangeRequestId=r.ChangeRequestId
            JOIN dbo.LocationPublicationStages s ON s.ChangeRequestId=r.ChangeRequestId
            WHERE l.LocationName=N'Race customer pending'
              AND l.Address=N'Unique concurrent race address'
              AND r.OperationCode=N'CreateLocation' AND r.RiskCode=N'High'
              AND r.Status=N'Pending' AND cr.ReservationStatus=N'Pending'
              AND s.StageStatus=N'PendingGeocode'
              AND cr.LocationId=l.LocationId AND s.LocationId=l.LocationId
            """));
        using var outside = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        outside.DefaultRequestHeaders.Authorization = new("Bearer", Token(3, "E3", "admin"));
        using var denied = await outside.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations", payload);
        Assert.False(denied.IsSuccessStatusCode);

        // A real requester cannot self-review. An independently appointed
        // Leader must approve before the independently scoped Admin acts.
        using var illegalSelf = await visitor.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(created.RowVersion, Guid.NewGuid(),
                "Requester must not self-review", true));
        Assert.False(illegalSelf.IsSuccessStatusCode);
        // Admin cannot read Stage RowVersion or inject geocode evidence
        // before the canonical independent Leader first-level review.
        using var earlyAdmin = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        earlyAdmin.DefaultRequestHeaders.Authorization = new("Bearer", Token(2, "E2", "admin"));
        using var earlyStageGet = await earlyAdmin.GetAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/stage");
        Assert.False(earlyStageGet.IsSuccessStatusCode);
        var earlyVersion = (byte[])(await new SqlCommand("""
            SELECT s.RowVersion FROM dbo.LocationPublicationStages s
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """,h.Db){Parameters={new SqlParameter("@id",created.RequestPublicId)}}
            .ExecuteScalarAsync() ?? throw new InvalidOperationException("OPTIONC_EARLY_STAGE_MISSING"));
        using var earlyGeocode = await earlyAdmin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/geocode-evidence",
            new V180OptionCGeocodeEvidence(Convert.ToBase64String(earlyVersion),true,
                25.03m,121.56m,"Evidence cannot precede Leader")
                {EvidenceKey=Guid.NewGuid()});
        Assert.False(earlyGeocode.IsSuccessStatusCode);
        Assert.Equal("PendingGeocode", await h.StageStatus(created.RequestPublicId));
        using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(4, "E4", "leader"));
        using var reviewed = await leader.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(created.RowVersion, Guid.NewGuid(),
                "Independent Leader approves new Location", true));
        Assert.True(reviewed.IsSuccessStatusCode,
            await reviewed.Content.ReadAsStringAsync());
        Assert.Equal("1", await h.EventCount(created.RequestPublicId, "LeaderApproved"));

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Authorization = new("Bearer", Token(2, "E2", "admin"));
        using var deniedStage = await visitor.GetAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/stage");
        Assert.False(deniedStage.IsSuccessStatusCode);
        using var readStage = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/stage");
        Assert.True(readStage.IsSuccessStatusCode,
            await readStage.Content.ReadAsStringAsync());
        var stageForAdmin = await readStage.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_ADMIN_STAGE_READ_FAILED");
        Assert.Equal("PendingGeocode", stageForAdmin.StageStatus);

        using var prematurePublish = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/publish-reviewed",
            new V180B3Review(created.RowVersion, Guid.NewGuid(),
                "No evidence must prevent publication"));
        Assert.False(prematurePublish.IsSuccessStatusCode);
        Assert.Equal("Pending", await h.RequestStatus(created.RequestPublicId));

        var stageVersion = (byte[])(await new SqlCommand("""
            SELECT s.RowVersion FROM dbo.LocationPublicationStages s
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """, h.Db) { Parameters = { new SqlParameter("@id", created.RequestPublicId) } }
            .ExecuteScalarAsync() ?? throw new InvalidOperationException("OPTIONC_CREATE_STAGE_VERSION_MISSING"));
        Assert.Equal(Convert.ToBase64String(stageVersion), stageForAdmin.StageRowVersion);
        using var geocode = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/geocode-evidence",
            new V180OptionCGeocodeEvidence(stageForAdmin.StageRowVersion,
                true, 25.03m, 121.56m, "Independent geocode verified") { EvidenceKey=Guid.NewGuid() });
        Assert.True(geocode.IsSuccessStatusCode,
            await geocode.Content.ReadAsStringAsync());
        var geoView = await geocode.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_GEOCODE_VIEW_MISSING");
        using var duplicate = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/duplicate-evidence",
            new V180OptionCDuplicateEvidence(geoView.StageRowVersion, true,
                "Reviewed duplicate evidence cleared", Guid.NewGuid()));
        Assert.True(duplicate.IsSuccessStatusCode,
            await duplicate.Content.ReadAsStringAsync());
        var duplicateView = await duplicate.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_DUPLICATE_VIEW_MISSING");
        Assert.Equal("ReadyForPublish", duplicateView.StageStatus);
        using var published = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{created.RequestPublicId}/publish-reviewed",
            new V180B3Review(created.RowVersion, Guid.NewGuid(),
                "Independent Admin final CreateLocation approval"));
        Assert.True(published.IsSuccessStatusCode,
            await published.Content.ReadAsStringAsync());
        Assert.Equal("Applied", await h.RequestStatus(created.RequestPublicId));
        Assert.Equal("1", await h.Scalar(
            "SELECT CONVERT(nvarchar(1),IsActive) FROM dbo.Locations WHERE LocationId=@id",
            P("@id", locationId)));
        Assert.Equal("Approved", await h.Scalar(
            "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=@id",
            P("@id", locationId)));
        Assert.Equal("1", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.LocationPublishedVersions
            WHERE LocationId=@id AND VersionNumber=1 AND IsCurrent=1
            """, P("@id", locationId)));
        using var publishedPicker = await visitor.GetAsync(
            "api/v1/locations/search?pageSize=100");
        Assert.True(publishedPicker.IsSuccessStatusCode,
            await publishedPicker.Content.ReadAsStringAsync());
        var publishedResults = await publishedPicker.Content.ReadFromJsonAsync<V170LocationSearchResult>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_PUBLISHED_PICKER_VIEW_MISSING");
        Assert.Contains(publishedResults.Items, x => x.LocationId == locationId);

        Assert.Equal("Published", await h.Scalar("""
            SELECT cr.ReservationStatus FROM dbo.LocationCreateReservations cr
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=cr.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """, P("@id", created.RequestPublicId)));
        foreach (var evt in new[] { "Submitted", "LeaderApproved",
            "GeocodeEvidenceRecorded", "DuplicateReviewEvidenceRecorded",
            "Approved", "Applied" })
            Assert.Equal("1", await h.EventCount(created.RequestPublicId, evt));
        // Earlier Trips / Snapshots must retain their original LocationIds.
        Assert.Equal("10", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.VisitTripStops WHERE LocationId BETWEEN 10 AND 19"));
        Assert.Equal("10", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.VisitTripSnapshotStops WHERE LocationId BETWEEN 10 AND 19"));

        var rejectedPayload = new V180OptionCCreateLocation(1,
            "Second CreateLocation needs independent review",
            new V180B3LocationFields("Candidate rejected identity", "Taipei", "Xinyi",
                "Pending rejected candidate address", null, null, null));
        using var rejectionSubmit = await visitor.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations", rejectedPayload);
        Assert.True(rejectionSubmit.IsSuccessStatusCode,
            await rejectionSubmit.Content.ReadAsStringAsync());
        var rejectRequest = await rejectionSubmit.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_REJECT_VIEW_MISSING");
        using var rejectReview = await leader.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{rejectRequest.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(rejectRequest.RowVersion,
                Guid.NewGuid(), "Independent Leader rejects new Location", false));
        Assert.True(rejectReview.IsSuccessStatusCode,
            await rejectReview.Content.ReadAsStringAsync());
        Assert.Equal("Rejected", await h.RequestStatus(rejectRequest.RequestPublicId));
        Assert.Equal("Abandoned", await h.StageStatus(rejectRequest.RequestPublicId));
        Assert.Equal("Abandoned", await h.Scalar("""
            SELECT cr.ReservationStatus FROM dbo.LocationCreateReservations cr
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=cr.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """, P("@id", rejectRequest.RequestPublicId)));
        Assert.Equal("0", await h.Scalar("""
            SELECT CONVERT(nvarchar(1),IsActive)
            FROM dbo.Locations WHERE LocationId=@id
            """, P("@id", int.Parse(rejectRequest.EntityId))));
        Assert.Equal("0", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE LocationId=@id
            """, P("@id", int.Parse(rejectRequest.EntityId))));
        // Terminal rejection/Abandoned reservation releases the identity
        // for a fresh, independently reviewable CreateLocation; the former
        // LocationId and audit trail remain historical and never get reused.
        using var resubmitRejected = await visitor.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations", rejectedPayload);
        Assert.True(resubmitRejected.IsSuccessStatusCode,
            await resubmitRejected.Content.ReadAsStringAsync());
        var resubmitted = await resubmitRejected.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_RESUBMIT_MISSING");
        Assert.NotEqual(rejectRequest.RequestPublicId, resubmitted.RequestPublicId);
        Assert.NotEqual(rejectRequest.EntityId, resubmitted.EntityId);
        Assert.Equal("Pending", resubmitted.Status);
        Assert.Equal("PendingGeocode", await h.StageStatus(resubmitted.RequestPublicId));
        Assert.Equal("1", await h.Scalar("""
            SELECT COUNT(*) FROM dbo.LocationCreateReservations cr
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=cr.ChangeRequestId
            WHERE r.RequestPublicId=@id AND cr.ReservationStatus=N'Pending'
            """, P("@id", resubmitted.RequestPublicId)));

        // Create-specific live HR / Team appointment / Admin role revocation
        // AFTER successful independent first-level review. Stage and events
        // must remain unchanged across denied reads/evidence attempts.
        using var nextLeader = await leader.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{resubmitted.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(resubmitted.RowVersion,
                Guid.NewGuid(), "New Create independently reviewed", true));
        Assert.True(nextLeader.IsSuccessStatusCode,
            await nextLeader.Content.ReadAsStringAsync());
        using var beforeRevoke = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{resubmitted.RequestPublicId}/stage");
        Assert.True(beforeRevoke.IsSuccessStatusCode,
            await beforeRevoke.Content.ReadAsStringAsync());
        var authorizedStage = await beforeRevoke.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_REVOCATION_STAGE_MISSING");
        await h.Exec("UPDATE dbo.TeamLeaderAssignments SET EffectiveTo='2001-01-01' WHERE EmploymentId=104");
        using var noLeader = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{resubmitted.RequestPublicId}/stage");
        Assert.False(noLeader.IsSuccessStatusCode);
        await h.Exec("UPDATE dbo.TeamLeaderAssignments SET EffectiveTo=NULL WHERE EmploymentId=104");

        await h.Exec("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Leave' WHERE EmploymentId=101");
        using var noRequester = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{resubmitted.RequestPublicId}/stage");
        Assert.False(noRequester.IsSuccessStatusCode);
        await h.Exec("UPDATE dbo.EmploymentStatusPeriods SET EmploymentStatus=N'Active' WHERE EmploymentId=101");

        await h.Exec("UPDATE dbo.UserRoleAssignments SET EffectiveTo='2001-01-01' WHERE UserId=2");
        using var noAdmin = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{resubmitted.RequestPublicId}/stage");
        Assert.False(noAdmin.IsSuccessStatusCode);
        await h.Exec("UPDATE dbo.UserRoleAssignments SET EffectiveTo=NULL WHERE UserId=2");

        Assert.Equal("Pending", await h.RequestStatus(resubmitted.RequestPublicId));
        Assert.Equal("PendingGeocode", await h.StageStatus(resubmitted.RequestPublicId));
        Assert.Equal("0", await h.EventCount(
            resubmitted.RequestPublicId, "GeocodeEvidenceRecorded"));
        using var restored = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{resubmitted.RequestPublicId}/stage");
        Assert.True(restored.IsSuccessStatusCode,
            await restored.Content.ReadAsStringAsync());
        var restoredStage = await restored.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_REVOCATION_RESTORE_MISSING");
        Assert.Equal(authorizedStage.StageRowVersion, restoredStage.StageRowVersion);
    }

    [Fact]
    public async Task Create_and_lowrisk_atomic_candidate_requires_independent_leader_and_preserves_history()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        var root = FindRoot();
        foreach (var relative in new[] {
            "leader-review/LeaderReviewDetails.disposable.sql",
            "create-lowrisk/CreateLowRiskContract.disposable.sql" })
            await h.Exec(File.ReadAllText(Path.Combine(root, "docs", "release",
                "optionc-canonical-sql-draft", relative.Replace('/', Path.DirectorySeparatorChar))));

        // Live authorization is resolved by the real HTTP/DI pipeline, not
        // by the fixture's audit rows or a caller-supplied approval boolean.
        await h.Exec("""
            INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive,CreatedAt)
                VALUES(4,1,1,N'E4',N'Independent Leader',1,SYSUTCDATETIME());
            INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt,UpdatedAt)
                VALUES(4,104,N'Internal',N'E4',N'Demo',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,LegacyUserId,HireDate,SourceType,CreatedAt)
                VALUES(104,104,1,N'E4',4,'2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,SourceType,CreatedAt)
                VALUES(104,N'Active','2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(4,2,SYSUTCDATETIME());
            INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,CreatedAt)
                VALUES(4,2,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.UserTeamScopes(UserId,TeamId,IsPrimary,IsActive,AssignedAt)
                VALUES(4,1,1,1,SYSUTCDATETIME());
            INSERT dbo.UserTeamAssignments(UserId,TeamId,IsPrimary,EffectiveFrom,CreatedAt)
                VALUES(4,1,1,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt)
                VALUES(1,104,'2000-01-01',SYSUTCDATETIME());
            """);
        const string createJson = """{"LocationName":"New customer","Address":"New customer address","City":"Taipei","MasterNote":"New note"}""";
        const string lowJson = """{"LocationName":"Original 10","Address":"Original address 10","MasterNote":"Reviewed low risk note"}""";
        var createId = Guid.NewGuid();
        var lowId = Guid.NewGuid();
        await h.Exec("""
            INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,
                LocationType,ApprovalStatus,IsActive,LocationName,Address)
            VALUES(40,1,1,1,N'Customer',N'Pending',0,N'New customer',N'New customer address');
            DECLARE @rv binary(8)=(SELECT RowVersion FROM dbo.Locations WHERE LocationId=40);
            INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
            VALUES(@id,1,1,N'Location',N'40',N'CreateLocation',N'High',
                @rv,NULL,@json,1,SYSUTCDATETIME(),N'Pending');
            DECLARE @rid bigint=SCOPE_IDENTITY();
            INSERT dbo.LocationCreateReservations(ChangeRequestId,LocationId,
                OrganizationId,TeamId,ReservedLocationRowVersion,ReservationStatus,
                CreatedAt,UpdatedAt)
            VALUES(@rid,40,1,1,@rv,N'Pending',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId)
            VALUES(@rid,N'Submitted',1,SYSUTCDATETIME(),NEWID());
            INSERT dbo.LocationPublicationStages(ChangeRequestId,LocationId,
                OrganizationId,TeamId,ExpectedLocationRowVersion,
                ProposedJson,ProposedSha256,StageStatus,GeocodeStatus,
                GeocodeAddressSha256,Latitude,Longitude,DuplicateReviewStatus,
                DuplicateEvidenceSha256,CreatedAt,UpdatedAt)
            VALUES(@rid,40,1,1,@rv,@json,
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@json)),
                N'ReadyForPublish',N'Succeeded',
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),N'New customer address')),
                25.03,121.56,N'Cleared',
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@json)),
                SYSUTCDATETIME(),SYSUTCDATETIME());
            """, P("@id", createId), P("@json", createJson));
        Assert.Equal("0", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE LocationId=40"));
        Assert.Equal("0", await h.Scalar(
            "SELECT CONVERT(nvarchar(1),IsActive) FROM dbo.Locations WHERE LocationId=40"));
        await using (var tx = await h.Begin())
        {
            var missingLeader = await Assert.ThrowsAsync<SqlException>(() =>
                V180OptionCCreateLowRiskPublicationWriter.ExecuteAsync(
                    h.Db, tx, createId, 1, 2, Guid.NewGuid(),
                    "Admin create", true, true, CancellationToken.None));
            Assert.Equal(51602, missingLeader.Number);
            await tx.RollbackAsync();
        }
        await h.Exec("""
            DECLARE @rid bigint=(SELECT ChangeRequestId FROM dbo.ChangeRequests WHERE RequestPublicId=@id);
            DECLARE @leaderKey uniqueidentifier=NEWID();
            INSERT dbo.ChangeRequestLeaderReviews(ChangeRequestId,ReviewStage,
                Status,ReviewerUserId,ReviewedAt,Reason,DecisionKey)
            VALUES(@rid,N'LeaderFirstLevel',N'Approved',4,SYSUTCDATETIME(),
                N'Independent leader reviewed',@leaderKey);
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@rid,N'LeaderApproved',4,SYSUTCDATETIME(),NEWID(),@leaderKey);
            DECLARE @hash varchar(64)=CONVERT(varchar(64),
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@json)),2);
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@rid,N'GeocodeEvidenceRecorded',4,SYSUTCDATETIME(),NEWID(),NEWID(),
                    CONCAT(N'{"decision":"Succeeded","proposedEvidenceSha256":"',@hash,N'"}')),
                  (@rid,N'DuplicateReviewEvidenceRecorded',4,SYSUTCDATETIME(),NEWID(),NEWID(),
                    CONCAT(N'{"decision":"Cleared","proposedEvidenceSha256":"',@hash,N'"}'));
            """, P("@id", createId), P("@json", createJson));
        await using (var tx = await h.Begin())
        {
            var sameLeader = await Assert.ThrowsAsync<SqlException>(() =>
                V180OptionCCreateLowRiskPublicationWriter.ExecuteAsync(
                    h.Db, tx, createId, 1, 4, Guid.NewGuid(),
                    "Self final review", true, true, CancellationToken.None));
            Assert.Equal(51603, sameLeader.Number);
            await tx.RollbackAsync();
        }
        await using (var tx = await h.Begin())
        {
            var noAdmin = await Assert.ThrowsAsync<SqlException>(() =>
                V180OptionCCreateLowRiskPublicationWriter.ExecuteAsync(
                    h.Db, tx, createId, 1, 2, Guid.NewGuid(),
                    "No admin", true, false, CancellationToken.None));
            Assert.Equal(51603, noAdmin.Number);
            await tx.RollbackAsync();
        }
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OptionC:Candidate:Enabled", "true");
            builder.UseSetting("ConnectionStrings:DefaultConnection", h.ConnectionString);
            builder.UseSetting("Auth:Mode", "Demo");
            builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
            builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
            builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
        });
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Authorization = new("Bearer", Token(2, "E2", "admin"));
        using var leaderClient = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        leaderClient.DefaultRequestHeaders.Authorization = new("Bearer", Token(4, "E4", "leader"));
        var createRv = (byte[])(await new SqlCommand(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE RequestPublicId=@id", h.Db)
            { Parameters = { new SqlParameter("@id", createId) } }.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("OPTIONC_CREATE_ROWVERSION_MISSING"));
        var createHttp = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{createId}/publish-reviewed",
            new V180B3Review(Convert.ToBase64String(createRv), Guid.NewGuid(),
                "Independent Admin create publication"));
        Assert.True(createHttp.IsSuccessStatusCode,
            await createHttp.Content.ReadAsStringAsync());
        Assert.Equal("1", await h.Scalar(
            "SELECT CONVERT(nvarchar(1),IsActive) FROM dbo.Locations WHERE LocationId=40"));
        Assert.Equal("1", await h.Scalar(
            "SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=40 AND IsCurrent=1"));
        Assert.Equal("Published", await h.Scalar(
            "SELECT ReservationStatus FROM dbo.LocationCreateReservations WHERE LocationId=40"));
        Assert.Equal("Applied", await h.RequestStatus(createId));

        var historicalTrip = await h.Scalar(
            "SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 FOR JSON PATH");
        var historicalSnapshot = await h.Scalar(
            "SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 FOR JSON PATH");
        await h.Exec("""
            DECLARE @rv binary(8)=(SELECT RowVersion FROM dbo.Locations WHERE LocationId=10);
            DECLARE @beforeJson nvarchar(max)=(
                SELECT LocationName,City,District,Address,PlusCode,TaxId,MasterNote
                FROM dbo.Locations WHERE LocationId=10 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
            VALUES(@id,1,1,N'Location',N'10',N'UpdatePublishedLocation',N'Low',
                @rv,@beforeJson,@json,1,SYSUTCDATETIME(),N'Pending');
            DECLARE @rid bigint=SCOPE_IDENTITY();
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId)
            VALUES(@rid,N'Submitted',1,SYSUTCDATETIME(),NEWID());
            INSERT dbo.LocationPublicationStages(ChangeRequestId,LocationId,
                OrganizationId,TeamId,ExpectedLocationRowVersion,
                ProposedJson,ProposedSha256,StageStatus,GeocodeStatus,
                DuplicateReviewStatus,DuplicateEvidenceSha256,CreatedAt,UpdatedAt)
            VALUES(@rid,10,1,1,@rv,@json,
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@json)),
                N'ReadyForPublish',N'NotRequired',N'Cleared',
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@json)),
                SYSUTCDATETIME(),SYSUTCDATETIME());
            DECLARE @leaderKey uniqueidentifier=NEWID();
            INSERT dbo.ChangeRequestLeaderReviews(ChangeRequestId,ReviewStage,
                Status,ReviewerUserId,ReviewedAt,Reason,DecisionKey)
            VALUES(@rid,N'LeaderFirstLevel',N'Approved',4,SYSUTCDATETIME(),
                N'Independent low-risk review',@leaderKey);
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@rid,N'LeaderApproved',4,SYSUTCDATETIME(),NEWID(),@leaderKey);
            DECLARE @hash varchar(64)=CONVERT(varchar(64),
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@json)),2);
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@rid,N'DuplicateReviewEvidenceRecorded',4,SYSUTCDATETIME(),NEWID(),NEWID(),
                CONCAT(N'{"decision":"Cleared","proposedEvidenceSha256":"',@hash,N'"}'));
            """, P("@id", lowId), P("@json", lowJson));
        await using (var tx = await h.Begin())
        {
            var wrongReviewer = await Assert.ThrowsAsync<SqlException>(() =>
                V180OptionCCreateLowRiskPublicationWriter.ExecuteAsync(
                    h.Db, tx, lowId, 1, 2, Guid.NewGuid(),
                    "Admin cannot bypass leader", true, true, CancellationToken.None));
            Assert.Equal(51604, wrongReviewer.Number);
            await tx.RollbackAsync();
        }
        // Even a canonically labelled Low request cannot substitute an
        // invented before-image for the locked published Location.
        await using (var tx = await h.Begin())
        {
            await using (var forge = new SqlCommand("""
                UPDATE dbo.ChangeRequests SET BeforeJson=N'{"LocationName":"forged"}'
                WHERE RequestPublicId=@id;
                """, h.Db, tx))
            {
                forge.Parameters.AddWithValue("@id", lowId);
                await forge.ExecuteNonQueryAsync();
            }
            var forged = await Assert.ThrowsAsync<SqlException>(() =>
                V180OptionCCreateLowRiskPublicationWriter.ExecuteAsync(
                    h.Db, tx, lowId, 1, 4, Guid.NewGuid(),
                    "Forged before-image must fail", true, false, CancellationToken.None));
            Assert.Equal(51621, forged.Number);
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending", await h.RequestStatus(lowId));
        var lowRv = (byte[])(await new SqlCommand(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE RequestPublicId=@id", h.Db)
            { Parameters = { new SqlParameter("@id", lowId) } }.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("OPTIONC_LOW_ROWVERSION_MISSING"));
        // The historical LeaderApproved row is not sufficient authority:
        // revocation immediately before publication must fail closed.
        await h.Exec("UPDATE dbo.Users SET IsActive=0 WHERE UserId=4");
        var revokedHttp = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{lowId}/publish-reviewed",
            new V180B3Review(Convert.ToBase64String(lowRv), Guid.NewGuid(),
                "Revoked leader must be denied"));
        Assert.False(revokedHttp.IsSuccessStatusCode);
        Assert.Equal("Pending", await h.RequestStatus(lowId));
        Assert.Equal("1", await h.Scalar(
            "SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        await h.Exec("UPDATE dbo.Users SET IsActive=1 WHERE UserId=4");
        var adminLowHttp = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{lowId}/publish-reviewed",
            new V180B3Review(Convert.ToBase64String(lowRv), Guid.NewGuid(),
                "Admin cannot bypass low-risk leader"));
        Assert.False(adminLowHttp.IsSuccessStatusCode);
        Assert.Equal("Pending", await h.RequestStatus(lowId));
        var lowHttp = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{lowId}/publish-reviewed",
            new V180B3Review(Convert.ToBase64String(lowRv), Guid.NewGuid(),
                "Independent Leader low-risk publication"));
        Assert.True(lowHttp.IsSuccessStatusCode,
            await lowHttp.Content.ReadAsStringAsync());
        Assert.Equal("Reviewed low risk note", await h.Scalar(
            "SELECT MasterNote FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("2", await h.Scalar(
            "SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        Assert.Equal(historicalTrip, await h.Scalar(
            "SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 FOR JSON PATH"));
        Assert.Equal(historicalSnapshot, await h.Scalar(
            "SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 FOR JSON PATH"));
        Assert.Equal("Applied", await h.RequestStatus(lowId));

        // Full LOW-risk candidate path: Visitor HTTP submission -> server
        // classification -> pending private stage -> independent live Leader
        // review and duplicate evidence -> Leader HTTP atomic publication.
        using var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        visitor.DefaultRequestHeaders.Authorization = new("Bearer", Token(1, "E1", "visitor"));
        var latestLocationRv = (byte[])(await new SqlCommand(
            "SELECT RowVersion FROM dbo.Locations WHERE LocationId=10", h.Db).ExecuteScalarAsync()
            ?? throw new InvalidOperationException("OPTIONC_LOW_SOURCE_VERSION_MISSING"));
        var submitLow = await visitor.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations/10/changes",
            new V180B3SubmitLocation(10, Convert.ToBase64String(latestLocationRv),
                "Visitor updates note only",
                new V180B3LocationFields("Original 10", null, null,
                    "Original address 10", null, null, "Second reviewed note")));
        Assert.True(submitLow.IsSuccessStatusCode,
            await submitLow.Content.ReadAsStringAsync());
        var submittedLow = await submitLow.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_LOW_SUBMISSION_MISSING");
        Assert.Equal("Low", submittedLow.RiskCode);
        Assert.Equal("Pending", submittedLow.Status);
        Assert.Equal("PendingDuplicateReview", await h.StageStatus(submittedLow.RequestPublicId));
        // Admin cannot use the generic reject endpoint to bypass the
        // Scheme A independent Leader-first low-risk review.
        var adminRejectLow = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/reject",
            new V180B3Review(submittedLow.RowVersion, Guid.NewGuid(),
                "Admin is not the low-risk first-level reviewer"));
        Assert.False(adminRejectLow.IsSuccessStatusCode);
        Assert.Equal("Pending", await h.RequestStatus(submittedLow.RequestPublicId));
        Assert.Equal("PendingDuplicateReview", await h.StageStatus(submittedLow.RequestPublicId));
        Assert.Equal("Reviewed low risk note", await h.Scalar(
            "SELECT MasterNote FROM dbo.Locations WHERE LocationId=10"));

        var unreviewedStageRv = (byte[])(await new SqlCommand("""
            SELECT s.RowVersion FROM dbo.LocationPublicationStages s
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """, h.Db) { Parameters = { new SqlParameter("@id", submittedLow.RequestPublicId) } }
            .ExecuteScalarAsync() ?? throw new InvalidOperationException("OPTIONC_LOW_UNREVIEWED_STAGE_MISSING"));
        var prematureEvidence = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/duplicate-evidence",
            new V180OptionCDuplicateEvidence(Convert.ToBase64String(unreviewedStageRv), true,
                "Leader may not clear duplicate evidence before first-level approval",
                Guid.NewGuid()));
        Assert.False(prematureEvidence.IsSuccessStatusCode);
        Assert.Equal("PendingDuplicateReview", await h.StageStatus(submittedLow.RequestPublicId));
        var reviewed = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(submittedLow.RowVersion,
                Guid.NewGuid(), "Independent Leader accepts low-risk note", true));
        Assert.True(reviewed.IsSuccessStatusCode,
            await reviewed.Content.ReadAsStringAsync());
        var stageRv = (byte[])(await new SqlCommand("""
            SELECT s.RowVersion FROM dbo.LocationPublicationStages s
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """, h.Db) { Parameters = { new SqlParameter("@id", submittedLow.RequestPublicId) } }
            .ExecuteScalarAsync() ?? throw new InvalidOperationException("OPTIONC_LOW_STAGE_VERSION_MISSING"));
        var deniedEvidence = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/duplicate-evidence",
            new V180OptionCDuplicateEvidence(Convert.ToBase64String(stageRv), true,
                "Admin cannot impersonate independent Leader", Guid.NewGuid()));
        Assert.False(deniedEvidence.IsSuccessStatusCode);
        var duplicateLow = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/duplicate-evidence",
            new V180OptionCDuplicateEvidence(Convert.ToBase64String(stageRv), true,
                "No duplicate in reviewed low-risk note", Guid.NewGuid()));
        Assert.True(duplicateLow.IsSuccessStatusCode,
            await duplicateLow.Content.ReadAsStringAsync());
        var duplicateView = await duplicateLow.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_LOW_EVIDENCE_MISSING");
        Assert.Equal("ReadyForPublish", duplicateView.StageStatus);
        Assert.Equal("NotRequired", duplicateView.GeocodeStatus);

        var lowDecisionKey = Guid.NewGuid();
        var publishLow = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/publish-reviewed",
            new V180B3Review(submittedLow.RowVersion, lowDecisionKey,
                "Independent Leader publishes note only"));
        Assert.True(publishLow.IsSuccessStatusCode,
            await publishLow.Content.ReadAsStringAsync());
        Assert.Equal("Applied", await h.RequestStatus(submittedLow.RequestPublicId));
        var reconcileLow = await leaderClient.GetAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/reconcile?decisionKey={lowDecisionKey}");
        Assert.True(reconcileLow.IsSuccessStatusCode,
            await reconcileLow.Content.ReadAsStringAsync());
        Assert.Contains("COMMITTED_EVIDENCE_REQUIRES_REVIEW",
            await reconcileLow.Content.ReadAsStringAsync());
        var adminReconcileLow = await admin.GetAsync(
            $"api/v1/optionc-candidate/requests/{submittedLow.RequestPublicId}/reconcile?decisionKey={lowDecisionKey}");
        Assert.False(adminReconcileLow.IsSuccessStatusCode);
        Assert.Equal("Second reviewed note", await h.Scalar(
            "SELECT MasterNote FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("3", await h.Scalar(
            "SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        var rejectLocationRv = (byte[])(await new SqlCommand(
            "SELECT RowVersion FROM dbo.Locations WHERE LocationId=10", h.Db).ExecuteScalarAsync()
            ?? throw new InvalidOperationException("OPTIONC_REJECT_LOW_VERSION_MISSING"));
        var rejectSubmit = await visitor.PostAsJsonAsync(
            "api/v1/optionc-candidate/locations/10/changes",
            new V180B3SubmitLocation(10, Convert.ToBase64String(rejectLocationRv),
                "Visitor note to be rejected",
                new V180B3LocationFields("Original 10", null, null,
                    "Original address 10", null, null, "Must never publish")));
        Assert.True(rejectSubmit.IsSuccessStatusCode,
            await rejectSubmit.Content.ReadAsStringAsync());
        var rejectedLow = await rejectSubmit.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_REJECT_LOW_REQUEST_MISSING");
        Assert.Equal("Low", rejectedLow.RiskCode);
        var rejectReview = await leaderClient.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{rejectedLow.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(rejectedLow.RowVersion,
                Guid.NewGuid(), "Independent Leader rejects note", false));
        Assert.True(rejectReview.IsSuccessStatusCode,
            await rejectReview.Content.ReadAsStringAsync());
        Assert.Equal("Rejected", await h.RequestStatus(rejectedLow.RequestPublicId));
        Assert.Equal("Abandoned", await h.StageStatus(rejectedLow.RequestPublicId));
        Assert.Equal("Second reviewed note", await h.Scalar(
            "SELECT MasterNote FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("3", await h.Scalar(
            "SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        Assert.Equal(historicalTrip, await h.Scalar(
            "SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 FOR JSON PATH"));
        Assert.Equal(historicalSnapshot, await h.Scalar(
            "SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 FOR JSON PATH"));
    }

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
        var pendingStatus = await visitor.GetAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, pendingStatus.StatusCode);
        var pendingView = await pendingStatus.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_STATUS_RESPONSE_MISSING");
        Assert.Equal("Pending", pendingView.Status);
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
        var appliedStatus = await visitor.GetAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, appliedStatus.StatusCode);
        var appliedView = await appliedStatus.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_STATUS_RESPONSE_MISSING");
        Assert.Equal("Applied", appliedView.Status);
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
        // The legacy high-risk HTTP publish fixture above intentionally uses
        // the pre-Leader baseline. Add the isolated Leader schema and a live
        // appointed reviewer for the NEW Scheme A final-rejection contract.
        await h.Exec(File.ReadAllText(Path.Combine(FindRoot(), "docs", "release",
            "optionc-canonical-sql-draft", "leader-review",
            "LeaderReviewDetails.disposable.sql")));
        await h.Exec("""
            INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive,CreatedAt)
                VALUES(4,1,1,N'E4',N'Independent Leader',1,SYSUTCDATETIME());
            INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt,UpdatedAt)
                VALUES(4,104,N'Internal',N'E4',N'Demo',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,LegacyUserId,HireDate,SourceType,CreatedAt)
                VALUES(104,104,1,N'E4',4,'2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,SourceType,CreatedAt)
                VALUES(104,N'Active','2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(4,2,SYSUTCDATETIME());
            INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,CreatedAt)
                VALUES(4,2,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.UserTeamScopes(UserId,TeamId,IsPrimary,IsActive,AssignedAt)
                VALUES(4,1,1,1,SYSUTCDATETIME());
            INSERT dbo.UserTeamAssignments(UserId,TeamId,IsPrimary,EffectiveFrom,CreatedAt)
                VALUES(4,1,1,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt)
                VALUES(1,104,'2000-01-01',SYSUTCDATETIME());
            """);
        // Admin cannot reject a regular high-risk change before the
        // independent Leader-first review, even though rejection is safe.
        var prematureAdminReject = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{rejectedRequest.RequestPublicId}/reject",
            new V180B3Review(rejectedRequest.RowVersion, Guid.NewGuid(),
                "Admin must wait for Leader first-level review"));
        Assert.False(prematureAdminReject.IsSuccessStatusCode);
        Assert.Equal("Pending", await h.RequestStatus(rejectedRequest.RequestPublicId));
        using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost") });
        leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(4, "E4", "leader"));
        var leaderReview = await leader.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{rejectedRequest.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(rejectedRequest.RowVersion,
                Guid.NewGuid(), "Independent Leader approves final Admin review", true));
        Assert.True(leaderReview.IsSuccessStatusCode,
            await leaderReview.Content.ReadAsStringAsync());
        var reject = await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{rejectedRequest.RequestPublicId}/reject", new V180B3Review(
            rejectedRequest.RowVersion, Guid.NewGuid(), "HTTP independent rejection"));
        Assert.Equal(System.Net.HttpStatusCode.OK, reject.StatusCode);
        var rejected = await reject.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_HTTP_REJECT_RESPONSE_MISSING");
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Abandoned", await h.StageStatus(rejected.RequestPublicId));
        Assert.Equal("Original address 11", await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=11"));
    }

    [Fact]
    public async Task Leader_first_level_review_real_http_stage_evidence_and_admin_publication_preserve_history()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;

        // Add a distinct, live-appointed Leader reviewer. This is fixture setup,
        // not authorization evidence; all decisions still use the live adapter.
        await h.Exec("""
            INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive,CreatedAt)
                VALUES(4,1,1,N'E4',N'Leader reviewer',1,SYSUTCDATETIME());
            INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt,UpdatedAt)
                VALUES(4,104,N'Internal',N'E4',N'Demo',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,LegacyUserId,HireDate,SourceType,CreatedAt)
                VALUES(104,104,1,N'E4',4,'2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,SourceType,CreatedAt)
                VALUES(104,N'Active','2000-01-01',N'fixture',SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(4,2,SYSUTCDATETIME());
            INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,CreatedAt)
                VALUES(4,2,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.UserTeamScopes(UserId,TeamId,IsPrimary,IsActive,AssignedAt)
                VALUES(4,1,1,1,SYSUTCDATETIME());
            INSERT dbo.UserTeamAssignments(UserId,TeamId,IsPrimary,EffectiveFrom,CreatedAt)
                VALUES(4,1,1,'2000-01-01',SYSUTCDATETIME());
            INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt)
                VALUES(1,104,'2000-01-01',SYSUTCDATETIME());
            """);

        var root = FindRoot();
        await h.Exec(File.ReadAllText(Path.Combine(root, "docs", "release",
            "optionc-canonical-sql-draft", "leader-review",
            "LeaderReviewDetails.disposable.sql")));

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
        var rowVersion = (byte[])(await new SqlCommand(
            "SELECT RowVersion FROM dbo.Locations WHERE LocationId=10", h.Db).ExecuteScalarAsync()
            ?? throw new InvalidOperationException("ROWVERSION_TEST_FIXTURE_MISSING"));
        var submit = await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/10/changes",
            new V180B3SubmitLocation(10, Convert.ToBase64String(rowVersion),
                "Leader first-level review",
                new V180B3LocationFields("Original 10", "Taipei", "Xinyi",
                    "Leader-reviewed address", null, null, "Leader note")));
        Assert.Equal(System.Net.HttpStatusCode.OK, submit.StatusCode);
        var submitted = await submit.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_LEADER_SUBMIT_RESPONSE_MISSING");

        using var leader = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        leader.DefaultRequestHeaders.Authorization = new("Bearer", Token(4, "E4", "leader"));
        var leaderReview = await leader.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/leader-review",
            new V180OptionCLeaderReviewDecision(submitted.RowVersion, Guid.NewGuid(),
                "Team-scope Leader first-level approval", true));
        Assert.True(leaderReview.StatusCode == System.Net.HttpStatusCode.OK,
            await leaderReview.Content.ReadAsStringAsync());
        var leaderView = await leaderReview.Content.ReadFromJsonAsync<V180OptionCLeaderReviewView>()
            ?? throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_RESPONSE_MISSING");
        Assert.Equal("Approved", leaderView.LeaderStatus);
        Assert.Equal("Pending", leaderView.RequestStatus);

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Authorization = new("Bearer", Token(2, "E2", "admin"));
        var stageVersion = (byte[])(await new SqlCommand(
            "SELECT RowVersion FROM dbo.LocationPublicationStages WHERE ChangeRequestId=(SELECT ChangeRequestId FROM dbo.ChangeRequests WHERE RequestPublicId=@id)",
            h.Db) { Parameters = { new SqlParameter("@id", submitted.RequestPublicId) } }.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("OPTIONC_STAGE_FIXTURE_MISSING"));
        var geocode = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/geocode-evidence",
            new V180OptionCGeocodeEvidence(Convert.ToBase64String(stageVersion), true,
                25.03m, 121.56m, "Application-owned disposable geocode")
            { EvidenceKey = Guid.NewGuid() });
        Assert.Equal(System.Net.HttpStatusCode.OK, geocode.StatusCode);
        var geocodeView = await geocode.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_GEOCODE_RESPONSE_MISSING");
        Assert.Equal("PendingDuplicateReview", geocodeView.StageStatus);

        var duplicate = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/duplicate-evidence",
            new V180OptionCDuplicateEvidence(geocodeView.StageRowVersion, true,
                "No duplicate in application-owned review", Guid.NewGuid()));
        Assert.Equal(System.Net.HttpStatusCode.OK, duplicate.StatusCode);
        var duplicateView = await duplicate.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()
            ?? throw new InvalidOperationException("OPTIONC_DUPLICATE_RESPONSE_MISSING");
        Assert.Equal("ReadyForPublish", duplicateView.StageStatus);

        var approve = await admin.PostAsJsonAsync(
            $"api/v1/optionc-candidate/requests/{submitted.RequestPublicId}/approve",
            new V180B3Review(submitted.RowVersion, Guid.NewGuid(),
                "Independent Admin final approval"));
        Assert.Equal(System.Net.HttpStatusCode.OK, approve.StatusCode);
        var applied = await approve.Content.ReadFromJsonAsync<V180B3RequestView>()
            ?? throw new InvalidOperationException("OPTIONC_LEADER_APPROVE_RESPONSE_MISSING");
        Assert.Equal("Applied", applied.Status);
        Assert.Equal("Leader-reviewed address", await h.Scalar(
            "SELECT Address FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("2", await h.Scalar(
            "SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        Assert.Equal("1", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=(SELECT ChangeRequestId FROM dbo.ChangeRequests WHERE RequestPublicId=@id) AND EventType=N'LeaderApproved'",
            P("@id", submitted.RequestPublicId)));
        Assert.Equal("1", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=(SELECT ChangeRequestId FROM dbo.ChangeRequests WHERE RequestPublicId=@id) AND EventType=N'GeocodeEvidenceRecorded'",
            P("@id", submitted.RequestPublicId)));
        Assert.Equal("1", await h.Scalar(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=(SELECT ChangeRequestId FROM dbo.ChangeRequests WHERE RequestPublicId=@id) AND EventType=N'DuplicateReviewEvidenceRecorded'",
            P("@id", submitted.RequestPublicId)));
    }

    [Fact]
    public async Task MergeFields_real_http_submission_approval_reject_and_history_are_atomic()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        await h.Exec("""
            INSERT dbo.ProjectLocations(ProjectLocationId,ProjectId,LocationId,IsActive,IsPrimary,UpdatedAt) VALUES(1,900,10,1,1,SYSUTCDATETIME());
            INSERT dbo.UserFavoriteLocations(UserFavoriteLocationId,UserId,LocationId) VALUES(1,1,10);
            INSERT dbo.GovernmentLocationMasters(GovernmentLocationMasterId,MatchedLocationId,UpdatedAt) VALUES(1,10,SYSUTCDATETIME());
            """);
        var sourceRefs = await h.Scalar("SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 FOR JSON PATH");
        var snapshotRefs = await h.Scalar("SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 FOR JSON PATH");
        var sourceVersion = (byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=10", h.Db).ExecuteScalarAsync() ?? throw new InvalidOperationException());
        var survivorVersion = (byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=11", h.Db).ExecuteScalarAsync() ?? throw new InvalidOperationException());

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
        var submit = await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/merge-fields",
            new V180OptionCMergeSubmit(10, 11, Convert.ToBase64String(sourceVersion),
                Convert.ToBase64String(survivorVersion), "Merge duplicate locations",
                new V180B3LocationFields("Merged survivor", "Taipei", "Xinyi",
                    "Merged address", null, null, "Merge note")));
        Assert.Equal(System.Net.HttpStatusCode.OK, submit.StatusCode);
        var pending = await submit.Content.ReadFromJsonAsync<V180B3RequestView>() ?? throw new InvalidOperationException();
        Assert.Equal("Pending", pending.Status);
        Assert.Equal("1", await h.Scalar("SELECT COUNT(*) FROM dbo.LocationMergeDetails WHERE ChangeRequestId=(SELECT ChangeRequestId FROM dbo.ChangeRequests WHERE RequestPublicId=@id)", P("@id", pending.RequestPublicId)));
        Assert.Equal("1", await h.EventCount(pending.RequestPublicId, "Submitted"));
        Assert.Equal("PendingGeocode", await h.StageStatus(pending.RequestPublicId));
        Assert.Equal("Original address 11", await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=11"));

        await h.Exec("""
            UPDATE s SET GeocodeStatus=N'Succeeded',
              GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),
              Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',
              DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish',UpdatedAt=SYSUTCDATETIME()
            FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
            WHERE r.RequestPublicId=@id;
            """, P("@id", pending.RequestPublicId));

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Authorization = new("Bearer", Token(2, "E2", "admin"));
        var self = await visitor.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{pending.RequestPublicId}/approve",
            new V180B3Review(pending.RowVersion, Guid.NewGuid(), "Self approval denied"));
        Assert.NotEqual(System.Net.HttpStatusCode.OK, self.StatusCode);
        var mergeApproveKey = Guid.NewGuid();
        var approve = await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{pending.RequestPublicId}/approve",
            new V180B3Review(pending.RowVersion, mergeApproveKey, "Independent merge approval"));
        Assert.Equal(System.Net.HttpStatusCode.OK, approve.StatusCode);
        var applied = await approve.Content.ReadFromJsonAsync<V180B3RequestView>() ?? throw new InvalidOperationException();
        Assert.Equal("Applied", applied.Status);

        Assert.Equal("Merged survivor", await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=11"));
        Assert.Equal("Merged address", await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=11"));
        Assert.Equal("0", await h.Scalar("SELECT CONVERT(nvarchar(1),IsActive) FROM dbo.Locations WHERE LocationId=10"));
        Assert.Equal("11", await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.ProjectLocations WHERE ProjectLocationId=1"));
        Assert.Equal("11", await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.UserFavoriteLocations WHERE UserFavoriteLocationId=1"));
        Assert.Equal("11", await h.Scalar("SELECT CONVERT(nvarchar(20),MatchedLocationId) FROM dbo.GovernmentLocationMasters WHERE GovernmentLocationMasterId=1"));
        Assert.Equal(sourceRefs, await h.Scalar("SELECT VisitTripStopId,LocationId FROM dbo.VisitTripStops WHERE LocationId=10 FOR JSON PATH"));
        Assert.Equal(snapshotRefs, await h.Scalar("SELECT VisitTripSnapshotStopId,LocationId FROM dbo.VisitTripSnapshotStops WHERE LocationId=10 FOR JSON PATH"));
        Assert.Equal("2", await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=11 AND IsCurrent=1"));
        Assert.Equal("1", await h.Scalar("SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE LocationId=11 AND VersionNumber=1"));
        Assert.Equal("1", await h.EventCount(pending.RequestPublicId, "Approved"));
        Assert.Equal("1", await h.EventCount(pending.RequestPublicId, "Applied"));
        var reconcile = await admin.GetAsync($"api/v1/optionc-candidate/requests/{pending.RequestPublicId}/reconcile?decisionKey={mergeApproveKey}");
        Assert.Equal(System.Net.HttpStatusCode.OK, reconcile.StatusCode);
        Assert.Contains("COMMITTED_EVIDENCE_REQUIRES_REVIEW", await reconcile.Content.ReadAsStringAsync());

        var rejectSource = (byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=12", h.Db).ExecuteScalarAsync() ?? throw new InvalidOperationException());
        var rejectSurvivor = (byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=13", h.Db).ExecuteScalarAsync() ?? throw new InvalidOperationException());
        var rejectSubmit = await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/merge-fields",
            new V180OptionCMergeSubmit(12, 13, Convert.ToBase64String(rejectSource),
                Convert.ToBase64String(rejectSurvivor), "Reject merge",
                new V180B3LocationFields("Rejected survivor", "Taipei", "Xinyi",
                    "Rejected address", null, null, "Reject note")));
        Assert.Equal(System.Net.HttpStatusCode.OK, rejectSubmit.StatusCode);
        var rejectPending = await rejectSubmit.Content.ReadFromJsonAsync<V180B3RequestView>() ?? throw new InvalidOperationException();
        var reject = await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{rejectPending.RequestPublicId}/reject",
            new V180B3Review(rejectPending.RowVersion, Guid.NewGuid(), "Independent rejection"));
        Assert.Equal(System.Net.HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("Rejected", await h.RequestStatus(rejectPending.RequestPublicId));
        Assert.Equal("Abandoned", await h.StageStatus(rejectPending.RequestPublicId));
        Assert.Equal("Original 12", await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=12"));
        Assert.Equal("Original 13", await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=13"));
    }

    [Fact]
    public async Task MergeFields_http_submission_then_lost_commit_ack_reconciles_complete_publication_evidence()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        await h.Exec("""
            INSERT dbo.ProjectLocations(ProjectLocationId,ProjectId,LocationId,IsActive,IsPrimary,UpdatedAt) VALUES(2,901,10,1,1,SYSUTCDATETIME());
            INSERT dbo.UserFavoriteLocations(UserFavoriteLocationId,UserId,LocationId) VALUES(2,1,10);
            INSERT dbo.GovernmentLocationMasters(GovernmentLocationMasterId,MatchedLocationId,UpdatedAt) VALUES(2,10,SYSUTCDATETIME());
            """);
        var sourceVersion=(byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=10",h.Db).ExecuteScalarAsync()??throw new InvalidOperationException());
        var survivorVersion=(byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=11",h.Db).ExecuteScalarAsync()??throw new InvalidOperationException());
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>
        {
            builder.UseSetting("OptionC:Candidate:Enabled","true");builder.UseSetting("ConnectionStrings:DefaultConnection",h.ConnectionString);builder.UseSetting("Auth:Mode","Demo");builder.UseSetting("Auth:JwtKey","test-key-that-is-at-least-32-characters-long");builder.UseSetting("BackgroundJobs:WorkerEnabled","false");builder.UseSetting("BackgroundJobs:RecoverOnStartup","false");
        });
        using var visitor=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});visitor.DefaultRequestHeaders.Authorization=new("Bearer",Token(1,"E1","visitor"));
        var submit=await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/merge-fields",new V180OptionCMergeSubmit(10,11,Convert.ToBase64String(sourceVersion),Convert.ToBase64String(survivorVersion),"Merge lost ack",new V180B3LocationFields("Ack survivor","Taipei","Xinyi","Ack address",null,null,"Ack note")));
        Assert.Equal(System.Net.HttpStatusCode.OK,submit.StatusCode);var pending=await submit.Content.ReadFromJsonAsync<V180B3RequestView>()??throw new InvalidOperationException();
        await h.Exec("UPDATE s SET GeocodeStatus=N'Succeeded',GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish',UpdatedAt=SYSUTCDATETIME() FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",P("@id",pending.RequestPublicId));
        var direct=new SqlConnectionStringBuilder(h.ConnectionString){Pooling=false,Encrypt=false};await using var proxy=new CommitAckLossProxy(direct.DataSource);var proxied=new SqlConnectionStringBuilder(direct.ConnectionString){DataSource=$"127.0.0.1,{proxy.Port}",Pooling=false,ConnectTimeout=5};var key=Guid.NewGuid();var lost=false;
        await using(var c=new SqlConnection(proxied.ConnectionString))
        {
            await c.OpenAsync();await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);await Service().ApproveAsync(c,tx,pending.RequestPublicId,1,2,key,"Merge lost acknowledgement",CancellationToken.None);proxy.ArmCommitAcknowledgementLoss();
            try{await tx.CommitAsync();}catch(Exception ex) when(ex is SqlException or IOException or InvalidOperationException){lost=proxy.CommitResponseDropped;}
        }
        Assert.True(lost,$"The merge proxy must forward COMMIT and drop only the acknowledgement. {proxy.Trace}");var observed=await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(h.ConnectionString,pending.RequestPublicId,key,2,CancellationToken.None);Assert.Equal("COMMITTED_EVIDENCE_REQUIRES_REVIEW",observed);
        Assert.Equal("Applied",await h.RequestStatus(pending.RequestPublicId));Assert.Equal("2",await h.RequestValue(pending.RequestPublicId,"ReviewedByUserId"));Assert.Equal("0",await h.Scalar("SELECT CONVERT(nvarchar(1),IsActive) FROM dbo.Locations WHERE LocationId=10"));Assert.Equal("Ack survivor",await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=11"));Assert.Equal("11",await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.ProjectLocations WHERE ProjectLocationId=2"));Assert.Equal("11",await h.Scalar("SELECT CONVERT(nvarchar(20),LocationId) FROM dbo.UserFavoriteLocations WHERE UserFavoriteLocationId=2"));Assert.Equal("11",await h.Scalar("SELECT CONVERT(nvarchar(20),MatchedLocationId) FROM dbo.GovernmentLocationMasters WHERE GovernmentLocationMasterId=2"));Assert.Equal("2",await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=11 AND IsCurrent=1"));Assert.Equal("1",await h.Scalar("SELECT COUNT(*) FROM dbo.LocationPublishedVersions WHERE LocationId=11 AND VersionNumber=1"));Assert.Equal("1",await h.EventCount(pending.RequestPublicId,"Approved"));Assert.Equal("1",await h.EventCount(pending.RequestPublicId,"Applied"));Assert.True(proxy.CommitRequestsForwarded>0,$"No commit packet was forwarded. {proxy.Trace}");
    }

    [Fact]
    public async Task MergeFields_stale_source_or_survivor_rowversion_fails_closed_without_partial_publication()
    {
        await using var h=await Harness.TryCreateAsync();if(h is null)return;
        async Task<Guid> Submit(int source,int survivor)
        {
            var sv=(byte[])(await new SqlCommand($"SELECT RowVersion FROM dbo.Locations WHERE LocationId={source}",h.Db).ExecuteScalarAsync()??throw new InvalidOperationException());var uv=(byte[])(await new SqlCommand($"SELECT RowVersion FROM dbo.Locations WHERE LocationId={survivor}",h.Db).ExecuteScalarAsync()??throw new InvalidOperationException());
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{builder.UseSetting("OptionC:Candidate:Enabled","true");builder.UseSetting("ConnectionStrings:DefaultConnection",h.ConnectionString);builder.UseSetting("Auth:Mode","Demo");builder.UseSetting("Auth:JwtKey","test-key-that-is-at-least-32-characters-long");builder.UseSetting("BackgroundJobs:WorkerEnabled","false");builder.UseSetting("BackgroundJobs:RecoverOnStartup","false");});using var visitor=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});visitor.DefaultRequestHeaders.Authorization=new("Bearer",Token(1,"E1","visitor"));var response=await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/merge-fields",new V180OptionCMergeSubmit(source,survivor,Convert.ToBase64String(sv),Convert.ToBase64String(uv),"Stale merge",new V180B3LocationFields($"Survivor {survivor}","Taipei","Xinyi",$"Address {survivor}",null,null,"Stale note")));Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);var view=await response.Content.ReadFromJsonAsync<V180B3RequestView>()??throw new InvalidOperationException();await h.Exec("UPDATE s SET GeocodeStatus=N'Succeeded',GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish' FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",P("@id",view.RequestPublicId));return view.RequestPublicId;
        }
        var first=await Submit(10,11);await h.Exec("UPDATE dbo.Locations SET LocationName=N'Concurrent source touch' WHERE LocationId=10");await using(var tx=await h.Begin()){var sourceConflict=await Assert.ThrowsAsync<SqlException>(()=>Service().ApproveAsync(h.Db,tx,first,1,2,Guid.NewGuid(),"Stale source",CancellationToken.None));Assert.Contains("OPTIONC_MERGE_SOURCE_CONFLICT",sourceConflict.Message);await tx.RollbackAsync();}Assert.Equal("Pending",await h.RequestStatus(first));Assert.Equal("Original 11",await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=11"));
        var second=await Submit(12,13);await h.Exec("UPDATE dbo.Locations SET LocationName=N'Concurrent survivor touch' WHERE LocationId=13");await using(var tx=await h.Begin()){var survivorConflict=await Assert.ThrowsAsync<SqlException>(()=>Service().ApproveAsync(h.Db,tx,second,1,2,Guid.NewGuid(),"Stale survivor",CancellationToken.None));Assert.Contains("OPTIONC_MERGE_SURVIVOR_CONFLICT",survivorConflict.Message);await tx.RollbackAsync();}Assert.Equal("Pending",await h.RequestStatus(second));Assert.Equal("Original 12",await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=12"));Assert.Equal("Concurrent survivor touch",await h.Scalar("SELECT LocationName FROM dbo.Locations WHERE LocationId=13"));
    }


    [Fact]
    public async Task Candidate_http_stage_evidence_is_live_authorized_version_bound_and_fail_closed_on_revocation()
    {
        await using var h=await Harness.TryCreateAsync();if(h is null)return;
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.UseSetting("OptionC:Candidate:Enabled","true");builder.UseSetting("ConnectionStrings:DefaultConnection",h.ConnectionString);
            builder.UseSetting("Auth:Mode","Demo");builder.UseSetting("Auth:JwtKey","test-key-that-is-at-least-32-characters-long");
            builder.UseSetting("BackgroundJobs:WorkerEnabled","false");builder.UseSetting("BackgroundJobs:RecoverOnStartup","false");
        });
        using var visitor=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        visitor.DefaultRequestHeaders.Authorization=new("Bearer",Token(1,"E1","visitor"));
        var rv=Convert.ToBase64String((byte[])(await new SqlCommand("SELECT RowVersion FROM dbo.Locations WHERE LocationId=10",h.Db).ExecuteScalarAsync()??throw new InvalidOperationException()));
        var submit=await visitor.PostAsJsonAsync("api/v1/optionc-candidate/locations/10/changes",new V180B3SubmitLocation(10,rv,"Evidence flow",new V180B3LocationFields("Original 10","Taipei","Xinyi","Evidence address",null,null,"Evidence note")));
        Assert.Equal(System.Net.HttpStatusCode.OK,submit.StatusCode);var request=await submit.Content.ReadFromJsonAsync<V180B3RequestView>()??throw new InvalidOperationException();
        using var admin=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});admin.DefaultRequestHeaders.Authorization=new("Bearer",Token(2,"E2","admin"));
        await using var stageCmd=new SqlCommand("SELECT s.RowVersion FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",h.Db);stageCmd.Parameters.AddWithValue("@id",request.RequestPublicId);var stageRv=Convert.ToBase64String((byte[])(await stageCmd.ExecuteScalarAsync()??throw new InvalidOperationException()));
        var geo=await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{request.RequestPublicId}/geocode-evidence",new V180OptionCGeocodeEvidence(stageRv,true,25.03m,121.56m,"Provider evidence") { EvidenceKey = Guid.NewGuid() });
        Assert.Equal(System.Net.HttpStatusCode.OK,geo.StatusCode);var geov=await geo.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()??throw new InvalidOperationException();Assert.Equal("PendingDuplicateReview",geov.StageStatus);
        var dup=await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{request.RequestPublicId}/duplicate-evidence",new V180OptionCDuplicateEvidence(geov.StageRowVersion,true,"Independent duplicate review",Guid.NewGuid()));
        Assert.Equal(System.Net.HttpStatusCode.OK,dup.StatusCode);var dupv=await dup.Content.ReadFromJsonAsync<V180OptionCStageEvidenceView>()??throw new InvalidOperationException();Assert.Equal("ReadyForPublish",dupv.StageStatus);Assert.Equal("Cleared",dupv.DuplicateReviewStatus);
        Assert.Equal("1",await h.EventCount(request.RequestPublicId,"GeocodeEvidenceRecorded"));
        Assert.Equal("1",await h.EventCount(request.RequestPublicId,"DuplicateReviewEvidenceRecorded"));
        var auditDetails=await h.Scalar("SELECT TOP(1) e.DetailsJson FROM dbo.ChangeRequestEvents e JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId WHERE r.RequestPublicId=@id AND e.EventType=N'DuplicateReviewEvidenceRecorded' ORDER BY e.ChangeRequestEventId DESC",P("@id",request.RequestPublicId));
        Assert.Contains("application-owned-duplicate-review",auditDetails);
        Assert.Contains(request.RequestPublicId.ToString(),auditDetails);
        Assert.Equal("Original address 10",await h.Scalar("SELECT Address FROM dbo.Locations WHERE LocationId=10"));
        var stale=await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{request.RequestPublicId}/duplicate-evidence",new V180OptionCDuplicateEvidence(geov.StageRowVersion,true,"Replay",Guid.NewGuid()));Assert.NotEqual(System.Net.HttpStatusCode.OK,stale.StatusCode);
        await h.Exec("UPDATE dbo.OptionCLiveApprovalActors SET IsIndependentAdminEligible=0 WHERE UserId=2");
        var revoked=await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{request.RequestPublicId}/geocode-evidence",new V180OptionCGeocodeEvidence(dupv.StageRowVersion,true,25.03m,121.56m,"Revoked"));Assert.NotEqual(System.Net.HttpStatusCode.OK,revoked.StatusCode);
    }

    [Fact]
    public async Task Stage_evidence_key_collision_rolls_back_stage_and_audit_atomically()
    {
        await using var h=await Harness.TryCreateAsync();if(h is null)return;
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.UseSetting("OptionC:Candidate:Enabled","true");
            builder.UseSetting("ConnectionStrings:DefaultConnection",h.ConnectionString);
            builder.UseSetting("Auth:Mode","Demo");
            builder.UseSetting("Auth:JwtKey","test-key-that-is-at-least-32-characters-long");
            builder.UseSetting("BackgroundJobs:WorkerEnabled","false");
            builder.UseSetting("BackgroundJobs:RecoverOnStartup","false");
        });
        using var visitor=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        visitor.DefaultRequestHeaders.Authorization=new("Bearer",Token(1,"E1","visitor"));
        using var admin=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
        admin.DefaultRequestHeaders.Authorization=new("Bearer",Token(2,"E2","admin"));

        async Task<V180B3RequestView> Submit(int location)
        {
            var rv=Convert.ToBase64String((byte[])(await new SqlCommand($"SELECT RowVersion FROM dbo.Locations WHERE LocationId={location}",h.Db).ExecuteScalarAsync()??throw new InvalidOperationException()));
            var response=await visitor.PostAsJsonAsync($"api/v1/optionc-candidate/locations/{location}/changes",
                new V180B3SubmitLocation(location,rv,"Evidence collision",
                    new V180B3LocationFields($"Original {location}","Taipei","Xinyi",$"Evidence collision {location}",null,null,"Evidence note")));
            Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
            return await response.Content.ReadFromJsonAsync<V180B3RequestView>()??throw new InvalidOperationException();
        }
        async Task<string> StageVersion(Guid id)
        {
            await using var cmd=new SqlCommand("SELECT s.RowVersion FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",h.Db);
            cmd.Parameters.AddWithValue("@id",id);
            return Convert.ToBase64String((byte[])(await cmd.ExecuteScalarAsync()??throw new InvalidOperationException()));
        }

        var first=await Submit(10);
        var second=await Submit(11);
        var key=Guid.NewGuid();
        var firstGeo=await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{first.RequestPublicId}/geocode-evidence",
            new V180OptionCGeocodeEvidence(await StageVersion(first.RequestPublicId),true,25.03m,121.56m,"Collision source"){EvidenceKey=key});
        Assert.Equal(System.Net.HttpStatusCode.OK,firstGeo.StatusCode);
        Assert.Equal("1",await h.EventCount(first.RequestPublicId,"GeocodeEvidenceRecorded"));
        var secondStageBefore=await h.StageStatus(second.RequestPublicId);
        var secondGeo=await admin.PostAsJsonAsync($"api/v1/optionc-candidate/requests/{second.RequestPublicId}/geocode-evidence",
            new V180OptionCGeocodeEvidence(await StageVersion(second.RequestPublicId),true,25.04m,121.57m,"Collision replay"){EvidenceKey=key});
        Assert.NotEqual(System.Net.HttpStatusCode.OK,secondGeo.StatusCode);
        Assert.Equal(secondStageBefore,await h.StageStatus(second.RequestPublicId));
        Assert.Equal("Pending",await h.RequestStatus(second.RequestPublicId));
        Assert.Equal("0",await h.EventCount(second.RequestPublicId,"GeocodeEvidenceRecorded"));
    }

    private static string Token(int userId, string employeeNo, string role,
        int? organizationId = null, int? teamId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-characters-long"));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Name, employeeNo),
            new("employee_no", employeeNo), new(ClaimTypes.Role, role)
        };
        if (organizationId.HasValue)
            claims.Add(new Claim("organization_id", organizationId.Value.ToString()));
        if (teamId.HasValue)
        {
            claims.Add(new Claim("team_id", teamId.Value.ToString()));
            claims.Add(new Claim("team_scopes", System.Text.Json.JsonSerializer.Serialize(
                new[] { new TeamScopeDto(teamId.Value, "Team 1", true) })));
        }
        var token = new JwtSecurityToken("FieldVisit.UAT", "FieldVisit.UAT.Users", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
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
                foreach(var p in new[]{"1800_011/Up.sql","1800_012/Up.sql","1800_014/Preflight.sql","1800_014/Up.sql","1800_014/Backfill.sql","audit/StageEvidenceAuditEvents.disposable.sql"})
                    await V180OptionCFinalSecurityGateSqlTests.Exec(db,File.ReadAllText(Path.Combine(repo,"docs","release","optionc-canonical-sql-draft",p.Replace('/',Path.DirectorySeparatorChar))));
                await V180OptionCFinalSecurityGateSqlTests.Exec(db,File.ReadAllText(Path.Combine(repo,"docs","release","optionc-canonical-sql-draft","merge","LocationMergeDetails.disposable.sql")));
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
                CREATE TABLE dbo.ProjectLocations(ProjectLocationId bigint NOT NULL PRIMARY KEY,ProjectId int NOT NULL,LocationId int NOT NULL,IsActive bit NOT NULL,IsPrimary bit NOT NULL,UpdatedAt datetime2 NULL);
                CREATE TABLE dbo.UserFavoriteLocations(UserFavoriteLocationId bigint NOT NULL PRIMARY KEY,UserId int NOT NULL,LocationId int NOT NULL);
                CREATE TABLE dbo.GovernmentLocationMasters(GovernmentLocationMasterId bigint NOT NULL PRIMARY KEY,MatchedLocationId int NULL,UpdatedAt datetime2 NULL);
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

    private static string FindRoot()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName,
                "docs/release/optionc-canonical-sql-draft/1800_011/Up.sql")))
                return d.FullName;
            d = d.Parent;
        }
        throw new FileNotFoundException("OPTIONC_FINAL_GATE_REPO_ROOT_NOT_FOUND");
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
