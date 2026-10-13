using Microsoft.Data.SqlClient;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Runs reviewed-source *draft copies* of 011/012 ONLY on a random disposable
/// localhost SQL Server database. No Azure/UAT/Production connection accepted.
/// This is a schema rehearsal, not an approval to migrate or enable B3.
/// </summary>
public sealed class V180OptionCSqlDraftRehearsalTests
{
    [Fact]
    public async Task Canonical_011_012_create_verify_and_reject_replay_in_disposable_sql()
    {
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_ISOLATED_SQL_REQUIRED");
            return; // local developer machines without SQL Server
        }
        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        if (root.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
            || !root.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase)
            || !root.UserID.Equals("sa", StringComparison.OrdinalIgnoreCase)
            || root.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_SQL_NONLOCAL_TARGET_DENIED");
        var databaseName = "OptionCIsolated_" + Guid.NewGuid().ToString("N");
        var rootDir = FindRepoRoot();
        await using (var admin = new SqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"CREATE DATABASE [{databaseName}]");
        }
        try
        {
            var isolated = new SqlConnectionStringBuilder(root.ConnectionString)
            { InitialCatalog = databaseName, Pooling = false };
            await using var db = new SqlConnection(isolated.ConnectionString);
            await db.OpenAsync();
            await ExecuteAsync(db, """
                CREATE TABLE dbo.SchemaVersions(
                    VersionNumber nvarchar(40) NOT NULL PRIMARY KEY,
                    Description nvarchar(500) NULL,
                    AppliedAt datetime2(3) NOT NULL,
                    AppliedBy nvarchar(200) NULL);
                CREATE TABLE dbo.Organizations(OrganizationId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Teams(TeamId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Users(UserId int NOT NULL PRIMARY KEY);
                CREATE TABLE dbo.Locations(LocationId int NOT NULL PRIMARY KEY);
                INSERT dbo.Organizations VALUES(1);
                INSERT dbo.Teams VALUES(1);
                INSERT dbo.Users VALUES(1),(2);
                INSERT dbo.Locations VALUES(10);
                INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
                  VALUES(N'1.8.0-010',N'isolated bootstrap',SYSUTCDATETIME(),N'isolated');
                """);
            var up011 = ReadDraft(rootDir, "1800_011", "Up.sql");
            var up012 = ReadDraft(rootDir, "1800_012", "Up.sql");
            await ExecuteAsync(db, up011);
            Assert.Equal("1.8.0-011", await ScalarAsync(db,
                ReadDraft(rootDir, "1800_011", "Verify.sql")));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(db, up011));

            var insertPending = """
                INSERT dbo.ChangeRequests(RequestPublicId,OrganizationId,TeamId,
                    EntityKind,EntityId,OperationCode,RiskCode,ExpectedEntityRowVersion,
                    BeforeJson,ProposedJson,RequestedByUserId,SubmittedAt,Status)
                VALUES(NEWID(),1,1,N'Location',N'10',N'UpdatePublishedLocation',
                    N'High',0x0000000000000001,N'{}',N'{}',1,SYSUTCDATETIME(),N'Pending');
                """;
            await ExecuteAsync(db, insertPending);
            var duplicate = await Assert.ThrowsAsync<SqlException>(
                () => ExecuteAsync(db, insertPending));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });

            await ExecuteAsync(db, up012);
            Assert.Equal("1.8.0-012", await ScalarAsync(db,
                ReadDraft(rootDir, "1800_012", "Verify.sql")));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(db, up012));
            Assert.Equal("0", await ScalarAsync(db,
                "SELECT CAST(COUNT(*) AS nvarchar(20)) FROM sys.tables WHERE name=N'LocationChangeRequests'"));

            var decisionKey = Guid.NewGuid();
            var insertDecision = """
                INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,
                    OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                VALUES(1,N'Approved',2,SYSUTCDATETIME(),NEWID(),@decision,NULL);
                """;
            await ExecuteAsync(db, insertDecision, new SqlParameter("@decision", decisionKey));
            var replay = await Assert.ThrowsAsync<SqlException>(
                () => ExecuteAsync(db, insertDecision, new SqlParameter("@decision", decisionKey)));
            Assert.Contains(replay.Number, new[] { 2601, 2627 });

            var selfReview = """
                UPDATE dbo.ChangeRequests SET Status=N'Rejected',
                    ReviewedByUserId=RequestedByUserId,
                    ReviewedAt=SYSUTCDATETIME(),ReviewReason=N'self review'
                WHERE ChangeRequestId=1;
                """;
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(db, selfReview));
            Assert.Equal("Pending", await ScalarAsync(db,
                "SELECT Status FROM dbo.ChangeRequests WHERE ChangeRequestId=1"));
        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await ExecuteAsync(admin,
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName,
                "docs/release/optionc-canonical-sql-draft/1800_011/Up.sql")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("OPTIONC_SQL_DRAFT_ROOT_NOT_FOUND");
    }

    private static string ReadDraft(string root, string version, string file) =>
        File.ReadAllText(Path.Combine(root, "docs", "release",
            "optionc-canonical-sql-draft", version, file));

    private static async Task ExecuteAsync(SqlConnection db, string sql,
        params SqlParameter[] parameters)
    {
        await using var cmd = new SqlCommand(sql, db) { CommandTimeout = 60 };
        cmd.Parameters.AddRange(parameters);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(SqlConnection db, string sql)
    {
        await using var cmd = new SqlCommand(sql, db) { CommandTimeout = 60 };
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
    }
}
