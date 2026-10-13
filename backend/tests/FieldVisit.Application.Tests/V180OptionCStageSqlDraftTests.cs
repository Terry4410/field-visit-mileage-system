using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Nondeploy 011/012 + Option C staging draft rehearsal, isolated SQL Server
/// only. Proves persistent stage and FK/unique/hash gates, not UAT readiness.
/// </summary>
public sealed class V180OptionCStageSqlDraftTests
{
    [Fact]
    public async Task Stage_is_persisted_separately_from_published_location_and_cannot_bypass_constraints()
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_STAGE_ISOLATED_SQL_REQUIRED");
            return;
        }
        var root = new SqlConnectionStringBuilder(raw) { Pooling=false, ConnectTimeout=5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
            || !root.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase)
            || !root.UserID.Equals("sa", StringComparison.OrdinalIgnoreCase)
            || root.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_STAGE_NONLOCAL_TARGET_DENIED");
        var name = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        var repo = FindRepoRoot();
        await using (var admin = new SqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await Exec(admin, $"CREATE DATABASE [{name}]");
        }
        try
        {
            var local = new SqlConnectionStringBuilder(root.ConnectionString)
            { InitialCatalog=name, Pooling=false };
            await using var db = new SqlConnection(local.ConnectionString);
            await db.OpenAsync();
            await Exec(db, """
                CREATE TABLE dbo.SchemaVersions(
                    VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,
                    Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,
                    AppliedBy nvarchar(200) NULL);
                CREATE TABLE dbo.Organizations(OrganizationId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Teams(TeamId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Users(UserId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Locations(
                    LocationId int NOT NULL PRIMARY KEY,
                    OrganizationId int NOT NULL,TeamId int NOT NULL,
                    CreatedByUserId int NOT NULL,LocationType nvarchar(20) NOT NULL,
                    ApprovalStatus nvarchar(20) NOT NULL,IsActive bit NOT NULL,
                    LocationName nvarchar(200) NULL,City nvarchar(100) NULL,
                    District nvarchar(100) NULL,Address nvarchar(500) NULL,
                    PlusCode nvarchar(100) NULL,TaxId nvarchar(50) NULL,
                    MasterNote nvarchar(1000) NULL,
                    RowVersion rowversion NOT NULL);
                INSERT dbo.Organizations VALUES(1);
                INSERT dbo.Teams VALUES(1);
                INSERT dbo.Users VALUES(1),(2);
                INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,
                    LocationType,ApprovalStatus,IsActive)
                    VALUES(10,1,1,1,N'Customer',N'Approved',1),
                          (11,1,1,1,N'Customer',N'Approved',1);
                INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
                    VALUES(N'1.8.0-010',N'isolated',SYSUTCDATETIME(),N'isolated');
                """);
            await Exec(db, Read(repo,"1800_011/Up.sql"));
            await Exec(db, Read(repo,"1800_012/Up.sql"));
            var stage = Read(repo,"location-stages/StageTable.disposable.sql");
            await Exec(db,stage);
            Assert.Equal("1.8.0-012",await Scalar(db,
                "SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC"));
            await Assert.ThrowsAsync<SqlException>(()=>Exec(db,stage));

            await Exec(db, """
                INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                    EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                    BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                VALUES(NEWID(),1,1,N'Location',N'10',N'UpdatePublishedLocation',
                    N'High',0x0000000000000001,N'{}',N'{"Address":"New"}',
                    1,SYSUTCDATETIME(),N'Pending');
                """);
            var insert = """
                INSERT dbo.LocationPublicationStages(
                    ChangeRequestId,LocationId,OrganizationId,TeamId,
                    ExpectedLocationRowVersion,ProposedJson,ProposedSha256,
                    StageStatus,GeocodeStatus,DuplicateReviewStatus,CreatedAt,UpdatedAt)
                VALUES(1,10,1,1,0x0000000000000001,N'{"Address":"New"}',
                    HASHBYTES('SHA2_256',CONVERT(varbinary(max),N'{"Address":"New"}')),
                    N'PendingGeocode',N'Pending',N'Pending',SYSUTCDATETIME(),SYSUTCDATETIME());
                """;
            await Exec(db,insert);
            Assert.Equal("PendingGeocode",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE ChangeRequestId=1"));
            Assert.Equal("Approved",await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=10"));
            Assert.Equal("1",await Scalar(db,
                "SELECT CAST(IsActive AS nvarchar(1)) FROM dbo.Locations WHERE LocationId=10"));
            Assert.Equal("1.8.0-012",await Scalar(db,
                "SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC"));

            var duplicate=await Assert.ThrowsAsync<SqlException>(()=>Exec(db,insert));
            Assert.Contains(duplicate.Number,new[]{2601,2627});
            // Hash and orphan constraints are tested separately; SQL may
            // reject a duplicate PK before evaluating any FK/CHECK.
            var invalidHash=await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                UPDATE dbo.LocationPublicationStages SET
                    ProposedSha256=0x0000000000000000000000000000000000000000000000000000000000000000
                WHERE ChangeRequestId=1;
                """));
            Assert.Equal(547,invalidHash.Number);
            var orphan=await Assert.ThrowsAsync<SqlException>(()=>Exec(db,
                insert.Replace("VALUES(1,10,","VALUES(999,11,",StringComparison.Ordinal)));
            Assert.Equal(547,orphan.Number);

            await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                UPDATE dbo.LocationPublicationStages
                SET StageStatus=N'ReadyForPublish'
                WHERE ChangeRequestId=1;
                """));
            Assert.Equal("PendingGeocode",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE ChangeRequestId=1"));
            // Tampering with the immutable proposed JSON invalidates the hash.
            await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                UPDATE dbo.LocationPublicationStages
                SET ProposedJson=N'{"Address":"Tampered"}'
                WHERE ChangeRequestId=1;
                """));
            // Invalid geocoding proof cannot be marked ready.
            await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                UPDATE dbo.LocationPublicationStages SET
                    GeocodeStatus=N'Succeeded',DuplicateReviewStatus=N'Cleared',
                    DuplicateEvidenceSha256=0x0000000000000000000000000000000000000000000000000000000000000000,
                    StageStatus=N'ReadyForPublish'
                WHERE ChangeRequestId=1;
                """));
            Assert.Equal("PendingGeocode",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE ChangeRequestId=1"));

            // Stale geocoder callbacks must not mark a new staged address as ready.
            await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                UPDATE dbo.LocationPublicationStages SET
                    GeocodeStatus=N'Succeeded',
                    GeocodeAddressSha256=HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),N'Old address')),
                    Latitude=25.03, Longitude=121.56
                WHERE ChangeRequestId=1;
                """));
            // A duplicate review for a different version is likewise rejected.
            await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                UPDATE dbo.LocationPublicationStages SET
                    DuplicateReviewStatus=N'Cleared',
                    DuplicateEvidenceSha256=HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),N'Wrong proposal'))
                WHERE ChangeRequestId=1;
                """));
            // Exact-version evidence can advance a persistent stage while the
            // published source remains Approved/Active and untouched.
            await Exec(db, """
                UPDATE dbo.LocationPublicationStages SET
                    GeocodeStatus=N'Succeeded',
                    GeocodeAddressSha256=HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),N'New')),
                    Latitude=25.03, Longitude=121.56,
                    DuplicateReviewStatus=N'Cleared',
                    DuplicateEvidenceSha256=ProposedSha256,
                    StageStatus=N'ReadyForPublish',
                    UpdatedAt=SYSUTCDATETIME()
                WHERE ChangeRequestId=1;
                """);
            Assert.Equal("ReadyForPublish",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE ChangeRequestId=1"));
            Assert.Equal("Approved",await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=10"));
            Assert.Equal("1",await Scalar(db,
                "SELECT CAST(IsActive AS nvarchar(1)) FROM dbo.Locations WHERE LocationId=10"));

            // Stable LocationId published-version ledger: one current row and
            // immutable version identity, not a replacement LocationId.
            await Exec(db, """
                INSERT dbo.LocationPublishedVersions(LocationId,VersionNumber,
                    ChangeRequestId,IsCurrent,PublishedJson,PublishedSha256,
                    PublishedAt,PublishedByUserId,SourceLocationRowVersion)
                VALUES(10,1,NULL,1,N'{"Address":"Old"}',
                    HASHBYTES('SHA2_256',CONVERT(varbinary(max),N'{"Address":"Old"}')),
                    SYSUTCDATETIME(),1,0x0000000000000001);
                """);
            var twoCurrent=await Assert.ThrowsAsync<SqlException>(()=>Exec(db, """
                INSERT dbo.LocationPublishedVersions(LocationId,VersionNumber,
                    ChangeRequestId,IsCurrent,PublishedJson,PublishedSha256,
                    PublishedAt,PublishedByUserId,SourceLocationRowVersion)
                VALUES(10,2,1,1,N'{"Address":"New"}',
                    HASHBYTES('SHA2_256',CONVERT(varbinary(max),N'{"Address":"New"}')),
                    SYSUTCDATETIME(),2,0x0000000000000001);
                """));
            Assert.Contains(twoCurrent.Number,new[]{2601,2627});
            // The historical version remains available once current advances.
            await Exec(db, """
                BEGIN TRANSACTION;
                UPDATE dbo.LocationPublishedVersions
                    SET IsCurrent=0 WHERE LocationId=10 AND VersionNumber=1;
                INSERT dbo.LocationPublishedVersions(LocationId,VersionNumber,
                    ChangeRequestId,IsCurrent,PublishedJson,PublishedSha256,
                    PublishedAt,PublishedByUserId,SourceLocationRowVersion)
                VALUES(10,2,1,1,N'{"Address":"New"}',
                    HASHBYTES('SHA2_256',CONVERT(varbinary(max),N'{"Address":"New"}')),
                    SYSUTCDATETIME(),2,0x0000000000000001);
                COMMIT TRANSACTION;
                """);
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(COUNT(*) AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
            Assert.Equal("Approved",await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=10"));

            // Rollback across request, stage, and version ledger: a failed
            // audit event must leave the old current version and Pending
            // request intact. Test a real SQL FK failure in a transaction.
            var rolledBack=false;
            await using(var rollbackTx=(SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    await using var mutation=new SqlCommand("""
                        UPDATE dbo.LocationPublishedVersions
                        SET IsCurrent=0 WHERE LocationId=10 AND VersionNumber=2;
                        UPDATE dbo.LocationPublishedVersions
                        SET IsCurrent=1 WHERE LocationId=10 AND VersionNumber=1;
                        UPDATE dbo.LocationPublicationStages
                        SET StageStatus=N'Published' WHERE ChangeRequestId=1;
                        UPDATE dbo.Locations SET ApprovalStatus=N'Pending'
                        WHERE LocationId=10;
                        UPDATE dbo.ChangeRequests SET Status=N'Applied',
                            ReviewedByUserId=2,
                            ReviewedAt=DATEADD(SECOND,1,SubmittedAt),
                            AppliedAt=DATEADD(SECOND,1,SubmittedAt),
                            ReviewReason=N'Atomic rollback rehearsal'
                        WHERE ChangeRequestId=1;
                        """,db,rollbackTx);
                    await mutation.ExecuteNonQueryAsync();
                    await using var brokenAudit=new SqlCommand("""
                        INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,
                            ActorUserId,OccurredAt,CorrelationId,DecisionKey)
                        VALUES(999999,N'Approved',2,SYSUTCDATETIME(),NEWID(),NEWID());
                        """,db,rollbackTx);
                    await brokenAudit.ExecuteNonQueryAsync();
                }
                catch(SqlException)
                {
                    rolledBack=true;
                }
                finally
                {
                    await rollbackTx.RollbackAsync();
                }
            }
            Assert.True(rolledBack);
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(COUNT(*) AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=10"));
            Assert.Equal("Pending",await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=1"));
            Assert.Equal("ReadyForPublish",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE ChangeRequestId=1"));
            Assert.Equal("Approved",await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=10"));
            Assert.Equal("0",await Scalar(db,
                "SELECT CAST(COUNT(*) AS nvarchar(10)) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=1"));

            // Exercise the actual unregistered stage writer under a
            // caller-owned SERIALIZABLE transaction, not just direct SQL.
            var requestPublicId=Guid.NewGuid();
            await using(var requestCmd=new SqlCommand("""
                INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                    EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                    BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                SELECT @id,1,1,N'Location',N'11',N'UpdatePublishedLocation',
                    N'High',l.RowVersion,N'{}',N'{"Address":"Second"}',
                    1,SYSUTCDATETIME(),N'Pending'
                FROM dbo.Locations l WHERE l.LocationId=11;
                """,db))
            {
                requestCmd.Parameters.AddWithValue("@id",requestPublicId);
                await requestCmd.ExecuteNonQueryAsync();
            }
            await using(var tx=(SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                var created=await FieldVisit.Infrastructure.V180OptionCStageDraftWriter
                    .CreateFromLockedRequestAsync(db,tx,requestPublicId,1,CancellationToken.None);
                Assert.True(created>0);
                await tx.CommitAsync();
            }
            Assert.Equal("PendingGeocode",await Scalar(db, """
                SELECT s.StageStatus FROM dbo.LocationPublicationStages s
                JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
                WHERE r.RequestPublicId=(SELECT TOP(1) RequestPublicId
                    FROM dbo.ChangeRequests WHERE EntityId=N'11');
                """));
            Assert.Equal("Approved",await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=11"));
            // Rehearse the REAL unregistered writer: request-first locking,
            // source/current-version swap and two canonical audit events in
            // ONE externally owned transaction. No UAT/production execution.
            await Exec(db, """
                UPDATE dbo.ChangeRequests SET ProposedJson=
                    N'{"LocationName":"Reviewed","City":"Taipei","District":"Xinyi","Address":"Second","PlusCode":null,"TaxId":null,"MasterNote":null}'
                WHERE RequestPublicId=(SELECT TOP(1) RequestPublicId
                    FROM dbo.ChangeRequests WHERE EntityId=N'11');
                """);
            // Recreate staging from the newly altered request JSON, keeping
            // the stage/request immutable pair consistent for this fixture.
            await Exec(db, """
                UPDATE s SET ProposedJson=r.ProposedJson,
                    ProposedSha256=HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),r.ProposedJson)),
                    GeocodeStatus=N'Succeeded',
                    GeocodeAddressSha256=HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),N'Second')),
                    Latitude=25.03,Longitude=121.56,
                    DuplicateReviewStatus=N'Cleared',
                    DuplicateEvidenceSha256=HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),r.ProposedJson)),
                    StageStatus=N'ReadyForPublish'
                FROM dbo.LocationPublicationStages s
                JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId
                WHERE r.EntityId=N'11';
                INSERT dbo.LocationPublishedVersions(LocationId,VersionNumber,
                    ChangeRequestId,IsCurrent,PublishedJson,PublishedSha256,
                    PublishedAt,PublishedByUserId,SourceLocationRowVersion)
                SELECT 11,1,NULL,1,N'{"Address":"Original"}',
                    HASHBYTES('SHA2_256',
                        CONVERT(varbinary(max),N'{"Address":"Original"}')),
                    SYSUTCDATETIME(),1,l.RowVersion
                FROM dbo.Locations l WHERE l.LocationId=11;
                """);
            // Negative authorization gates must reject before ANY DML.
            await using(var denyTx=(SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    FieldVisit.Infrastructure.V180OptionCAtomicPublicationWriter
                        .ExecuteAsync(db,denyTx,requestPublicId,1,2,Guid.NewGuid(),
                            "Denied",false,true,CancellationToken.None));
                await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    FieldVisit.Infrastructure.V180OptionCAtomicPublicationWriter
                        .ExecuteAsync(db,denyTx,requestPublicId,1,2,Guid.NewGuid(),
                            "Denied",true,false,CancellationToken.None));
                await Assert.ThrowsAsync<SqlException>(()=>
                    FieldVisit.Infrastructure.V180OptionCAtomicPublicationWriter
                        .ExecuteAsync(db,denyTx,requestPublicId,1,1,Guid.NewGuid(),
                            "Self review",true,true,CancellationToken.None));
                await denyTx.RollbackAsync();
            }
            Assert.Equal("Pending",await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE EntityId=N'11'"));
            Assert.Equal("ReadyForPublish",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE LocationId=11"));
            // Organization boundary is checked against the canonical request,
            // not a client-supplied LocationId or team claim.
            await using(var crossOrgTx=(SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<SqlException>(()=>
                    FieldVisit.Infrastructure.V180OptionCAtomicPublicationWriter
                        .ExecuteAsync(db,crossOrgTx,requestPublicId,999,2,Guid.NewGuid(),
                            "Wrong organization",true,true,CancellationToken.None));
                await crossOrgTx.RollbackAsync();
            }
            Assert.Equal("Pending",await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE EntityId=N'11'"));
            Assert.Equal("ReadyForPublish",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE LocationId=11"));
            Assert.Equal("1",await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=11 AND IsCurrent=1"));
            var decision=Guid.NewGuid();
            await using(var publishTx=(SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await FieldVisit.Infrastructure.V180OptionCAtomicPublicationWriter
                    .ExecuteAsync(db,publishTx,requestPublicId,1,2,decision,
                        "Independently reviewed",true,true,CancellationToken.None);
                await publishTx.CommitAsync();
            }
            Assert.Equal("Applied",await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId='"+requestPublicId+"'"));
            Assert.Equal("Published",await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages WHERE LocationId=11"));
            Assert.Equal("Reviewed",await Scalar(db,
                "SELECT LocationName FROM dbo.Locations WHERE LocationId=11"));
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=11 AND IsCurrent=1"));
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(COUNT(*) AS nvarchar(10)) FROM dbo.LocationPublishedVersions WHERE LocationId=11"));
            Assert.Equal("2",await Scalar(db,
                "SELECT CAST(COUNT(*) AS nvarchar(10)) FROM dbo.ChangeRequestEvents e JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId WHERE r.EntityId=N'11'"));
            Assert.Equal("Approved",await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=11"));
            // Replaying the same already-applied request must fail closed.
            await using(var replayTx=(SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<SqlException>(()=>
                    FieldVisit.Infrastructure.V180OptionCAtomicPublicationWriter.ExecuteAsync(
                        db,replayTx,requestPublicId,1,2,decision,
                        "Independently reviewed",true,true,CancellationToken.None));
                await replayTx.RollbackAsync();
            }

        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await Exec(admin,$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
    }
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir.FullName,
                "docs/release/optionc-canonical-sql-draft/1800_011/Up.sql")))
                return dir.FullName;
            dir=dir.Parent;
        }
        throw new FileNotFoundException("OPTIONC_STAGE_ROOT_NOT_FOUND");
    }
    private static string Read(string root,string relative) =>
        File.ReadAllText(Path.Combine(root,"docs","release",
            "optionc-canonical-sql-draft",relative.Replace('/',Path.DirectorySeparatorChar)));
    private static async Task Exec(SqlConnection db,string sql)
    {
        await using var cmd=new SqlCommand(sql,db){CommandTimeout=60};
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<string> Scalar(SqlConnection db,string sql)
    {
        await using var cmd=new SqlCommand(sql,db){CommandTimeout=60};
        return Convert.ToString(await cmd.ExecuteScalarAsync())??"";
    }
}
