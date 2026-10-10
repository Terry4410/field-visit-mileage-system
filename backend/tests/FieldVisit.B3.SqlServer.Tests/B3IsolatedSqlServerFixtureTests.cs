using System.Data;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Real Microsoft SQL Server 2022 ONLY, in a disposable localhost CI container.
/// An independent random-name test database is created and destroyed here.
/// This is NOT a production/UAT migration, full API UAT, or authorization to
/// execute schema 1800_011. No external server/connection string is accepted.
/// </summary>
public sealed class DisposableSqlServer : IAsyncLifetime
{
    public string DatabaseName { get; } = "B3SqlFixture_" + Guid.NewGuid().ToString("N");
    private string _master = "";
    public string DatabaseConnection { get; private set; } = "";
    private int _counter = 10_000;

    private static void CheckLocal(SqlConnectionStringBuilder b)
    {
        if(b.DataSource is not ("127.0.0.1,14335" or "localhost,14335")
            || !string.Equals(b.InitialCatalog,"master",StringComparison.OrdinalIgnoreCase)
            || !string.Equals(b.UserID,"sa",StringComparison.OrdinalIgnoreCase)
            || b.IntegratedSecurity)
            throw new InvalidOperationException(
                "B3_SQL_TEST_CONNECTION must be local CI SQL Server on port 14335, master only");
    }

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("B3_SQL_TEST_CONNECTION")
            ?? throw new InvalidOperationException("No isolated SQL fixture connection configured");
        var builder = new SqlConnectionStringBuilder(raw) { ConnectTimeout = 4 };
        CheckLocal(builder);
        _master=builder.ConnectionString;
        Exception? last=null;
        for(var attempt=0;attempt<90;attempt++)
        {
            try
            {
                await using var ready=new SqlConnection(_master);
                await ready.OpenAsync();
                last=null;
                break;
            }
            catch(SqlException ex)
            {
                last=ex;
                await Task.Delay(1000);
            }
        }
        if(last is not null)throw new InvalidOperationException("Disposable local SQL Server unavailable",last);

        await using(var root=new SqlConnection(_master))
        {
            await root.OpenAsync();
            await Command(root,$"CREATE DATABASE [{DatabaseName}]");
        }
        builder.InitialCatalog=DatabaseName;
        DatabaseConnection=builder.ConnectionString;
        await using var conn=await ConnectAsync();
        foreach(var sql in new[]
        {
            "CREATE TABLE dbo.SchemaVersions (VersionNumber nvarchar(30) NOT NULL, AppliedAt datetime2(3) NOT NULL)",
            "CREATE TABLE dbo.Organizations (OrganizationId int NOT NULL PRIMARY KEY)",
            "CREATE TABLE dbo.Teams (TeamId int NOT NULL PRIMARY KEY)",
            "CREATE TABLE dbo.Users (UserId int NOT NULL PRIMARY KEY)",
            """
            CREATE TABLE dbo.ChangeRequests(
                ChangeRequestId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                RequestPublicId uniqueidentifier NOT NULL,
                OrganizationId int NOT NULL,
                TeamId int NULL,
                EntityKind nvarchar(40) NOT NULL,
                EntityId nvarchar(80) NOT NULL,
                OperationCode nvarchar(80) NOT NULL,
                RiskCode nvarchar(20) NOT NULL,
                ExpectedEntityRowVersion varbinary(8) NULL,
                BeforeJson nvarchar(max) NULL,
                ProposedJson nvarchar(max) NOT NULL,
                EvidenceJson nvarchar(max) NULL,
                RequestedByUserId int NOT NULL,
                SubmittedAt datetime2(3) NOT NULL,
                Status nvarchar(30) NOT NULL,
                ReviewedByUserId int NULL,
                ReviewedAt datetime2(3) NULL,
                ReviewReason nvarchar(1000) NULL,
                AppliedAt datetime2(3) NULL,
                RowVersion rowversion NOT NULL,
                CONSTRAINT FK_B3_Requests_Org FOREIGN KEY(OrganizationId)
                    REFERENCES dbo.Organizations(OrganizationId) ON DELETE NO ACTION,
                CONSTRAINT FK_B3_Requests_Team FOREIGN KEY(TeamId)
                    REFERENCES dbo.Teams(TeamId) ON DELETE NO ACTION,
                CONSTRAINT FK_B3_Requests_Requester FOREIGN KEY(RequestedByUserId)
                    REFERENCES dbo.Users(UserId) ON DELETE NO ACTION,
                CONSTRAINT FK_B3_Requests_Reviewer FOREIGN KEY(ReviewedByUserId)
                    REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
            )
            """,
            """
            CREATE TABLE dbo.ChangeRequestEvents(
                ChangeRequestEventId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                ChangeRequestId bigint NOT NULL,
                EventType nvarchar(40) NOT NULL,
                ActorUserId int NULL,
                OccurredAt datetime2(3) NOT NULL,
                CorrelationId uniqueidentifier NOT NULL,
                DecisionKey uniqueidentifier NULL,
                DetailsJson nvarchar(max) NULL,
                CONSTRAINT FK_B3_Events_Request FOREIGN KEY(ChangeRequestId)
                    REFERENCES dbo.ChangeRequests(ChangeRequestId) ON DELETE NO ACTION,
                CONSTRAINT FK_B3_Events_Actor FOREIGN KEY(ActorUserId)
                    REFERENCES dbo.Users(UserId) ON DELETE NO ACTION
            )
            """,
            "CREATE UNIQUE INDEX UX_B3_ChangeRequests_RequestPublicId ON dbo.ChangeRequests(RequestPublicId)",
            "CREATE UNIQUE INDEX UX_B3_ChangeRequests_Org_Entity_Pending ON dbo.ChangeRequests(OrganizationId,EntityKind,EntityId) WHERE [Status] = 'Pending'",
            "CREATE UNIQUE INDEX UX_B3_ChangeRequestEvents_DecisionKey ON dbo.ChangeRequestEvents(DecisionKey) WHERE [DecisionKey] IS NOT NULL"
        })
            await Command(conn,sql);

        // The fixture is assembled from candidate EF design-time constraints,
        // not from an 011 migration file and never on UAT/Production.
        await using var modelDb=new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(DatabaseConnection).Options);
        var model=modelDb.GetService<IDesignTimeModel>().Model;
        foreach(var type in new[] {typeof(V180B3ChangeRequest),typeof(V180B3ChangeEvent)})
        {
            var e=model.FindEntityType(type)!;
            var table=e.GetTableName()!;
            foreach(var check in e.GetCheckConstraints())
            {
                if(!check.Name.StartsWith("CK_B3_",StringComparison.Ordinal))
                    throw new InvalidOperationException("unexpected fixture check constraint");
                await Command(conn,$"ALTER TABLE dbo.[{table}] WITH CHECK ADD CONSTRAINT [{check.Name}] CHECK ({check.Sql})");
            }
        }
        await Command(conn,"INSERT INTO dbo.Organizations VALUES (1),(2)");
        await Command(conn,"INSERT INTO dbo.Teams VALUES (7),(8)");
        await Command(conn,"INSERT INTO dbo.Users VALUES (10),(20),(30)");
        await Command(conn,"INSERT INTO dbo.SchemaVersions VALUES ('1.8.0-011',SYSUTCDATETIME())");
    }

    private static async Task Command(SqlConnection conn,string sql,
        params (string key,object? value)[] values)
    {
        await using var cmd=new SqlCommand(sql,conn) { CommandTimeout=20 };
        foreach(var (key,value) in values)
            cmd.Parameters.AddWithValue(key,value??DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<SqlConnection> ConnectAsync()
    {
        var conn=new SqlConnection(DatabaseConnection);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<object?> ScalarAsync(string sql,
        params (string key,object? value)[] values)
    {
        await using var conn=await ConnectAsync();
        await using var cmd=new SqlCommand(sql,conn) {CommandTimeout=20};
        foreach(var (key,value) in values)
            cmd.Parameters.AddWithValue(key,value??DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<int> ExecuteAsync(string sql,
        params (string key,object? value)[] values)
    {
        await using var conn=await ConnectAsync();
        await using var cmd=new SqlCommand(sql,conn) {CommandTimeout=20};
        foreach(var (key,value) in values)
            cmd.Parameters.AddWithValue(key,value??DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> InsertRequestAsync(string? entityId=null,int org=1)
    {
        entityId??=Interlocked.Increment(ref _counter).ToString();
        var value=await ScalarAsync("""
            INSERT INTO dbo.ChangeRequests(
                RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,
                OperationCode,RiskCode,ExpectedEntityRowVersion,ProposedJson,
                RequestedByUserId,SubmittedAt,Status)
            VALUES(@guid,@org,7,N'Location',@entity,N'UpdatePublishedLocation',
                N'High',0x0102030405060708,N'{"Name":"temporary"}',
                10,SYSUTCDATETIME(),N'Pending');
            SELECT CAST(SCOPE_IDENTITY() AS bigint)
            """,("@guid",Guid.NewGuid()),("@org",org),("@entity",entityId));
        return Convert.ToInt64(value);
    }

    public async Task DisposeAsync()
    {
        if(string.IsNullOrWhiteSpace(_master))return;
        // Never drop arbitrary databases; only our random-name fixture.
        if(!DatabaseName.StartsWith("B3SqlFixture_",StringComparison.Ordinal))return;
        await using var conn=new SqlConnection(_master);
        try
        {
            await conn.OpenAsync();
            await Command(conn,$"IF DB_ID(N'{DatabaseName}') IS NOT NULL " +
                $"BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{DatabaseName}]; END");
        }
        catch(SqlException ex)
        {
            // CI container is itself discarded; report cleanup failure
            // but never point at any other database.
            Console.WriteLine("Temporary SQL fixture cleanup error: "+ex.Number);
            throw;
        }
    }
}

/// <summary>
/// Real SQL Server catalog/index/FK/CHECK/rowversion tests in disposable DB.
/// These are *partial B4-SQL tests*, not authorized Business UAT.
/// </summary>
public sealed class B3IsolatedSqlServerFixtureTests : IClassFixture<DisposableSqlServer>
{
    private readonly DisposableSqlServer _fixture;
    public B3IsolatedSqlServerFixtureTests(DisposableSqlServer fixture) =>
        _fixture=fixture;

    [Fact]
    public async Task Candidate_011_catalog_probe_executes_and_accepts_intact_schema()
    {
        var actual=await _fixture.ScalarAsync(V180B3SqlSafetyRules.CatalogCheckSql);
        Assert.Equal(1,Convert.ToInt32(actual));
        var version=(string?)await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.LatestSchemaVersionSql);
        V180B3SqlSafetyRules.RequireLatestSchemaVersion(version);
    }

    [Fact]
    public async Task Two_concurrent_pending_inserts_yield_one_winner_and_unique_conflict()
    {
        var id="RACE_"+Guid.NewGuid().ToString("N");
        async Task<(bool success,int number)> Insert()
        {
            try{await _fixture.InsertRequestAsync(id);return(true,0);}
            catch(SqlException ex){return(false,ex.Number);}
        }
        var results=await Task.WhenAll(Task.Run(Insert),Task.Run(Insert));
        Assert.Single(results.Where(x=>x.success));
        Assert.Contains(results,x=>!x.success && x.number is 2601 or 2627);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",id))));
    }

    [Fact]
    public async Task Same_decision_key_across_concurrent_reviews_never_creates_two_events()
    {
        var a=await _fixture.InsertRequestAsync();
        var b=await _fixture.InsertRequestAsync();
        var key=Guid.NewGuid();
        async Task<(bool success,int number)> Write(long id)
        {
            try
            {
                await _fixture.ExecuteAsync("""
                    INSERT INTO dbo.ChangeRequestEvents(
                        ChangeRequestId,EventType,ActorUserId,OccurredAt,
                        CorrelationId,DecisionKey,DetailsJson)
                    VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),
                        @correlation,@decision,N'{"Reason":"test"}')
                    """,("@id",id),("@correlation",Guid.NewGuid()),("@decision",key));
                return(true,0);
            }
            catch(SqlException ex){return(false,ex.Number);}
        }
        var results=await Task.WhenAll(Task.Run(()=>Write(a)),Task.Run(()=>Write(b)));
        Assert.Single(results.Where(x=>x.success));
        Assert.Contains(results,x=>!x.success && x.number is 2601 or 2627);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE DecisionKey=@id",
            ("@id",key))));
    }

    [Fact]
    public async Task Stale_rowversion_cannot_update_request()
    {
        var id=await _fixture.InsertRequestAsync();
        var old=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        Assert.Equal(8,old.Length);
        Assert.Equal(1,await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET BeforeJson=N'{}' WHERE ChangeRequestId=@id AND RowVersion=@v",
            ("@id",id),("@v",old)));
        Assert.Equal(0,await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET BeforeJson=N'{\"x\":1}' WHERE ChangeRequestId=@id AND RowVersion=@v",
            ("@id",id),("@v",old)));
    }

    [Fact]
    public async Task Trusted_audit_FKs_prevent_deleting_request_or_user_with_history()
    {
        var id=await _fixture.InsertRequestAsync();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,
                CorrelationId,DetailsJson)
            VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),@g,N'{}')
            """,("@id",id),("@g",Guid.NewGuid()));
        var ex=await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync(
                "DELETE FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
                ("@id",id)));
        Assert.Equal(547,ex.Number);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id))));
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync("DELETE FROM dbo.Users WHERE UserId=10"))).Number);
    }

    [Fact]
    public async Task Candidate_CHECK_rejects_unapproved_states_and_malformed_JSON()
    {
        var id=await _fixture.InsertRequestAsync();
        foreach(var sql in new[]
        {
            "UPDATE dbo.ChangeRequests SET Status=N'Applied' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET Status=N'Rejected' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET ProposedJson=N'broken' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET ExpectedEntityRowVersion=NULL WHERE ChangeRequestId=@id"
        })
        {
            var error=await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",id)));
            Assert.Equal(547,error.Number);
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",("@id",id)));
    }

    [Fact]
    public async Task Catalog_denies_disabled_unique_index_and_enabled_table_trigger()
    {
        const string index="UX_B3_ChangeRequests_Org_Entity_Pending";
        await _fixture.ExecuteAsync($"ALTER INDEX [{index}] ON dbo.ChangeRequests DISABLE");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync($"ALTER INDEX [{index}] ON dbo.ChangeRequests REBUILD");
        }
        // SQL Server CREATE TRIGGER requires its own batch.
        await _fixture.ExecuteAsync("""
            CREATE TRIGGER dbo.B3_Fixture_NoSideEffects ON dbo.ChangeRequests
            AFTER INSERT AS BEGIN SET NOCOUNT ON; END
            """);
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync("DROP TRIGGER dbo.B3_Fixture_NoSideEffects");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Live_latest_version_is_denied_if_an_older_schema_row_is_newer()
    {
        await _fixture.ExecuteAsync(
            "INSERT INTO dbo.SchemaVersions VALUES (N'1.8.0-010',DATEADD(day,1,SYSUTCDATETIME()))");
        try
        {
            var version=(string?)await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.LatestSchemaVersionSql);
            Assert.Equal("1.8.0-010",version);
            Assert.Equal("B3_SCHEMA_NOT_VERIFIED",
                Assert.Throws<InvalidOperationException>(()=>
                    V180B3SqlSafetyRules.RequireLatestSchemaVersion(version)).Message);
        }
        finally
        {
            await _fixture.ExecuteAsync(
                "DELETE FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010'");
        }
    }

    [Fact]
    public async Task Real_sql_constraints_block_self_review_null_reason_and_premature_review_fields()
    {
        var id=await _fixture.InsertRequestAsync();
        var commands=new[]
        {
            "UPDATE dbo.ChangeRequests SET Status=N'Rejected',ReviewedByUserId=10,ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'valid' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET Status=N'Rejected',ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),ReviewReason=NULL WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET Status=N'Rejected',ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'  ' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET ReviewReason=N'premature' WHERE ChangeRequestId=@id"
        };
        foreach(var command in commands)
        {
            var ex=await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(command,("@id",id)));
            Assert.Equal(547,ex.Number);
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",("@id",id)));
    }

    [Fact]
    public async Task Real_sql_rejects_invalid_audit_types_missing_actor_and_broken_json()
    {
        var id=await _fixture.InsertRequestAsync();
        var commands=new[]
        {
            "INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId) VALUES(@id,N'Approved',20,SYSUTCDATETIME(),NEWID())",
            "INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId) VALUES(@id,N'Submitted',NULL,SYSUTCDATETIME(),NEWID())",
            "INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId) VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID())",
            "INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DetailsJson) VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),N'broken')"
        };
        foreach(var command in commands)
        {
            var ex=await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(command,("@id",id)));
            Assert.Equal(547,ex.Number);
        }
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Serializable_transaction_rolls_back_request_if_audit_insert_is_invalid()
    {
        var entity="ROLLBACK_"+Guid.NewGuid().ToString("N");
        await using var conn=await _fixture.ConnectAsync();
        await using var tx=(SqlTransaction)await conn.BeginTransactionAsync(
            IsolationLevel.Serializable);
        await using var create=new SqlCommand("""
            INSERT INTO dbo.ChangeRequests(
                RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,
                OperationCode,RiskCode,ExpectedEntityRowVersion,ProposedJson,
                RequestedByUserId,SubmittedAt,Status)
            VALUES(@guid,1,7,N'Location',@entity,N'UpdatePublishedLocation',
                N'High',0x0102030405060708,N'{"Name":"test"}',
                10,SYSUTCDATETIME(),N'Pending');
            SELECT CAST(SCOPE_IDENTITY() AS bigint)
            """,conn,tx);
        create.Parameters.AddWithValue("@guid",Guid.NewGuid());
        create.Parameters.AddWithValue("@entity",entity);
        var id=Convert.ToInt64(await create.ExecuteScalarAsync());
        await using var invalid=new SqlCommand("""
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId)
            VALUES(@id,N'Approved',20,SYSUTCDATETIME(),NEWID())
            """,conn,tx);
        invalid.Parameters.AddWithValue("@id",id);
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(
            ()=>invalid.ExecuteNonQueryAsync())).Number);
        await tx.RollbackAsync();
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",entity))));
    }

    [Fact]
    public async Task Untrusted_foreign_key_fails_catalog_until_it_is_retrusted()
    {
        const string name="FK_B3_Events_Actor";
        await _fixture.ExecuteAsync(
            $"ALTER TABLE dbo.ChangeRequestEvents NOCHECK CONSTRAINT [{name}]");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync(
                $"ALTER TABLE dbo.ChangeRequestEvents WITH CHECK CHECK CONSTRAINT [{name}]");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Valid_rejection_reopens_pending_slot_but_preserves_decision_history()
    {
        var entity="REOPEN_"+Guid.NewGuid().ToString("N");
        var original=await _fixture.InsertRequestAsync(entity);
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20, ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'Independent rejection'
                WHERE ChangeRequestId=@id AND Status=N'Pending'
                """,conn,tx);
            update.Parameters.AddWithValue("@id",original);
            Assert.Equal(1,await update.ExecuteNonQueryAsync());
            await using var audit=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),
                    NEWID(),@key,N'{"reason":"independent"}')
                """,conn,tx);
            audit.Parameters.AddWithValue("@id",original);
            audit.Parameters.AddWithValue("@key",Guid.NewGuid());
            Assert.Equal(1,await audit.ExecuteNonQueryAsync());
            await tx.CommitAsync();
        }
        var fresh=await _fixture.InsertRequestAsync(entity);
        Assert.NotEqual(original,fresh);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity AND Status=N'Pending'",
            ("@entity",entity))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity AND Status=N'Rejected'",
            ("@entity",entity))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND EventType=N'Rejected'",
            ("@id",original))));
    }

    [Fact]
    public async Task Competing_serializable_reviewers_on_same_rowversion_commit_one_audit_event()
    {
        var id=await _fixture.InsertRequestAsync();
        var version=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        async Task<bool> Review(int reviewer)
        {
            await using var conn=await _fixture.ConnectAsync();
            await using var tx=(SqlTransaction)await conn.BeginTransactionAsync(
                IsolationLevel.Serializable);
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=@reviewer,ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'Independent decision'
                WHERE ChangeRequestId=@id AND Status=N'Pending' AND RowVersion=@version
                """,conn,tx);
            update.Parameters.AddWithValue("@id",id);
            update.Parameters.AddWithValue("@reviewer",reviewer);
            update.Parameters.AddWithValue("@version",version);
            if(await update.ExecuteNonQueryAsync()!=1)
            {
                await tx.RollbackAsync();
                return false;
            }
            await using var audit=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',@reviewer,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            audit.Parameters.AddWithValue("@id",id);
            audit.Parameters.AddWithValue("@reviewer",reviewer);
            audit.Parameters.AddWithValue("@key",Guid.NewGuid());
            Assert.Equal(1,await audit.ExecuteNonQueryAsync());
            await tx.CommitAsync();
            return true;
        }
        var outcomes=await Task.WhenAll(
            Task.Run(()=>Review(20)),Task.Run(()=>Review(30)));
        Assert.Single(outcomes.Where(x=>x));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND EventType=N'Rejected'",
            ("@id",id))));
    }

    [Fact]
    public async Task Request_public_identity_cannot_be_reused_for_a_different_location()
    {
        var id=await _fixture.InsertRequestAsync();
        var key=(Guid)(await _fixture.ScalarAsync(
            "SELECT RequestPublicId FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        var sql="""
            INSERT INTO dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                EntityKind,EntityId,OperationCode,RiskCode,
                ExpectedEntityRowVersion,ProposedJson,RequestedByUserId,SubmittedAt,Status)
            VALUES(@key,1,7,N'Location',@entity,N'UpdatePublishedLocation',N'High',
                0x0102030405060708,N'{}',10,SYSUTCDATETIME(),N'Pending')
            """;
        var ex=await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync(sql,
                ("@key",key),("@entity",Guid.NewGuid().ToString("N"))));
        Assert.True(ex.Number is 2601 or 2627);
    }

    [Fact]
    public async Task Pending_unique_key_partitions_identical_entity_ids_by_organization()
    {
        var entity="PARTITION_"+Guid.NewGuid().ToString("N");
        var one=await _fixture.InsertRequestAsync(entity,org:1);
        var two=await _fixture.InsertRequestAsync(entity,org:2);
        Assert.NotEqual(one,two);
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity AND Status=N'Pending'",
            ("@entity",entity))));
        // A separate live role/team/HR service check is still mandatory.
    }

    [Fact]
    public async Task Catalog_rejects_untrusted_check_until_constraint_is_retrusted()
    {
        const string constraint="CK_B3_ChangeRequests_KnownStatus";
        await _fixture.ExecuteAsync(
            $"ALTER TABLE dbo.ChangeRequests NOCHECK CONSTRAINT [{constraint}]");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync(
                $"ALTER TABLE dbo.ChangeRequests WITH CHECK CHECK CONSTRAINT [{constraint}]");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Failed_rejection_audit_rolls_back_status_rowversion_and_preserves_prior_history()
    {
        var id=await _fixture.InsertRequestAsync();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DetailsJson)
            VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),N'{}')
            """,("@id",id));
        var before=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'Independent rejection'
                WHERE ChangeRequestId=@id AND Status=N'Pending' AND RowVersion=@rv
                """,conn,tx);
            update.Parameters.AddWithValue("@id",id);
            update.Parameters.AddWithValue("@rv",before);
            Assert.Equal(1,await update.ExecuteNonQueryAsync());
            await using var invalid=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Approved',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            invalid.Parameters.AddWithValue("@id",id);
            invalid.Parameters.AddWithValue("@key",Guid.NewGuid());
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(
                ()=>invalid.ExecuteNonQueryAsync())).Number);
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(before,(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND EventType=N'Submitted'",
            ("@id",id))));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND EventType=N'Rejected'",
            ("@id",id))));
    }

    [Fact]
    public async Task Replayed_decision_key_rolls_back_second_request_and_keeps_first_audit()
    {
        var first=await _fixture.InsertRequestAsync();
        var second=await _fixture.InsertRequestAsync();
        var key=Guid.NewGuid();
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'first'
                WHERE ChangeRequestId=@id AND Status=N'Pending'
                """,conn,tx);
            update.Parameters.AddWithValue("@id",first);
            Assert.Equal(1,await update.ExecuteNonQueryAsync());
            await using var audit=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            audit.Parameters.AddWithValue("@id",first);
            audit.Parameters.AddWithValue("@key",key);
            Assert.Equal(1,await audit.ExecuteNonQueryAsync());
            await tx.CommitAsync();
        }
        var before=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",second)))!;
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=30,ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'second'
                WHERE ChangeRequestId=@id AND Status=N'Pending' AND RowVersion=@rv
                """,conn,tx);
            update.Parameters.AddWithValue("@id",second);
            update.Parameters.AddWithValue("@rv",before);
            Assert.Equal(1,await update.ExecuteNonQueryAsync());
            await using var duplicate=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',30,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            duplicate.Parameters.AddWithValue("@id",second);
            duplicate.Parameters.AddWithValue("@key",key);
            var error=await Assert.ThrowsAsync<SqlException>(
                ()=>duplicate.ExecuteNonQueryAsync());
            Assert.True(error.Number is 2601 or 2627);
            await tx.RollbackAsync();
        }
        Assert.Equal("Rejected",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",first)));
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",second)));
        Assert.Equal(before,(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",second)))!);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE DecisionKey=@key",
            ("@key",key))));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",second))));
    }

    [Fact]
    public async Task Concurrent_submission_transactions_commit_exactly_one_submitted_event()
    {
        var entity="ATOMIC_RACE_"+Guid.NewGuid().ToString("N");
        async Task<(bool ok,int sqlError)> Submit()
        {
            await using var conn=await _fixture.ConnectAsync();
            await using var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                await using var create=new SqlCommand("""
                    INSERT INTO dbo.ChangeRequests(RequestPublicId,OrganizationId,
                        TeamId,EntityKind,EntityId,OperationCode,RiskCode,
                        ExpectedEntityRowVersion,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                    VALUES(@guid,1,7,N'Location',@entity,N'UpdatePublishedLocation',
                        N'High',0x0102030405060708,N'{}',10,SYSUTCDATETIME(),N'Pending');
                    SELECT CAST(SCOPE_IDENTITY() AS bigint)
                    """,conn,tx);
                create.Parameters.AddWithValue("@guid",Guid.NewGuid());
                create.Parameters.AddWithValue("@entity",entity);
                var id=Convert.ToInt64(await create.ExecuteScalarAsync());
                await using var audit=new SqlCommand("""
                    INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                        ActorUserId,OccurredAt,CorrelationId,DetailsJson)
                    VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),N'{}')
                    """,conn,tx);
                audit.Parameters.AddWithValue("@id",id);
                Assert.Equal(1,await audit.ExecuteNonQueryAsync());
                await tx.CommitAsync();
                return(true,0);
            }
            catch(SqlException ex)
            {
                await tx.RollbackAsync();
                return(false,ex.Number);
            }
        }
        var results=await Task.WhenAll(Task.Run(Submit),Task.Run(Submit));
        Assert.Single(results.Where(x=>x.ok));
        Assert.Contains(results,x=>!x.ok && x.sqlError is 2601 or 2627);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",entity))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
            WHERE r.EntityId=@entity AND e.EventType=N'Submitted'
            """,("@entity",entity))));
    }

    [Fact]
    public async Task Catalog_denies_altered_decision_filter_until_exact_index_is_restored()
    {
        const string index="UX_B3_ChangeRequestEvents_DecisionKey";
        await _fixture.ExecuteAsync($"DROP INDEX [{index}] ON dbo.ChangeRequestEvents");
        try
        {
            await _fixture.ExecuteAsync($"""
                CREATE UNIQUE INDEX [{index}]
                ON dbo.ChangeRequestEvents(DecisionKey)
                WHERE DecisionKey IS NOT NULL AND EventType = N'Rejected'
                """);
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync($"DROP INDEX IF EXISTS [{index}] ON dbo.ChangeRequestEvents");
            await _fixture.ExecuteAsync($"""
                CREATE UNIQUE INDEX [{index}]
                ON dbo.ChangeRequestEvents(DecisionKey)
                WHERE [DecisionKey] IS NOT NULL
                """);
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Catalog_denies_column_width_drift_until_exact_type_is_restored()
    {
        await _fixture.ExecuteAsync(
            "ALTER TABLE dbo.ChangeRequests ALTER COLUMN EvidenceJson nvarchar(4000) NULL");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync(
                "ALTER TABLE dbo.ChangeRequests ALTER COLUMN EvidenceJson nvarchar(max) NULL");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

}
