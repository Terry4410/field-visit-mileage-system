using System.Data;
using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

public sealed record V180OptionCMergeSubmit(
    int SourceLocationId,
    int SurvivorLocationId,
    string SourceExpectedRowVersion,
    string SurvivorExpectedRowVersion,
    string Reason,
    V180B3LocationFields ProposedSurvivor);

public sealed class V180OptionCMergeService(
    AppDbContext db, ICurrentUserService current, IConfiguration config)
{
    private async Task<SqlConnection> RequireTargetAsync(CancellationToken ct)
    {
        if (!config.GetValue<bool>("OptionC:Candidate:Enabled"))
            throw new InvalidOperationException("OPTIONC_CANDIDATE_DISABLED");
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.LocationMergeDetails',N'U') IS NULL THROW 51463,N'OPTIONC_MERGE_SCHEMA_NOT_VERIFIED',1;",
            connection);
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }

    public async Task<V180B3RequestView> SubmitAsync(
        V180OptionCMergeSubmit input, CancellationToken ct)
    {
        var sourceVersion = Decode(input.SourceExpectedRowVersion);
        var survivorVersion = Decode(input.SurvivorExpectedRowVersion);
        if (input.SourceLocationId <= 0 || input.SurvivorLocationId <= 0
            || input.SourceLocationId == input.SurvivorLocationId
            || string.IsNullOrWhiteSpace(input.Reason)
            || input.Reason != input.Reason.Trim() || input.Reason.Length > 1000)
            throw new InvalidOperationException("OPTIONC_MERGE_INPUT_INVALID");

        var connection = await RequireTargetAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actorId = current.GetRequired().UserId;
            var scope = await new V180OptionCLiveAuthorizationAdapter(db)
                .AuthorizeSubmissionAsync(actorId, input.SurvivorLocationId,
                    BusinessTime.Today, ct);

            var first = Math.Min(input.SourceLocationId, input.SurvivorLocationId);
            var second = Math.Max(input.SourceLocationId, input.SurvivorLocationId);
            var locations = new Dictionary<int, Location>();
            foreach (var id in new[] { first, second })
            {
                var location = await db.Locations.FromSqlInterpolated($"""
                    SELECT * FROM dbo.Locations WITH (UPDLOCK,HOLDLOCK)
                    WHERE LocationId={id}
                    """).SingleOrDefaultAsync(ct)
                    ?? throw new KeyNotFoundException("OPTIONC_MERGE_LOCATION_NOT_FOUND");
                locations[id] = location;
            }
            var source = locations[input.SourceLocationId];
            var survivor = locations[input.SurvivorLocationId];
            if (source.OrganizationId != survivor.OrganizationId
                || source.OrganizationId != scope.OrganizationId
                || source.TeamId != survivor.TeamId
                || source.TeamId != scope.TeamId
                || !source.IsActive || !survivor.IsActive
                || source.ApprovalStatus != "Approved"
                || survivor.ApprovalStatus != "Approved"
                || source.LocationType != "Customer"
                || survivor.LocationType != "Customer")
                throw new UnauthorizedAccessException("OPTIONC_MERGE_SCOPE_DENIED");
            if (!source.RowVersion.SequenceEqual(sourceVersion)
                || !survivor.RowVersion.SequenceEqual(survivorVersion))
                throw new InvalidOperationException("ROWVERSION_CONFLICT");

            var before = new V180B3LocationFields(survivor.LocationName,
                survivor.City, survivor.District, survivor.Address, survivor.PlusCode,
                survivor.TaxId, survivor.MasterNote);
            var proposal = V180B3ProposalSafetyRules.Validate(
                input.ProposedSurvivor, before, input.Reason);

            await using (var pending = new SqlCommand("""
                SELECT CASE WHEN EXISTS(
                    SELECT 1 FROM dbo.ChangeRequests r
                    LEFT JOIN dbo.LocationMergeDetails m ON m.ChangeRequestId=r.ChangeRequestId
                    WHERE r.Status=N'Pending' AND r.OrganizationId=@org
                      AND (r.EntityId IN(CONVERT(nvarchar(80),@source),CONVERT(nvarchar(80),@survivor))
                           OR m.SourceLocationId IN(@source,@survivor)
                           OR m.SurvivorLocationId IN(@source,@survivor)))
                    THEN 1 ELSE 0 END;
                """, connection, (SqlTransaction)tx.GetDbTransaction()))
            {
                pending.Parameters.AddWithValue("@org", scope.OrganizationId);
                pending.Parameters.AddWithValue("@source", input.SourceLocationId);
                pending.Parameters.AddWithValue("@survivor", input.SurvivorLocationId);
                if (Convert.ToInt32(await pending.ExecuteScalarAsync(ct)) != 0)
                    throw new InvalidOperationException("OPTIONC_MERGE_PENDING_EXISTS");
            }

            var row = new V180B3ChangeRequest
            {
                RequestPublicId = Guid.NewGuid(),
                OrganizationId = scope.OrganizationId, TeamId = scope.TeamId,
                EntityKind = "Location", EntityId = input.SurvivorLocationId.ToString(),
                OperationCode = "UpdatePublishedLocation", RiskCode = "High",
                ExpectedEntityRowVersion = survivor.RowVersion.ToArray(),
                BeforeJson = JsonSerializer.Serialize(before),
                ProposedJson = JsonSerializer.Serialize(proposal.Proposed),
                EvidenceJson = JsonSerializer.Serialize(new
                {
                    Mode = "MergeFields", SourceLocationId = input.SourceLocationId,
                    SurvivorLocationId = input.SurvivorLocationId,
                    SourceRowVersion = Convert.ToBase64String(source.RowVersion),
                    SurvivorRowVersion = Convert.ToBase64String(survivor.RowVersion)
                }),
                RequestedByUserId = actorId, SubmittedAt = DateTime.UtcNow,
                Status = "Pending"
            };
            db.ChangeRequests.Add(row);
            await db.SaveChangesAsync(ct);
            db.ChangeRequestEvents.Add(new V180B3ChangeEvent
            {
                ChangeRequestId = row.ChangeRequestId, EventType = "Submitted",
                ActorUserId = actorId, OccurredAt = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid(),
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Mode = "MergeFields", SourceLocationId = input.SourceLocationId,
                    SurvivorLocationId = input.SurvivorLocationId, Reason = proposal.Reason
                })
            });
            await db.SaveChangesAsync(ct);

            await using (var detail = new SqlCommand("""
                INSERT dbo.LocationMergeDetails(
                    ChangeRequestId,SourceLocationId,SurvivorLocationId,
                    SourceOrganizationId,SurvivorOrganizationId,SourceTeamId,
                    SurvivorTeamId,SourceLocationRowVersion,SurvivorLocationRowVersion,
                    BeforeJson,ProposedJson,EvidenceJson)
                VALUES(@request,@source,@survivor,@sourceOrg,@survivorOrg,@sourceTeam,
                    @survivorTeam,@sourceVersion,@survivorVersion,@before,@proposed,@evidence);
                """, connection, (SqlTransaction)tx.GetDbTransaction()))
            {
                detail.Parameters.AddWithValue("@request", row.ChangeRequestId);
                detail.Parameters.AddWithValue("@source", input.SourceLocationId);
                detail.Parameters.AddWithValue("@survivor", input.SurvivorLocationId);
                detail.Parameters.AddWithValue("@sourceOrg", source.OrganizationId!.Value);
                detail.Parameters.AddWithValue("@survivorOrg", survivor.OrganizationId!.Value);
                detail.Parameters.AddWithValue("@sourceTeam", source.TeamId!.Value);
                detail.Parameters.AddWithValue("@survivorTeam", survivor.TeamId!.Value);
                detail.Parameters.AddWithValue("@sourceVersion", source.RowVersion);
                detail.Parameters.AddWithValue("@survivorVersion", survivor.RowVersion);
                detail.Parameters.AddWithValue("@before", JsonSerializer.Serialize(new { Source = source, Survivor = before }));
                detail.Parameters.AddWithValue("@proposed", row.ProposedJson);
                detail.Parameters.AddWithValue("@evidence", row.EvidenceJson!);
                await detail.ExecuteNonQueryAsync(ct);
            }

            await V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(
                connection, (SqlTransaction)tx.GetDbTransaction(),
                row.RequestPublicId, scope.OrganizationId, ct);
            await tx.CommitAsync(ct);
            return new V180B3RequestView(row.RequestPublicId, row.EntityId, row.TeamId,
                row.RiskCode, row.Status, row.RequestedByUserId, row.SubmittedAt,
                row.BeforeJson, row.ProposedJson, Convert.ToBase64String(row.RowVersion));
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static byte[] Decode(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch { throw new InvalidOperationException("OPTIONC_MERGE_ROWVERSION_INVALID"); }
    }
}
