using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// UNREGISTERED staging writer candidate. Creates a private, unapproved
/// publication stage, never updates dbo.Locations, and never publishes.
/// Caller must have revalidated live submission rights; this writer alone
/// grants no role. The table exists only in disposable SQL rehearsal.
/// </summary>
internal static class V180OptionCStageDraftWriter
{
    internal static async Task<long> CreateFromLockedRequestAsync(
        SqlConnection db, SqlTransaction tx, Guid requestPublicId,
        int organizationId, CancellationToken ct)
    {
        if (db.State != ConnectionState.Open || tx.Connection != db
            || tx.IsolationLevel != IsolationLevel.Serializable
            || requestPublicId == Guid.Empty || organizationId <= 0)
            throw new InvalidOperationException("OPTIONC_STAGE_SERIALIZABLE_REQUIRED");

        // Request FIRST; location SECOND; stage INSERT THIRD. No HTTP inputs
        // for identity, source version or proposed JSON. Each is read in SQL.
        long requestId;
        int locationId;
        int teamId;
        int requester;
        byte[] expected;
        string proposed;
        await using (var req = new SqlCommand("""
            SELECT ChangeRequestId,EntityId,TeamId,RequestedByUserId,
                ExpectedEntityRowVersion,ProposedJson
            FROM dbo.ChangeRequests WITH(UPDLOCK,HOLDLOCK)
            WHERE RequestPublicId=@publicId AND OrganizationId=@org
              AND EntityKind=N'Location'
              AND OperationCode=N'UpdatePublishedLocation'
              AND RiskCode=N'High' AND Status=N'Pending'
            """, db, tx))
        {
            req.Parameters.AddWithValue("@publicId", requestPublicId);
            req.Parameters.AddWithValue("@org", organizationId);
            await using var reader = await req.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)
                || reader.IsDBNull(1) || reader.IsDBNull(2)
                || !int.TryParse(reader.GetString(1), out locationId)
                || locationId <= 0)
                throw new InvalidOperationException("OPTIONC_STAGE_REQUEST_INVALID");
            requestId = reader.GetInt64(0);
            teamId = reader.GetInt32(2);
            requester = reader.GetInt32(3);
            expected = (byte[])reader.GetValue(4);
            proposed = reader.GetString(5);
        }
        if (teamId <= 0 || requester <= 0 || expected.Length != 8
            || proposed.Length is 0 or > 10000)
            throw new InvalidOperationException("OPTIONC_STAGE_REQUEST_INVALID");

        await using (var loc = new SqlCommand("""
            SELECT RowVersion FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK)
            WHERE LocationId=@id AND OrganizationId=@org AND TeamId=@team
              AND CreatedByUserId=@requester AND LocationType=N'Customer'
              AND ApprovalStatus=N'Approved' AND IsActive=1
            """, db, tx))
        {
            loc.Parameters.AddWithValue("@id", locationId);
            loc.Parameters.AddWithValue("@org", organizationId);
            loc.Parameters.AddWithValue("@team", teamId);
            loc.Parameters.AddWithValue("@requester", requester);
            var actual = await loc.ExecuteScalarAsync(ct) as byte[];
            if (actual is not { Length: 8 } || !actual.SequenceEqual(expected))
                throw new InvalidOperationException("OPTIONC_STAGE_SOURCE_DRIFT");
        }

        // No published-location DML. DB FK, hash CHECK and filtered unique
        // index remain authoritative for replay/races.
        await using var insert = new SqlCommand("""
            INSERT dbo.LocationPublicationStages(
                ChangeRequestId,LocationId,OrganizationId,TeamId,
                ExpectedLocationRowVersion,ProposedJson,ProposedSha256,
                StageStatus,GeocodeStatus,DuplicateReviewStatus,CreatedAt,UpdatedAt)
            VALUES(@request,@location,@org,@team,@expected,@proposed,
                HASHBYTES('SHA2_256',CONVERT(varbinary(max),@proposed)),
                N'PendingGeocode',N'Pending',N'Pending',SYSUTCDATETIME(),SYSUTCDATETIME());
            """, db, tx);
        insert.Parameters.AddWithValue("@request", requestId);
        insert.Parameters.AddWithValue("@location", locationId);
        insert.Parameters.AddWithValue("@org", organizationId);
        insert.Parameters.AddWithValue("@team", teamId);
        insert.Parameters.AddWithValue("@expected", expected);
        insert.Parameters.AddWithValue("@proposed", proposed);
        if (await insert.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("OPTIONC_STAGE_INSERT_CONFLICT");
        return requestId;
    }
}
