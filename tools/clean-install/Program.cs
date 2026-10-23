using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

const string databaseName = "FieldVisitV180CleanInstall";
const string expectedHost = "localhost,1433";
const int sqlMajorVersion = 16;
var masterConnectionString = "";
var databaseCreated = false;
var exitCode = 1;

static string Require(string key) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException("INPUT_MISSING");

static void Status(string code) => Console.WriteLine($"CLEAN_INSTALL={code}");

try
{
    var argsMap = ParseArgs(args);
    var rcRoot = Path.GetFullPath(argsMap["--rc-root"]);
    var output = Path.GetFullPath(argsMap["--output"]);
    var expectedRc = Require("CLEAN_INSTALL_RC_SHA");
    if (expectedRc != "9d431ea318fcec34f04f3cc36a8fb6d01d00c48a")
        throw new InvalidOperationException("SOURCE_PIN_MISMATCH");

    var contextPath = Path.Combine(rcRoot, "backend/src/FieldVisit.Infrastructure/AppDbContext.cs");
    if (!File.Exists(contextPath)) throw new InvalidOperationException("SOURCE_FILE_MISSING");
    Directory.CreateDirectory(output);

    var password = Require("CLEAN_INSTALL_SQL_PASSWORD");
    var masterBuilder = new SqlConnectionStringBuilder
    {
        DataSource = expectedHost,
        InitialCatalog = "master",
        UserID = "sa",
        Password = password,
        Encrypt = false,
        TrustServerCertificate = true,
        ConnectTimeout = 5,
        ConnectRetryCount = 0,
        Pooling = false,
        ApplicationName = "FieldVisit-CleanInstall-Disposable-CI"
    };
    if (masterBuilder.DataSource != expectedHost || masterBuilder.InitialCatalog != "master" ||
        masterBuilder.ConnectRetryCount != 0 || masterBuilder.Pooling)
        throw new InvalidOperationException("LOCAL_TARGET_GUARD");
    masterConnectionString = masterBuilder.ConnectionString;

    var placeholder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
    {
        InitialCatalog = databaseName
    }.ConnectionString;
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(placeholder).Options;
    await using var modelContext = new AppDbContext(options);
    var relational = modelContext.Model.GetRelationalModel();
    var expectedTables = relational.Tables
        .Where(t => string.Equals(t.Schema ?? "dbo", "dbo", StringComparison.Ordinal))
        .Select(t => t.Name)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    if (expectedTables.Length == 0) throw new InvalidOperationException("MODEL_EMPTY");

    var createSql = modelContext.Database.GenerateCreateScript();
    var sqlBytes = new UTF8Encoding(false).GetBytes(createSql);
    var sqlPath = Path.Combine(output, "model-baseline.sql");
    await File.WriteAllBytesAsync(sqlPath, sqlBytes);

    var detectedSqlMajorVersion = 0;
    await using (var master = new SqlConnection(masterBuilder.ConnectionString))
    {
        await master.OpenAsync();
        detectedSqlMajorVersion = Convert.ToInt32(await new SqlCommand(
            "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))", master)
            { CommandTimeout = 5 }.ExecuteScalarAsync());
        if (detectedSqlMajorVersion != sqlMajorVersion) throw new InvalidOperationException("SQL_VERSION_MISMATCH");
        var exists = Convert.ToInt32(await new SqlCommand(
            "SELECT COUNT(*) FROM sys.databases WHERE name=@name", master)
            { CommandTimeout = 5 }.AlsoAdd("@name", databaseName).ExecuteScalarAsync());
        if (exists != 0) throw new InvalidOperationException("TARGET_NOT_EMPTY");
        await new SqlCommand("CREATE DATABASE [FieldVisitV180CleanInstall]", master)
            { CommandTimeout = 30 }.ExecuteNonQueryAsync();
        databaseCreated = true;
    }

    var installBuilder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
    {
        InitialCatalog = databaseName
    };
    await using (var install = new SqlConnection(installBuilder.ConnectionString))
    {
        await install.OpenAsync();
        var batches = Regex.Split(createSql, "(?im)^\\s*GO\\s*(?:--.*)?$")
            .Where(batch => !string.IsNullOrWhiteSpace(batch));
        foreach (var batch in batches)
        {
            await using var command = new SqlCommand(batch, install) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }

    var installedTables = new SortedSet<string>(StringComparer.Ordinal);
    var installedColumns = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    await using (var installed = new SqlConnection(installBuilder.ConnectionString))
    {
        await installed.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT s.name, t.name, c.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id
            WHERE s.name=N'dbo'
            ORDER BY t.name,c.column_id;
            """, installed) { CommandTimeout = 10 };
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(1);
            installedTables.Add(table);
            if (!installedColumns.TryGetValue(table, out var columns))
                installedColumns[table] = columns = new SortedSet<string>(StringComparer.Ordinal);
            columns.Add(reader.GetString(2));
        }
    }
    if (!expectedTables.SequenceEqual(installedTables, StringComparer.Ordinal))
        throw new InvalidOperationException("TABLE_PARITY_FAIL");

    var expectedColumns = relational.Tables
        .Where(t => string.Equals(t.Schema ?? "dbo", "dbo", StringComparison.Ordinal))
        .GroupBy(t => t.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key,
            g => new SortedSet<string>(g.SelectMany(t => t.Columns).Select(c => c.Name), StringComparer.Ordinal),
            StringComparer.Ordinal);
    if (expectedColumns.Count != installedColumns.Count || expectedColumns.Any(pair =>
            !installedColumns.TryGetValue(pair.Key, out var actual) || !pair.Value.SetEquals(actual)))
        throw new InvalidOperationException("COLUMN_PARITY_FAIL");

    var catalog = new Dictionary<string, int>(StringComparer.Ordinal);
    await using (var catalogConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await catalogConnection.OpenAsync();
        await using var catalogCommand = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM sys.foreign_keys),
              (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1),
              (SELECT COUNT(*) FROM sys.check_constraints),
              (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1),
              (SELECT COUNT(*) FROM sys.default_constraints),
              (SELECT COUNT(*) FROM sys.indexes WHERE object_id > 0 AND index_id > 0 AND type > 0),
              (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped=0),
              (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped=0 AND is_disabled=1);
            """, catalogConnection) { CommandTimeout = 10 };
        await using var reader = await catalogCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("CATALOG_CONTRACT_FAIL");
        catalog["foreignKeys"] = reader.GetInt32(0);
        catalog["foreignKeysDisabledOrUntrusted"] = reader.GetInt32(1);
        catalog["checkConstraints"] = reader.GetInt32(2);
        catalog["checkConstraintsDisabledOrUntrusted"] = reader.GetInt32(3);
        catalog["defaultConstraints"] = reader.GetInt32(4);
        catalog["indexes"] = reader.GetInt32(5);
        catalog["triggers"] = reader.GetInt32(6);
        catalog["triggersDisabled"] = reader.GetInt32(7);
    }
    if (catalog["foreignKeysDisabledOrUntrusted"] != 0 ||
        catalog["checkConstraintsDisabledOrUntrusted"] != 0 || catalog["triggersDisabled"] != 0)
        throw new InvalidOperationException("CATALOG_TRUST_STATE_FAIL");

    // Actual EF SQL write/read compatibility on a synthetic transaction; rollback is explicit.
    await using (var db = new AppDbContext(options))
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
        db.Organizations.Add(new Organization
        {
            OrganizationCode = "CI-" + Guid.NewGuid().ToString("N"),
            OrganizationName = "Synthetic Clean Install",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        if (await db.Organizations.CountAsync() != 1) throw new InvalidOperationException("EF_WRITE_READ_FAIL");
        await tx.RollbackAsync();
    }
    await using (var verifyEmpty = new AppDbContext(options))
        if (await verifyEmpty.Organizations.CountAsync() != 0)
            throw new InvalidOperationException("SYNTHETIC_ROLLBACK_FAIL");

    var appDbContextSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(contextPath))).ToLowerInvariant();
    var scriptSha = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant();
    var manifest = new
    {
        format = "field-visit-clean-install-model-baseline-v1",
        sourceRcSha = expectedRc,
        appDbContextSha256 = appDbContextSha,
        generatedSqlSha256 = scriptSha,
        sqlServerMajorVersion = detectedSqlMajorVersion,
        mappedTableCount = expectedTables.Length,
        mappedColumnCount = expectedColumns.Values.Sum(x => x.Count),
        mappedTables = expectedTables,
        catalogObjectCounts = catalog,
        scope = "EF relational model only; not the complete v1.8.0 SQL install package",
        sqlOnlyObjects = "Not installed by this baseline; inventory and reviewed target DDL remain required",
        seedData = "No accounts, roles, employees, business rows, or passwords are created"
    };
    await File.WriteAllTextAsync(Path.Combine(output, "model-baseline-manifest.json"),
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Status("MODEL_BASELINE_SQL2022_PASS");
    Status("EF_TABLE_COLUMN_PARITY_PASS");
    Status("MODEL_DDL_CONSTRAINT_TRUST_STATE_PASS");
    Status("EF_SYNTHETIC_WRITE_ROLLBACK_PASS");
    Status("FULL_V180_INSTALL_HOLD_SQL_ONLY_OBJECTS");
    Console.WriteLine($"MAPPED_TABLES={expectedTables.Length}");
    Console.WriteLine($"MAPPED_COLUMNS={expectedColumns.Values.Sum(x => x.Count)}");
    Console.WriteLine($"MODEL_BASELINE_SHA256={scriptSha}");
    exitCode = 0;
}
catch (Exception)
{
    Status("FAIL_DETAILS_SUPPRESSED");
}
finally
{
    if (databaseCreated)
    {
        try
        {
            await using var cleanup = new SqlConnection(masterConnectionString);
            await cleanup.OpenAsync();
            await using var command = new SqlCommand("""
                IF DB_ID(N'FieldVisitV180CleanInstall') IS NOT NULL
                BEGIN
                    ALTER DATABASE [FieldVisitV180CleanInstall] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [FieldVisitV180CleanInstall];
                END;
                """, cleanup) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
            Status("DISPOSABLE_DATABASE_CLEANUP_PASS");
        }
        catch (Exception)
        {
            Status("DISPOSABLE_DATABASE_CLEANUP_FAIL");
            exitCode = 1;
        }
    }
}
return exitCode;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--rc-root" or "--output"))
            throw new InvalidOperationException("ARGUMENT_CONTRACT");
        result.Add(args[i], args[i + 1]);
    }
    if (result.Count != 2) throw new InvalidOperationException("ARGUMENT_CONTRACT");
    return result;
}

internal static class SqlCommandExtensions
{
    public static SqlCommand AlsoAdd(this SqlCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
        return command;
    }
}
