using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// ISOLATED, read-only SQL reconciliation proof on B3Probe* tables after a
/// simulated missing COMMIT acknowledgment. No write, retry, approval, API,
/// application dependency or production/UAT connection is permitted.
/// The outcome is an OBSERVATION, never permission to send an HTTP success.
/// </summary>
internal static class B3UnknownCommitReconciliationProbe
{
    internal enum Observation
    {
        CommittedEvidenceComplete,
        NoCommitEvidenceComplete,
        IndeterminateRequiresInvestigation
    }

    private sealed record EvidenceEvent(string Kind,int? Actor,
        Guid Correlation,Guid? Decision);

    internal static async Task<Observation> ObserveAsync(
        DisposableSqlServer fixture,B3ProbeCase original,Guid decisionKey,
        int expectedReviewerId=20)
    {
        if(!fixture.DatabaseName.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
            || original.SourceId<=0 || original.RequestId<=0
            || original.SourceRowVersion is not {Length:8}
            || original.RequestRowVersion is not {Length:8}
            || decisionKey==Guid.Empty || expectedReviewerId<=0)
            return Observation.IndeterminateRequiresInvestigation;

        // A genuinely NEW SQL connection, not the caller's unknown transaction.
        // Failure to connect/inspect must be surfaced to the operator; the
        // caller may NEVER translate an exception into "not committed".
        await using var conn=await fixture.ConnectAsync();
        await using var tx=(SqlTransaction)await conn.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            if(!string.Equals(conn.Database,fixture.DatabaseName,
                    StringComparison.Ordinal))
                return Observation.IndeterminateRequiresInvestigation;

            const string rowSql="""
                SELECT r.OrganizationId,r.TeamId,r.RequestedByUserId,
                    r.Status,r.ReviewedByUserId,r.ReviewedAt,r.AppliedAt,
                    r.ReviewReason,r.RowVersion,
                    s.OrganizationId,s.TeamId,s.CreatedByUserId,
                    s.LocationType,s.ApprovalStatus,s.IsActive,s.RowVersion,
                    s.LocationName,s.City,s.District,s.Address,
                    s.PlusCode,s.TaxId,s.MasterNote
                FROM dbo.B3ProbeRequests r
                JOIN dbo.B3ProbeSources s ON s.SourceId=r.SourceId
                WHERE r.RequestId=@request AND r.SourceId=@source
                """;
            await using var rowCmd=new SqlCommand(rowSql,conn,tx);
            rowCmd.Parameters.AddWithValue("@request",original.RequestId);
            rowCmd.Parameters.AddWithValue("@source",original.SourceId);
            string? requestStatus=null,requestReason=null;
            int? reviewer=null;
            DateTime? reviewedAt=null,appliedAt=null;
            byte[]? requestVersion=null,locationVersion=null;
            bool correctScope=false;
            string?[]? fields=null;
            await using(var reader=await rowCmd.ExecuteReaderAsync())
            {
                if(!await reader.ReadAsync())
                    return Observation.IndeterminateRequiresInvestigation;
                int? OptionalInt(int i)=>reader.IsDBNull(i)?null:reader.GetInt32(i);
                DateTime? OptionalDate(int i)=>reader.IsDBNull(i)?null:reader.GetDateTime(i);
                string? OptionalString(int i)=>reader.IsDBNull(i)?null:reader.GetString(i);
                requestStatus=reader.GetString(3);
                reviewer=OptionalInt(4);
                reviewedAt=OptionalDate(5);
                appliedAt=OptionalDate(6);
                requestReason=OptionalString(7);
                requestVersion=reader.GetFieldValue<byte[]>(8);
                locationVersion=reader.GetFieldValue<byte[]>(15);
                correctScope=reader.GetInt32(0)==1 && reader.GetInt32(1)==7
                    && reader.GetInt32(2)==10
                    && reader.GetInt32(9)==1 && reader.GetInt32(10)==7
                    && reader.GetInt32(11)==10
                    && reader.GetString(12)=="Customer"
                    && reader.GetString(13)=="Approved"
                    && reader.GetBoolean(14);
                fields=new string?[7];
                for(int i=0;i<7;i++)fields[i]=OptionalString(i+16);
                if(await reader.ReadAsync())
                    return Observation.IndeterminateRequiresInvestigation;
            }
            if(!correctScope || fields is null)
                return Observation.IndeterminateRequiresInvestigation;

            var events=new List<EvidenceEvent>();
            await using(var cmd=new SqlCommand("""
                SELECT EventType,ActorUserId,CorrelationId,DecisionKey
                FROM dbo.B3ProbeEvents WHERE RequestId=@request
                ORDER BY EventId
                """,conn,tx))
            {
                cmd.Parameters.AddWithValue("@request",original.RequestId);
                await using var reader=await cmd.ExecuteReaderAsync();
                while(await reader.ReadAsync())
                    events.Add(new EvidenceEvent(reader.GetString(0),
                        reader.IsDBNull(1)?null:reader.GetInt32(1),
                        reader.GetGuid(2),
                        reader.IsDBNull(3)?null:reader.GetGuid(3)));
            }
            int keyUse;
            await using(var cmd=new SqlCommand("""
                SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE DecisionKey=@key
                """,conn,tx))
            {
                cmd.Parameters.AddWithValue("@key",decisionKey);
                keyUse=Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            static bool Matches(string?[] actual,
                string? name,string? city,string? district,string? address,
                string? plus,string? tax,string? note)=>
                actual.SequenceEqual(new[]{name,city,district,address,plus,tax,note});

            var originalFields=B3ProbeFields.Original;
            var proposedFields=B3ProbeFields.Proposed;
            var sameOriginal=Matches(fields,originalFields.LocationName,
                originalFields.City,originalFields.District,originalFields.Address,
                originalFields.PlusCode,originalFields.TaxId,originalFields.MasterNote);
            var sameProposed=Matches(fields,proposedFields.LocationName,
                proposedFields.City,proposedFields.District,proposedFields.Address,
                proposedFields.PlusCode,proposedFields.TaxId,proposedFields.MasterNote);

            if(requestStatus=="Applied" && reviewer==expectedReviewerId
                && reviewedAt.HasValue && appliedAt>=reviewedAt
                && !string.IsNullOrWhiteSpace(requestReason)
                && requestReason==requestReason.Trim()
                && requestVersion is {Length:8}
                && !requestVersion.SequenceEqual(original.RequestRowVersion)
                && locationVersion is {Length:8}
                && !locationVersion.SequenceEqual(original.SourceRowVersion)
                && sameProposed && keyUse==1 && events.Count==2
                && events[0].Kind=="Approved"
                && events[0].Actor==expectedReviewerId
                && events[0].Decision==decisionKey
                && events[0].Correlation!=Guid.Empty
                && events[1].Kind=="Applied"
                && events[1].Actor==expectedReviewerId
                && events[1].Decision is null
                && events[1].Correlation==events[0].Correlation)
                return Observation.CommittedEvidenceComplete;

            if(requestStatus=="Pending" && reviewer is null
                && reviewedAt is null && appliedAt is null
                && requestReason is null
                && requestVersion is {Length:8}
                && requestVersion.SequenceEqual(original.RequestRowVersion)
                && locationVersion is {Length:8}
                && locationVersion.SequenceEqual(original.SourceRowVersion)
                && sameOriginal && keyUse==0 && events.Count==0)
                return Observation.NoCommitEvidenceComplete;

            // Partial event pair, changed source, new rowversion, reused key,
            // wrong tenant/actor or unavailable record => NEVER auto-retry.
            return Observation.IndeterminateRequiresInvestigation;
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
