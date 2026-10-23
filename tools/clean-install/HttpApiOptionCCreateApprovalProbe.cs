using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

/// <summary>
/// Exercises the RC-derived, explicitly enabled Option C candidate through
/// loopback HTTP and the same genuinely clean-installed SQL Server 2022 DB.
/// Candidate enablement is test-process-only; every Option C service also
/// requires IsolatedSqlTest plus the fixed OptionCIsolated_* loopback target.
/// </summary>
internal static class CleanInstallOptionCCreateApprovalProbe
{
    private const string TargetDatabase = "OptionCIsolated_FieldVisitV180CleanInstall";
    private static InvalidOperationException Fail(string code) => new("CLEAN_BIZ_OPTIONC_" + code);

    private static async Task<HttpClient> LoginAsync(HttpClient host, string account, string password)
    {
        using var response = await host.PostAsJsonAsync("/api/v1/auth/demo-login",
            new { account, password });
        if (response.StatusCode != HttpStatusCode.OK) throw Fail("LOGIN");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw Fail("TOKEN_MISSING");
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        })
        {
            BaseAddress = host.BaseAddress,
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> RequireJsonAsync(
        HttpResponseMessage response, HttpStatusCode expected, string stage)
    {
        if (response.StatusCode != expected) throw Fail(stage + "_HTTP");
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync()); }
        catch (JsonException) { throw Fail(stage + "_JSON"); }
    }

    internal static async Task VerifyAsync(
        HttpClient host, string cs, CleanInstallBusinessProbe.Fixture fixture,
        string demoPassword)
    {
        var builder = new SqlConnectionStringBuilder(cs);
        if (builder.DataSource != "localhost,1433" || builder.InitialCatalog != TargetDatabase
            || builder.UserID != "sa" || builder.IntegratedSecurity)
            throw Fail("NONDISPOSABLE_TARGET");

        using var visitor = await LoginAsync(host, fixture.Visitor.Account, demoPassword);
        using var leader = await LoginAsync(host, fixture.Leader.Account, demoPassword);
        using var admin = await LoginAsync(host, fixture.Admin.Account, demoPassword);

        async Task<Guid> SubmitCreateAsync(HttpClient requester, string suffix)
        {
            using var response = await requester.PostAsJsonAsync(
                "/api/v1/optionc-candidate/locations", new
                {
                    teamId = fixture.TeamId,
                    reason = "Disposable three-role clean-install approval test",
                    proposed = new
                    {
                        locationName = "Synthetic Option C " + suffix,
                        city = "Taipei",
                        district = "Datong",
                        address = "100 Disposable Test Road " + suffix,
                        plusCode = (string?)null,
                        taxId = (string?)null,
                        masterNote = "NONDEPLOY SQL2022 synthetic test"
                    }
                });
            using var body = await RequireJsonAsync(response, HttpStatusCode.OK, "CREATE");
            var root = body.RootElement;
            if (root.GetProperty("operationCode").GetString() != "CreateLocation"
                || root.GetProperty("status").GetString() != "Pending"
                || root.GetProperty("teamId").GetInt32() != fixture.TeamId
                || root.GetProperty("requestedByUserId").GetInt32() <= 0
                || string.IsNullOrWhiteSpace(root.GetProperty("rowVersion").GetString()))
                throw Fail("CREATE_CONTRACT");
            return root.GetProperty("requestPublicId").GetGuid();
        }

        async Task<JsonDocument> RequestAsync(HttpClient actor, Guid id)
        {
            using var response = await actor.GetAsync("/api/v1/optionc-candidate/requests/" + id);
            return await RequireJsonAsync(response, HttpStatusCode.OK, "REQUEST_READ");
        }

        async Task<string> LeaderReviewAsync(HttpClient requester, Guid id, bool approve)
        {
            using var current = await RequestAsync(requester, id);
            var rowVersion = current.RootElement.GetProperty("rowVersion").GetString()
                ?? throw Fail("REQUEST_ROWVERSION");
            using var queue = await leader.GetAsync("/api/v1/optionc-candidate/leader/review-queue");
            using var queued = await RequireJsonAsync(queue, HttpStatusCode.OK, "LEADER_QUEUE");
            if (!queued.RootElement.EnumerateArray().Any(x =>
                    x.GetProperty("requestPublicId").GetGuid() == id))
                throw Fail("LEADER_QUEUE_SCOPE");
            using var reviewed = await leader.PostAsJsonAsync(
                "/api/v1/optionc-candidate/requests/" + id + "/leader-review", new
                {
                    requestRowVersion = rowVersion,
                    decisionKey = Guid.NewGuid(),
                    reason = "Independent appointed Leader decision",
                    approve
                });
            using var result = await RequireJsonAsync(reviewed, HttpStatusCode.OK, "LEADER_REVIEW");
            if (result.RootElement.GetProperty("leaderStatus").GetString()
                != (approve ? "Approved" : "Rejected"))
                throw Fail("LEADER_REVIEW_STATE");
            return result.RootElement.GetProperty("rowVersion").GetString()
                ?? throw Fail("LEADER_RESULT_ROWVERSION");
        }

        var successfulId = await SubmitCreateAsync(visitor, "APPROVED");
        var requestVersion = await LeaderReviewAsync(visitor, successfulId, approve: true);

        using var stageResponse = await admin.GetAsync(
            "/api/v1/optionc-candidate/requests/" + successfulId + "/stage");
        using var stage = await RequireJsonAsync(stageResponse, HttpStatusCode.OK, "ADMIN_STAGE");
        var stageVersion = stage.RootElement.GetProperty("stageRowVersion").GetString()
            ?? throw Fail("STAGE_ROWVERSION");

        using var geoResponse = await admin.PostAsJsonAsync(
            "/api/v1/optionc-candidate/requests/" + successfulId + "/geocode-evidence", new
            {
                stageRowVersion = stageVersion,
                succeeded = true,
                latitude = 25.033m,
                longitude = 121.5654m,
                reason = "Synthetic deterministic geocode evidence",
                evidenceKey = Guid.NewGuid()
            });
        using var geo = await RequireJsonAsync(geoResponse, HttpStatusCode.OK, "GEOCODE_EVIDENCE");
        stageVersion = geo.RootElement.GetProperty("stageRowVersion").GetString()
            ?? throw Fail("GEOCODE_ROWVERSION");

        using var duplicateResponse = await admin.PostAsJsonAsync(
            "/api/v1/optionc-candidate/requests/" + successfulId + "/duplicate-evidence", new
            {
                stageRowVersion = stageVersion,
                cleared = true,
                reason = "Synthetic isolated duplicate review",
                evidenceKey = Guid.NewGuid()
            });
        using var duplicate = await RequireJsonAsync(
            duplicateResponse, HttpStatusCode.OK, "DUPLICATE_EVIDENCE");
        _ = duplicate.RootElement.GetProperty("stageRowVersion").GetString()
            ?? throw Fail("DUPLICATE_ROWVERSION");

        using var publishResponse = await admin.PostAsJsonAsync(
            "/api/v1/optionc-candidate/requests/" + successfulId + "/publish-reviewed", new
            {
                requestRowVersion = requestVersion,
                decisionKey = Guid.NewGuid(),
                reason = "Independent Admin final approval"
            });
        using var published = await RequireJsonAsync(
            publishResponse, HttpStatusCode.OK, "ADMIN_PUBLISH");
        var publishedRoot = published.RootElement;
        if (publishedRoot.GetProperty("status").GetString() != "Applied"
            || publishedRoot.GetProperty("requestedByUserId").GetInt32() != fixture.Visitor.UserId)
            throw Fail("ADMIN_PUBLISH_STATE");
        if (!int.TryParse(publishedRoot.GetProperty("entityId").GetString(), out var locationId)
            || locationId <= 0)
            throw Fail("PUBLISHED_LOCATION_ID");

        await VerifyPublishedStateAsync(cs, successfulId, locationId, fixture);

        // The Admin may initiate a scoped request but cannot approve their own
        // request. The leader's distinct first-stage approval is not enough to
        // bypass the final independence check.
        var selfId = await SubmitCreateAsync(admin, "SELF-APPROVAL-DENIED");
        var selfVersion = await LeaderReviewAsync(admin, selfId, approve: true);
        using var selfResponse = await admin.PostAsJsonAsync(
            "/api/v1/optionc-candidate/requests/" + selfId + "/publish-reviewed", new
            {
                requestRowVersion = selfVersion,
                decisionKey = Guid.NewGuid(),
                reason = "Attempted same-actor final approval"
            });
        if (selfResponse.StatusCode != HttpStatusCode.Forbidden)
            throw Fail("SELF_APPROVAL_NOT_DENIED");
        await VerifySelfApprovalStoppedAsync(cs, selfId, fixture.Admin.UserId);

        Console.WriteLine("CLEAN_INSTALL=REAL_HTTP_VISITOR_CREATE_LEADER_REVIEW_ADMIN_APPROVAL_SQL2022_PASS");
        Console.WriteLine("CLEAN_INSTALL=REAL_HTTP_ADMIN_SELF_APPROVAL_DENIAL_ATOMIC_PASS");
    }

    private static async Task VerifyPublishedStateAsync(
        string cs, Guid requestId, int locationId, CleanInstallBusinessProbe.Fixture f)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM dbo.ChangeRequests r
            JOIN dbo.Locations l ON l.LocationId=TRY_CONVERT(int,r.EntityId)
            JOIN dbo.LocationCreateReservations cr ON cr.ChangeRequestId=r.ChangeRequestId
            JOIN dbo.LocationPublishedVersions pv ON pv.ChangeRequestId=r.ChangeRequestId
            JOIN dbo.LocationPublicationStages s ON s.ChangeRequestId=r.ChangeRequestId
            WHERE r.RequestPublicId=@request AND r.Status=N'Applied'
              AND r.RequestedByUserId=@visitor AND r.TeamId=@team
              AND l.LocationId=@location AND l.ApprovalStatus=N'Approved' AND l.IsActive=1
              AND cr.ReservationStatus=N'Published'
              AND pv.IsCurrent=1 AND pv.VersionNumber=1
              AND s.StageStatus=N'Published'
              AND EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents e
                WHERE e.ChangeRequestId=r.ChangeRequestId AND e.EventType=N'LeaderApproved')
              AND EXISTS(SELECT 1 FROM dbo.ChangeRequestEvents e
                WHERE e.ChangeRequestId=r.ChangeRequestId AND e.EventType=N'Applied');
            """, connection) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("@request", requestId);
        command.Parameters.AddWithValue("@location", locationId);
        command.Parameters.AddWithValue("@visitor", f.Visitor.UserId);
        command.Parameters.AddWithValue("@team", f.TeamId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 1)
            throw Fail("PUBLISHED_SQL_ATOMIC_STATE");
    }

    private static async Task VerifySelfApprovalStoppedAsync(
        string cs, Guid requestId, int adminId)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM dbo.ChangeRequests r
            JOIN dbo.Locations l ON l.LocationId=TRY_CONVERT(int,r.EntityId)
            JOIN dbo.LocationCreateReservations cr ON cr.ChangeRequestId=r.ChangeRequestId
            WHERE r.RequestPublicId=@request AND r.Status=N'Pending'
              AND r.RequestedByUserId=@admin AND r.ReviewedByUserId IS NULL
              AND l.ApprovalStatus=N'Pending' AND l.IsActive=0
              AND cr.ReservationStatus=N'Pending'
              AND NOT EXISTS(SELECT 1 FROM dbo.LocationPublishedVersions pv
                WHERE pv.ChangeRequestId=r.ChangeRequestId);
            """, connection) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("@request", requestId);
        command.Parameters.AddWithValue("@admin", adminId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 1)
            throw Fail("SELF_APPROVAL_PARTIAL_WRITE");
    }
}
