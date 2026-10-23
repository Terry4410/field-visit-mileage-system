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

    // Install only the seven SQL-only table definitions proven by the pinned
    // 1800_001/006 source. This is NOT complete A+C/trigger/schema parity.
    var sqlOnlySqlPath = Path.Combine(output, "sql-only-core-tables.sql");
    var sqlOnlyManifestPath = Path.Combine(output, "sql-only-core-tables-manifest.json");
    if (!File.Exists(sqlOnlySqlPath) || !File.Exists(sqlOnlyManifestPath))
        throw new InvalidOperationException("SQL_ONLY_SOURCE_PACKAGE_MISSING");
    var sqlOnlyBytes = await File.ReadAllBytesAsync(sqlOnlySqlPath);
    var sqlOnlyHash = Convert.ToHexString(SHA256.HashData(sqlOnlyBytes)).ToLowerInvariant();
    using (var sqlOnlyManifest = JsonDocument.Parse(await File.ReadAllTextAsync(sqlOnlyManifestPath)))
    {
        var root = sqlOnlyManifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-sql-only-core-tables-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("sqlSha256").GetString() != sqlOnlyHash
            || root.GetProperty("requiredSqlOnlyTableCount").GetInt32() != 7)
            throw new InvalidOperationException("SQL_ONLY_SOURCE_MANIFEST_MISMATCH");
    }
    var sqlOnlyExpected = new[]
    {
        "MailDeliveryLogs", "MailOutbox", "NotificationEmailAllowlist",
        "NotificationEnvironmentPolicies", "NotificationSettingRecipients",
        "NotificationSettings", "SchemaMigrationDataBaselines"
    };
    var sqlOnlyText = Encoding.UTF8.GetString(sqlOnlyBytes);
    await using (var sqlOnlyConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await sqlOnlyConnection.OpenAsync();
        foreach (var batch in Regex.Split(sqlOnlyText, "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, sqlOnlyConnection) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var actualSqlOnly = new List<string>();
    await using (var sqlOnlyVerify = new SqlConnection(installBuilder.ConnectionString))
    {
        await sqlOnlyVerify.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT name FROM sys.tables
            WHERE schema_id=SCHEMA_ID(N'dbo')
              AND name IN
              (N'MailDeliveryLogs',N'MailOutbox',N'NotificationEmailAllowlist',
               N'NotificationEnvironmentPolicies',N'NotificationSettingRecipients',
               N'NotificationSettings',N'SchemaMigrationDataBaselines')
            ORDER BY name;
            """, sqlOnlyVerify) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            actualSqlOnly.Add(reader.GetString(0));
    }
    if (!sqlOnlyExpected.SequenceEqual(actualSqlOnly, StringComparer.Ordinal))
        throw new InvalidOperationException("SQL_ONLY_TABLE_CATALOG_MISMATCH");

    // Replay the 21 *effective* source 010 trigger definitions only after
    // every EF and SQL-only core table exists. These are not A+C 011–020.
    var triggerPath = Path.Combine(output, "effective-source-triggers.sql");
    var triggerManifestPath = Path.Combine(output, "effective-source-triggers-manifest.json");
    if (!File.Exists(triggerPath) || !File.Exists(triggerManifestPath))
        throw new InvalidOperationException("EFFECTIVE_TRIGGER_PACKAGE_MISSING");
    var triggerBytes = await File.ReadAllBytesAsync(triggerPath);
    var triggerSha = Convert.ToHexString(SHA256.HashData(triggerBytes)).ToLowerInvariant();
    var expectedTriggerNames = new HashSet<string>(StringComparer.Ordinal);
    using (var triggerManifest = JsonDocument.Parse(await File.ReadAllTextAsync(triggerManifestPath)))
    {
        var root = triggerManifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-effective-source-triggers-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("effectiveTriggerCount").GetInt32() != 21
            || root.GetProperty("sqlSha256").GetString() != triggerSha)
            throw new InvalidOperationException("EFFECTIVE_TRIGGER_MANIFEST_INVALID");
        foreach (var entry in root.GetProperty("triggers").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString() ?? "";
            if (!expectedTriggerNames.Add(name))
                throw new InvalidOperationException("EFFECTIVE_TRIGGER_DUPLICATE_NAME");
        }
    }
    if (expectedTriggerNames.Count != 21)
        throw new InvalidOperationException("EFFECTIVE_TRIGGER_SOURCE_COUNT");
    await using (var triggerConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await triggerConnection.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(triggerBytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, triggerConnection) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var installedTriggers = new HashSet<string>(StringComparer.Ordinal);
    await using (var triggerVerify = new SqlConnection(installBuilder.ConnectionString))
    {
        await triggerVerify.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT t.name, t.is_disabled
            FROM sys.triggers t
            JOIN sys.sql_modules m ON m.object_id=t.object_id
            WHERE t.parent_class=1 AND t.is_ms_shipped=0
              AND m.definition IS NOT NULL;
            """, triggerVerify) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetBoolean(1) || !installedTriggers.Add(reader.GetString(0)))
                throw new InvalidOperationException("EFFECTIVE_TRIGGER_CATALOG_STATE");
        }
    }
    if (!expectedTriggerNames.SetEquals(installedTriggers))
        throw new InvalidOperationException("EFFECTIVE_TRIGGER_CATALOG_MISMATCH");

    // Candidate A+C table shapes are exercised against this disposable DB
    // only. Their original Up.sql files explicitly forbid live execution.
    var optioncPath = Path.Combine(output, "optionc-sql-only-candidates.sql");
    var optioncManifestPath = Path.Combine(output, "optionc-sql-only-candidates-manifest.json");
    if (!File.Exists(optioncPath) || !File.Exists(optioncManifestPath))
        throw new InvalidOperationException("OPTIONC_TABLE_PROBE_SOURCE_MISSING");
    var optioncBytes = await File.ReadAllBytesAsync(optioncPath);
    var optioncSha = Convert.ToHexString(SHA256.HashData(optioncBytes)).ToLowerInvariant();
    using (var candidateManifest = JsonDocument.Parse(await File.ReadAllTextAsync(optioncManifestPath)))
    {
        var root = candidateManifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-optionc-sql-only-table-probe-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("disposableCandidateTableCount").GetInt32() != 4
            || root.GetProperty("sqlSha256").GetString() != optioncSha)
            throw new InvalidOperationException("OPTIONC_TABLE_PROBE_MANIFEST");
    }
    await using (var candidateConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await candidateConnection.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(optioncBytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, candidateConnection) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var expectedCandidateTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "LocationPublicationStages", "LocationPublishedVersions",
        "LocationCreateReservations", "LocationMergeDetails"
    };
    var candidateTables = new HashSet<string>(StringComparer.Ordinal);
    await using (var candidateVerify = new SqlConnection(installBuilder.ConnectionString))
    {
        await candidateVerify.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')
            AND name IN (N'LocationPublicationStages',N'LocationPublishedVersions',
                         N'LocationCreateReservations',N'LocationMergeDetails');
            """, candidateVerify) { CommandTimeout = 10 };
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (!candidateTables.Add(reader.GetString(0)))
                throw new InvalidOperationException("OPTIONC_TABLE_PROBE_DUPLICATE");
    }
    if (!expectedCandidateTables.SetEquals(candidateTables))
        throw new InvalidOperationException("OPTIONC_TABLE_PROBE_CATALOG_MISMATCH");

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
        sqlOnlyTableSqlSha256 = sqlOnlyHash,
        sqlOnlyInstalledTableCount = sqlOnlyExpected.Length,
        effective010TriggerSqlSha256 = triggerSha,
        installedEffectiveTriggerCount = installedTriggers.Count,
        optioncCandidateTableSqlSha256 = optioncSha,
        optioncCandidateTableCount = candidateTables.Count,
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
    Status("SQL_ONLY_SEVEN_TABLES_SQL2022_PASS");
    Status("EFFECTIVE_010_TRIGGERS_SQL2022_PASS");
    Status("OPTIONC_FOUR_CANDIDATE_TABLES_SQL2022_PASS");
    Status("FULL_V180_INSTALL_HOLD_OPTIONC_FINAL_OBJECTS_APP_COMPAT");
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
