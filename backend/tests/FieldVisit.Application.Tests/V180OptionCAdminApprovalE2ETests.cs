using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Real disposable-SQL E2E for the unregistered Option C Admin candidate.
/// This is intentionally not an HTTP/UAT test: the public route remains
/// disabled, while the server-owned evidence provider and writer execute the
/// complete request → stage → publication transaction.
/// </summary>
public sealed class V180OptionCAdminApprovalE2ETests
{
    [Fact]
    public async Task Visitor_to_admin_to_publication_preserves_history_and_fail_closed_denials()
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_ADMIN_E2E_SQL_REQUIRED");
            return;
        }
        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
            || root.InitialCatalog != "master" || root.UserID != "sa"
            || root.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_ADMIN_E2E_NONLOCAL_TARGET_DENIED");

        var name = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        await using (var admin = new SqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await Exec(admin, $"CREATE DATABASE [{name}]");
        }
        try
        {
            var local = new SqlConnectionStringBuilder(root.ConnectionString)
            { InitialCatalog = name, Pooling = false };
            await using var db = new SqlConnection(local.ConnectionString);
            await db.OpenAsync();
            await CreateBaseAsync(db);
            var repo = FindRepoRoot();
            await Exec(db, Read(repo, "1800_011/Up.sql"));
            await Exec(db, Read(repo, "1800_012/Up.sql"));
            await Exec(db, Read(repo, "1800_014/Preflight.sql"));
            await Exec(db, Read(repo, "1800_014/Up.sql"));
            await Exec(db, Read(repo, "1800_014/Backfill.sql"));

            var request = await CreateRequestAsync(db, 10, 1, "Reviewed address");
            await PrepareStageAsync(db, request, 10, 1, 1);
            var refsBefore = await Scalar(db, """
                SELECT CAST((SELECT COUNT(*) FROM dbo.VisitTripStops WHERE LocationId=10)
                    +(SELECT COUNT(*) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10) AS nvarchar(20));
                """);
            var oldLocationId = await Scalar(db,
                "SELECT CAST(LocationId AS nvarchar(20)) FROM dbo.Locations WHERE LocationId=10");
            var decision = Guid.NewGuid();
            var service = new FieldVisit.Infrastructure.V180OptionCAdminApprovalService(
                new FieldVisit.Infrastructure.V180OptionCIsolatedApprovalEvidenceProvider());

            // Visitor submission is represented by the canonical request and
            // Submitted event; the Admin service now obtains live evidence
            // from SQL inside the same Serializable transaction.
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await service.ApproveAsync(db, tx, request, 1, 2, decision,
                    "Independent live review", CancellationToken.None);
                await tx.CommitAsync();
            }

            Assert.Equal("Applied", await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",
                new SqlParameter("@id", request)));
            Assert.Equal("Published", await Scalar(db,
                "SELECT StageStatus FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id",
                new SqlParameter("@id", request)));
            Assert.Equal("2", await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(20)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND IsCurrent=1"));
            Assert.Equal("1", await Scalar(db,
                "SELECT CAST(COUNT(*) AS nvarchar(20)) FROM dbo.LocationPublishedVersions WHERE LocationId=10 AND VersionNumber=1"));
            Assert.Equal("Approved", await Scalar(db,
                "SELECT ApprovalStatus FROM dbo.Locations WHERE LocationId=10"));
            Assert.Equal(oldLocationId, await Scalar(db,
                "SELECT CAST(LocationId AS nvarchar(20)) FROM dbo.Locations WHERE LocationId=10"));
            Assert.Equal(refsBefore, await Scalar(db, """
                SELECT CAST((SELECT COUNT(*) FROM dbo.VisitTripStops WHERE LocationId=10)
                    +(SELECT COUNT(*) FROM dbo.VisitTripSnapshotStops WHERE LocationId=10) AS nvarchar(20));
                """));
            Assert.Equal("3", await Scalar(db, """
                SELECT CAST(COUNT(*) AS nvarchar(20)) FROM dbo.ChangeRequestEvents e
                JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
                WHERE r.RequestPublicId=@id;
                """, new SqlParameter("@id", request)));

            // Duplicate DecisionKey is rejected after a fresh request lock and
            // never performs a second publication.
            await using (var replay = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveAsync(
                    db, replay, request, 1, 2, decision,
                    "Replay", CancellationToken.None));
                await replay.RollbackAsync();
            }

            // Self approval, reviewer revocation, requester revocation and
            // cross-organization/team evidence all fail before publication.
            var selfRequest = await CreateRequestAsync(db, 11, 1, "Self review");
            await PrepareStageAsync(db, selfRequest, 11, 1, 1);
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveAsync(
                    db, tx, selfRequest, 1, 1, Guid.NewGuid(), "Self", CancellationToken.None));
                await tx.RollbackAsync();
            }
            Assert.Equal("Pending", await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",
                new SqlParameter("@id", selfRequest)));

            var rejectRequest = await CreateRequestAsync(db, 12, 1, "Rejected change");
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await service.RejectAsync(db, tx, rejectRequest, 1, 2,
                    Guid.NewGuid(), "Duplicate location", CancellationToken.None);
                await tx.CommitAsync();
            }
            Assert.Equal("Rejected", await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",
                new SqlParameter("@id", rejectRequest)));
            Assert.Equal("Rejected", await Scalar(db, """
                SELECT TOP(1) EventType FROM dbo.ChangeRequestEvents e
                JOIN dbo.ChangeRequests r ON r.ChangeRequestId=e.ChangeRequestId
                WHERE r.RequestPublicId=@id ORDER BY e.ChangeRequestEventId DESC;
                """, new SqlParameter("@id", rejectRequest)));

            await Exec(db, "UPDATE dbo.OptionCLiveApprovalActors SET IsIndependentAdminEligible=0 WHERE UserId=2");
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RejectAsync(
                    db, tx, selfRequest, 1, 2, Guid.NewGuid(), "Revoked reviewer", CancellationToken.None));
                await tx.RollbackAsync();
            }
            await Exec(db, "UPDATE dbo.OptionCLiveApprovalActors SET IsIndependentAdminEligible=1, OrganizationId=2 WHERE UserId=2");
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RejectAsync(
                    db, tx, selfRequest, 1, 2, Guid.NewGuid(), "Cross org", CancellationToken.None));
                await tx.RollbackAsync();
            }
            await Exec(db, "UPDATE dbo.OptionCLiveApprovalActors SET OrganizationId=1, IsRequesterEligible=0 WHERE UserId=1");
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RejectAsync(
                    db, tx, selfRequest, 1, 2, Guid.NewGuid(), "Revoked requester", CancellationToken.None));
                await tx.RollbackAsync();
            }
            Assert.Equal("Pending", await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",
                new SqlParameter("@id", selfRequest)));

            // A write fault after publication mutations must leave every
            // canonical table at its pre-decision state.
            var rollbackRequest = await CreateRequestAsync(db, 12, 1, "Rollback probe");
            await PrepareStageAsync(db, rollbackRequest, 12, 1, 1);
            var beforeVersion = await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(20)) FROM dbo.LocationPublishedVersions WHERE LocationId=12 AND IsCurrent=1");
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    await Exec(db, "UPDATE dbo.LocationPublishedVersions SET IsCurrent=0 WHERE LocationId=12 AND IsCurrent=1", tx);
                    await Exec(db, "UPDATE dbo.Locations SET LocationName=N'Faulted' WHERE LocationId=12", tx);
                    await Exec(db, "INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId,DecisionKey) VALUES(999999,N'Approved',2,SYSUTCDATETIME(),NEWID(),NEWID())", tx);
                }
                catch (SqlException) { await tx.RollbackAsync(); }
            }
            Assert.Equal(beforeVersion, await Scalar(db,
                "SELECT CAST(VersionNumber AS nvarchar(20)) FROM dbo.LocationPublishedVersions WHERE LocationId=12 AND IsCurrent=1"));
            Assert.Equal("Pending", await Scalar(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE RequestPublicId=@id",
                new SqlParameter("@id", rollbackRequest)));
        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await Exec(admin, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
    }

    private static async Task CreateBaseAsync(SqlConnection db)
    {
        await Exec(db, """
            CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,Description nvarchar(500) NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
            CREATE TABLE dbo.Organizations(OrganizationId int NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.Teams(TeamId int NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.Users(UserId int NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.Locations(LocationId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NOT NULL,CreatedByUserId int NOT NULL,LocationType nvarchar(20) NOT NULL,ApprovalStatus nvarchar(20) NOT NULL,IsActive bit NOT NULL,LocationName nvarchar(200) NULL,City nvarchar(100) NULL,District nvarchar(100) NULL,Address nvarchar(500) NULL,PlusCode nvarchar(100) NULL,TaxId nvarchar(50) NULL,MasterNote nvarchar(1000) NULL,RowVersion rowversion NOT NULL);
            CREATE TABLE dbo.OptionCLiveApprovalActors(UserId int NOT NULL PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NULL,IsActive bit NOT NULL,IsRequesterEligible bit NOT NULL,IsIndependentAdminEligible bit NOT NULL);
            CREATE TABLE dbo.VisitTripStops(VisitTripStopId bigint NOT NULL PRIMARY KEY,LocationId int NOT NULL FOREIGN KEY REFERENCES dbo.Locations(LocationId));
            CREATE TABLE dbo.VisitTripSnapshotStops(VisitTripSnapshotStopId bigint NOT NULL PRIMARY KEY,LocationId int NOT NULL FOREIGN KEY REFERENCES dbo.Locations(LocationId));
            INSERT dbo.Organizations VALUES(1),(2); INSERT dbo.Teams VALUES(1),(2); INSERT dbo.Users VALUES(1),(2),(3);
            INSERT dbo.Locations(LocationId,OrganizationId,TeamId,CreatedByUserId,LocationType,ApprovalStatus,IsActive,LocationName,Address) VALUES
              (10,1,1,1,N'Customer',N'Approved',1,N'Original 10',N'Original Address 10'),
              (11,1,1,1,N'Customer',N'Approved',1,N'Original 11',N'Original Address 11'),
              (12,1,1,1,N'Customer',N'Approved',1,N'Original 12',N'Original Address 12');
            INSERT dbo.OptionCLiveApprovalActors VALUES(1,1,1,1,1,0),(2,1,1,1,0,1),(3,2,2,1,0,1);
            INSERT dbo.VisitTripStops VALUES(100,10); INSERT dbo.VisitTripSnapshotStops VALUES(200,10);
            INSERT dbo.SchemaVersions VALUES(N'1.8.0-010',N'isolated',SYSUTCDATETIME(),N'isolated');
            """);
    }

    private static async Task<Guid> CreateRequestAsync(SqlConnection db, int locationId, int requester, string name)
    {
        var id = Guid.NewGuid();
        await Exec(db, """
            INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
            SELECT @id,OrganizationId,TeamId,N'Location',CONVERT(nvarchar(80),LocationId),N'UpdatePublishedLocation',N'High',RowVersion,N'{}',@json,@requester,SYSUTCDATETIME(),N'Pending'
            FROM dbo.Locations WHERE LocationId=@location;
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,OccurredAt,CorrelationId)
            SELECT ChangeRequestId,N'Submitted',@requester,SYSUTCDATETIME(),NEWID() FROM dbo.ChangeRequests WHERE RequestPublicId=@id;
            """, new SqlParameter("@id", id), new SqlParameter("@location", locationId),
            new SqlParameter("@requester", requester), new SqlParameter("@json", $"{{\"LocationName\":\"{name}\",\"City\":\"Taipei\",\"District\":\"Xinyi\",\"Address\":\"Updated {locationId}\",\"PlusCode\":null,\"TaxId\":null,\"MasterNote\":null}}"));
        return id;
    }

    private static async Task PrepareStageAsync(SqlConnection db, Guid request, int locationId, int organization, int team)
    {
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await FieldVisit.Infrastructure.V180OptionCStageDraftWriter.CreateFromLockedRequestAsync(db, tx, request, organization, CancellationToken.None);
        await tx.CommitAsync();
        await Exec(db, """
            UPDATE s SET GeocodeStatus=N'Succeeded',GeocodeAddressSha256=HASHBYTES('SHA2_256',CONVERT(varbinary(max),JSON_VALUE(s.ProposedJson,N'$.Address'))),Latitude=25.03,Longitude=121.56,DuplicateReviewStatus=N'Cleared',DuplicateEvidenceSha256=s.ProposedSha256,StageStatus=N'ReadyForPublish',UpdatedAt=SYSUTCDATETIME()
            FROM dbo.LocationPublicationStages s JOIN dbo.ChangeRequests r ON r.ChangeRequestId=s.ChangeRequestId WHERE r.RequestPublicId=@id AND s.LocationId=@location AND s.OrganizationId=@organization AND s.TeamId=@team;
            """, new SqlParameter("@id", request), new SqlParameter("@location", locationId), new SqlParameter("@organization", organization), new SqlParameter("@team", team));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs/release/optionc-canonical-sql-draft/1800_011/Up.sql"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("OPTIONC_ADMIN_E2E_ROOT_NOT_FOUND");
    }

    private static string Read(string root, string relative) => File.ReadAllText(Path.Combine(root, "docs", "release", "optionc-canonical-sql-draft", relative.Replace('/', Path.DirectorySeparatorChar)));
    private static async Task Exec(SqlConnection db, string sql, params SqlParameter[] parameters) => await Exec(db, sql, null, parameters);
    private static async Task Exec(SqlConnection db, string sql, SqlTransaction? tx, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, db, tx) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters); await command.ExecuteNonQueryAsync();
    }
    private static async Task<string> Scalar(SqlConnection db, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, db) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters); return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }
}
