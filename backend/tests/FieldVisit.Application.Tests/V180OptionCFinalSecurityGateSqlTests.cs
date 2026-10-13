using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>Real SQL Server 2022 final decision gates on disposable OptionCIsolated databases.</summary>
public sealed class V180OptionCFinalSecurityGateSqlTests
{
    [Fact]
    public async Task Reject_persists_terminal_request_stage_and_audit_without_publication_and_rolls_back_audit_failure()
    {
        await using var h = await Harness.TryCreateAsync();
        if (h is null) return;
        Assert.Equal("512", await h.Scalar("SELECT CONVERT(nvarchar(20),@@OPTIONS & 512)"));
        var locationBefore = await h.Scalar("""
            SELECT (SELECT LocationId,ApprovalStatus,IsActive,LocationName,Address,
              CONVERT(varchar(18),RowVersion,1) AS RowVersion FROM dbo.Locations
              WHERE LocationId=10 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """);
        var tripBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripStops WHERE LocationId=10");
        var snapshotBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10");
        var versionBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10");
        var currentBefore = await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1");
        var request = await h.CreateRequest(10); await h.PrepareStage(request);
        var decision = Guid.NewGuid();
        await using (var tx = await h.Begin())
        {
            await Service().RejectAsync(h.Db, tx, request, 1, 2, decision, "Duplicate confirmed", CancellationToken.None);
            await tx.CommitAsync();
        }
        Assert.Equal("Rejected", await h.RequestStatus(request));
        Assert.Equal("2", await h.RequestValue(request,"ReviewedByUserId"));
        Assert.Equal("Abandoned", await h.StageStatus(request));
        Assert.Equal("1", await h.Scalar("""
            SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.RequestPublicId=@id AND e.EventType=N'Rejected'
              AND e.ActorUserId=2 AND e.DecisionKey=@key
              AND JSON_VALUE(e.DetailsJson,N'$.Reason')=N'Duplicate confirmed';
            """, P("@id",request),P("@key",decision)));
        Assert.Equal(locationBefore, await h.Scalar("""
            SELECT (SELECT LocationId,ApprovalStatus,IsActive,LocationName,Address,
              CONVERT(varchar(18),RowVersion,1) AS RowVersion FROM dbo.Locations
              WHERE LocationId=10 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """));
        Assert.Equal(tripBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripStops WHERE LocationId=10"));
        Assert.Equal(snapshotBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10"));
        Assert.Equal(versionBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
        Assert.Equal(currentBefore, await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));

        // Same request replay and cross-request DecisionKey reuse are denied.
        await using (var tx = await h.Begin())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().RejectAsync(
                h.Db,tx,request,1,2,decision,"Replay",CancellationToken.None));
            await tx.RollbackAsync();
        }
        var second=await h.CreateRequest(11); await h.PrepareStage(second);
        await using (var tx = await h.Begin())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().RejectAsync(
                h.Db,tx,second,1,2,decision,"Reused key",CancellationToken.None));
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",await h.RequestStatus(second));
        Assert.Equal("ReadyForPublish",await h.StageStatus(second));

        // Inject actual audit-table failure after both canonical updates.
        var rollback=await h.CreateRequest(12); await h.PrepareStage(rollback);
        await h.Exec("""
            EXEC(N'CREATE TRIGGER dbo.TR_OptionC_TestRejectAuditFailure
              ON dbo.ChangeRequestEvents AFTER INSERT AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted WHERE EventType=N''Rejected'')
                  THROW 51991,N''OPTIONC_TEST_AUDIT_FAILURE'',1;
              END');
            """);
        await using (var tx = await h.Begin())
        {
            await Assert.ThrowsAsync<SqlException>(() => Service().RejectAsync(
                h.Db,tx,rollback,1,2,Guid.NewGuid(),"Audit failure",CancellationToken.None));
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",await h.RequestStatus(rollback));
        Assert.Equal("ReadyForPublish",await h.StageStatus(rollback));
        Assert.Equal("0",await h.EventCount(rollback,"Rejected"));
    }

    [Fact]
    public async Task Independent_connections_racing_approve_approve_approve_reject_and_reject_reject_commit_exactly_one()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var cases=new[]{("approve","approve",10),("approve","reject",11),("reject","reject",12)};
        foreach(var (firstDecision,secondDecision,location) in cases)
        {
            await h.ResetActors();
            var request=await h.CreateRequest(location); await h.PrepareStage(request);
            var gate=new GateProvider();
            var first=Task.Run(()=>h.Decide(Service(gate),request,firstDecision,Guid.NewGuid(),null));
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var ready=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second=Task.Run(()=>h.Decide(Service(),request,secondDecision,Guid.NewGuid(),ready));
            var secondSpid=await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await h.WaitForLockWait(secondSpid);
            gate.Release.TrySetResult();
            var outcomes=await Task.WhenAll(first,second);
            Assert.Single(outcomes.Where(x=>x.Committed));
            Assert.Single(outcomes.Where(x=>x.Denied));
            var status=await h.RequestStatus(request);
            Assert.Contains(status,new[]{"Applied","Rejected"});
            Assert.Equal("1",await h.Scalar("""
                SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
                JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
                WHERE r.RequestPublicId=@id AND e.EventType IN(N'Approved',N'Rejected');
                """,P("@id",request)));
            Assert.Equal(status=="Applied"?"Published":"Abandoned",await h.StageStatus(request));
            Assert.Equal(status=="Applied"?"2":"1",await h.Scalar("""
                SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions
                WHERE LocationId=@location AND IsCurrent=1;
                """,P("@location",location)));
            Assert.Equal("1",await h.Scalar("""
                SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions
                WHERE LocationId=@location AND IsCurrent=1;
                """,P("@location",location)));
        }
    }

    [Fact]
    public async Task Reviewer_and_requester_hr_role_revocation_after_request_lock_is_fail_closed()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var cases=new[]
        {
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=0 WHERE UserId=2", // reviewer HR
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=1,IsIndependentAdminEligible=0 WHERE UserId=2", // reviewer role
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=0 WHERE UserId=1", // requester HR
            "UPDATE dbo.OptionCLiveApprovalActors SET IsActive=1,IsRequesterEligible=0 WHERE UserId=1" // requester role
        };
        var location=13;
        foreach(var mutation in cases)
        {
            await h.ResetActors();
            var request=await h.CreateRequest(location++); await h.PrepareStage(request);
            var gate=new GateProvider(); var service=Service(gate);
            var running=Task.Run(()=>h.Decide(service,request,"reject",Guid.NewGuid(),null));
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await h.Exec(mutation); // committed on a separate SQL session before live evidence is read
            gate.Release.TrySetResult();
            var outcome=await running;
            Assert.True(outcome.Denied); Assert.False(outcome.Committed);
            Assert.Equal("Pending",await h.RequestStatus(request));
            Assert.Equal("ReadyForPublish",await h.StageStatus(request));
            Assert.Equal("0",await h.EventCount(request,"Rejected"));
        }
    }

    [Fact]
    public async Task Organization_and_team_mismatch_are_denied_without_request_or_stage_mutation()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        foreach(var (location,mutation) in new[]{
            (10,"UPDATE dbo.OptionCLiveApprovalActors SET OrganizationId=2 WHERE UserId=2"),
            (11,"UPDATE dbo.OptionCLiveApprovalActors SET TeamId=2 WHERE UserId=2")})
        {
            await h.ResetActors();
            var request=await h.CreateRequest(location); await h.PrepareStage(request);
            await h.Exec(mutation);
            await using var tx=await h.Begin();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Service().RejectAsync(
                h.Db,tx,request,1,2,Guid.NewGuid(),"Unauthorized",CancellationToken.None));
            await tx.RollbackAsync();
            Assert.Equal("Pending",await h.RequestStatus(request));
            Assert.Equal("ReadyForPublish",await h.StageStatus(request));
            Assert.Equal("0",await h.EventCount(request,"Rejected"));
        }
    }

    [Fact]
    public async Task Lost_commit_ack_is_reconciled_on_fresh_nonpooled_connection_without_retry()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var request=await h.CreateRequest(10); await h.PrepareStage(request);
        var key=Guid.NewGuid();
        var direct=new SqlConnectionStringBuilder(h.Db.ConnectionString){Pooling=false};
        await using var proxy=new CommitAckLossProxy(direct.DataSource);
        var throughProxy=new SqlConnectionStringBuilder(direct.ConnectionString)
        { DataSource=$"127.0.0.1,{proxy.Port}",Pooling=false,ConnectTimeout=5 };
        var acknowledgementLost=false;
        await using(var c=new SqlConnection(throughProxy.ConnectionString))
        {
            await c.OpenAsync();
            await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
            await Service().ApproveAsync(c,tx,request,1,2,key,"Unknown commit fault injection",CancellationToken.None);
            try { await tx.CommitAsync(); }
            catch(Exception ex) when(ex is SqlException or IOException or InvalidOperationException)
            { acknowledgementLost=proxy.CommitResponseDropped; }
        }
        Assert.True(acknowledgementLost,"The proxy must forward the real SQL COMMIT then drop its acknowledgement.");
        var observed=await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
            direct.ConnectionString,request,key,2,CancellationToken.None);
        Assert.Equal("COMMITTED_EVIDENCE_REQUIRES_REVIEW",observed);
        Assert.Equal("Applied",await h.RequestStatus(request));
        Assert.Equal("2",await h.RequestValue(request,"ReviewedByUserId"));
        Assert.Equal("Published",await h.StageStatus(request));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),VersionNumber) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
        Assert.Equal("2",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
        Assert.Equal("1",await h.Scalar("""
            SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.RequestPublicId=@id AND e.EventType=N'Approved'
              AND e.DecisionKey=@key AND e.ActorUserId=2;
            """,P("@id",request),P("@key",key)));
        Assert.Equal("1",await h.Scalar("""
            SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.RequestPublicId=@id AND e.EventType=N'Applied' AND e.ActorUserId=2;
            """,P("@id",request)));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND VersionNumber=1"));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND VersionNumber=2 AND IsCurrent=1"));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripStops WHERE LocationId=10"));
        Assert.Equal("1",await h.Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10"));
        Assert.Equal(1,proxy.CommitRequestsForwarded);
    }

    [Fact]
    public async Task Reconciler_distinguishes_confirmed_rollback_from_indeterminate_evidence()
    {
        await using var h=await Harness.TryCreateAsync();
        if(h is null)return;
        var request=await h.CreateRequest(10); await h.PrepareStage(request);
        var key=Guid.NewGuid();
        await using(var tx=await h.Begin())
        {
            await Service().ApproveAsync(h.Db,tx,request,1,2,key,"Explicit rollback evidence",CancellationToken.None);
            await tx.RollbackAsync();
        }
        Assert.Equal("NO_COMMIT_EVIDENCE_REQUIRES_REVIEW",
            await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
                h.Db.ConnectionString,request,key,2,CancellationToken.None));
        Assert.Equal("INDETERMINATE",
            await FieldVisit.Infrastructure.V180OptionCUnknownCommitReconciler.ObserveAsync(
                h.Db.ConnectionString,request,Guid.NewGuid(),2,CancellationToken.None));
    }

    private sealed class CommitAckLossProxy : IAsyncDisposable
    {
        private readonly System.Net.Sockets.TcpListener listener;
        private readonly CancellationTokenSource stop=new();
        private readonly Task loop;
        private int commitRequests;
        private int responseDropped;
        internal int Port=>((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        internal int CommitRequestsForwarded=>Volatile.Read(ref commitRequests);
        internal bool CommitResponseDropped=>Volatile.Read(ref responseDropped)==1;
        private readonly string targetHost;
        private readonly int targetPort;
        internal CommitAckLossProxy(string target)
        {
            var comma=target.LastIndexOf(',');
            targetHost=target[..comma];targetPort=int.Parse(target[(comma+1)..]);
            listener=new(System.Net.IPAddress.Loopback,0);listener.Start();
            loop=RunAsync();
        }
        private async Task RunAsync()
        {
            try
            {
                using var client=await listener.AcceptTcpClientAsync(stop.Token);
                using var server=new System.Net.Sockets.TcpClient();
                await server.ConnectAsync(targetHost,targetPort,stop.Token);
                var toServer=PumpAsync(client.GetStream(),server.GetStream(),true);
                var toClient=PumpAsync(server.GetStream(),client.GetStream(),false);
                await Task.WhenAny(toServer,toClient);
                client.Close();server.Close();
            }
            catch(OperationCanceledException) { }
            catch(System.Net.Sockets.SocketException) when(stop.IsCancellationRequested) { }
        }
        private async Task PumpAsync(System.Net.Sockets.NetworkStream input,System.Net.Sockets.NetworkStream output,bool clientToServer)
        {
            var buffer=new byte[8192];var packet=new List<byte>();var message=new List<byte>();bool armed=false,dropNext=false;
            while(!stop.IsCancellationRequested)
            {
                int n;try{n=await input.ReadAsync(buffer,stop.Token);}catch{return;} if(n==0)return;
                for(int i=0;i<n;i++) packet.Add(buffer[i]);
                while(packet.Count>=8)
                {
                    int length=(packet[2]<<8)|packet[3];if(length<8||packet.Count<length)break;
                    byte type=packet[0],status=packet[1];var payload=packet.GetRange(8,length-8).ToArray();
                    packet.RemoveRange(0,length);
                    if(clientToServer)
                    {
                        if(type==0x01) { message.AddRange(payload); if((status&0x01)!=0) {
                            var sql=System.Text.Encoding.Unicode.GetString(message.ToArray());
                            if(sql.Contains("INSERT dbo.ChangeRequestEvents",StringComparison.OrdinalIgnoreCase)
                               ||sql.Contains("INSERT INTO dbo.ChangeRequestEvents",StringComparison.OrdinalIgnoreCase)) armed=true;
                            message.Clear();
                        }}
                        if(armed&&type==0x0e){dropNext=true;armed=false;Interlocked.Increment(ref commitRequests);}
                        await output.WriteAsync(buffer.AsMemory(0,n),stop.Token);await output.FlushAsync(stop.Token);
                    }
                    else
                    {
                        if(dropNext)
                        {
                            Interlocked.Exchange(ref responseDropped,1);
                            clientSocketClose();
                            return;
                        }
                        await output.WriteAsync(buffer.AsMemory(0,n),stop.Token);await output.FlushAsync(stop.Token);
                    }
                }
                if(clientToServer&&packet.Count==0) { /* complete framed input forwarded above */ }
            }
            void clientSocketClose(){listener.Stop();}
        }
        public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();try{await loop.WaitAsync(TimeSpan.FromSeconds(2));}catch{}stop.Dispose();}
    }

    private static FieldVisit.Infrastructure.V180OptionCAdminApprovalService Service(
        FieldVisit.Infrastructure.IV180OptionCLiveApprovalEvidenceProvider? provider=null) =>
        new(provider??new FieldVisit.Infrastructure.V180OptionCIsolatedApprovalEvidenceProvider());

    private sealed class GateProvider:FieldVisit.Infrastructure.IV180OptionCLiveApprovalEvidenceProvider
    {
        internal TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly FieldVisit.Infrastructure.V180OptionCIsolatedApprovalEvidenceProvider inner=new();
        public async Task<FieldVisit.Infrastructure.V180OptionCLiveApprovalEvidence> ReadAsync(
            SqlConnection c,SqlTransaction t,long id,int requester,int reviewer,int org,int? team,CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await inner.ReadAsync(c,t,id,requester,reviewer,org,team,ct);
        }
    }

    private sealed class Harness:IAsyncDisposable
    {
        private readonly SqlConnection root; private readonly string name;
        internal SqlConnection Db{get;}
        private Harness(SqlConnection root,SqlConnection db,string name){this.root=root;Db=db;this.name=name;}

        internal static async Task<Harness?> TryCreateAsync()
        {
            var raw=Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(raw))
            {
                if(Environment.GetEnvironmentVariable("E1_R1_REQUIRED")=="1")
                    throw new InvalidOperationException("OPTIONC_FINAL_GATES_SQL_REQUIRED");
                return null;
            }
            var rb=new SqlConnectionStringBuilder(raw){Pooling=false,ConnectTimeout=5};
            if(rb.DataSource is not("localhost,1433" or "127.0.0.1,1433")||rb.InitialCatalog!="master"||rb.UserID!="sa"||rb.IntegratedSecurity)
                throw new InvalidOperationException("OPTIONC_FINAL_GATES_NONLOCAL_TARGET_DENIED");
            var root=new SqlConnection(rb.ConnectionString);await root.OpenAsync();
            var name="OptionCIsolated_"+Guid.NewGuid().ToString("N");await Exec(root,$"CREATE DATABASE [{name}]");
            var dbb=new SqlConnectionStringBuilder(rb.ConnectionString){InitialCatalog=name,Pooling=false};
            var db=new SqlConnection(dbb.ConnectionString);
            try
            {
                await db.OpenAsync();await CreateBase(db);
                var repo=FindRoot();
                foreach(var p in new[]{"1800_011/Up.sql","1800_012/Up.sql","1800_014/Preflight.sql","1800_014/Up.sql","1800_014/Backfill.sql"})
                    await Exec(db,File.ReadAllText(Path.Combine(repo,"docs","release","optionc-canonical-sql-draft",p.Replace('/',Path.DirectorySeparatorChar))));
                return new Harness(root,db,name);
            }
            catch{await db.DisposeAsync();await Exec(root,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");await root.DisposeAsync();throw;}
        }

        internal async Task<SqlTransaction> Begin()=> (SqlTransaction)await Db.BeginTransactionAsync(IsolationLevel.Serializable);
        internal async Task ResetActors()=>await Exec("UPDATE dbo.OptionCLiveApprovalActors SET OrganizationId=1,TeamId=1,IsActive=1,IsRequesterEligible=1,IsIndependentAdminEligible=CASE WHEN UserId=2 THEN 1 ELSE 0 END WHERE UserId IN(1,2)");
        internal async Task<Guid> CreateRequest(int location)
        {
            var id=Guid.NewGuid();
            await Exec("""
                INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                SELECT @id,OrganizationId,TeamId,N'Location',CONVERT(nvarchar(80),LocationId),N'UpdatePublishedLocation',N'High',RowVersion,N'{}',@json,1,SYSUTCDATETIME(),N'Pending' FROM dbo.Locations WHERE LocationId=@location;
                INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId)
                SELECT ChangeRequestId,N'Submitted',1,SYSUTCDATETIME(),NEWID() FROM dbo.ChangeRequests WHERE RequestPublicId=@id;
                """,P("@id",id),P("@location",location),
                P("@json",$"""{{"LocationName":"Changed {location}","City":"Taipei","District":"Xinyi","Address":"New address {location}","PlusCode":null,"TaxId":null,"MasterNote":null}}"""));
            return id;
        }
        internal async Task PrepareStage(Guid id)
        {
            await using(var tx=await Begin()){await FieldVisit.Infrastructure.V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(Db,tx,id,1,CancellationToken.None);await tx.CommitAsync();}
            await Exec("""
                UPDATE s SET GeocodeStatus=N'Succeeded',GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish',UpdatedAt=SYSUTCDATETIME()
                FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id;
                """,P("@id",id));
        }
        internal async Task<string> RequestStatus(Guid id)=>await Scalar("SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",P("@id",id));
        internal async Task<string> RequestValue(Guid id,string column)=>await Scalar($"SELECT CONVERT(nvarchar(200),{column}) FROM dbo.ChangeRequests WHERE RequestPublicId=@id",P("@id",id));
        internal async Task<string> StageStatus(Guid id)=>await Scalar("SELECT s.StageStatus FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",P("@id",id));
        internal async Task<string> EventCount(Guid id,string type)=>await Scalar("SELECT CONVERT(nvarchar(20),COUNT(*)) FROM dbo.ChangeRequestEvents e JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId WHERE r.RequestPublicId=@id AND e.EventType=@type",P("@id",id),P("@type",type));
        internal async Task<string> Scalar(string sql,params SqlParameter[] ps)=>await ScalarOn(Db,sql,ps);
        internal async Task Exec(string sql,params SqlParameter[] ps)=>await ExecOn(Db,sql,ps);

        internal async Task<Outcome> Decide(FieldVisit.Infrastructure.V180OptionCAdminApprovalService service,Guid request,string kind,Guid key,TaskCompletionSource<int>? sessionReady)
        {
            var b=new SqlConnectionStringBuilder(Db.ConnectionString){Pooling=false};
            await using var c=new SqlConnection(b.ConnectionString);await c.OpenAsync();
            await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable);
            if(sessionReady is not null)sessionReady.TrySetResult(int.Parse(await ScalarOn(c,"SELECT @@SPID")));
            try
            {
                if(kind=="approve")await service.ApproveAsync(c,tx,request,1,2,key,"Concurrent decision",CancellationToken.None);
                else await service.RejectAsync(c,tx,request,1,2,key,"Concurrent decision",CancellationToken.None);
                await tx.CommitAsync();return new(true,false);
            }
            catch(UnauthorizedAccessException){await tx.RollbackAsync(CancellationToken.None);return new(false,true);}
        }
        internal async Task WaitForLockWait(int spid)
        {
            for(var i=0;i<100;i++)
            {
                if(await Scalar("SELECT CONVERT(nvarchar(2),CASE WHEN EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE session_id=@id AND blocking_session_id<>0) THEN 1 ELSE 0 END)",P("@id",spid))=="1")return;
                await Task.Delay(50);
            }
            throw new TimeoutException("OPTIONC_CONCURRENT_DECISION_NEVER_BLOCKED");
        }
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await ExecOn(root,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");await root.DisposeAsync();}

        private static async Task CreateBase(SqlConnection c)
        {
            var locations=string.Join(",",Enumerable.Range(10,10).Select(i=>$"({i},1,1,1,N'Customer',N'Approved',1,N'Original {i}',N'Original address {i}')"));
            await ExecOn(c,$"""
                CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
                CREATE TABLE dbo.Organizations(OrganizationId int NOT NULL PRIMARY KEY); CREATE TABLE dbo.Teams(TeamId int NOT NULL PRIMARY KEY); CREATE TABLE dbo.Users(UserId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Locations(LocationId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NOT NULL,CreatedByUserId int NOT NULL,LocationType nvarchar(20) NOT NULL,ApprovalStatus nvarchar(20) NOT NULL,IsActive bit NOT NULL,LocationName nvarchar(200) NULL,City nvarchar(100) NULL,District nvarchar(100) NULL,Address nvarchar(500) NULL,PlusCode nvarchar(100) NULL,TaxId nvarchar(50) NULL,MasterNote nvarchar(1000) NULL,RowVersion rowversion NOT NULL);
                CREATE TABLE dbo.OptionCLiveApprovalActors(UserId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NULL,IsActive bit NOT NULL,IsRequesterEligible bit NOT NULL,IsIndependentAdminEligible bit NOT NULL);
                CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint NOT NULL PRIMARY KEY,LocationId int NOT NULL FOREIGN KEY REFERENCES dbo.Locations(LocationId));
                CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint NOT NULL PRIMARY KEY,LocationId int NOT NULL FOREIGN KEY REFERENCES dbo.Locations(LocationId));
                INSERT dbo.Organizations VALUES(1),(2);INSERT dbo.Teams VALUES(1),(2);INSERT dbo.Users VALUES(1),(2),(3);INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,LocationType,ApprovalStatus,IsActive,LocationName,Address) VALUES {locations};
                INSERT dbo.OptionCLiveApprovalActors VALUES(1,1,1,1,1,0),(2,1,1,1,0,1),(3,2,2,1,0,1);
                INSERT dbo.VisitTripStops SELECT LocationId+1000,LocationId FROM dbo.Locations;INSERT dbo.VisitTripSnapshotStops SELECT LocationId+2000,LocationId FROM dbo.Locations;
                INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'isolated',SYSUTCDATETIME(),N'isolated');
                """);
        }
        private static string FindRoot(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d is not null){if(File.Exists(Path.Combine(d.FullName,"docs/release/optionc-canonical-sql-draft/1800_011/Up.sql")))return d.FullName;d=d.Parent;}throw new FileNotFoundException("OPTIONC_FINAL_GATE_REPO_ROOT_NOT_FOUND");}
    }

    private static async Task ExecOn(SqlConnection c,string sql,params SqlParameter[] ps){await using var cmd=new SqlCommand(sql,c){CommandTimeout=60};cmd.Parameters.AddRange(ps);await cmd.ExecuteNonQueryAsync();}
    private static async Task<string> ScalarOn(SqlConnection c,string sql,params SqlParameter[] ps){await using var cmd=new SqlCommand(sql,c){CommandTimeout=60};cmd.Parameters.AddRange(ps);return Convert.ToString(await cmd.ExecuteScalarAsync())??"";}
    private static async Task Exec(SqlConnection c,string sql,params SqlParameter[] ps)=>await ExecOn(c,sql,ps);
    private static SqlParameter P(string key,object value)=>new(key,value);
    private sealed record Outcome(bool Committed,bool Denied);
}
