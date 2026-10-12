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
    public async Task Ef_composable_request_update_lock_executes_on_real_sql_server()
    {
        // This exercises the same FromSqlInterpolated + SingleOrDefault
        // shape used by the WORK-only Reject and approval-preview paths.
        // It is NOT an approval executor or a UAT database test.
        var publicId=Guid.NewGuid();
        var entity="LOCK_"+Guid.NewGuid().ToString("N");
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequests(
                RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,
                OperationCode,RiskCode,ExpectedEntityRowVersion,ProposedJson,
                RequestedByUserId,SubmittedAt,Status)
            VALUES(@guid,1,7,N'Location',@entity,N'UpdatePublishedLocation',
                N'High',0x0102030405060708,N'{"Name":"temporary"}',
                10,SYSUTCDATETIME(),N'Pending')
            """,("@guid",publicId),("@entity",entity));
        await using var db=new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(_fixture.DatabaseConnection).Options);
        await using var tx=await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var row=await db.ChangeRequests.FromSqlInterpolated($"""
            SELECT * FROM dbo.ChangeRequests WITH (UPDLOCK, HOLDLOCK)
            WHERE RequestPublicId = {publicId} AND OrganizationId = {1}
            """).SingleOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(publicId,row.RequestPublicId);
        Assert.Equal("Pending",row.Status);
        var foreign=await db.ChangeRequests.FromSqlInterpolated($"""
            SELECT * FROM dbo.ChangeRequests WITH (UPDLOCK, HOLDLOCK)
            WHERE RequestPublicId = {publicId} AND OrganizationId = {2}
            """).AsNoTracking().SingleOrDefaultAsync();
        Assert.Null(foreign);
        await tx.RollbackAsync();
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


    [Fact]
    public async Task Failed_submitted_event_fk_rolls_back_new_pending_without_orphan_history()
    {
        var entity="SUBMITTED_FK_ROLLBACK_"+Guid.NewGuid().ToString("N");
        var existing=await _fixture.InsertRequestAsync();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DetailsJson)
            VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),N'{}')
            """,("@id",existing));
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
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
            var created=Convert.ToInt64(await create.ExecuteScalarAsync());
            await using var badEvent=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DetailsJson)
                VALUES(@id,N'Submitted',999999,SYSUTCDATETIME(),NEWID(),N'{}')
                """,conn,tx);
            badEvent.Parameters.AddWithValue("@id",created);
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(
                ()=>badEvent.ExecuteNonQueryAsync())).Number);
            await tx.RollbackAsync();
        }
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",entity))));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON e.ChangeRequestId=r.ChangeRequestId
            WHERE r.EntityId=@entity
            """,("@entity",entity))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",existing))));
    }

    [Fact]
    public async Task Rejected_history_blocks_delete_after_new_pending_is_created()
    {
        var entity="HISTORY_FK_"+Guid.NewGuid().ToString("N");
        var old=await _fixture.InsertRequestAsync(entity);
        var key=Guid.NewGuid();
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'Independent review'
                WHERE ChangeRequestId=@id AND Status=N'Pending'
                """,conn,tx);
            update.Parameters.AddWithValue("@id",old);
            Assert.Equal(1,await update.ExecuteNonQueryAsync());
            await using var audit=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            audit.Parameters.AddWithValue("@id",old);
            audit.Parameters.AddWithValue("@key",key);
            Assert.Equal(1,await audit.ExecuteNonQueryAsync());
            await tx.CommitAsync();
        }
        var fresh=await _fixture.InsertRequestAsync(entity);
        var ex=await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync("DELETE FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
                ("@id",old)));
        Assert.Equal(547,ex.Number);
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",entity))));
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",fresh)));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND DecisionKey=@key",
            ("@id",old),("@key",key))));
    }

    [Fact]
    public async Task Catalog_denies_reordered_pending_unique_index_keys_until_restored()
    {
        const string idx="UX_B3_ChangeRequests_Org_Entity_Pending";
        await _fixture.ExecuteAsync($"DROP INDEX [{idx}] ON dbo.ChangeRequests");
        try
        {
            await _fixture.ExecuteAsync($"""
                CREATE UNIQUE INDEX [{idx}] ON dbo.ChangeRequests(
                    EntityKind,OrganizationId,EntityId) WHERE [Status]=N'Pending'
                """);
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync($"DROP INDEX IF EXISTS [{idx}] ON dbo.ChangeRequests");
            await _fixture.ExecuteAsync($"""
                CREATE UNIQUE INDEX [{idx}] ON dbo.ChangeRequests(
                    OrganizationId,EntityKind,EntityId) WHERE [Status]=N'Pending'
                """);
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Catalog_denies_missing_event_table_without_attempted_schema_repair()
    {
        await _fixture.ExecuteAsync(
            "EXEC sp_rename N'dbo.ChangeRequestEvents', N'ChangeRequestEvents_IsolatedProbe'");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM sys.tables WHERE name=N'ChangeRequestEvents'")));
            Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM sys.tables WHERE name=N'ChangeRequestEvents_IsolatedProbe'")));
        }
        finally
        {
            await _fixture.ExecuteAsync(
                "EXEC sp_rename N'dbo.ChangeRequestEvents_IsolatedProbe', N'ChangeRequestEvents'");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }


    [Fact]
    public async Task Catalog_denies_disabled_decision_unique_index_until_rebuilt()
    {
        const string idx="UX_B3_ChangeRequestEvents_DecisionKey";
        await _fixture.ExecuteAsync($"ALTER INDEX [{idx}] ON dbo.ChangeRequestEvents DISABLE");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync($"ALTER INDEX [{idx}] ON dbo.ChangeRequestEvents REBUILD");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Catalog_denies_enabled_event_trigger_until_removed()
    {
        await _fixture.ExecuteAsync("""
            CREATE TRIGGER dbo.B3_Fixture_EventTrigger
            ON dbo.ChangeRequestEvents AFTER INSERT
            AS BEGIN SET NOCOUNT ON; END
            """);
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync("DROP TRIGGER dbo.B3_Fixture_EventTrigger");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Catalog_denies_untrusted_event_decision_check_until_retrusted()
    {
        const string check="CK_B3_ChangeRequestEvents_DecisionState";
        await _fixture.ExecuteAsync(
            $"ALTER TABLE dbo.ChangeRequestEvents NOCHECK CONSTRAINT [{check}]");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync(
                $"ALTER TABLE dbo.ChangeRequestEvents WITH CHECK CHECK CONSTRAINT [{check}]");
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Rolled_back_submission_can_retry_with_one_committed_audit()
    {
        var entity="RETRY_PENDING_"+Guid.NewGuid().ToString("N");
        async Task<long> Create(SqlConnection conn,SqlTransaction tx)
        {
            await using var cmd=new SqlCommand("""
                INSERT INTO dbo.ChangeRequests(RequestPublicId,OrganizationId,
                    TeamId,EntityKind,EntityId,OperationCode,RiskCode,
                    ExpectedEntityRowVersion,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                VALUES(@guid,1,7,N'Location',@entity,N'UpdatePublishedLocation',
                    N'High',0x0102030405060708,N'{}',10,SYSUTCDATETIME(),N'Pending');
                SELECT CAST(SCOPE_IDENTITY() AS bigint)
                """,conn,tx);
            cmd.Parameters.AddWithValue("@guid",Guid.NewGuid());
            cmd.Parameters.AddWithValue("@entity",entity);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var id=await Create(conn,tx);
            await using var failed=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DetailsJson)
                VALUES(@id,N'Submitted',999999,SYSUTCDATETIME(),NEWID(),N'{}')
                """,conn,tx);
            failed.Parameters.AddWithValue("@id",id);
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(
                ()=>failed.ExecuteNonQueryAsync())).Number);
            await tx.RollbackAsync();
        }
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",entity))));
        await using(var conn=await _fixture.ConnectAsync())
        await using(var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var id=await Create(conn,tx);
            await using var valid=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DetailsJson)
                VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),N'{}')
                """,conn,tx);
            valid.Parameters.AddWithValue("@id",id);
            Assert.Equal(1,await valid.ExecuteNonQueryAsync());
            await tx.CommitAsync();
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity AND Status=N'Pending'",
            ("@entity",entity))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON e.ChangeRequestId=r.ChangeRequestId
            WHERE r.EntityId=@entity AND e.EventType=N'Submitted'
            """,("@entity",entity))));
    }

    [Fact]
    public async Task Stale_reviewer_rowversion_cannot_create_decision_audit()
    {
        var id=await _fixture.InsertRequestAsync();
        var old=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        Assert.Equal(1,await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET BeforeJson=N'{}' WHERE ChangeRequestId=@id",
            ("@id",id)));
        var current=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        Assert.NotEqual(Convert.ToHexString(old),Convert.ToHexString(current));
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
            update.Parameters.AddWithValue("@rv",old);
            Assert.Equal(0,await update.ExecuteNonQueryAsync());
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(current,(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!);
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Catalog_denies_missing_request_public_unique_index_until_restored()
    {
        const string idx="UX_B3_ChangeRequests_RequestPublicId";
        await _fixture.ExecuteAsync($"DROP INDEX [{idx}] ON dbo.ChangeRequests");
        try
        {
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.CatalogCheckSql)));
        }
        finally
        {
            await _fixture.ExecuteAsync($"""
                CREATE UNIQUE INDEX [{idx}] ON dbo.ChangeRequests(RequestPublicId)
                """);
        }
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Cross_organization_pending_submission_preserves_separate_audit_histories()
    {
        var entity="ORG_AUDIT_"+Guid.NewGuid().ToString("N");
        var first=await _fixture.InsertRequestAsync(entity,org:1);
        var second=await _fixture.InsertRequestAsync(entity,org:2);
        foreach(var id in new[] {first,second})
            await _fixture.ExecuteAsync("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DetailsJson)
                VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),N'{}')
                """,("@id",id));
        var duplicate=await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.InsertRequestAsync(entity,org:1));
        Assert.True(duplicate.Number is 2601 or 2627);
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity AND Status=N'Pending'",
            ("@entity",entity))));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON e.ChangeRequestId=r.ChangeRequestId
            WHERE r.EntityId=@entity AND e.EventType=N'Submitted'
            """,("@entity",entity))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
            JOIN dbo.ChangeRequests r ON e.ChangeRequestId=r.ChangeRequestId
            WHERE r.EntityId=@entity AND r.OrganizationId=2
            """,("@entity",entity))));
        // Partitioned indexes are not proof of API authorization or tenant isolation.
    }


    [Fact]
    public async Task Rejected_request_cannot_reopen_pending_when_newer_pending_exists()
    {
        var entity="REOPEN_CONFLICT_"+Guid.NewGuid().ToString("N");
        var old=await _fixture.InsertRequestAsync(entity);
        await _fixture.ExecuteAsync("""
            UPDATE dbo.ChangeRequests SET Status=N'Rejected',ReviewedByUserId=20,
                ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'independent review'
            WHERE ChangeRequestId=@id
            """,("@id",old));
        var decision=Guid.NewGuid();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
            """,("@id",old),("@key",decision));
        var current=await _fixture.InsertRequestAsync(entity);
        var error=await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync("""
                UPDATE dbo.ChangeRequests SET Status=N'Pending',ReviewedByUserId=NULL,
                    ReviewedAt=NULL,ReviewReason=NULL
                WHERE ChangeRequestId=@id
                """,("@id",old)));
        Assert.True(error.Number is 2601 or 2627);
        Assert.Equal("Rejected",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",old)));
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",current)));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND DecisionKey=@key",
            ("@id",old),("@key",decision))));
    }

    [Fact]
    public async Task Invalid_organization_or_team_fk_never_persists_change_request()
    {
        var entity="INVALID_FK_"+Guid.NewGuid().ToString("N");
        foreach(var (org,team) in new[] {(999,7),(1,999)})
        {
            var error=await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync("""
                    INSERT INTO dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                        EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                        ProposedJson,RequestedByUserId,SubmittedAt,Status)
                    VALUES(@key,@org,@team,N'Location',@entity,
                        N'UpdatePublishedLocation',N'High',0x0102030405060708,
                        N'{}',10,SYSUTCDATETIME(),N'Pending')
                    """,("@key",Guid.NewGuid()),("@org",org),("@team",team),("@entity",entity)));
            Assert.Equal(547,error.Number);
        }
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE EntityId=@entity",
            ("@entity",entity))));
    }

    [Fact]
    public async Task Invalid_request_operation_or_risk_codes_are_rejected_by_real_checks()
    {
        var id=await _fixture.InsertRequestAsync();
        foreach(var sql in new[]
        {
            "UPDATE dbo.ChangeRequests SET EntityKind=N'Project' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET OperationCode=N'DeleteLocation' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET RiskCode=N'Low' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET Status=N'Approved' WHERE ChangeRequestId=@id"
        })
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",id)))).Number);
        Assert.Equal("UpdatePublishedLocation",(string?)await _fixture.ScalarAsync(
            "SELECT OperationCode FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
    }

    [Fact]
    public async Task Pending_request_cannot_carry_reviewer_or_applied_fields()
    {
        var id=await _fixture.InsertRequestAsync();
        foreach(var sql in new[]
        {
            "UPDATE dbo.ChangeRequests SET ReviewedByUserId=20 WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET ReviewedAt=SYSUTCDATETIME() WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET ReviewReason=N'premature' WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET AppliedAt=SYSUTCDATETIME() WHERE ChangeRequestId=@id"
        })
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",id)))).Number);
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequests
            WHERE ChangeRequestId=@id AND
                (ReviewedByUserId IS NOT NULL OR ReviewedAt IS NOT NULL OR
                 ReviewReason IS NOT NULL OR AppliedAt IS NOT NULL)
            """,("@id",id))));
    }

    [Fact]
    public async Task Submitted_audit_cannot_have_decision_key_and_rejected_audit_requires_one()
    {
        var id=await _fixture.InsertRequestAsync();
        foreach(var sql in new[]
        {
            """
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Submitted',10,SYSUTCDATETIME(),NEWID(),NEWID(),N'{}')
            """,
            """
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DetailsJson)
            VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),N'{}')
            """
        })
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",id)))).Number);
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Malformed_audit_event_cannot_replace_committed_decision_history()
    {
        var id=await _fixture.InsertRequestAsync();
        var key=Guid.NewGuid();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{"Reason":"valid"}')
            """,("@id",id),("@key",key));
        var ex=await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync("""
                UPDATE dbo.ChangeRequestEvents SET DetailsJson=N'broken'
                WHERE ChangeRequestId=@id AND DecisionKey=@key
                """,("@id",id),("@key",key)));
        Assert.Equal(547,ex.Number);
        Assert.Equal("{\"Reason\":\"valid\"}",(string?)await _fixture.ScalarAsync(
            "SELECT DetailsJson FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND DecisionKey=@key",
            ("@id",id),("@key",key)));
    }

    [Fact]
    public async Task Decision_audit_fk_preserves_reviewer_and_event_history()
    {
        var id=await _fixture.InsertRequestAsync();
        await _fixture.ExecuteAsync("""
            UPDATE dbo.ChangeRequests SET Status=N'Rejected',ReviewedByUserId=30,
                ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'valid'
            WHERE ChangeRequestId=@id
            """,("@id",id));
        var key=Guid.NewGuid();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Rejected',30,SYSUTCDATETIME(),NEWID(),@key,N'{}')
            """,("@id",id),("@key",key));
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync("DELETE FROM dbo.Users WHERE UserId=30"))).Number);
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
            _fixture.ExecuteAsync("DELETE FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
                ("@id",id)))).Number);
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id AND DecisionKey=@key",
            ("@id",id),("@key",key))));
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_until_latest_version_is_restored()
    {
        await _fixture.ExecuteAsync(
            "INSERT INTO dbo.SchemaVersions VALUES (N'1.8.0-012', DATEADD(day,2,SYSUTCDATETIME()))");
        try
        {
            var latest=(string?)await _fixture.ScalarAsync(
                V180B3SqlSafetyRules.LatestSchemaVersionSql);
            Assert.Equal("1.8.0-012",latest);
            Assert.Equal("B3_SCHEMA_NOT_VERIFIED",
                Assert.Throws<InvalidOperationException>(()=>
                    V180B3SqlSafetyRules.RequireLatestSchemaVersion(latest)).Message);
        }
        finally
        {
            await _fixture.ExecuteAsync(
                "DELETE FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-012'");
        }
        V180B3SqlSafetyRules.RequireLatestSchemaVersion(
            (string?)await _fixture.ScalarAsync(V180B3SqlSafetyRules.LatestSchemaVersionSql));
    }

    [Fact]
    public async Task Request_with_invalid_expected_rowversion_bytes_is_denied_without_audit()
    {
        var id=await _fixture.InsertRequestAsync();
        var before=(byte[])(await _fixture.ScalarAsync(
            "SELECT ExpectedEntityRowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        foreach(var sql in new[]
        {
            "UPDATE dbo.ChangeRequests SET ExpectedEntityRowVersion=0x010203 WHERE ChangeRequestId=@id",
            "UPDATE dbo.ChangeRequests SET ExpectedEntityRowVersion=NULL WHERE ChangeRequestId=@id"
        })
            Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",id)))).Number);
        Assert.Equal(before,(byte[])(await _fixture.ScalarAsync(
            "SELECT ExpectedEntityRowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!);
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }


    // EF's actual SQL Server query and concurrency execution, NOT HTTP E2E.
    private AppDbContext QueryDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.DatabaseConnection).Options);

    private static FieldVisit.Application.CurrentUserDto QueueActor(
        int userId=10,int? organizationId=1) =>
        new(userId,"B3SqlFixture","Isolated test",null,organizationId,
            7,"Fixture team",new[]{"visitor"},
            new[]{new FieldVisit.Application.TeamScopeDto(7,"Fixture team",true)});

    [Fact]
    public async Task Ef_SQL_Mine_query_hides_peer_and_cross_organization_proposals()
    {
        var prefix="SQL_SCOPE_MINE_"+Guid.NewGuid().ToString("N");
        var mine=await _fixture.InsertRequestAsync(prefix+"_mine");
        var peer=await _fixture.InsertRequestAsync(prefix+"_peer");
        var otherOrg=await _fixture.InsertRequestAsync(prefix+"_other",org:2);
        await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET RequestedByUserId=20 WHERE ChangeRequestId=@id",
            ("@id",peer));
        foreach(var (id,content) in new[]{
            (mine,"MINE_ALLOWED"),(peer,"PEER_FORBIDDEN"),(otherOrg,"ORG_FORBIDDEN")})
            await _fixture.ExecuteAsync(
                "UPDATE dbo.ChangeRequests SET ProposedJson=@json WHERE ChangeRequestId=@id",
                ("@json","{\"Name\":\""+content+"\"}"),("@id",id));
        await using var db=QueryDb();
        var results=await V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ProposedJson).ToArrayAsync();
        Assert.Single(results);
        Assert.Contains("MINE_ALLOWED",results[0]);
        Assert.DoesNotContain("PEER_FORBIDDEN",string.Join("|",results));
        Assert.DoesNotContain("ORG_FORBIDDEN",string.Join("|",results));
    }

    [Fact]
    public async Task Ef_SQL_Admin_pending_query_excludes_foreign_org_and_rejected()
    {
        var prefix="SQL_SCOPE_ADMIN_"+Guid.NewGuid().ToString("N");
        var local=await _fixture.InsertRequestAsync(prefix+"_local");
        var foreign=await _fixture.InsertRequestAsync(prefix+"_foreign",org:2);
        var rejected=await _fixture.InsertRequestAsync(prefix+"_rejected");
        await _fixture.ExecuteAsync("""
            UPDATE dbo.ChangeRequests
            SET Status=N'Rejected',ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),
                ReviewReason=N'independently rejected'
            WHERE ChangeRequestId=@id
            """,("@id",rejected));
        await using var db=QueryDb();
        var found=await V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ChangeRequestId).ToArrayAsync();
        Assert.Equal(new long[]{local},found);
        Assert.DoesNotContain(foreign,found);
        Assert.DoesNotContain(rejected,found);
        var other=await V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),QueueActor(organizationId:2))
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ChangeRequestId).ToArrayAsync();
        Assert.Equal(new long[]{foreign},other);
    }

    [Fact]
    public async Task Ef_SQL_Requester_refuses_unknown_identity_or_tenant_before_query()
    {
        await using var db=QueryDb();
        foreach(var actor in new[]{
            QueueActor(organizationId:null),
            QueueActor(organizationId:0),
            QueueActor(organizationId:-1),
            QueueActor(userId:0),
            QueueActor(userId:-1)})
        {
            Assert.Throws<UnauthorizedAccessException>(()=>
                V180B3QueueScopeRules.ForRequester(
                    db.ChangeRequests.AsNoTracking(),actor));
        }
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),QueueActor(organizationId:null)));
    }

    [Fact]
    public async Task Ef_SQL_Mine_query_does_not_mutate_rowversion_or_create_events()
    {
        var prefix="SQL_READ_ONLY_"+Guid.NewGuid().ToString("N");
        var id=await _fixture.InsertRequestAsync(prefix);
        var before=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        await using(var db=QueryDb())
        {
            var rows=await V180B3QueueScopeRules.ForRequester(
                    db.ChangeRequests.AsNoTracking(),QueueActor())
                .Where(x=>x.EntityId==prefix).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(id,rows[0].ChangeRequestId);
        }
        var after=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        Assert.Equal(before,after);
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Ef_SQL_stale_tracked_request_update_fails_concurrency_without_audit()
    {
        var id=await _fixture.InsertRequestAsync(
            "EF_CONCURRENCY_"+Guid.NewGuid().ToString("N"));
        await using var first=QueryDb();
        await using var stale=QueryDb();
        var a=await first.ChangeRequests.SingleAsync(x=>x.ChangeRequestId==id);
        var b=await stale.ChangeRequests.SingleAsync(x=>x.ChangeRequestId==id);
        a.BeforeJson="{\"Name\":\"first\"}";
        Assert.Equal(1,await first.SaveChangesAsync());
        b.BeforeJson="{\"Name\":\"stale\"}";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
        Assert.Equal("{\"Name\":\"first\"}",(string?)await _fixture.ScalarAsync(
            "SELECT BeforeJson FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }


    [Fact]
    public async Task Ef_SQL_rejected_history_visible_only_to_owner_not_admin_pending()
    {
        var prefix="SQL_REJECTED_OWNER_"+Guid.NewGuid().ToString("N");
        var mine=await _fixture.InsertRequestAsync(prefix+"_mine");
        var peer=await _fixture.InsertRequestAsync(prefix+"_peer");
        await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET RequestedByUserId=20 WHERE ChangeRequestId=@id",
            ("@id",peer));
        foreach(var id in new[]{mine,peer})
        {
            await _fixture.ExecuteAsync("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',ReviewedByUserId=30,
                    ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'independent decision'
                WHERE ChangeRequestId=@id
                """,("@id",id));
            await _fixture.ExecuteAsync("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',30,SYSUTCDATETIME(),NEWID(),NEWID(),N'{}')
                """,("@id",id));
        }
        await using var db=QueryDb();
        var own=await V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ChangeRequestId).ToArrayAsync();
        Assert.Equal(new long[]{mine},own);
        var pending=await V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ChangeRequestId).ToArrayAsync();
        Assert.Empty(pending);
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.ChangeRequestEvents
            WHERE ChangeRequestId IN (@mine,@peer) AND EventType=N'Rejected'
            """,("@mine",mine),("@peer",peer))));
    }

    [Fact]
    public async Task Ef_SQL_peer_and_cross_org_sensitive_before_evidence_json_not_disclosed()
    {
        var prefix="SQL_SENSITIVE_JSON_"+Guid.NewGuid().ToString("N");
        var mine=await _fixture.InsertRequestAsync(prefix+"_mine");
        var peer=await _fixture.InsertRequestAsync(prefix+"_peer");
        var foreign=await _fixture.InsertRequestAsync(prefix+"_foreign",org:2);
        await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET RequestedByUserId=20 WHERE ChangeRequestId=@id",
            ("@id",peer));
        foreach(var (id,content) in new[]{
            (mine,"SAFE_OWN"),(peer,"FORBIDDEN_PEER"),(foreign,"FORBIDDEN_FOREIGN")})
            await _fixture.ExecuteAsync("""
                UPDATE dbo.ChangeRequests
                SET BeforeJson=@before, EvidenceJson=@evidence
                WHERE ChangeRequestId=@id
                """,("@before","{\"Name\":\""+content+"\"}"),
                ("@evidence","{\"Evidence\":\""+content+"\"}"),("@id",id));
        await using var db=QueryDb();
        var views=await V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>new{x.BeforeJson,x.EvidenceJson}).ToArrayAsync();
        Assert.Single(views);
        Assert.Contains("SAFE_OWN",views[0].BeforeJson);
        Assert.Contains("SAFE_OWN",views[0].EvidenceJson);
        Assert.DoesNotContain("FORBIDDEN_PEER",
            string.Join("|",views.Select(x=>x.BeforeJson+x.EvidenceJson)));
        Assert.DoesNotContain("FORBIDDEN_FOREIGN",
            string.Join("|",views.Select(x=>x.BeforeJson+x.EvidenceJson)));
    }

    [Fact]
    public async Task Ef_SQL_switching_actor_org_between_queries_never_reuses_previous_tenant_scope()
    {
        var prefix="SQL_ORG_SWITCH_"+Guid.NewGuid().ToString("N");
        var org1=await _fixture.InsertRequestAsync(prefix+"_org1",org:1);
        var org2=await _fixture.InsertRequestAsync(prefix+"_org2",org:2);
        await using var db=QueryDb();
        foreach(var org in new[]{1,2,1,2})
        {
            var ids=await V180B3QueueScopeRules.ForRequester(
                    db.ChangeRequests.AsNoTracking(),QueueActor(organizationId:org))
                .Where(x=>x.EntityId.StartsWith(prefix))
                .Select(x=>x.ChangeRequestId).ToArrayAsync();
            Assert.Equal(new long[]{org==1?org1:org2},ids);
        }
    }

    [Fact]
    public async Task Ef_SQL_admin_org_queue_includes_peer_but_requester_mine_does_not()
    {
        var prefix="SQL_ADMIN_VS_OWNER_"+Guid.NewGuid().ToString("N");
        var mine=await _fixture.InsertRequestAsync(prefix+"_mine");
        var peer=await _fixture.InsertRequestAsync(prefix+"_peer");
        var foreign=await _fixture.InsertRequestAsync(prefix+"_foreign",org:2);
        await _fixture.ExecuteAsync(
            "UPDATE dbo.ChangeRequests SET RequestedByUserId=20 WHERE ChangeRequestId=@id",
            ("@id",peer));
        await using var db=QueryDb();
        var own=await V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ChangeRequestId).OrderBy(x=>x).ToArrayAsync();
        var admin=await V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),QueueActor())
            .Where(x=>x.EntityId.StartsWith(prefix))
            .Select(x=>x.ChangeRequestId).OrderBy(x=>x).ToArrayAsync();
        Assert.Equal(new long[]{mine},own);
        Assert.Equal(new long[]{mine,peer},admin);
        Assert.DoesNotContain(foreign,admin);
    }

    [Fact]
    public async Task Sql_transaction_rejected_event_fk_failure_rolls_back_request_decision()
    {
        var id=await _fixture.InsertRequestAsync(
            "ROLLBACK_EVENT_FK_"+Guid.NewGuid().ToString("N"));
        var original=(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!;
        await using(var conn=await _fixture.ConnectAsync())
        {
            using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
            await using(var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'fixture rollback'
                WHERE ChangeRequestId=@id
                """,conn,tx))
            {
                update.Parameters.AddWithValue("@id",id);
                Assert.Equal(1,await update.ExecuteNonQueryAsync());
            }
            await using var broken=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(
                    ChangeRequestId,EventType,ActorUserId,OccurredAt,
                    CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',999999,SYSUTCDATETIME(),
                    NEWID(),NEWID(),N'{}')
                """,conn,tx);
            broken.Parameters.AddWithValue("@id",id);
            await Assert.ThrowsAsync<SqlException>(()=>broken.ExecuteNonQueryAsync());
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(original,(byte[])(await _fixture.ScalarAsync(
            "SELECT RowVersion FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)))!);
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Sql_transaction_duplicate_decision_key_rolls_back_second_request()
    {
        var first=await _fixture.InsertRequestAsync(
            "DECISION_TX_FIRST_"+Guid.NewGuid().ToString("N"));
        var second=await _fixture.InsertRequestAsync(
            "DECISION_TX_SECOND_"+Guid.NewGuid().ToString("N"));
        var key=Guid.NewGuid();
        await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
            """,("@id",first),("@key",key));
        await using(var conn=await _fixture.ConnectAsync())
        {
            using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
            await using(var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'duplicate key'
                WHERE ChangeRequestId=@id
                """,conn,tx))
            {
                update.Parameters.AddWithValue("@id",second);
                Assert.Equal(1,await update.ExecuteNonQueryAsync());
            }
            await using var duplicate=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                    ActorUserId,OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            duplicate.Parameters.AddWithValue("@id",second);
            duplicate.Parameters.AddWithValue("@key",key);
            var ex=await Assert.ThrowsAsync<SqlException>(
                ()=>duplicate.ExecuteNonQueryAsync());
            Assert.True(ex.Number is 2601 or 2627);
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",second)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",second))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE DecisionKey=@key",
            ("@key",key))));
    }

    [Fact]
    public async Task Sql_transaction_check_constraint_failure_rolls_back_decision_and_audit()
    {
        var id=await _fixture.InsertRequestAsync(
            "ROLLBACK_CHECK_"+Guid.NewGuid().ToString("N"));
        await using(var conn=await _fixture.ConnectAsync())
        {
            using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
            await using(var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),
                    ReviewReason=N'rollback check'
                WHERE ChangeRequestId=@id
                """,conn,tx))
            {
                update.Parameters.AddWithValue("@id",id);
                Assert.Equal(1,await update.ExecuteNonQueryAsync());
            }
            await using var invalid=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(
                    ChangeRequestId,EventType,ActorUserId,OccurredAt,
                    CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'UnapprovedEvent',20,SYSUTCDATETIME(),
                    NEWID(),NEWID(),N'{}')
                """,conn,tx);
            invalid.Parameters.AddWithValue("@id",id);
            var error=await Assert.ThrowsAsync<SqlException>(
                ()=>invalid.ExecuteNonQueryAsync());
            Assert.Equal(547,error.Number);
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Sql_rolled_back_decision_key_is_reusable_without_phantom_event()
    {
        var id=await _fixture.InsertRequestAsync(
            "ROLLBACK_KEY_"+Guid.NewGuid().ToString("N"));
        var key=Guid.NewGuid();
        await using(var conn=await _fixture.ConnectAsync())
        {
            using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
            await using var insert=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(
                    ChangeRequestId,EventType,ActorUserId,OccurredAt,
                    CorrelationId,DecisionKey,DetailsJson)
                VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
                """,conn,tx);
            insert.Parameters.AddWithValue("@id",id);
            insert.Parameters.AddWithValue("@key",key);
            Assert.Equal(1,await insert.ExecuteNonQueryAsync());
            await tx.RollbackAsync();
        }
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE DecisionKey=@key",
            ("@key",key))));
        Assert.Equal(1,await _fixture.ExecuteAsync("""
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,
                CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),NEWID(),@key,N'{}')
            """,("@id",id),("@key",key)));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE DecisionKey=@key",
            ("@key",key))));
    }

    [Fact]
    public async Task Serializable_update_lock_blocks_competing_request_reader_until_release()
    {
        // Proves UPDLOCK/HOLDLOCK serializes a second reviewer on the same
        // Pending request in disposable SQL Server; no approval/apply occurs.
        var id=await _fixture.InsertRequestAsync(
            "LOCK_CONTENTION_"+Guid.NewGuid().ToString("N"));
        await using var first=await _fixture.ConnectAsync();
        await using var second=await _fixture.ConnectAsync();
        using var tx=first.BeginTransaction(IsolationLevel.Serializable);
        await using(var lockRow=new SqlCommand("""
            SELECT ChangeRequestId FROM dbo.ChangeRequests WITH (UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@id
            """,first,tx))
        {
            lockRow.Parameters.AddWithValue("@id",id);
            Assert.Equal(id,Convert.ToInt64(await lockRow.ExecuteScalarAsync()));
        }
        await using(var competing=new SqlCommand("""
            SET LOCK_TIMEOUT 500;
            SELECT ChangeRequestId FROM dbo.ChangeRequests WITH (UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@id
            """,second))
        {
            competing.Parameters.AddWithValue("@id",id);
            competing.CommandTimeout=5;
            var timeout=await Assert.ThrowsAsync<SqlException>(
                async ()=>await competing.ExecuteScalarAsync());
            Assert.Equal(1222,timeout.Number);
        }
        await tx.RollbackAsync();
        await using(var released=new SqlCommand("""
            SET LOCK_TIMEOUT 3000;
            SELECT ChangeRequestId FROM dbo.ChangeRequests WITH (UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@id
            """,second))
        {
            released.Parameters.AddWithValue("@id",id);
            Assert.Equal(id,Convert.ToInt64(await released.ExecuteScalarAsync()));
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
    }

    [Fact]
    public async Task Serializable_request_lock_does_not_block_unrelated_pending_request()
    {
        // Locks must serialize competing reviewers of ONE request without
        // unintentionally preventing review of a different Pending request.
        var firstId=await _fixture.InsertRequestAsync(
            "LOCK_SCOPE_A_"+Guid.NewGuid().ToString("N"));
        var otherId=await _fixture.InsertRequestAsync(
            "LOCK_SCOPE_B_"+Guid.NewGuid().ToString("N"));
        await using var first=await _fixture.ConnectAsync();
        await using var second=await _fixture.ConnectAsync();
        using var tx=first.BeginTransaction(IsolationLevel.Serializable);
        await using(var held=new SqlCommand("""
            SELECT ChangeRequestId FROM dbo.ChangeRequests WITH (UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@id
            """,first,tx))
        {
            held.Parameters.AddWithValue("@id",firstId);
            Assert.Equal(firstId,Convert.ToInt64(await held.ExecuteScalarAsync()));
        }
        await using(var unrelated=new SqlCommand("""
            SET LOCK_TIMEOUT 2000;
            SELECT ChangeRequestId FROM dbo.ChangeRequests WITH (UPDLOCK,HOLDLOCK)
            WHERE ChangeRequestId=@id
            """,second))
        {
            unrelated.CommandTimeout=5;
            unrelated.Parameters.AddWithValue("@id",otherId);
            Assert.Equal(otherId,Convert.ToInt64(await unrelated.ExecuteScalarAsync()));
        }
        await tx.RollbackAsync();
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",firstId)));
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",otherId)));
    }

    [Fact]
    public async Task Serializable_rejection_transaction_rolls_back_request_and_audit_on_injected_failure()
    {
        var id=await _fixture.InsertRequestAsync(
            "ROLLBACK_REJECT_"+Guid.NewGuid().ToString("N"));
        var correlation=Guid.NewGuid();
        await using(var conn=await _fixture.ConnectAsync())
        {
            using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                await using(var update=new SqlCommand("""
                    UPDATE dbo.ChangeRequests
                    SET Status=N'Rejected', ReviewedByUserId=20,
                        ReviewedAt=SYSUTCDATETIME(), ReviewReason=N'Fixture only'
                    WHERE ChangeRequestId=@id AND Status=N'Pending'
                    """,conn,tx))
                {
                    update.Parameters.AddWithValue("@id",id);
                    Assert.Equal(1,await update.ExecuteNonQueryAsync());
                }
                await using(var audit=new SqlCommand("""
                    INSERT INTO dbo.ChangeRequestEvents(
                        ChangeRequestId,EventType,ActorUserId,OccurredAt,
                        CorrelationId,DecisionKey)
                    VALUES(@id,N'Rejected',20,SYSUTCDATETIME(),@correlation,@decision)
                    """,conn,tx))
                {
                    audit.Parameters.AddWithValue("@id",id);
                    audit.Parameters.AddWithValue("@correlation",correlation);
                    audit.Parameters.AddWithValue("@decision",Guid.NewGuid());
                    Assert.Equal(1,await audit.ExecuteNonQueryAsync());
                }
                throw new InvalidOperationException("B3_ISOLATED_INJECTED_FAILURE");
            }
            catch(InvalidOperationException ex) when(
                ex.Message=="B3_ISOLATED_INJECTED_FAILURE")
            {
                await tx.RollbackAsync();
            }
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE ChangeRequestId=@id AND ReviewedAt IS NOT NULL",
            ("@id",id))));
    }

    [Fact]
    public async Task Serializable_rejection_audit_insert_error_rolls_back_request_update()
    {
        var id=await _fixture.InsertRequestAsync(
            "ROLLBACK_FK_"+Guid.NewGuid().ToString("N"));
        await using(var conn=await _fixture.ConnectAsync())
        {
            using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
            await using(var update=new SqlCommand("""
                UPDATE dbo.ChangeRequests
                SET Status=N'Rejected', ReviewedByUserId=20,
                    ReviewedAt=SYSUTCDATETIME(), ReviewReason=N'Fixture only'
                WHERE ChangeRequestId=@id AND Status=N'Pending'
                """,conn,tx))
            {
                update.Parameters.AddWithValue("@id",id);
                Assert.Equal(1,await update.ExecuteNonQueryAsync());
            }
            await using(var audit=new SqlCommand("""
                INSERT INTO dbo.ChangeRequestEvents(
                    ChangeRequestId,EventType,ActorUserId,OccurredAt,
                    CorrelationId,DecisionKey)
                VALUES(@id,N'Rejected',999999,SYSUTCDATETIME(),@correlation,@decision)
                """,conn,tx))
            {
                audit.Parameters.AddWithValue("@id",id);
                audit.Parameters.AddWithValue("@correlation",Guid.NewGuid());
                audit.Parameters.AddWithValue("@decision",Guid.NewGuid());
                await Assert.ThrowsAsync<SqlException>(()=>audit.ExecuteNonQueryAsync());
            }
            await tx.RollbackAsync();
        }
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=@id",
            ("@id",id)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
            ("@id",id))));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE ChangeRequestId=@id AND ReviewedByUserId IS NOT NULL",
            ("@id",id))));
    }

    // Five real SQL tests on separate disposable B3Probe* objects. They are
    // NOT an executable 1800_012 schema/migration or live B3 Approve endpoint.
    [Fact]
    public async Task Hypothetical_atomic_probe_commits_two_updates_and_correlated_events()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        var key=Guid.NewGuid();
        await probe.ApplyProbeAsync(row,key);
        Assert.Equal("Applied",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
            ("@id",row.RequestId)));
        Assert.Equal("Applied in disposable probe",(string?)await _fixture.ScalarAsync(
            "SELECT LocationName FROM dbo.B3ProbeSources WHERE SourceId=@id",
            ("@id",row.SourceId)));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(DISTINCT CorrelationId) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id AND EventType=N'Approved' AND DecisionKey=@key",
            ("@id",row.RequestId),("@key",key))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id AND EventType=N'Applied' AND DecisionKey IS NULL",
            ("@id",row.RequestId))));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            probe.ApplyProbeAsync(row,Guid.NewGuid()));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_rolls_back_all_four_postwrite_fault_points()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        foreach(var fault in new[]{"after-source","after-request","after-approved","after-applied"})
        {
            var row=await probe.SeedPendingAsync();
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                probe.ApplyProbeAsync(row,Guid.NewGuid(),fault));
            Assert.Equal("B3_PROBE_INJECTED_FAILURE",error.Message);
            Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
                "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)));
            Assert.Equal("Original fixture location",(string?)await _fixture.ScalarAsync(
                "SELECT LocationName FROM dbo.B3ProbeSources WHERE SourceId=@id",
                ("@id",row.SourceId)));
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
                ("@id",row.RequestId))));
            Assert.Equal(row.SourceRowVersion,(byte[])(await _fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeSources WHERE SourceId=@id",
                ("@id",row.SourceId)))!);
            Assert.Equal(row.RequestRowVersion,(byte[])(await _fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)))!);
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.B3ProbeRequests WHERE RequestId=@id AND (ReviewedByUserId IS NOT NULL OR AppliedAt IS NOT NULL)",
                ("@id",row.RequestId))));
        }
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_duplicate_decision_key_rolls_back_second_apply()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var first=await probe.SeedPendingAsync();
        var second=await probe.SeedPendingAsync();
        var key=Guid.NewGuid();
        await probe.ApplyProbeAsync(first,key);
        await Assert.ThrowsAsync<SqlException>(()=>
            probe.ApplyProbeAsync(second,key));
        Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
            ("@id",second.RequestId)));
        Assert.Equal("Original fixture location",(string?)await _fixture.ScalarAsync(
            "SELECT LocationName FROM dbo.B3ProbeSources WHERE SourceId=@id",
            ("@id",second.SourceId)));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",second.RequestId))));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",first.RequestId))));
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_stale_request_and_source_versions_fail_closed()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var sourceChanged=await probe.SeedPendingAsync();
        await _fixture.ExecuteAsync(
            "UPDATE dbo.B3ProbeSources SET LocationName=N'Outside edit' WHERE SourceId=@id",
            ("@id",sourceChanged.SourceId));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            probe.ApplyProbeAsync(sourceChanged,Guid.NewGuid()));
        Assert.Equal("Outside edit",(string?)await _fixture.ScalarAsync(
            "SELECT LocationName FROM dbo.B3ProbeSources WHERE SourceId=@id",
            ("@id",sourceChanged.SourceId)));
        var requestChanged=await probe.SeedPendingAsync();
        await _fixture.ExecuteAsync(
            "UPDATE dbo.B3ProbeRequests SET RequestMemo=N'Outside edit' WHERE RequestId=@id",
            ("@id",requestChanged.RequestId));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            probe.ApplyProbeAsync(requestChanged,Guid.NewGuid()));
        foreach(var row in new[]{sourceChanged,requestChanged})
        {
            Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
                "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)));
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
                ("@id",row.RequestId))));
        }
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_concurrent_reviewers_commit_at_most_once()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        async Task<bool> Attempt(Guid key)
        {
            try { await probe.ApplyProbeAsync(row,key); return true; }
            catch(InvalidOperationException ex) when(
                ex.Message=="B3_PROBE_STALE_REQUEST") { return false; }
        }
        var outcomes=await Task.WhenAll(
            Attempt(Guid.NewGuid()),Attempt(Guid.NewGuid()));
        Assert.Equal(1,outcomes.Count(x=>x));
        Assert.Equal("Applied",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
            ("@id",row.RequestId)));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_seven_fields_persist_and_scope_columns_remain_unchanged()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        Assert.Equal(B3ProbeFields.Original,await probe.ReadFieldsAsync(row.SourceId));
        await probe.ApplyProbeAsync(row,Guid.NewGuid());
        Assert.Equal(B3ProbeFields.Proposed,await probe.ReadFieldsAsync(row.SourceId));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync("""
            SELECT COUNT(*) FROM dbo.B3ProbeSources
            WHERE SourceId=@id AND OrganizationId=1 AND TeamId=7
              AND CreatedByUserId=10 AND LocationType=N'Customer'
              AND ApprovalStatus=N'Approved' AND IsActive=1
            """,("@id",row.SourceId))));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_seven_fields_nullables_are_written_as_sql_null()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        var proposed=new V180B3LocationFields(
            "Changed name",null,null,null,null,null,null);
        await probe.ApplyProbeAsync(row,Guid.NewGuid(),proposedFields:proposed);
        Assert.Equal(proposed,await probe.ReadFieldsAsync(row.SourceId));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_seven_field_rollback_restores_data_and_versions()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        foreach(var fault in new[]{"after-source","after-request","after-approved","after-applied"})
        {
            var row=await probe.SeedPendingAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>
                probe.ApplyProbeAsync(row,Guid.NewGuid(),fault,
                    proposedFields:B3ProbeFields.Proposed));
            Assert.Equal(B3ProbeFields.Original,await probe.ReadFieldsAsync(row.SourceId));
            Assert.Equal(row.SourceRowVersion,(byte[])(await _fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeSources WHERE SourceId=@id",
                ("@id",row.SourceId)))!);
            Assert.Equal(row.RequestRowVersion,(byte[])(await _fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)))!);
            Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
                "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)));
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
                ("@id",row.RequestId))));
        }
    }

    [Fact]
    public async Task Hypothetical_atomic_probe_denies_foreign_peer_inactive_unapproved_and_noncustomer_scopes()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var rows=new[]{
            await probe.SeedPendingAsync(sourceOrganizationId:2),
            await probe.SeedPendingAsync(sourceTeamId:8),
            await probe.SeedPendingAsync(sourceOwnerUserId:30),
            await probe.SeedPendingAsync(sourceIsActive:false),
            await probe.SeedPendingAsync(sourceApprovalStatus:"Pending"),
            await probe.SeedPendingAsync(sourceLocationType:"Official")
        };
        foreach(var row in rows)
        {
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                probe.ApplyProbeAsync(row,Guid.NewGuid()));
            Assert.Equal("B3_PROBE_STALE_SOURCE",error.Message);
            Assert.Equal(B3ProbeFields.Original,await probe.ReadFieldsAsync(row.SourceId));
            Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
                "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)));
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
                ("@id",row.RequestId))));
        }
    }

    // Forward-only 012-like CHECK contract is rehearsed on B3Probe objects
    // only. These assertions do not constitute an executable migration.
    [Fact]
    public async Task Hypothetical_012_probe_request_check_rejects_unknown_null_and_self_review_shapes()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        var invalid=new[]{
            "UPDATE dbo.B3ProbeRequests SET Status=N'Applied' WHERE RequestId=@id",
            "UPDATE dbo.B3ProbeRequests SET Status=N'Applied',ReviewedByUserId=10,ReviewedAt=SYSUTCDATETIME(),AppliedAt=SYSUTCDATETIME(),ReviewReason=N'Self' WHERE RequestId=@id",
            "UPDATE dbo.B3ProbeRequests SET Status=N'Applied',ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),AppliedAt=SYSUTCDATETIME(),ReviewReason=N'   ' WHERE RequestId=@id",
            "UPDATE dbo.B3ProbeRequests SET Status=N'Rejected',ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),AppliedAt=SYSUTCDATETIME(),ReviewReason=N'Rejected' WHERE RequestId=@id",
            "UPDATE dbo.B3ProbeRequests SET Status=N'Rejected',ReviewedByUserId=20,ReviewedAt=SYSUTCDATETIME(),ReviewReason=NULL WHERE RequestId=@id",
            "UPDATE dbo.B3ProbeRequests SET Status=N'Applied',ReviewedByUserId=20,ReviewedAt=DATEADD(day,1,SYSUTCDATETIME()),AppliedAt=SYSUTCDATETIME(),ReviewReason=N'Out of order' WHERE RequestId=@id",
            "UPDATE dbo.B3ProbeRequests SET Status=N'Returned' WHERE RequestId=@id"
        };
        foreach(var sql in invalid)
        {
            var ex=await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",row.RequestId)));
            Assert.Equal(547,ex.Number);
            Assert.Equal("Pending",(string?)await _fixture.ScalarAsync(
                "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)));
            Assert.Equal(row.RequestRowVersion,(byte[])(await _fixture.ScalarAsync(
                "SELECT RowVersion FROM dbo.B3ProbeRequests WHERE RequestId=@id",
                ("@id",row.RequestId)))!);
        }
    }

    [Fact]
    public async Task Hypothetical_012_probe_request_check_accepts_valid_independent_rejection()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        Assert.Equal(1,await _fixture.ExecuteAsync("""
            UPDATE dbo.B3ProbeRequests
            SET Status=N'Rejected',ReviewedByUserId=20,
                ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'Independent decline'
            WHERE RequestId=@id AND Status=N'Pending'
            """,("@id",row.RequestId)));
        Assert.Equal("Rejected",(string?)await _fixture.ScalarAsync(
            "SELECT Status FROM dbo.B3ProbeRequests WHERE RequestId=@id",
            ("@id",row.RequestId)));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeRequests WHERE RequestId=@id AND ReviewedByUserId=20 AND AppliedAt IS NULL AND ReviewReason=N'Independent decline'",
            ("@id",row.RequestId))));
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
        // The row CHECK does not itself require an event; executor must do so.
    }

    [Fact]
    public async Task Hypothetical_012_probe_audit_check_rejects_null_actor_key_empty_guid_and_unknown_type()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        var key=Guid.NewGuid();
        var corr=Guid.NewGuid();
        (string kind,int? actor,Guid? decision,Guid correlation)[] invalid={
            ("Approved",20,null,corr),
            ("Rejected",20,null,corr),
            ("Submitted",20,key,corr),
            ("Applied",20,key,corr),
            ("Approved",null,key,corr),
            ("Approved",0,key,corr),
            ("Approved",20,Guid.Empty,corr),
            ("Approved",20,key,Guid.Empty),
            ("Unknown",20,null,corr),
        };
        const string sql="""
            INSERT INTO dbo.B3ProbeEvents(
                RequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@id,@kind,@actor,SYSUTCDATETIME(),@corr,@key)
            """;
        foreach(var item in invalid)
        {
            var ex=await Assert.ThrowsAsync<SqlException>(()=>
                _fixture.ExecuteAsync(sql,("@id",row.RequestId),
                    ("@kind",item.kind),("@actor",item.actor),
                    ("@corr",item.correlation),("@key",item.decision)));
            Assert.Equal(547,ex.Number);
            Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
                ("@id",row.RequestId))));
        }
    }

    [Fact]
    public async Task Hypothetical_012_probe_audit_check_accepts_four_valid_per_row_event_shapes()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        var row=await probe.SeedPendingAsync();
        var corr=Guid.NewGuid();
        (string kind,Guid? decision)[] valid={
            ("Submitted",null),("Rejected",Guid.NewGuid()),
            ("Approved",Guid.NewGuid()),("Applied",null)
        };
        const string sql="""
            INSERT INTO dbo.B3ProbeEvents(
                RequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey)
            VALUES(@id,@kind,20,SYSUTCDATETIME(),@corr,@key)
            """;
        foreach(var item in valid)
            Assert.Equal(1,await _fixture.ExecuteAsync(sql,
                ("@id",row.RequestId),("@kind",item.kind),
                ("@corr",corr),("@key",item.decision)));
        Assert.Equal(4,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id",
            ("@id",row.RequestId))));
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.B3ProbeEvents WHERE RequestId=@id AND DecisionKey IS NOT NULL",
            ("@id",row.RequestId))));
        // Per-row SQL checks cannot ensure one Approved/Applied pair or
        // authenticate reviewer identity. Transactional executor is required.
    }

    [Fact]
    public async Task Hypothetical_012_probe_untrusted_review_check_is_detected_until_retrusted()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        const string catalog="""
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(N'dbo.B3ProbeRequests',N'U')
              AND name IN (N'CK_B3ProbeRequests_KnownStatus',
                           N'CK_B3ProbeRequests_ReviewShape')
              AND is_disabled=0 AND is_not_trusted=0
            """;
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(catalog)));
        try
        {
            await _fixture.ExecuteAsync("""
                ALTER TABLE dbo.B3ProbeRequests
                NOCHECK CONSTRAINT CK_B3ProbeRequests_ReviewShape
                """);
            // Re-enable without validating legacy rows: enabled, but UNTRUSTED.
            // A catalog that checks is_disabled alone would incorrectly accept.
            await _fixture.ExecuteAsync("""
                ALTER TABLE dbo.B3ProbeRequests
                WITH NOCHECK CHECK CONSTRAINT CK_B3ProbeRequests_ReviewShape
                """);
            Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(catalog)));
            Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync("""
                SELECT COUNT(*) FROM sys.check_constraints
                WHERE parent_object_id=OBJECT_ID(N'dbo.B3ProbeRequests',N'U')
                  AND name=N'CK_B3ProbeRequests_ReviewShape'
                  AND is_disabled=0 AND is_not_trusted=1
                """)));
        }
        finally
        {
            await _fixture.ExecuteAsync("""
                ALTER TABLE dbo.B3ProbeRequests
                WITH CHECK CHECK CONSTRAINT CK_B3ProbeRequests_ReviewShape
                """);
        }
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(catalog)));
    }

    [Fact]
    public async Task Hypothetical_012_probe_untrusted_audit_check_is_denied_until_retrusted()
    {
        var probe=new B3HypotheticalAtomicApplyProbe(_fixture);
        await probe.CreateTestOnlySchemaAsync();
        const string catalog="""
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(N'dbo.B3ProbeEvents',N'U')
              AND name IN (N'CK_B3ProbeEvents_KnownType',
                           N'CK_B3ProbeEvents_DecisionShape')
              AND is_disabled=0 AND is_not_trusted=0
            """;
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(catalog)));
        try
        {
            await _fixture.ExecuteAsync("""
                ALTER TABLE dbo.B3ProbeEvents
                NOCHECK CONSTRAINT CK_B3ProbeEvents_DecisionShape
                """);
            await _fixture.ExecuteAsync("""
                ALTER TABLE dbo.B3ProbeEvents
                WITH NOCHECK CHECK CONSTRAINT CK_B3ProbeEvents_DecisionShape
                """);
            Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(catalog)));
            Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync("""
                SELECT COUNT(*) FROM sys.check_constraints
                WHERE parent_object_id=OBJECT_ID(N'dbo.B3ProbeEvents',N'U')
                  AND name=N'CK_B3ProbeEvents_DecisionShape'
                  AND is_disabled=0 AND is_not_trusted=1
                """)));
        }
        finally
        {
            await _fixture.ExecuteAsync("""
                ALTER TABLE dbo.B3ProbeEvents
                WITH CHECK CHECK CONSTRAINT CK_B3ProbeEvents_DecisionShape
                """);
        }
        Assert.Equal(2,Convert.ToInt32(await _fixture.ScalarAsync(catalog)));
    }


    // Forward-only 012: these tests ALTER the real-named fixture tables but
    // always ROLLBACK the entire rehearsal. No UAT DDL / Approve API.
    [Fact]
    public async Task Hypothetical_012_real_named_tables_catalog_denies_untrusted_and_missing_version()
    {
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                async Task<int> Shape() => Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                        B312DisposableForwardOnlyRehearsal.PartialShapeSql));
                Assert.Equal(1,await Shape());
                // The 011 application readiness MUST NOT accept this 012 schema.
                Assert.Equal(0,Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                        V180B3SqlSafetyRules.CatalogCheckSql)));
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,
                    "ALTER TABLE dbo.ChangeRequests NOCHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState");
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,
                    "ALTER TABLE dbo.ChangeRequests WITH NOCHECK CHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState");
                Assert.Equal(0,await Shape());
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,
                    "ALTER TABLE dbo.ChangeRequests WITH CHECK CHECK CONSTRAINT CK_B3_ChangeRequests_ReviewState");
                Assert.Equal(1,await Shape());
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,
                    "DELETE FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-012'");
                Assert.Equal(0,await Shape());
            });
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
        Assert.Equal("1.8.0-011",(string?)await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.LatestSchemaVersionSql));
    }

    [Fact]
    public async Task Hypothetical_012_real_named_tables_atomic_state_and_audit_checks_rollback()
    {
        var publicId=Guid.NewGuid();
        var decisionKey=Guid.NewGuid();
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                var id=Convert.ToInt64(await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx, """
                    INSERT INTO dbo.ChangeRequests(
                      RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,
                      OperationCode,RiskCode,ExpectedEntityRowVersion,ProposedJson,
                      RequestedByUserId,SubmittedAt,Status)
                    VALUES(@publicId,1,7,N'Location',N'012-rehearsal',
                      N'UpdatePublishedLocation',N'High',0x0102030405060708,
                      N'{"Name":"012"}',10,SYSUTCDATETIME(),N'Pending');
                    SELECT CAST(SCOPE_IDENTITY() AS bigint);
                    """,("@publicId",publicId)));
                // SQL row constraints reject a missing or blank review reason.
                await Assert.ThrowsAsync<SqlException>(()=>
                    B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    UPDATE dbo.ChangeRequests SET Status=N'Applied',
                    ReviewedByUserId=20,ReviewedAt=DATEADD(second,2,SubmittedAt),
                    AppliedAt=DATEADD(second,3,SubmittedAt),ReviewReason=N'  '
                    WHERE ChangeRequestId=@id
                    """,("@id",id)));
                await Assert.ThrowsAsync<SqlException>(()=>
                    B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    UPDATE dbo.ChangeRequests SET Status=N'Applied',
                    ReviewedByUserId=10,ReviewedAt=DATEADD(second,2,SubmittedAt),
                    AppliedAt=DATEADD(second,3,SubmittedAt),ReviewReason=N'ok'
                    WHERE ChangeRequestId=@id
                    """,("@id",id)));
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    UPDATE dbo.ChangeRequests SET Status=N'Applied',
                    ReviewedByUserId=20,ReviewedAt=DATEADD(second,2,SubmittedAt),
                    AppliedAt=DATEADD(second,3,SubmittedAt),ReviewReason=N'independent approval'
                    WHERE ChangeRequestId=@id
                    """,("@id",id));
                var correlation=Guid.NewGuid();
                await Assert.ThrowsAsync<SqlException>(()=>
                    B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                      ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                    VALUES(@id,N'Approved',20,SYSUTCDATETIME(),@correlation,NULL)
                    """,("@id",id),("@correlation",correlation)));
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                      ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                    VALUES(@id,N'Approved',20,SYSUTCDATETIME(),@correlation,@decision)
                    """,("@id",id),("@correlation",correlation),("@decision",decisionKey));
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                      ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                    VALUES(@id,N'Applied',20,SYSUTCDATETIME(),@correlation,NULL)
                    """,("@id",id),("@correlation",correlation));
                await Assert.ThrowsAsync<SqlException>(()=>
                    B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    INSERT INTO dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                      ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                    VALUES(@id,N'Approved',20,SYSUTCDATETIME(),NEWID(),@decision)
                    """,("@id",id),("@decision",decisionKey)));
                Assert.Equal(2,Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                    "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
                    ("@id",id))));
            });
        Assert.Equal(0,Convert.ToInt32(await _fixture.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.ChangeRequests WHERE RequestPublicId=@publicId",
            ("@publicId",publicId))));
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Hypothetical_012_real_named_tables_disabled_unique_decision_index_fails_shape()
    {
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                Assert.Equal(1,Convert.ToInt32(await B312DisposableForwardOnlyRehearsal.ScalarAsync(
                    conn,tx,B312DisposableForwardOnlyRehearsal.PartialShapeSql)));
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,
                    "ALTER INDEX UX_B3_ChangeRequestEvents_DecisionKey ON dbo.ChangeRequestEvents DISABLE");
                Assert.Equal(0,Convert.ToInt32(await B312DisposableForwardOnlyRehearsal.ScalarAsync(
                    conn,tx,B312DisposableForwardOnlyRehearsal.PartialShapeSql)));
            });
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }


    [Fact]
    public async Task Hypothetical_012_strict_fingerprint_denies_token_preserving_check_relaxation()
    {
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                var approved=await B312DisposableCatalogFingerprint.CaptureAsync(conn,tx);
                await B312DisposableCatalogFingerprint.RequireUnchangedAsync(conn,tx,approved);
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    ALTER TABLE dbo.ChangeRequests
                      DROP CONSTRAINT CK_B3_ChangeRequests_KnownStatus;
                    ALTER TABLE dbo.ChangeRequests WITH CHECK
                      ADD CONSTRAINT CK_B3_ChangeRequests_KnownStatus
                      CHECK ([Status] IN (N'Pending',N'Rejected',N'Applied',N'Cancelled'));
                    """);
                // Names, 8 trusted checks and the word 'Applied' still match
                // the previous partial fingerprint: it MUST NOT be release-ready.
                Assert.Equal(1,Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                        B312DisposableForwardOnlyRehearsal.PartialShapeSql)));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    B312DisposableCatalogFingerprint.RequireUnchangedAsync(conn,tx,approved));
            });
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Hypothetical_012_strict_fingerprint_denies_same_name_narrowed_unique_filter()
    {
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                var approved=await B312DisposableCatalogFingerprint.CaptureAsync(conn,tx);
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    DROP INDEX UX_B3_ChangeRequestEvents_DecisionKey
                      ON dbo.ChangeRequestEvents;
                    CREATE UNIQUE INDEX UX_B3_ChangeRequestEvents_DecisionKey
                      ON dbo.ChangeRequestEvents(DecisionKey)
                      WHERE DecisionKey IS NOT NULL AND ActorUserId IS NOT NULL;
                    """);
                Assert.Equal(1,Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                        B312DisposableForwardOnlyRehearsal.PartialShapeSql)));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    B312DisposableCatalogFingerprint.RequireUnchangedAsync(conn,tx,approved));
            });
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

    [Fact]
    public async Task Hypothetical_012_strict_fingerprint_denies_duplicate_011_predecessor()
    {
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                var approved=await B312DisposableCatalogFingerprint.CaptureAsync(conn,tx);
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,
                    "INSERT INTO dbo.SchemaVersions(VersionNumber,AppliedAt) VALUES(N'1.8.0-011',DATEADD(second,-20,SYSUTCDATETIME()))");
                // Latest marker still reads 012, but history is ambiguous.
                Assert.Equal(1,Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                        B312DisposableForwardOnlyRehearsal.PartialShapeSql)));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    B312DisposableCatalogFingerprint.RequireUnchangedAsync(conn,tx,approved));
            });
        Assert.Equal("1.8.0-011",(string?)await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.LatestSchemaVersionSql));
    }

    [Fact]
    public async Task Hypothetical_012_strict_fingerprint_denies_enabled_side_effect_trigger()
    {
        await B312DisposableForwardOnlyRehearsal.RunRollbackOnlyAsync(_fixture,
            async (conn,tx) =>
            {
                var approved=await B312DisposableCatalogFingerprint.CaptureAsync(conn,tx);
                await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx, """
                    CREATE TRIGGER dbo.TR_B312_Disposable_DeniedSideEffects
                    ON dbo.ChangeRequests AFTER UPDATE
                    AS BEGIN SET NOCOUNT ON; END
                    """);
                Assert.Equal(1,Convert.ToInt32(
                    await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                        B312DisposableForwardOnlyRehearsal.PartialShapeSql)));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    B312DisposableCatalogFingerprint.RequireUnchangedAsync(conn,tx,approved));
            });
        Assert.Equal(1,Convert.ToInt32(await _fixture.ScalarAsync(
            V180B3SqlSafetyRules.CatalogCheckSql)));
    }

}
