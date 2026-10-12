using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Disposable B3Probe* SQL-only simulation of a server-side COMMIT followed
/// by an error before acknowledgement. Not real TCP loss or an app executor.
/// </summary>
internal static class B3CommitAcknowledgmentLossProbe
{
    internal const int AckSimulationError=51091;

    internal static async Task SimulateAsync(
        DisposableSqlServer fixture,B3ProbeCase target,Guid key,bool commit)
    {
        if(!fixture.DatabaseName.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
            || key==Guid.Empty || target.SourceRowVersion is not {Length:8}
            || target.RequestRowVersion is not {Length:8})
            throw new InvalidOperationException("B3_PROBE_NOT_DISPOSABLE");
        var proposed=B3ProbeFields.Proposed;
        await using var conn=await fixture.ConnectAsync();
        if(conn.Database!=fixture.DatabaseName)
            throw new InvalidOperationException("B3_PROBE_NOT_DISPOSABLE");
        await using var command=new SqlCommand("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.B3ProbeSources SET
                LocationName=@name,City=@city,District=@district,
                Address=@address,PlusCode=@plus,TaxId=@tax,MasterNote=@note
            WHERE SourceId=@source AND OrganizationId=1 AND TeamId=7
                AND CreatedByUserId=10 AND LocationType=N'Customer'
                AND ApprovalStatus=N'Approved' AND IsActive=1
                AND RowVersion=@sourceVersion;
            IF @@ROWCOUNT<>1 THROW 51092,N'SOURCE_CONFLICT',1;
            UPDATE dbo.B3ProbeRequests SET
                Status=N'Applied',ReviewedByUserId=20,
                ReviewedAt=SYSUTCDATETIME(),AppliedAt=SYSUTCDATETIME(),
                ReviewReason=N'Independent fixture review'
            WHERE RequestId=@request AND SourceId=@source
                AND OrganizationId=1 AND TeamId=7
                AND RequestedByUserId=10 AND Status=N'Pending'
                AND RowVersion=@requestVersion;
            IF @@ROWCOUNT<>1 THROW 51093,N'REQUEST_CONFLICT',1;
            DECLARE @correlation uniqueidentifier=NEWID();
            INSERT INTO dbo.B3ProbeEvents(
                RequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES (@request,N'Approved',20,SYSUTCDATETIME(),@correlation,@key);
            IF @@ROWCOUNT<>1 THROW 51094,N'APPROVED_INSERT_CONFLICT',1;
            INSERT INTO dbo.B3ProbeEvents(
                RequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES (@request,N'Applied',20,SYSUTCDATETIME(),@correlation,NULL);
            IF @@ROWCOUNT<>1 THROW 51095,N'APPLIED_INSERT_CONFLICT',1;
            IF @shouldCommit=1 COMMIT TRANSACTION;
            ELSE ROLLBACK TRANSACTION;
            THROW 51091,N'ACK_NOT_RECEIVED_SIMULATION',1;
            """,conn){CommandTimeout=25};
        foreach(var (name,value) in new (string,object)[]
        {
            ("@name",proposed.LocationName),("@city",proposed.City!),
            ("@district",proposed.District!),("@address",proposed.Address!),
            ("@plus",proposed.PlusCode!),("@tax",proposed.TaxId!),
            ("@note",proposed.MasterNote!),("@source",target.SourceId),
            ("@request",target.RequestId),("@sourceVersion",target.SourceRowVersion),
            ("@requestVersion",target.RequestRowVersion),("@key",key),
            ("@shouldCommit",commit)
        })
            command.Parameters.AddWithValue(name,value);
        await command.ExecuteNonQueryAsync();
    }
}
