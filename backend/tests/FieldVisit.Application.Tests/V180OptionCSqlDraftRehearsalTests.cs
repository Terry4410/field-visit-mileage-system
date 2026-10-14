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

            // Two physically independent SQL connections race for the same
            // Organization/Entity Pending slot. A serial precheck is NOT
            // sufficient; the database filtered unique index is authoritative.
            await ExecuteAsync(db, "INSERT dbo.Locations VALUES(11),(12)");
            var raceInsert = insertPending.Replace(
                "N'10',N'UpdatePublishedLocation'",
                "N'11',N'UpdatePublishedLocation'", StringComparison.Ordinal);
            var pendingRace = await Task.WhenAll(
                TryInsertFromIndependentConnectionAsync(isolated.ConnectionString, raceInsert),
                TryInsertFromIndependentConnectionAsync(isolated.ConnectionString, raceInsert));
            Assert.Equal(1, pendingRace.Count(x => x == 0));
            Assert.Equal(1, pendingRace.Count(x => x is 2601 or 2627));
            Assert.Equal("1", await ScalarAsync(db,
                "SELECT CAST(COUNT(*) AS nvarchar(20)) FROM dbo.ChangeRequests " +
                "WHERE OrganizationId=1 AND EntityKind=N'Location' " +
                "AND EntityId=N'11' AND Status=N'Pending'"));

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

            // The same decision key must remain unique across concurrent
            // independent reviewers, not merely on sequential retries.
            var racingDecision = Guid.NewGuid();
            var decisionSql = insertDecision.Replace(
                "@decision", "'" + racingDecision.ToString("D") + "'", StringComparison.Ordinal);
            var decisionRace = await Task.WhenAll(
                TryInsertFromIndependentConnectionAsync(isolated.ConnectionString, decisionSql),
                TryInsertFromIndependentConnectionAsync(isolated.ConnectionString, decisionSql));
            Assert.Equal(1, decisionRace.Count(x => x == 0));
            Assert.Equal(1, decisionRace.Count(x => x is 2601 or 2627));
            Assert.Equal("1", await ScalarAsync(db,
                "SELECT CAST(COUNT(*) AS nvarchar(20)) FROM dbo.ChangeRequestEvents " +
                $"WHERE DecisionKey='{racingDecision:D}'"));

            // Inject an invalid audit FK AFTER a request insert, then roll
            // back the entire transaction: neither Pending nor event may leak.
            await using (var failureTx = (SqlTransaction)await db.BeginTransactionAsync())
            {
                await using (var insert = new SqlCommand(
                    insertPending.Replace(
                        "N'10',N'UpdatePublishedLocation'",
                        "N'12',N'UpdatePublishedLocation'", StringComparison.Ordinal),
                    db, failureTx))
                    await insert.ExecuteNonQueryAsync();
                var auditError = await Assert.ThrowsAsync<SqlException>(async () =>
                {
                    await using var brokenAudit = new SqlCommand("""
                        INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,
                            OccurredAt,CorrelationId,DecisionKey,DetailsJson)
                        VALUES(999999,N'Submitted',1,SYSUTCDATETIME(),NEWID(),NULL,NULL);
                        """, db, failureTx);
                    await brokenAudit.ExecuteNonQueryAsync();
                });
                Assert.Equal(547, auditError.Number);
                await failureTx.RollbackAsync();
            }
            Assert.Equal("0", await ScalarAsync(db,
                "SELECT CAST(COUNT(*) AS nvarchar(20)) FROM dbo.ChangeRequests " +
                "WHERE OrganizationId=1 AND EntityKind=N'Location' AND EntityId=N'12'"));
            Assert.Equal("0", await ScalarAsync(db,
                "SELECT CAST(COUNT(*) AS nvarchar(20)) FROM dbo.ChangeRequestEvents " +
                "WHERE ChangeRequestId=999999"));

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

    [Fact]
    public async Task Uat_readonly_preflight_executes_at_010_011_012_014_and_additive_audit_phases()
    {
        // This runs the actual preflight SQL against a random disposable localhost
        // database, never against Azure/UAT. It proves SQL compilation and phase
        // behavior, not the real UAT Entra read-only identity or target.
        var raw = Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Environment.GetEnvironmentVariable("E1_R1_REQUIRED") == "1")
                throw new InvalidOperationException("OPTIONC_ISOLATED_SQL_REQUIRED");
            return;
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

            var scriptPath = Path.Combine(rootDir, "docs", "release",
                "optionc-canonical-sql-draft", "preflight", "UAT_ReadOnly_Preflight.sql");
            var script = File.ReadAllText(scriptPath);
            Assert.Contains("ApplicationIntent=ReadOnly", script);
            Assert.Contains("PREFLIGHT_COMPLETE_READ_ONLY", script);
            // SQLCMD directives are substituted only inside this isolated
            // harness. Production invocation stays behind the guarded workflow.
            script = string.Join("\n", script.Split('\n').Where(
                line => !line.TrimStart().StartsWith(":setvar", StringComparison.OrdinalIgnoreCase)));
            var serverName = await ScalarAsync(db,
                "SELECT CONVERT(nvarchar(256),SERVERPROPERTY('ServerName'))");
            Assert.False(string.IsNullOrWhiteSpace(serverName));
            script = script.Replace("REPLACE_WITH_APPROVED_FQDN",
                    serverName.Replace("'", "''", StringComparison.Ordinal),
                    StringComparison.Ordinal)
                .Replace("REPLACE_WITH_APPROVED_DATABASE", databaseName,
                    StringComparison.Ordinal);

            await ExecuteAsync(db, script); // 010: canonical tables absent
            await ExecuteAsync(db, ReadDraft(rootDir, "1800_011", "Up.sql"));
            await ExecuteAsync(db, script); // 011: Submitted/Rejected only
            await ExecuteAsync(db, ReadDraft(rootDir, "1800_012", "Up.sql"));
            await ExecuteAsync(db, script); // 012: four canonical event types
            await ExecuteAsync(db, ReadDraft(rootDir, "1800_014", "Up.sql"));
            await ExecuteAsync(db, script); // 014: no additive audit yet

            var auditPath = Path.Combine(rootDir, "docs", "release",
                "optionc-canonical-sql-draft", "audit",
                "StageEvidenceAuditEvents.disposable.sql");
            await ExecuteAsync(db, File.ReadAllText(auditPath));
            await ExecuteAsync(db, script); // 014: canonical evidence audit extension

            // Detect a parallel audit ledger as a STOP condition.
            await ExecuteAsync(db,
                "CREATE TABLE dbo.StageEvidenceAuditEvents(Id int NOT NULL PRIMARY KEY)");
            var parallelLedger = await Assert.ThrowsAsync<SqlException>(
                () => ExecuteAsync(db, script));
            Assert.Equal(51013, parallelLedger.Number);
            await ExecuteAsync(db, "DROP TABLE dbo.StageEvidenceAuditEvents");
            await ExecuteAsync(db, script);

            // Mismatched target must fail closed before any catalog inspection.
            var wrongTarget = script.Replace(
                $"DECLARE @ExpectedDatabase sysname=N'{databaseName}'",
                "DECLARE @ExpectedDatabase sysname=N'UNAUTHORIZED_DATABASE'",
                StringComparison.Ordinal);
            Assert.NotEqual(script, wrongTarget);
            var mismatch = await Assert.ThrowsAsync<SqlException>(
                () => ExecuteAsync(db, wrongTarget));
            Assert.Equal(51002, mismatch.Number);
        }
        finally
        {
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await ExecuteAsync(admin,
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        }
    }

    private static async Task<int> TryInsertFromIndependentConnectionAsync(
        string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(connection, sql);
            return 0;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ex.Number;
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
