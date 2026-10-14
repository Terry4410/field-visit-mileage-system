using System.Data;
using System.Text.Json;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

public sealed record V180OptionCGeocodeEvidence(string StageRowVersion, bool Succeeded, decimal? Latitude, decimal? Longitude, string Reason)
{
    public Guid EvidenceKey { get; init; }
}
public sealed record V180OptionCDuplicateEvidence(string StageRowVersion, bool Cleared, string Reason, Guid EvidenceKey);
public sealed record V180OptionCStageEvidenceView(Guid RequestPublicId, string StageStatus, string GeocodeStatus, string DuplicateReviewStatus, string StageRowVersion);

/// <summary>
/// Candidate-only, application-owned evidence transitions. Every transition
/// locks the canonical request first, then the Stage, and revalidates live
/// reviewer authority in the same Serializable transaction. It never updates
/// dbo.Locations and never publishes.
/// </summary>
public sealed class V180OptionCStageEvidenceService(AppDbContext db, ICurrentUserService current, IConfiguration config)
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

    private async Task RequireLiveCreateLeaderAsync(
        V180OptionCLiveAuthorizationAdapter auth, long requestId, int requesterId,
        int organizationId, int teamId, CancellationToken ct)
    {
        var leader = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(x =>
            x.ChangeRequestId == requestId && x.ReviewStage == "LeaderFirstLevel", ct);
        if (leader?.Status != "Approved" || leader.ReviewerUserId is not int leaderId
            || leaderId == requesterId || leader.DecisionKey is not Guid decisionKey
            || !await db.ChangeRequestEvents.AsNoTracking().AnyAsync(x =>
                x.ChangeRequestId == requestId && x.EventType == "LeaderApproved"
                && x.ActorUserId == leaderId && x.DecisionKey == decisionKey, ct))
            throw new UnauthorizedAccessException("OPTIONC_CREATE_LEADER_APPROVAL_REQUIRED");
        var live = await auth.AuthorizeLeaderReviewAsync(
            requesterId, leaderId, organizationId, teamId, BusinessTime.Today, ct);
        if (!live.RequesterEligible || !live.ReviewerLeaderEligible
            || !live.OrganizationMatches || !live.TeamMatches)
            throw new UnauthorizedAccessException("OPTIONC_CREATE_LEADER_REVOKED");
    }

    /// <summary>
    /// Read Stage/RowVersion only for an independently reauthorized Admin
    /// within the isolated candidate database. Never read from UAT/Production.
    /// </summary>
    public async Task<V180OptionCStageEvidenceView> ReadForAdminAsync(
        Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new InvalidOperationException("OPTIONC_STAGE_ID_REQUIRED");
        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var actor = current.GetRequired().UserId;
            var org = await auth.ResolveLiveOrganizationAsync(actor, ct);
            var row = await db.ChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
                x.RequestPublicId == id && x.OrganizationId == org
                && x.EntityKind == "Location" && x.RiskCode == "High"
                && x.Status == "Pending"
                && (x.OperationCode == "CreateLocation"
                    || x.OperationCode == "UpdatePublishedLocation"), ct)
                ?? throw new KeyNotFoundException("OPTIONC_STAGE_NOT_FOUND");
            if (row.TeamId is not int team || team <= 0
                || !int.TryParse(row.EntityId, out var location) || location <= 0)
                throw new UnauthorizedAccessException("OPTIONC_STAGE_SCOPE_DENIED");
            var reviewer = await auth.AuthorizeDecisionAsync(
                row.RequestedByUserId, actor, org, team, BusinessTime.Today, ct);
            if (!reviewer.RequesterEligible || !reviewer.IndependentAdminEligible
                || !reviewer.OrganizationMatches || !reviewer.TeamMatches)
                throw new UnauthorizedAccessException("OPTIONC_STAGE_SCOPE_DENIED");
            var sqlTx = (SqlTransaction)tx.GetDbTransaction();
            if (row.OperationCode == "CreateLocation")
            {
                await RequireLiveCreateLeaderAsync(auth, row.ChangeRequestId,
                    row.RequestedByUserId, org, team, ct);
                await using var catalog = new SqlCommand("""
                    SELECT CASE WHEN OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NOT NULL
                      AND EXISTS (SELECT 1 FROM dbo.SchemaVersions
                                  WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK') THEN 1 ELSE 0 END
                    """, connection, sqlTx);
                if (Convert.ToInt32(await catalog.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_SCHEMA_NOT_READY");
                await using var reservation = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.LocationCreateReservations WITH(HOLDLOCK)
                    WHERE ChangeRequestId=@request AND LocationId=@location
                      AND OrganizationId=@org AND TeamId=@team
                      AND ReservationStatus=N'Pending'
                    """, connection, sqlTx);
                reservation.Parameters.AddWithValue("@request", row.ChangeRequestId);
                reservation.Parameters.AddWithValue("@location", location);
                reservation.Parameters.AddWithValue("@org", org);
                reservation.Parameters.AddWithValue("@team", team);
                if (Convert.ToInt32(await reservation.ExecuteScalarAsync(ct)) != 1)
                    throw new UnauthorizedAccessException("OPTIONC_CREATE_RESERVATION_DENIED");
            }
            await using var cmd = new SqlCommand("""
                SELECT StageStatus,GeocodeStatus,DuplicateReviewStatus,RowVersion
                FROM dbo.LocationPublicationStages WITH(HOLDLOCK)
                WHERE ChangeRequestId=@request AND LocationId=@location
                  AND OrganizationId=@org AND TeamId=@team
                """, connection, sqlTx);
            cmd.Parameters.AddWithValue("@request", row.ChangeRequestId);
            cmd.Parameters.AddWithValue("@location", location);
            cmd.Parameters.AddWithValue("@org", org);
            cmd.Parameters.AddWithValue("@team", team);
            string stageStatus, geocode, duplicate; byte[] stageVersion;
            await using (var rdr = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await rdr.ReadAsync(ct))
                    throw new KeyNotFoundException("OPTIONC_STAGE_NOT_FOUND");
                stageStatus = rdr.GetString(0); geocode = rdr.GetString(1);
                duplicate = rdr.GetString(2); stageVersion = rdr.GetFieldValue<byte[]>(3);
            }
            await tx.CommitAsync(ct);
            return new(id,stageStatus,geocode,duplicate,
                Convert.ToBase64String(stageVersion));
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public Task<V180OptionCStageEvidenceView> RecordGeocodeAsync(Guid id, V180OptionCGeocodeEvidence input, CancellationToken ct)
        => TransitionAsync(id, input.StageRowVersion, input.Reason, input.Succeeded, input.Latitude, input.Longitude, null, input.EvidenceKey, ct);

    public Task<V180OptionCStageEvidenceView> RecordDuplicateReviewAsync(Guid id, V180OptionCDuplicateEvidence input, CancellationToken ct)
        => TransitionAsync(id, input.StageRowVersion, input.Reason, false, null, null, input.Cleared, input.EvidenceKey, ct);

    private async Task<V180OptionCStageEvidenceView> TransitionAsync(Guid id, string expectedStageVersion,
        string reason, bool geocodeSucceeded, decimal? latitude, decimal? longitude, bool? duplicateCleared,
        Guid evidenceKey, CancellationToken ct)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim() || reason.Length > 1000)
            throw new InvalidOperationException("OPTIONC_STAGE_EVIDENCE_INPUT_INVALID");
        var expected = V180B3RowVersionRules.Parse(expectedStageVersion);
        if (evidenceKey == Guid.Empty)
            throw new InvalidOperationException("OPTIONC_EVIDENCE_KEY_REQUIRED");
        if (geocodeSucceeded && (latitude is null || longitude is null || latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180))
            throw new InvalidOperationException("OPTIONC_GEOCODE_COORDINATES_INVALID");

        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actorId = current.GetRequired().UserId;
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var org = await auth.ResolveLiveOrganizationAsync(actorId, ct);
            long requestId; int teamId; int requesterId; string requestStatus, risk; byte[] stageVersion; string stageStatus, geocodeStatus, proposedSha;
            // Candidate CreateLocation reservation table is additive and optional.
            // SQL Server resolves table names at statement compilation even for
            // branches ruled out by OR. Never reference absent candidate schema
            // while processing existing UpdatePublishedLocation evidence.
            await using var catalog = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL THEN 0 ELSE 1 END",
                connection, (SqlTransaction)tx.GetDbTransaction());
            var createSchemaExists = Convert.ToInt32(await catalog.ExecuteScalarAsync(ct)) == 1;
            var scopeSql = createSchemaExists ? """
                SELECT r.ChangeRequestId,r.TeamId,r.RequestedByUserId,r.Status,
                       s.RowVersion,s.StageStatus,s.GeocodeStatus,s.DuplicateReviewStatus,s.ProposedSha256,r.RiskCode
                FROM dbo.ChangeRequests r WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.LocationPublicationStages s WITH(UPDLOCK,HOLDLOCK) ON s.ChangeRequestId=r.ChangeRequestId
                WHERE r.RequestPublicId=@id AND r.OrganizationId=@org
                  AND r.EntityKind=N'Location'
                  AND ((r.OperationCode=N'UpdatePublishedLocation' AND r.RiskCode IN(N'High',N'Low'))
                    OR (r.OperationCode=N'CreateLocation' AND r.RiskCode=N'High'
                      AND EXISTS (SELECT 1 FROM dbo.LocationCreateReservations cr WITH(HOLDLOCK)
                        WHERE cr.ChangeRequestId=r.ChangeRequestId
                          AND cr.LocationId=s.LocationId
                          AND cr.OrganizationId=r.OrganizationId
                          AND cr.TeamId=r.TeamId
                          AND cr.ReservationStatus=N'Pending')));
                """ : """
                SELECT r.ChangeRequestId,r.TeamId,r.RequestedByUserId,r.Status,
                       s.RowVersion,s.StageStatus,s.GeocodeStatus,s.DuplicateReviewStatus,s.ProposedSha256,r.RiskCode
                FROM dbo.ChangeRequests r WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.LocationPublicationStages s WITH(UPDLOCK,HOLDLOCK) ON s.ChangeRequestId=r.ChangeRequestId
                WHERE r.RequestPublicId=@id AND r.OrganizationId=@org
                  AND r.EntityKind=N'Location'
                  AND r.OperationCode=N'UpdatePublishedLocation'
                  AND r.RiskCode IN(N'High',N'Low');
                """;
            await using (var cmd = new SqlCommand(scopeSql, connection, (SqlTransaction)tx.GetDbTransaction()))
            {
                cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@org", org);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
                requestId=reader.GetInt64(0); teamId=reader.GetInt32(1); requesterId=reader.GetInt32(2); requestStatus=reader.GetString(3);
                stageVersion=reader.GetFieldValue<byte[]>(4); stageStatus=reader.GetString(5); geocodeStatus=reader.GetString(6); proposedSha=Convert.ToHexString(reader.GetFieldValue<byte[]>(8)); risk=reader.GetString(9);
            }
            if (requestStatus != "Pending" || stageStatus is "Published" or "Abandoned")
                throw new InvalidOperationException("OPTIONC_STAGE_TERMINAL");
            if (!stageVersion.SequenceEqual(expected))
                throw new InvalidOperationException("OPTIONC_STAGE_ROWVERSION_CONFLICT");
            if (risk == "Low")
            {
                // No geocoder evidence is needed when all address/identity
                // fields are unchanged. Duplicate clearance belongs to a
                // distinct live-appointed Leader, never the requester.
                if (!duplicateCleared.HasValue || geocodeStatus != "NotRequired")
                    throw new UnauthorizedAccessException("OPTIONC_LOW_GEOCODE_NOT_ALLOWED");
                await using var schema = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.SchemaVersions
                    WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK'
                    """, connection, (SqlTransaction)tx.GetDbTransaction());
                if (Convert.ToInt32(await schema.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_LOW_SCHEMA_NOT_READY");
                var leader = await auth.AuthorizeLeaderReviewAsync(
                    requesterId, actorId, org, teamId, BusinessTime.Today, ct);
                if (!leader.RequesterEligible || !leader.ReviewerLeaderEligible
                    || !leader.OrganizationMatches || !leader.TeamMatches)
                    throw new UnauthorizedAccessException("OPTIONC_STAGE_REVIEW_SCOPE_DENIED");
                var firstLevel = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(
                    x => x.ChangeRequestId == requestId
                      && x.ReviewStage == "LeaderFirstLevel", ct);
                if (firstLevel?.Status != "Approved"
                    || firstLevel.ReviewerUserId != actorId
                    || firstLevel.DecisionKey is not Guid approvedKey
                    || !await db.ChangeRequestEvents.AsNoTracking().AnyAsync(
                        x => x.ChangeRequestId == requestId
                          && x.EventType == "LeaderApproved"
                          && x.ActorUserId == actorId
                          && x.DecisionKey == approvedKey, ct))
                    throw new UnauthorizedAccessException("OPTIONC_LOW_LEADER_REVIEW_REQUIRED");
            }
            else
            {
                var evidence = await auth.AuthorizeDecisionAsync(
                    requesterId, actorId, org, teamId, BusinessTime.Today, ct);
                if (!evidence.RequesterEligible || !evidence.IndependentAdminEligible
                    || !evidence.OrganizationMatches || !evidence.TeamMatches)
                    throw new UnauthorizedAccessException("OPTIONC_STAGE_REVIEW_SCOPE_DENIED");
                // A canonical CreateLocation has no Admin evidence path before
                // independent live Leader approval and its canonical event.
                var operation = await db.ChangeRequests.AsNoTracking()
                    .Where(x => x.ChangeRequestId == requestId)
                    .Select(x => x.OperationCode).SingleAsync(ct);
                if (operation == "CreateLocation")
                    await RequireLiveCreateLeaderAsync(auth,requestId,requesterId,org,teamId,ct);
            }

            var sqlTx=(SqlTransaction)tx.GetDbTransaction();
            int changed;
            if (duplicateCleared.HasValue)
            {
                if (geocodeStatus != "Succeeded" && geocodeStatus != "NotRequired")
                    throw new InvalidOperationException("OPTIONC_DUPLICATE_REVIEW_GEOCODE_NOT_READY");
                await using var cmd=new SqlCommand("""
                    UPDATE dbo.LocationPublicationStages
                    SET DuplicateReviewStatus=CASE WHEN @cleared=1 THEN N'Cleared' ELSE N'Blocked' END,
                        DuplicateEvidenceSha256=CASE WHEN @cleared=1 THEN ProposedSha256 ELSE NULL END,
                        StageStatus=CASE WHEN @cleared=1 THEN N'ReadyForPublish' ELSE N'PendingDuplicateReview' END,
                        UpdatedAt=SYSUTCDATETIME()
                    WHERE ChangeRequestId=@request AND RowVersion=@version
                      AND StageStatus=N'PendingDuplicateReview'
                      AND (GeocodeStatus=N'Succeeded' OR GeocodeStatus=N'NotRequired');
                    SELECT CONVERT(int,@@ROWCOUNT);
                    """,connection,sqlTx);
                cmd.Parameters.AddWithValue("@cleared",duplicateCleared.Value?1:0);cmd.Parameters.AddWithValue("@request",requestId);cmd.Parameters.AddWithValue("@version",expected);
                changed=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            else
            {
                await using var cmd=new SqlCommand("""
                    UPDATE dbo.LocationPublicationStages
                    SET GeocodeStatus=CASE WHEN @succeeded=1 THEN N'Succeeded' ELSE N'Failed' END,
                        GeocodeAddressSha256=CASE WHEN @succeeded=1 THEN HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(ProposedJson,N'$.Address'))) ELSE NULL END,
                        Latitude=CASE WHEN @succeeded=1 THEN @lat ELSE NULL END,
                        Longitude=CASE WHEN @succeeded=1 THEN @lon ELSE NULL END,
                        StageStatus=CASE WHEN @succeeded=1 THEN N'PendingDuplicateReview' ELSE N'PendingGeocode' END,
                        UpdatedAt=SYSUTCDATETIME()
                    WHERE ChangeRequestId=@request AND RowVersion=@version
                      AND StageStatus=N'PendingGeocode' AND GeocodeStatus=N'Pending';
                    SELECT CONVERT(int,@@ROWCOUNT);
                    """,connection,sqlTx);
                cmd.Parameters.AddWithValue("@succeeded",geocodeSucceeded?1:0);cmd.Parameters.AddWithValue("@lat",(object?)latitude??DBNull.Value);cmd.Parameters.AddWithValue("@lon",(object?)longitude??DBNull.Value);cmd.Parameters.AddWithValue("@request",requestId);cmd.Parameters.AddWithValue("@version",expected);
                changed=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }
            if (changed != 1) throw new InvalidOperationException("OPTIONC_STAGE_TRANSITION_CONFLICT");

            var postStageVersion = await ReadStageVersionAsync(connection, sqlTx, requestId, ct);
            var eventType = duplicateCleared.HasValue
                ? "DuplicateReviewEvidenceRecorded"
                : "GeocodeEvidenceRecorded";
            var evidenceSource = duplicateCleared.HasValue
                ? "application-owned-duplicate-review"
                : "application-owned-geocoder";
            var decision = duplicateCleared.HasValue
                ? (duplicateCleared.Value ? "Cleared" : "Blocked")
                : (geocodeSucceeded ? "Succeeded" : "Failed");
            var details = JsonSerializer.Serialize(new
            {
                requestPublicId = id,
                actorUserId = actorId,
                actorRole = risk == "Low" ? "Leader" : "Admin",
                eventType,
                stageRowVersionBefore = Convert.ToBase64String(expected),
                stageRowVersionAfter = Convert.ToBase64String(postStageVersion),
                proposedEvidenceSha256 = proposedSha,
                evidenceKey,
                evidenceSource,
                decision,
                reason
            });
            await using (var eventCmd = new SqlCommand("""
                INSERT dbo.ChangeRequestEvents
                    (ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES
                    (@request,@event,@actor,SYSUTCDATETIME(),NEWID(),@decisionKey,@details);
                """, connection, sqlTx))
            {
                eventCmd.Parameters.AddWithValue("@request", requestId);
                eventCmd.Parameters.AddWithValue("@event", eventType);
                eventCmd.Parameters.AddWithValue("@actor", actorId);
                eventCmd.Parameters.AddWithValue("@decisionKey", evidenceKey);
                eventCmd.Parameters.AddWithValue("@details", details);
                await eventCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return await ReadViewAsync(connection,id,ct);
        }
        catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
    }

    private static async Task<byte[]> ReadStageVersionAsync(SqlConnection c, SqlTransaction tx, long requestId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT RowVersion FROM dbo.LocationPublicationStages WITH (HOLDLOCK) WHERE ChangeRequestId=@request",
            c, tx);
        cmd.Parameters.AddWithValue("@request", requestId);
        return (byte[])(await cmd.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("OPTIONC_STAGE_VERSION_MISSING"));
    }

    private static async Task<V180OptionCStageEvidenceView> ReadViewAsync(SqlConnection c, Guid id, CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT r.RequestPublicId,s.StageStatus,s.GeocodeStatus,s.DuplicateReviewStatus,s.RowVersion
            FROM dbo.ChangeRequests r JOIN dbo.LocationPublicationStages s ON s.ChangeRequestId=r.ChangeRequestId
            WHERE r.RequestPublicId=@id
            """,c);cmd.Parameters.AddWithValue("@id",id);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)) throw new KeyNotFoundException("OPTIONC_STAGE_NOT_FOUND");
        return new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),Convert.ToBase64String(reader.GetFieldValue<byte[]>(4)));
    }
}
