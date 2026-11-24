using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

// Disposable SQL Server 2022 only. Does not access Azure UAT or its data.
public sealed class LeaderApprovalRetryTransactionTests
{
    [Fact]
    public async Task Serializable_approval_unit_runs_under_enabled_sql_retry_strategy_and_rolls_back_on_error()
    {
        var password = Environment.GetEnvironmentVariable("PATHB_SQL_PASSWORD");
        if (string.IsNullOrEmpty(password))
            return; // CI drives this with its isolated ephemeral SQL Server.
        var root = new SqlConnectionStringBuilder
        {
            DataSource = "localhost,1433",
            InitialCatalog = "master",
            UserID = "sa",
            Password = password,
            Encrypt = false,
            TrustServerCertificate = true,
            Pooling = false
        };
        var dbName = "PathBLeaderRetry_" + Guid.NewGuid().ToString("N");
        await using (var master = new SqlConnection(root.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{dbName}]";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            root.InitialCatalog = dbName;
            await using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(root.ConnectionString, opts => opts.EnableRetryOnFailure(
                        5, TimeSpan.FromSeconds(10), null)).Options);
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE dbo.LeaderApprovalRetryProbe (Id int NOT NULL PRIMARY KEY)");

            // The transaction scope must include all writes and the COMMIT.
            await context.ExecuteWithRetryAsync(async ct =>
            {
                await using var tx = await context.BeginSerializableTransactionAsync(ct);
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT dbo.LeaderApprovalRetryProbe(Id) VALUES (1)", ct);
                await tx.CommitAsync(ct);
            }, CancellationToken.None);

            Assert.Equal(1, await context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.LeaderApprovalRetryProbe")
                .SingleAsync());

            // Failure before commit must not leave an approved row.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.ExecuteWithRetryAsync(async ct =>
                {
                    await using var tx = await context.BeginSerializableTransactionAsync(ct);
                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT dbo.LeaderApprovalRetryProbe(Id) VALUES (2)", ct);
                    throw new InvalidOperationException("EXPECTED_ROLLBACK");
                }, CancellationToken.None));

            Assert.Equal(1, await context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.LeaderApprovalRetryProbe")
                .SingleAsync());
        }
        finally
        {
            root.InitialCatalog = "master";
            await using var admin = new SqlConnection(root.ConnectionString);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
