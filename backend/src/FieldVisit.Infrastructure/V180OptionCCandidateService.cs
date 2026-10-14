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
            && x.OperationCode == "UpdatePublishedLocation" && (x.RiskCode == "High" || x.RiskCode == "Low")
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
            && x.OperationCode == "UpdatePublishedLocation"
            && (x.RiskCode == "High" || x.RiskCode == "Low"), ct)
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
            && x.EntityKind == "Location" && x.OperationCode == "UpdatePublishedLocation"
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
        if (request.TeamId is not int team || request.RequestedByUserId == reviewer)
            throw new UnauthorizedAccessException("OPTIONC_REVIEW_SCOPE_DENIED");
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
            var org = await auth.RequireIndependentAdminAsync(actorId, BusinessTime.Today, ct);
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(id, org)).AsNoTracking()
                .SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
            if (request.TeamId is not int team || request.RequestedByUserId == actorId)
                throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
            var evidence = await auth.AuthorizeDecisionAsync(request.RequestedByUserId, actorId,
                request.OrganizationId, team, BusinessTime.Today, ct);
            if (!evidence.OrganizationMatches || !evidence.TeamMatches)
                throw new UnauthorizedAccessException("OPTIONC_RECONCILE_SCOPE_DENIED");
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
