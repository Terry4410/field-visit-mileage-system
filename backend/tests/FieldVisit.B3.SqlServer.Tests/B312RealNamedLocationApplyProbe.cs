using System.Data;
using System.Text.Json;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Test-project ONLY candidate: dbo.Locations (reduced disposable test shape)
/// + actual-named ChangeRequests and ChangeRequestEvents. Everything is inside
/// a rollback-only 011-to-012 test transaction. The caller supplies simulated
/// authorization booleans; NO authoritative HR/Manager lookup or deployed
/// executor exists here. NEVER wire this class into the application.
/// </summary>
internal static class B312RealNamedLocationApplyProbe
{
    internal sealed record Seed(
        int LocationId,long RequestId,Guid PublicId,
        byte[] LocationVersion,byte[] RequestVersion);

    internal static async Task CreateLocationStubAsync(SqlConnection conn,SqlTransaction tx)
    {
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
            CREATE TABLE dbo.Locations(
                LocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                OrganizationId int NULL,TeamId int NULL,CreatedByUserId int NULL,
                LocationType nvarchar(24) NOT NULL,
                ApprovalStatus nvarchar(24) NOT NULL,IsActive bit NOT NULL,
                LocationName nvarchar(200) NOT NULL,
                City nvarchar(100) NULL,District nvarchar(100) NULL,
                Address nvarchar(500) NULL,PlusCode nvarchar(100) NULL,
                TaxId nvarchar(30) NULL,MasterNote nvarchar(1000) NULL,
                RowVersion rowversion NOT NULL)
            """);
    }

    internal static async Task<Seed> SeedPendingAsync(
        SqlConnection conn,SqlTransaction tx,int org=1,int team=7,
        int owner=10,string locationType="Customer",string status="Approved",
        bool active=true)
    {
        var original=B3ProbeFields.Original;
        var proposed=B3ProbeFields.Proposed;
        int locationId=Convert.ToInt32(await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx, """
            INSERT INTO dbo.Locations(
                OrganizationId,TeamId,CreatedByUserId,
                LocationType,ApprovalStatus,IsActive,
                LocationName,City,District,Address,PlusCode,TaxId,MasterNote)
            VALUES(@org,@team,@owner,@kind,@status,@active,
                @name,@city,@district,@address,@plus,@tax,@note);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,("@org",org),("@team",team),("@owner",owner),
            ("@kind",locationType),("@status",status),("@active",active),
            ("@name",original.LocationName),("@city",original.City),
            ("@district",original.District),("@address",original.Address),
            ("@plus",original.PlusCode),("@tax",original.TaxId),
            ("@note",original.MasterNote)));
        var locVersion=(byte[])(await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
            "SELECT RowVersion FROM dbo.Locations WHERE LocationId=@id",("@id",locationId)))!;
        var publicId=Guid.NewGuid();
        long requestId=Convert.ToInt64(await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx, """
            INSERT INTO dbo.ChangeRequests(
                RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,
                OperationCode,RiskCode,ExpectedEntityRowVersion,
                BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
            VALUES(@publicId,@org,@team,N'Location',@entity,
                N'UpdatePublishedLocation',N'High',@sourceVersion,
                @before,@proposed,@owner,SYSUTCDATETIME(),N'Pending');
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """,("@publicId",publicId),("@org",org),("@team",team),
            ("@entity",locationId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("@sourceVersion",locVersion),("@before",JsonSerializer.Serialize(original)),
            ("@proposed",JsonSerializer.Serialize(proposed)),("@owner",owner)));
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@id,N'Submitted',@owner,SYSUTCDATETIME(),NEWID(),NULL)
            """,("@id",requestId),("@owner",owner));
        var requestVersion=(byte[])(await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",requestId)))!;
        return new Seed(locationId,requestId,publicId,locVersion,requestVersion);
    }

    private static async Task<int> WriteAsync(SqlConnection conn,SqlTransaction tx,
        string sql,params (string Name,object? Value)[] args)
    {
        await using var cmd=new SqlCommand(sql,conn,tx){CommandTimeout=25};
        foreach(var (name,value) in args)
            cmd.Parameters.AddWithValue(name,value??DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<V180B3ChangeRequest> LockedRequestAsync(
        SqlConnection conn,SqlTransaction tx,Guid publicId)
    {
        await using var cmd=new SqlCommand("""
            SELECT ChangeRequestId,RequestPublicId,OrganizationId,TeamId,
                EntityKind,EntityId,OperationCode,RiskCode,
                ExpectedEntityRowVersion,BeforeJson,ProposedJson,
                RequestedByUserId,SubmittedAt,Status,RowVersion
            FROM dbo.ChangeRequests WITH (UPDLOCK,HOLDLOCK)
            WHERE RequestPublicId=@id AND OrganizationId=1
            """,conn,tx);
        cmd.Parameters.AddWithValue("@id",publicId);
        await using var reader=await cmd.ExecuteReaderAsync();
        if(!await reader.ReadAsync())
            throw new InvalidOperationException("012_TEST_REQUEST_SCOPE_DENIED");
        return new V180B3ChangeRequest
        {
            ChangeRequestId=reader.GetInt64(0),
            RequestPublicId=reader.GetGuid(1),
            OrganizationId=reader.GetInt32(2),
            TeamId=reader.IsDBNull(3)?null:reader.GetInt32(3),
            EntityKind=reader.GetString(4),
            EntityId=reader.GetString(5),
            OperationCode=reader.GetString(6),
            RiskCode=reader.GetString(7),
            ExpectedEntityRowVersion=reader.IsDBNull(8)?null:reader.GetFieldValue<byte[]>(8),
            BeforeJson=reader.IsDBNull(9)?null:reader.GetString(9),
            ProposedJson=reader.GetString(10),
            RequestedByUserId=reader.GetInt32(11),
            SubmittedAt=reader.GetDateTime(12),
            Status=reader.GetString(13),
            RowVersion=reader.GetFieldValue<byte[]>(14)
        };
    }

    private sealed record LockedLocation(int Id,int? Org,int? Team,int? Owner,
        string Type,string Status,bool Active,byte[] Version,
        V180B3LocationFields Fields);

    private static async Task<LockedLocation> LockedLocationAsync(
        SqlConnection conn,SqlTransaction tx,int id)
    {
        await using var cmd=new SqlCommand("""
            SELECT LocationId,OrganizationId,TeamId,CreatedByUserId,
                LocationType,ApprovalStatus,IsActive,RowVersion,
                LocationName,City,District,Address,PlusCode,TaxId,MasterNote
            FROM dbo.Locations WITH (UPDLOCK,HOLDLOCK)
            WHERE LocationId=@id AND OrganizationId=1
            """,conn,tx);
        cmd.Parameters.AddWithValue("@id",id);
        await using var reader=await cmd.ExecuteReaderAsync();
        if(!await reader.ReadAsync())
            throw new InvalidOperationException("012_TEST_LOCATION_SCOPE_DENIED");
        string? Field(int pos)=>reader.IsDBNull(pos)?null:reader.GetString(pos);
        return new LockedLocation(reader.GetInt32(0),
            reader.IsDBNull(1)?null:reader.GetInt32(1),
            reader.IsDBNull(2)?null:reader.GetInt32(2),
            reader.IsDBNull(3)?null:reader.GetInt32(3),
            reader.GetString(4),reader.GetString(5),reader.GetBoolean(6),
            reader.GetFieldValue<byte[]>(7),
            new V180B3LocationFields(reader.GetString(8),Field(9),Field(10),
                Field(11),Field(12),Field(13),Field(14)));
    }

    internal static async Task ApplyInsideTestTransactionAsync(
        SqlConnection conn,SqlTransaction tx,Seed seed,Guid decisionKey,
        string fault="",bool simulatedLiveAdmin=true,
        bool simulatedLiveMembership=true)
    {
        if(decisionKey==Guid.Empty)
            throw new InvalidOperationException("012_TEST_INVALID_DECISION");
        // Serializable transaction acquired by rollback-only test harness:
        // lock request FIRST and source SECOND, never invert the order.
        var request=await LockedRequestAsync(conn,tx,seed.PublicId);
        var source=await LockedLocationAsync(conn,tx,seed.LocationId);
        var reason="Independent fixture review";
        var review=new V180B3Review(Convert.ToBase64String(seed.RequestVersion),
            decisionKey,reason);
        var plan=V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
            request,1,seed.PublicId,20,review,source.Id,source.Org,source.Team,
            source.Owner,source.Type,source.Status,source.Active,source.Version,
            source.Fields);
        var replay=Convert.ToInt32(await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE DecisionKey=@key",
            ("@key",decisionKey)))>0;
        V180B3ApprovalApplyPreflight.RequireLockedApprovalBoundary(
            plan,request,source.Fields,source.Version,source.Id,
            source.Org,source.Team,source.Owner,source.Type,source.Status,
            source.Active,20,1,simulatedLiveAdmin,simulatedLiveMembership,
            decisionKey,reason,replay);

        // Normal disposable-SQL rehearsal executes the product's internal,
        // unregistered writer.  Fault-injection branches below deliberately
        // retain their inline writes so each intermediate rollback point can
        // be asserted without adding a test hook to product code.
        if(string.IsNullOrEmpty(fault))
        {
            await V180B3AtomicApplyExecutor.ExecuteAsync(conn,tx,plan,request,
                new V180B3AtomicApplyLockedContext(source.Id,source.Org,
                    source.Team,source.Owner,source.Type,source.Status,
                    source.Active,source.Version,source.Fields,20,1,
                    simulatedLiveAdmin,simulatedLiveMembership,decisionKey,
                    reason,replay),CancellationToken.None);
            return;
        }

        var proposed=JsonSerializer.Deserialize<V180B3LocationFields>(plan.ProposedJson)!;
        var sourceRows=await WriteAsync(conn,tx, """
            UPDATE dbo.Locations SET
                LocationName=@name,City=@city,District=@district,
                Address=@address,PlusCode=@plus,TaxId=@tax,MasterNote=@note
            WHERE LocationId=@id AND OrganizationId=1 AND TeamId=7
                AND CreatedByUserId=10 AND LocationType=N'Customer'
                AND ApprovalStatus=N'Approved' AND IsActive=1
                AND RowVersion=@sourceVersion
            """,("@name",proposed.LocationName),("@city",proposed.City),
            ("@district",proposed.District),("@address",proposed.Address),
            ("@plus",proposed.PlusCode),("@tax",proposed.TaxId),
            ("@note",proposed.MasterNote),("@id",source.Id),
            ("@sourceVersion",source.Version));
        V180B3ApprovalApplyPreflight.RequireExactlyOneGuardedSourceUpdate(
            plan,sourceRows,source.Id,source.Version);
        if(fault=="after-location")throw new InvalidOperationException("012_TEST_FAULT");

        var now=DateTime.UtcNow.AddSeconds(3);
        var requestRows=await WriteAsync(conn,tx, """
            UPDATE dbo.ChangeRequests SET Status=N'Applied',
                ReviewedByUserId=20,ReviewedAt=@now,AppliedAt=@now,
                ReviewReason=@reason
            WHERE ChangeRequestId=@id AND RequestPublicId=@publicId
                AND OrganizationId=1 AND TeamId=7
                AND RequestedByUserId=10 AND Status=N'Pending'
                AND RowVersion=@requestVersion
            """,("@now",now),("@reason",reason),("@id",request.ChangeRequestId),
            ("@publicId",seed.PublicId),("@requestVersion",request.RowVersion));
        V180B3ApprovalApplyPreflight.RequireExactlyOneGuardedRequestTransition(
            plan,requestRows,request,request.RowVersion);
        if(fault=="after-request")throw new InvalidOperationException("012_TEST_FAULT");

        var correlation=Guid.NewGuid();
        var approved=new V180B3ChangeEvent{
            ChangeRequestId=request.ChangeRequestId,EventType="Approved",
            ActorUserId=20,OccurredAt=now,CorrelationId=correlation,
            DecisionKey=decisionKey};
        var applied=new V180B3ChangeEvent{
            ChangeRequestId=request.ChangeRequestId,EventType="Applied",
            ActorUserId=20,OccurredAt=now,CorrelationId=correlation,
            DecisionKey=null};
        V180B3ApprovalApplyPreflight.RequireAtomicAuditPair(
            plan,request,approved,applied);
        if(await WriteAsync(conn,tx, """
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@id,N'Approved',20,@now,@correlation,@decision)
            """,("@id",request.ChangeRequestId),("@now",now),
            ("@correlation",correlation),("@decision",decisionKey))!=1)
            throw new InvalidOperationException("012_TEST_APPROVED_AUDIT_CONFLICT");
        if(fault=="after-approved")throw new InvalidOperationException("012_TEST_FAULT");
        if(await WriteAsync(conn,tx, """
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@id,N'Applied',20,@now,@correlation,NULL)
            """,("@id",request.ChangeRequestId),("@now",now),
            ("@correlation",correlation))!=1)
            throw new InvalidOperationException("012_TEST_APPLIED_AUDIT_CONFLICT");
        if(fault=="after-applied")throw new InvalidOperationException("012_TEST_FAULT");
    }
}
