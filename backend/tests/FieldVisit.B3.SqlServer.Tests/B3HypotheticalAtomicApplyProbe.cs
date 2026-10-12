using System.Data;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

// TEST-ONLY: disposable localhost B3SqlFixture database and separate B3Probe*
// objects, NEVER a candidate 1800_012 migration, application executor, or UAT access.
internal sealed record B3ProbeCase(
    long SourceId, long RequestId, byte[] SourceRowVersion, byte[] RequestRowVersion);

// Seven supported Location fields mirrored by a disposable SQL probe only.
internal static class B3ProbeFields
{
    internal static V180B3LocationFields Original => new(
        "Original fixture location", "Taipei", "Xinyi", "Original address",
        "PROBE_OLD", "TAX_OLD", "Original fixture note");
    internal static V180B3LocationFields Proposed => new(
        "Applied in disposable probe", "New Taipei", "Wugu", "Edited address",
        "PROBE_NEW", "TAX_NEW", "Edited fixture note");
}


internal sealed class B3HypotheticalAtomicApplyProbe(DisposableSqlServer fixture)
{
    public async Task CreateTestOnlySchemaAsync()
    {
        await fixture.ExecuteAsync("""
            IF OBJECT_ID(N'dbo.B3ProbeSources', N'U') IS NULL
            CREATE TABLE dbo.B3ProbeSources(
                SourceId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                OrganizationId int NOT NULL,
                TeamId int NOT NULL,
                CreatedByUserId int NOT NULL,
                LocationName nvarchar(200) NOT NULL,
                City nvarchar(100) NULL,
                District nvarchar(100) NULL,
                Address nvarchar(500) NULL,
                PlusCode nvarchar(100) NULL,
                TaxId nvarchar(30) NULL,
                MasterNote nvarchar(1000) NULL,
                LocationType nvarchar(24) NOT NULL,
                ApprovalStatus nvarchar(24) NOT NULL,
                IsActive bit NOT NULL,
                RowVersion rowversion NOT NULL)
            """);
        await fixture.ExecuteAsync("""
            IF OBJECT_ID(N'dbo.B3ProbeRequests', N'U') IS NULL
            CREATE TABLE dbo.B3ProbeRequests(
                RequestId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                RequestPublicId uniqueidentifier NOT NULL UNIQUE,
                SourceId bigint NOT NULL
                    REFERENCES dbo.B3ProbeSources(SourceId),
                OrganizationId int NOT NULL,
                TeamId int NOT NULL,
                RequestedByUserId int NOT NULL,
                Status nvarchar(16) NOT NULL,
                RequestMemo nvarchar(100) NOT NULL,
                ReviewedByUserId int NULL,
                ReviewedAt datetime2(3) NULL,
                AppliedAt datetime2(3) NULL,
                ReviewReason nvarchar(1000) NULL,
                RowVersion rowversion NOT NULL,
                CONSTRAINT CK_B3ProbeRequests_ReviewShape CHECK(
                  (Status=N'Pending' AND ReviewedByUserId IS NULL
                    AND ReviewedAt IS NULL AND AppliedAt IS NULL
                    AND ReviewReason IS NULL)
                  OR
                  (Status=N'Applied' AND ReviewedByUserId IS NOT NULL
                    AND ReviewedByUserId<>RequestedByUserId
                    AND ReviewedAt IS NOT NULL AND AppliedAt IS NOT NULL
                    AND ReviewReason IS NOT NULL
                    AND LEN(LTRIM(RTRIM(ReviewReason)))>0)))
            """);
        await fixture.ExecuteAsync("""
            IF OBJECT_ID(N'dbo.B3ProbeEvents', N'U') IS NULL
            CREATE TABLE dbo.B3ProbeEvents(
                EventId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                RequestId bigint NOT NULL
                    REFERENCES dbo.B3ProbeRequests(RequestId),
                EventType nvarchar(16) NOT NULL,
                ActorUserId int NOT NULL,
                OccurredAt datetime2(3) NOT NULL,
                CorrelationId uniqueidentifier NOT NULL,
                DecisionKey uniqueidentifier NULL,
                CONSTRAINT CK_B3ProbeEvents_Type CHECK(
                    (EventType=N'Approved' AND DecisionKey IS NOT NULL)
                    OR (EventType=N'Applied' AND DecisionKey IS NULL)))
            """);
        await fixture.ExecuteAsync("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id=OBJECT_ID(N'dbo.B3ProbeEvents',N'U')
                  AND name=N'UX_B3ProbeEvents_DecisionKey')
            CREATE UNIQUE INDEX UX_B3ProbeEvents_DecisionKey
                ON dbo.B3ProbeEvents(DecisionKey)
                WHERE DecisionKey IS NOT NULL
            """);
    }

    public async Task<B3ProbeCase> SeedPendingAsync(
        int sourceOrganizationId=1,int sourceTeamId=7,
        int sourceOwnerUserId=10,string sourceLocationType="Customer",
        string sourceApprovalStatus="Approved",bool sourceIsActive=true)
    {
        var original=B3ProbeFields.Original;
        var sourceId=Convert.ToInt64(await fixture.ScalarAsync("""
            INSERT INTO dbo.B3ProbeSources(
                OrganizationId,TeamId,CreatedByUserId,LocationName,
                City,District,Address,PlusCode,TaxId,MasterNote,
                LocationType,ApprovalStatus,IsActive)
            VALUES(@org,@team,@owner,@name,@city,@district,@address,
                @plus,@tax,@note,@type,@status,@active);
            SELECT CAST(SCOPE_IDENTITY() AS bigint)
            """,("@org",sourceOrganizationId),("@team",sourceTeamId),
                ("@owner",sourceOwnerUserId),("@name",original.LocationName),
                ("@city",original.City),("@district",original.District),
                ("@address",original.Address),("@plus",original.PlusCode),
                ("@tax",original.TaxId),("@note",original.MasterNote),
                ("@type",sourceLocationType),("@status",sourceApprovalStatus),
                ("@active",sourceIsActive)));
        var requestId=Convert.ToInt64(await fixture.ScalarAsync("""
            INSERT INTO dbo.B3ProbeRequests(
                RequestPublicId,SourceId,OrganizationId,TeamId,
                RequestedByUserId,Status,RequestMemo)
            VALUES(@public,@source,1,7,10,N'Pending',N'Initial');
            SELECT CAST(SCOPE_IDENTITY() AS bigint)
            """,("@public",Guid.NewGuid()),("@source",sourceId)));
        return new B3ProbeCase(sourceId,requestId,
            (byte[])(await fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeSources WHERE SourceId=@id",
                ("@id",sourceId)))!,
            (byte[])(await fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",requestId)))!);
    }


    public async Task<V180B3LocationFields> ReadFieldsAsync(long sourceId)
    {
        async Task<string?> Field(string column)
        {
            // Column is selected only from the private, constant allowlist.
            if(column is not ("LocationName" or "City" or "District" or
                "Address" or "PlusCode" or "TaxId" or "MasterNote"))
                throw new InvalidOperationException("B3_PROBE_COLUMN_NOT_ALLOWED");
            var value=await fixture.ScalarAsync(
                "SELECT ["+column+"] FROM dbo.B3ProbeSources WHERE SourceId=@id",
                ("@id",sourceId));
            return value is DBNull ? null : (string?)value;
        }
        return new V180B3LocationFields(
            (await Field("LocationName"))!,await Field("City"),
            await Field("District"),await Field("Address"),
            await Field("PlusCode"),await Field("TaxId"),
            await Field("MasterNote"));
    }

    private static async Task<object?> Sql(
        SqlConnection conn, SqlTransaction tx, string statement,
        params (string name,object value)[] args)
    {
        await using var cmd=new SqlCommand(statement,conn,tx) { CommandTimeout=20 };
        foreach(var (name,value) in args)
            cmd.Parameters.AddWithValue(name,value);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<int> Write(
        SqlConnection conn, SqlTransaction tx, string statement,
        params (string name,object value)[] args)
    {
        await using var cmd=new SqlCommand(statement,conn,tx) { CommandTimeout=20 };
        foreach(var (name,value) in args)
            cmd.Parameters.AddWithValue(name,value);
        return await cmd.ExecuteNonQueryAsync();
    }

    // Deliberately located in TEST PROJECT, not referenced by product code.
    // This transaction proves SQL atomicity only; no live HR/Admin evaluation,
    // production Locations columns, real 012 constraints, or UAT is exercised.
    public async Task ApplyProbeAsync(B3ProbeCase target,Guid decisionKey,
        string fault="",int reviewerUserId=20,
        V180B3LocationFields? proposedFields=null)
    {
        if(reviewerUserId<=0||reviewerUserId==10||decisionKey==Guid.Empty)
            throw new InvalidOperationException("B3_PROBE_INDEPENDENT_REVIEW_REQUIRED");
        await using var conn=await fixture.ConnectAsync();
        using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Deterministic lock ordering: request BEFORE source.
            var requestVersion=await Sql(conn,tx,"""
                SELECT RowVersion FROM dbo.B3ProbeRequests
                    WITH (UPDLOCK,HOLDLOCK)
                WHERE RequestId=@id AND SourceId=@source
                  AND OrganizationId=1 AND TeamId=7
                  AND RequestedByUserId=10 AND Status=N'Pending'
                """,("@id",target.RequestId),("@source",target.SourceId)) as byte[];
            if(requestVersion is not {Length:8}
                ||!requestVersion.SequenceEqual(target.RequestRowVersion))
                throw new InvalidOperationException("B3_PROBE_STALE_REQUEST");

            var sourceVersion=await Sql(conn,tx,"""
                SELECT RowVersion FROM dbo.B3ProbeSources
                    WITH (UPDLOCK,HOLDLOCK)
                WHERE SourceId=@id AND OrganizationId=1
                  AND TeamId=7 AND CreatedByUserId=10
                  AND LocationType=N'Customer'
                  AND ApprovalStatus=N'Approved' AND IsActive=1
                """,("@id",target.SourceId)) as byte[];
            if(sourceVersion is not {Length:8}
                ||!sourceVersion.SequenceEqual(target.SourceRowVersion))
                throw new InvalidOperationException("B3_PROBE_STALE_SOURCE");

            if(fault=="before-source")
                throw new InvalidOperationException("B3_PROBE_INJECTED_FAILURE");
            var proposed=proposedFields??B3ProbeFields.Proposed;
            // Seven parameters: never interpolate proposed Location data into SQL.
            var sourceRows=await Write(conn,tx,"""
                UPDATE dbo.B3ProbeSources SET
                    LocationName=@name,City=@city,District=@district,
                    Address=@address,PlusCode=@plus,TaxId=@tax,
                    MasterNote=@note
                WHERE SourceId=@id AND OrganizationId=1 AND TeamId=7
                  AND CreatedByUserId=10 AND LocationType=N'Customer'
                  AND ApprovalStatus=N'Approved' AND IsActive=1
                  AND RowVersion=@version
                """,("@name",proposed.LocationName),
                    ("@city",proposed.City??(object)DBNull.Value),
                    ("@district",proposed.District??(object)DBNull.Value),
                    ("@address",proposed.Address??(object)DBNull.Value),
                    ("@plus",proposed.PlusCode??(object)DBNull.Value),
                    ("@tax",proposed.TaxId??(object)DBNull.Value),
                    ("@note",proposed.MasterNote??(object)DBNull.Value),
                    ("@id",target.SourceId),("@version",sourceVersion));
            if(sourceRows!=1)
                throw new InvalidOperationException("B3_PROBE_SOURCE_CONFLICT");
            if(fault=="after-source")
                throw new InvalidOperationException("B3_PROBE_INJECTED_FAILURE");

            var now=DateTime.UtcNow;
            var requestRows=await Write(conn,tx,"""
                UPDATE dbo.B3ProbeRequests
                SET Status=N'Applied',ReviewedByUserId=@reviewer,
                    ReviewedAt=@now,AppliedAt=@now,ReviewReason=N'Fixture review'
                WHERE RequestId=@id AND SourceId=@source AND Status=N'Pending'
                  AND OrganizationId=1 AND TeamId=7 AND RequestedByUserId=10
                  AND RowVersion=@version
                """,("@reviewer",reviewerUserId),("@now",now),
                    ("@id",target.RequestId),("@source",target.SourceId),
                    ("@version",requestVersion));
            if(requestRows!=1)
                throw new InvalidOperationException("B3_PROBE_REQUEST_CONFLICT");
            if(fault=="after-request")
                throw new InvalidOperationException("B3_PROBE_INJECTED_FAILURE");

            var correlation=Guid.NewGuid();
            if(await Write(conn,tx,"""
                INSERT INTO dbo.B3ProbeEvents(
                    RequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                VALUES(@id,N'Approved',@reviewer,@now,@correlation,@decision)
                """,("@id",target.RequestId),("@reviewer",reviewerUserId),
                    ("@now",now),("@correlation",correlation),
                    ("@decision",decisionKey))!=1)
                throw new InvalidOperationException("B3_PROBE_APPROVED_EVENT_CONFLICT");
            if(fault=="after-approved")
                throw new InvalidOperationException("B3_PROBE_INJECTED_FAILURE");

            if(await Write(conn,tx,"""
                INSERT INTO dbo.B3ProbeEvents(
                    RequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                VALUES(@id,N'Applied',@reviewer,@now,@correlation,NULL)
                """,("@id",target.RequestId),("@reviewer",reviewerUserId),
                    ("@now",now),("@correlation",correlation))!=1)
                throw new InvalidOperationException("B3_PROBE_APPLIED_EVENT_CONFLICT");
            if(fault=="after-applied")
                throw new InvalidOperationException("B3_PROBE_INJECTED_FAILURE");
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
