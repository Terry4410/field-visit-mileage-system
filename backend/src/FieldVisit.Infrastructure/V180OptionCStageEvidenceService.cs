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
            var org = await auth.RequireIndependentAdminAsync(actorId, BusinessTime.Today, ct);
            long requestId; int teamId; int requesterId; string requestStatus; byte[] stageVersion; string stageStatus, geocodeStatus, proposedSha;
            await using (var cmd = new SqlCommand("""
                SELECT r.ChangeRequestId,r.TeamId,r.RequestedByUserId,r.Status,
                       s.RowVersion,s.StageStatus,s.GeocodeStatus,s.DuplicateReviewStatus,s.ProposedSha256
                FROM dbo.ChangeRequests r WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.LocationPublicationStages s WITH(UPDLOCK,HOLDLOCK) ON s.ChangeRequestId=r.ChangeRequestId
                WHERE r.RequestPublicId=@id AND r.OrganizationId=@org
                  AND r.EntityKind=N'Location' AND r.OperationCode=N'UpdatePublishedLocation'
                  AND r.RiskCode=N'High';
                """, connection, (SqlTransaction)tx.GetDbTransaction()))
            {
                cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@org", org);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
                requestId=reader.GetInt64(0); teamId=reader.GetInt32(1); requesterId=reader.GetInt32(2); requestStatus=reader.GetString(3);
                stageVersion=reader.GetFieldValue<byte[]>(4); stageStatus=reader.GetString(5); geocodeStatus=reader.GetString(6); proposedSha=Convert.ToHexString(reader.GetFieldValue<byte[]>(8));
            }
            if (requestStatus != "Pending" || stageStatus is "Published" or "Abandoned")
                throw new InvalidOperationException("OPTIONC_STAGE_TERMINAL");
            if (!stageVersion.SequenceEqual(expected))
                throw new InvalidOperationException("OPTIONC_STAGE_ROWVERSION_CONFLICT");
            var evidence = await auth.AuthorizeDecisionAsync(requesterId, actorId, org, teamId, BusinessTime.Today, ct);
            if (!evidence.RequesterEligible || !evidence.IndependentAdminEligible || !evidence.OrganizationMatches || !evidence.TeamMatches)
                throw new UnauthorizedAccessException("OPTIONC_STAGE_REVIEW_SCOPE_DENIED");

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
                actorRole = "Admin",
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
