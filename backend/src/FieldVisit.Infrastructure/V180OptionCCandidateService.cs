using System.Data;
using System.Text.Json;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Non-deploy candidate composition for the Option C HTTP surface. It is
/// enabled only by an explicit candidate flag and an isolated loopback SQL
/// target; deployed Option C/B3 flags remain independent and false.
/// </summary>
public sealed record V180OptionCCreateLocation(
    int TeamId, string Reason, V180B3LocationFields Proposed);

public sealed class V180OptionCCandidateService(
    AppDbContext db, ICurrentUserService current, IConfiguration config)
{
    private bool Enabled => config.GetValue<bool>("OptionC:Candidate:Enabled");

    private async Task<SqlConnection> RequireTargetAsync(CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("OPTIONC_CANDIDATE_DISABLED");
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);
        return connection;
    }

    /// <summary>
    /// Isolated CreateLocation entry point. A stable server-owned LocationId,
    /// inactive reservation row, canonical request, Submitted event and private
    /// Stage are all committed or rolled back in ONE Serializable transaction.
    /// No published Location or version is written here.
    /// </summary>
    public async Task<V180B3RequestView> CreateAsync(
        V180OptionCCreateLocation input, CancellationToken ct)
    {
        if (input is null || input.TeamId <= 0)
            throw new InvalidOperationException("OPTIONC_CREATE_INPUT_INVALID");
        var validated = V180B3ProposalSafetyRules.Validate(input.Proposed,
            new V180B3LocationFields("", null, null, null, null, null, null),
            input.Reason);
        var proposed = validated.Proposed;
        if (proposed.Address is null || proposed.Address.Length > 500)
            throw new InvalidOperationException("OPTIONC_CREATE_ADDRESS_REQUIRED");
        V180OptionCCreateLowRiskPolicy.ClassifyCreate(proposed.LocationName,
            proposed.Address, 1, input.TeamId);
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var sqlTx = (SqlTransaction)tx.GetDbTransaction();
            var actorId = current.GetRequired().UserId;
            var scope = await new V180OptionCLiveAuthorizationAdapter(db)
                .AuthorizeCreateSubmissionAsync(actorId, input.TeamId, BusinessTime.Today, ct);
            await using (var schema = new SqlCommand("""
                SELECT CAST(CASE WHEN EXISTS (
                  SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
                  AND OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NOT NULL
                  THEN 1 ELSE 0 END AS int)
                """, connection, sqlTx))
            {
                if (Convert.ToInt32(await schema.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_CREATE_SCHEMA_NOT_READY");
            }
            // The transaction-owned identity key serializes duplicate pending
            // submissions from independent HTTP requests and connections.
            var keySource = scope.OrganizationId + "|" + input.TeamId + "|"
                + proposed.LocationName.ToUpperInvariant() + "|"
                + proposed.Address.ToUpperInvariant();
            var keyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(keySource)));
            await using (var claim = new SqlCommand("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=@key,
                    @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
                SELECT @result;
                """, connection, sqlTx))
            {
                claim.Parameters.AddWithValue("@key", "OptionC.Create." + keyHash);
                if (Convert.ToInt32(await claim.ExecuteScalarAsync(ct)) < 0)
                    throw new InvalidOperationException("OPTIONC_CREATE_LOCK_DENIED");
            }
            if (await db.Locations.AsNoTracking().AnyAsync(x =>
                x.OrganizationId == scope.OrganizationId && x.TeamId == input.TeamId
                && x.ApprovalStatus == "Pending" && !x.IsActive
                && x.LocationName == proposed.LocationName
                && x.Address == proposed.Address, ct))
                throw new InvalidOperationException("OPTIONC_CREATE_PENDING_DUPLICATE");

            var requestId = Guid.NewGuid();
            var code = "OC-" + requestId.ToString("N");
            // Disposable SQL fixtures use a non-IDENTITY LocationId while
            // application databases may use IDENTITY. Both allocate IDs in
            // SQL, never from a client. Non-IDENTITY fallback takes TABLOCKX.
            await using var identityCheck = new SqlCommand("""
                SELECT CAST(COLUMNPROPERTY(OBJECT_ID(N'dbo.Locations'),
                    N'LocationId',N'IsIdentity') AS int)
                """, connection, sqlTx);
            var isIdentity = Convert.ToInt32(await identityCheck.ExecuteScalarAsync(ct)) == 1;
            var insertSql = isIdentity ? """
                INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,CreatedByUserId,
                    LocationType,ApprovalStatus,GeocodingStatus,IsActive,IsTemporary,
                    LocationName,Address,City,District,PlusCode,TaxId,MasterNote,CreatedAt)
                VALUES(@org,@team,@code,@user,N'Customer',N'Pending',N'Pending',0,0,
                    @name,@address,@city,@district,@plus,@tax,@note,SYSUTCDATETIME());
                SELECT CONVERT(int,SCOPE_IDENTITY());
                """ : """
                DECLARE @next int;
                SELECT @next=ISNULL(MAX(LocationId),0)+1
                    FROM dbo.Locations WITH(TABLOCKX,HOLDLOCK);
                INSERT dbo.Locations(LocationId,OrganizationId,TeamId,LocationCode,
                    CreatedByUserId,LocationType,ApprovalStatus,GeocodingStatus,
                    IsActive,IsTemporary,LocationName,Address,City,District,PlusCode,
                    TaxId,MasterNote,CreatedAt)
                VALUES(@next,@org,@team,@code,@user,N'Customer',N'Pending',N'Pending',
                    0,0,@name,@address,@city,@district,@plus,@tax,@note,SYSUTCDATETIME());
                SELECT @next;
                """;
            int locationId;
            await using (var insert = new SqlCommand(insertSql, connection, sqlTx))
            {
                insert.Parameters.AddWithValue("@org", scope.OrganizationId);
                insert.Parameters.AddWithValue("@team", input.TeamId);
                insert.Parameters.AddWithValue("@code", code);
                insert.Parameters.AddWithValue("@user", actorId);
                insert.Parameters.AddWithValue("@name", proposed.LocationName);
                insert.Parameters.AddWithValue("@address", proposed.Address);
                insert.Parameters.AddWithValue("@city", (object?)proposed.City ?? DBNull.Value);
                insert.Parameters.AddWithValue("@district", (object?)proposed.District ?? DBNull.Value);
                insert.Parameters.AddWithValue("@plus", (object?)proposed.PlusCode ?? DBNull.Value);
                insert.Parameters.AddWithValue("@tax", (object?)proposed.TaxId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@note", (object?)proposed.MasterNote ?? DBNull.Value);
                locationId = Convert.ToInt32(await insert.ExecuteScalarAsync(ct));
            }
            if (locationId <= 0)
                throw new InvalidOperationException("OPTIONC_CREATE_LOCATION_RESERVATION_FAILED");
            byte[] version;
            await using (var rv = new SqlCommand("""
                SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK)
                WHERE LocationId=@location AND OrganizationId=@org AND TeamId=@team
                  AND CreatedByUserId=@user AND IsActive=0 AND ApprovalStatus=N'Pending'
                """, connection, sqlTx))
            {
                rv.Parameters.AddWithValue("@location", locationId);
                rv.Parameters.AddWithValue("@org", scope.OrganizationId);
                rv.Parameters.AddWithValue("@team", input.TeamId);
                rv.Parameters.AddWithValue("@user", actorId);
                version = await rv.ExecuteScalarAsync(ct) as byte[]
                    ?? throw new InvalidOperationException("OPTIONC_CREATE_VERSION_MISSING");
            }
            if (version.Length != 8)
                throw new InvalidOperationException("OPTIONC_CREATE_VERSION_INVALID");
            var row = new V180B3ChangeRequest
            {
                RequestPublicId=requestId, OrganizationId=scope.OrganizationId,
                TeamId=input.TeamId, EntityKind="Location", EntityId=locationId.ToString(),
                OperationCode="CreateLocation", RiskCode="High",
                ExpectedEntityRowVersion=version, BeforeJson=null,
                ProposedJson=JsonSerializer.Serialize(proposed),
                EvidenceJson=JsonSerializer.Serialize(new { Source="LiveHR",
                    Operation="CreateLocation", scope.VisitorEligible, scope.LeaderEligible }),
                RequestedByUserId=actorId, SubmittedAt=DateTime.UtcNow, Status="Pending"
            };
            db.ChangeRequests.Add(row);
            await db.SaveChangesAsync(ct);
            await using (var reservation = new SqlCommand("""
                INSERT dbo.LocationCreateReservations(ChangeRequestId,LocationId,
                    OrganizationId,TeamId,ReservedLocationRowVersion,
                    ReservationStatus,CreatedAt,UpdatedAt)
                VALUES(@request,@location,@org,@team,@version,N'Pending',
                    SYSUTCDATETIME(),SYSUTCDATETIME());
                SELECT CONVERT(int,@@ROWCOUNT);
                """, connection, sqlTx))
            {
                reservation.Parameters.AddWithValue("@request", row.ChangeRequestId);
                reservation.Parameters.AddWithValue("@location", locationId);
                reservation.Parameters.AddWithValue("@org", scope.OrganizationId);
                reservation.Parameters.AddWithValue("@team", input.TeamId);
                reservation.Parameters.AddWithValue("@version", version);
                if (Convert.ToInt32(await reservation.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_FAILED");
            }
            db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
                ChangeRequestId=row.ChangeRequestId, EventType="Submitted",
                ActorUserId=actorId, OccurredAt=DateTime.UtcNow,
                CorrelationId=Guid.NewGuid(),
                DetailsJson=JsonSerializer.Serialize(new { Reason=validated.Reason,
                    LocationId=locationId, Operation="CreateLocation" })
            });
            await db.SaveChangesAsync(ct);
            await V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(
                connection, sqlTx, requestId, scope.OrganizationId, ct);
            await tx.CommitAsync(ct);
            return ToView(row);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task<V180B3RequestView> SubmitAsync(V180B3SubmitLocation input, CancellationToken ct)
    {
        var expected = V180B3RequestInputRules.RequireSubmission(input);
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actor = current.GetRequired();
            var actorId = actor.UserId;
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var scope = await auth.AuthorizeSubmissionAsync(actorId, input.LocationId, BusinessTime.Today, ct);
            var location = await db.Locations.FromSqlInterpolated($"""
                SELECT * FROM dbo.Locations WITH (UPDLOCK,HOLDLOCK)
                WHERE LocationId={input.LocationId} AND OrganizationId={scope.OrganizationId}
                """).SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_LOCATION_NOT_FOUND");
            if (location.RowVersion is null || !location.RowVersion.SequenceEqual(expected))
                throw new InvalidOperationException("ROWVERSION_CONFLICT");
            var before = new V180B3LocationFields(location.LocationName, location.City,
                location.District, location.Address, location.PlusCode, location.TaxId, location.MasterNote);
            var proposal = V180B3ProposalSafetyRules.Validate(input.Proposed, before, input.Reason);
            // The server, not the caller, owns risk classification. A Low
            // request is allowed only with the additive isolated SQL contract.
            var risk = V180OptionCCreateLowRiskPolicy.ClassifyPublishedUpdate(
                before.City, before.District, before.Address, before.PlusCode, before.TaxId,
                proposal.Proposed.City, proposal.Proposed.District, proposal.Proposed.Address,
                proposal.Proposed.PlusCode, proposal.Proposed.TaxId,
                published: true, changedAnyField: true);
            if (risk == V180OptionCCreateLowRiskPolicy.Low)
            {
                await using var gate = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.SchemaVersions
                    WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK'
                    """, connection, (SqlTransaction)tx.GetDbTransaction());
                if (Convert.ToInt32(await gate.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_LOW_SCHEMA_NOT_READY");
            }
            if (await db.ChangeRequests.AnyAsync(x => x.OrganizationId == scope.OrganizationId
                && x.EntityKind == "Location" && x.EntityId == input.LocationId.ToString()
                && x.Status == "Pending", ct))
                throw new InvalidOperationException("B3_PENDING_REQUEST_EXISTS");
            var row = new V180B3ChangeRequest {
                RequestPublicId = Guid.NewGuid(), OrganizationId = scope.OrganizationId,
                TeamId = scope.TeamId, EntityKind = "Location", EntityId = input.LocationId.ToString(),
                OperationCode = "UpdatePublishedLocation", RiskCode = risk,
                ExpectedEntityRowVersion = location.RowVersion.ToArray(),
                BeforeJson = JsonSerializer.Serialize(before), ProposedJson = JsonSerializer.Serialize(proposal.Proposed),
                EvidenceJson = JsonSerializer.Serialize(new { Source = "LiveHR", scope.VisitorEligible, scope.LeaderEligible }),
                RequestedByUserId = actorId, SubmittedAt = DateTime.UtcNow, Status = "Pending" };
            db.ChangeRequests.Add(row);
            await db.SaveChangesAsync(ct);
            db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
                ChangeRequestId = row.ChangeRequestId, EventType = "Submitted", ActorUserId = actorId,
                OccurredAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid(),
                DetailsJson = JsonSerializer.Serialize(new { Reason = proposal.Reason }) });
            await db.SaveChangesAsync(ct);
            var sqlTx = tx.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
            await V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(
                connection, sqlTx, row.RequestPublicId, scope.OrganizationId, ct);
            await tx.CommitAsync(ct);
            return ToView(row);
        }
        catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
    }

    public async Task<IReadOnlyList<V180B3RequestView>> OwnAsync(CancellationToken ct)
    {
        await RequireTargetAsync(ct);
        var actorId = current.GetRequired().UserId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db).ResolveLiveOrganizationAsync(actorId, ct);
        var rows = await db.ChangeRequests.AsNoTracking().Where(x => x.OrganizationId == org
            && x.RequestedByUserId == actorId && x.EntityKind == "Location"
            && (x.OperationCode == "UpdatePublishedLocation" || x.OperationCode == "CreateLocation")
            && (x.RiskCode == "High" || (x.OperationCode == "UpdatePublishedLocation" && x.RiskCode == "Low"))
            && (x.Status == "Pending" || x.Status == "Rejected" || x.Status == "Applied"))
            .OrderByDescending(x => x.SubmittedAt).Take(100).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<V180B3RequestView> StatusAsync(Guid id, CancellationToken ct)
    {
        await RequireTargetAsync(ct);
        var actorId = current.GetRequired().UserId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(actorId, ct);
        var row = await db.ChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RequestPublicId == id && x.OrganizationId == org
            && x.RequestedByUserId == actorId
            && x.EntityKind == "Location"
            && (x.OperationCode == "UpdatePublishedLocation" || x.OperationCode == "CreateLocation")
            && (x.RiskCode == "High" || (x.OperationCode == "UpdatePublishedLocation" && x.RiskCode == "Low")), ct)
            ?? throw new UnauthorizedAccessException("OPTIONC_REQUEST_SCOPE_DENIED");
        await tx.CommitAsync(ct);
        return ToView(row);
    }

    public async Task<IReadOnlyList<V180B3RequestView>> QueueAsync(CancellationToken ct)
    {
        await RequireTargetAsync(ct);
        var actorId = current.GetRequired().UserId;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .RequireIndependentAdminAsync(actorId, BusinessTime.Today, ct);
        var rows = await db.ChangeRequests.AsNoTracking().Where(x => x.OrganizationId == org
            && x.EntityKind == "Location"
            && (x.OperationCode == "UpdatePublishedLocation" || x.OperationCode == "CreateLocation")
            && x.RiskCode == "High" && x.Status == "Pending")
            .OrderBy(x => x.SubmittedAt).Take(100).ToListAsync(ct);
        var visible = new List<V180B3ChangeRequest>();
        foreach (var row in rows)
        {
            if (row.TeamId is not int team) continue;
            try
            {
                await new V180OptionCLiveAuthorizationAdapter(db).AuthorizeDecisionAsync(
                    row.RequestedByUserId, actorId, org, team, BusinessTime.Today, ct);
                visible.Add(row);
            }
            catch (UnauthorizedAccessException) { }
        }
        await tx.CommitAsync(ct);
        return visible.Select(ToView).ToList();
    }

    public async Task<V180B3RequestView> ApproveAsync(Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        await RequireTargetAsync(ct);
        await new V180OptionCAtomicPublicationOrchestrator(db, current)
            .ExecuteAsync(id, input.DecisionKey, input.Reason, ct);
        return await FindAsync(id, ct);
    }

    /// <summary>
    /// Candidate-only CreateLocation/High and UpdatePublishedLocation/Low
    /// publication. No deployed feature flag or production DB can enable it:
    /// RequireTargetAsync enforces explicit candidate setting and disposable
    /// local SQL Server, while the orchestrator revalidates live authority.
    /// </summary>
    public async Task<V180B3RequestView> PublishReviewedAsync(
        Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        await RequireTargetAsync(ct);
        await new V180OptionCCreateLowRiskPublicationOrchestrator(db, current)
            .ExecuteAsync(id, input.DecisionKey, input.Reason,
                input.RequestRowVersion, ct);
        return await FindAsync(id, ct);
    }

    public async Task<V180B3RequestView> RejectAsync(Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var reviewer = current.GetRequired().UserId;
        var auth = new V180OptionCLiveAuthorizationAdapter(db);
        var org = await auth.ResolveLiveOrganizationAsync(reviewer, ct);
        var request = await db.ChangeRequests.FromSqlInterpolated(
            V180B3ApprovalLockSql.RequestByPublicId(id, org)).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
        if (request.TeamId is not int team || request.RequestedByUserId == reviewer
            || request.EntityKind != "Location" || request.Status != "Pending"
            || request.RiskCode != "High"
            || request.OperationCode is not ("UpdatePublishedLocation" or "CreateLocation" or "MergeLocationFields"))
            throw new UnauthorizedAccessException("OPTIONC_REVIEW_SCOPE_DENIED");
        // Admin is the final reviewer of HIGH-risk requests only. Low-risk
        // rejection belongs to an independent live Leader; the Admin route
        // must never bypass the Leader-first review contract.
        // MergeFields uses the existing distinct merge approval workflow.
        // Keep its Admin rejection path intact; it does not publish anything.
        // Non-merge Create/Update requests require independent Leader-first
        // approval even for a final Admin rejection.
        await using var mergeCheck = new SqlCommand("""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM dbo.LocationMergeDetails
                WHERE ChangeRequestId=@id) THEN 1 ELSE 0 END
            """, connection, (SqlTransaction)tx.GetDbTransaction());
        mergeCheck.Parameters.AddWithValue("@id", request.ChangeRequestId);
        var isMerge = Convert.ToInt32(await mergeCheck.ExecuteScalarAsync(ct)) == 1;
        if (!isMerge)
        {
        var leader = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(
            x => x.ChangeRequestId == request.ChangeRequestId
              && x.ReviewStage == "LeaderFirstLevel", ct);
        if (leader?.Status != "Approved" || leader.ReviewerUserId is not int leaderId
            || leaderId == reviewer || leaderId == request.RequestedByUserId
            || leader.DecisionKey is not Guid leaderKey
            || !await db.ChangeRequestEvents.AsNoTracking().AnyAsync(
                x => x.ChangeRequestId == request.ChangeRequestId
                  && x.EventType == "LeaderApproved"
                  && x.ActorUserId == leaderId && x.DecisionKey == leaderKey, ct))
            throw new UnauthorizedAccessException("OPTIONC_ADMIN_REJECT_REQUIRES_LEADER_APPROVAL");
        var liveLeader = await auth.AuthorizeLeaderReviewAsync(
            request.RequestedByUserId, leaderId, request.OrganizationId,
            team, BusinessTime.Today, ct);
        if (!liveLeader.RequesterEligible || !liveLeader.ReviewerLeaderEligible
            || !liveLeader.OrganizationMatches || !liveLeader.TeamMatches)
            throw new UnauthorizedAccessException("OPTIONC_ADMIN_REJECT_LEADER_REVOKED");
        }
        var evidence = await auth.AuthorizeDecisionAsync(request.RequestedByUserId, reviewer,
            request.OrganizationId, team, BusinessTime.Today, ct);
        if (!evidence.OrganizationMatches || !evidence.TeamMatches)
            throw new UnauthorizedAccessException("OPTIONC_REVIEW_SCOPE_DENIED");
        if (await db.ChangeRequestEvents.AnyAsync(x => x.DecisionKey == input.DecisionKey, ct))
            throw new InvalidOperationException("OPTIONC_DECISION_REPLAY");
        var reason = V180B3ProposalSafetyRules.RequireIndependentReview(
            request.RequestedByUserId, reviewer, request.Status, request.RowVersion,
            input.RequestRowVersion, input.DecisionKey, input.Reason);
        request.Status = "Rejected"; request.ReviewedByUserId = reviewer;
        request.ReviewedAt = DateTime.UtcNow; request.ReviewReason = reason;
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
            ChangeRequestId = request.ChangeRequestId, EventType = "Rejected", ActorUserId = reviewer,
            OccurredAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid(), DecisionKey = input.DecisionKey,
            DetailsJson = JsonSerializer.Serialize(new { Reason = reason }) });
        var sqlTx = tx.GetDbTransaction() as SqlTransaction
            ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
        await using (var stage = new SqlCommand("""
            UPDATE dbo.LocationPublicationStages
            SET StageStatus=N'Abandoned',UpdatedAt=SYSUTCDATETIME()
            WHERE ChangeRequestId=@id
              AND StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish');
            SELECT CONVERT(int,@@ROWCOUNT);
            """, connection, sqlTx))
        {
            stage.Parameters.AddWithValue("@id", request.ChangeRequestId);
            if (Convert.ToInt32(await stage.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_REJECT_STAGE_CONFLICT");
        }
        if (request.OperationCode == "CreateLocation")
        {
            await using var reservation = new SqlCommand("""
                UPDATE dbo.LocationCreateReservations
                SET ReservationStatus=N'Abandoned',UpdatedAt=SYSUTCDATETIME()
                WHERE ChangeRequestId=@request AND ReservationStatus=N'Pending';
                SELECT CONVERT(int,@@ROWCOUNT);
                """, connection, sqlTx);
            reservation.Parameters.AddWithValue("@request", request.ChangeRequestId);
            if (Convert.ToInt32(await reservation.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_ADMIN_REJECT_RESERVATION_CONFLICT");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToView(request);
    }

    public async Task<string> ReconcileAsync(Guid id, Guid decisionKey, CancellationToken ct)
    {
        if (decisionKey == Guid.Empty)
            throw new InvalidOperationException("OPTIONC_DECISION_KEY_REQUIRED");
        var connection = await RequireTargetAsync(ct);
        var freshConnectionString = config.GetConnectionString("DefaultConnection")
            ?? connection.ConnectionString;
        var actorId = current.GetRequired().UserId;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var org = await auth.ResolveLiveOrganizationAsync(actorId, ct);
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(id, org)).AsNoTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
            if (request.TeamId is not int team || request.RequestedByUserId == actorId)
                throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            if (request.OperationCode == "UpdatePublishedLocation" && request.RiskCode == "Low")
            {
                var leader = await auth.AuthorizeLeaderReviewAsync(
                    request.RequestedByUserId, actorId, org, team, BusinessTime.Today, ct);
                var review = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(
                    x => x.ChangeRequestId == request.ChangeRequestId
                      && x.ReviewStage == "LeaderFirstLevel", ct);
                if (!leader.RequesterEligible || !leader.ReviewerLeaderEligible
                    || !leader.OrganizationMatches || !leader.TeamMatches
                    || review?.Status != "Approved" || review.ReviewerUserId != actorId)
                    throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            }
            else
            {
                var evidence = await auth.AuthorizeDecisionAsync(
                    request.RequestedByUserId, actorId,
                    request.OrganizationId, team, BusinessTime.Today, ct);
                if (!evidence.RequesterEligible || !evidence.IndependentAdminEligible
                    || !evidence.OrganizationMatches || !evidence.TeamMatches)
                    throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            }
            await tx.CommitAsync(ct);
        }
        // Observe using a fresh non-pooled connection after authorization so the
        // read cannot inherit stale transaction state or retry across COMMIT.
        return await V180OptionCUnknownCommitReconciler.ObserveAsync(
            freshConnectionString, id, decisionKey, actorId, ct);
    }

    private async Task<V180B3RequestView> FindAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(current.GetRequired().UserId, ct);
        var row = await db.ChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RequestPublicId == id && x.OrganizationId == org, ct)
            ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
        await tx.CommitAsync(ct);
        return ToView(row);
    }

    private static V180B3RequestView ToView(V180B3ChangeRequest r) => new(
        r.RequestPublicId, r.EntityId, r.TeamId, r.RiskCode, r.Status,
        r.RequestedByUserId, r.SubmittedAt, r.BeforeJson, r.ProposedJson,
        Convert.ToBase64String(r.RowVersion));
}
