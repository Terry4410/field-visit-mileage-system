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

    // New consolidated clean-install version ledger, not a replay of original
    // historical migration versions. Leave it EMPTY until final target-state
    // acceptance; never fabricate 001–020 applied markers for a fresh build.
    if (expectedTables.Contains("SchemaVersions", StringComparer.Ordinal))
        throw new InvalidOperationException("LEDGER_MODEL_SOURCE_COLLISION");
    var ledgerSqlPath = Path.GetFullPath(
        Path.Combine("database", "clean-install", "v180", "00_schema_versions.sql"));
    if (!File.Exists(ledgerSqlPath))
        throw new InvalidOperationException("CLEAN_LEDGER_SOURCE_MISSING");
    var ledgerSqlBytes = await File.ReadAllBytesAsync(ledgerSqlPath);
    var ledgerSqlSha = Convert.ToHexString(SHA256.HashData(ledgerSqlBytes)).ToLowerInvariant();
    await using (var ledgerConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await ledgerConnection.OpenAsync();
        await using var createLedger = new SqlCommand(Encoding.UTF8.GetString(ledgerSqlBytes), ledgerConnection)
        { CommandTimeout = 30 };
        await createLedger.ExecuteNonQueryAsync();
        var ledgerColumns = new HashSet<string>(StringComparer.Ordinal);
        await using (var columns = new SqlCommand("""
            SELECT name FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.SchemaVersions', N'U');
            """, ledgerConnection) { CommandTimeout = 10 })
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                ledgerColumns.Add(reader.GetString(0));
        }
        if (!ledgerColumns.SetEquals(new[]
            { "VersionNumber", "Description", "AppliedAt", "AppliedBy" }))
            throw new InvalidOperationException("CLEAN_LEDGER_COLUMN_SHAPE");
        await using var emptyLedger = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.SchemaVersions", ledgerConnection)
        { CommandTimeout = 10 };
        if (Convert.ToInt64(await emptyLedger.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("CLEAN_LEDGER_FAKE_HISTORY");
    }

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

    // Test 13 secondary indexes sourced from SQL-only 006/014/017 tables.
    // This is a selected source-scoped index set, not all final indexes.
    var extraIndexPath = Path.Combine(output, "sql-only-extra-indexes.sql");
    var extraIndexManifestPath = Path.Combine(output, "sql-only-extra-indexes-manifest.json");
    if (!File.Exists(extraIndexPath) || !File.Exists(extraIndexManifestPath))
        throw new InvalidOperationException("SQL_ONLY_INDEX_PACKAGE_MISSING");
    var extraIndexBytes = await File.ReadAllBytesAsync(extraIndexPath);
    var extraIndexSha = Convert.ToHexString(SHA256.HashData(extraIndexBytes)).ToLowerInvariant();
    var expectedIndexMap = new Dictionary<string, string>(StringComparer.Ordinal);
    using (var extraIndexManifest = JsonDocument.Parse(await File.ReadAllTextAsync(extraIndexManifestPath)))
    {
        var root = extraIndexManifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-sql-only-index-probe-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("indexCount").GetInt32() != 13
            || root.GetProperty("sqlSha256").GetString() != extraIndexSha)
            throw new InvalidOperationException("SQL_ONLY_INDEX_MANIFEST_MISMATCH");
        foreach (var entry in root.GetProperty("indexes").EnumerateArray())
        {
            if (!expectedIndexMap.TryAdd(entry.GetProperty("name").GetString() ?? "",
                                         entry.GetProperty("table").GetString() ?? ""))
                throw new InvalidOperationException("SQL_ONLY_INDEX_DUPLICATE_NAME");
        }
    }
    if (expectedIndexMap.Count != 13)
        throw new InvalidOperationException("SQL_ONLY_INDEX_SOURCE_COUNT");
    await using (var indexInstall = new SqlConnection(installBuilder.ConnectionString))
    {
        await indexInstall.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(extraIndexBytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, indexInstall) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var actualIndexes = new Dictionary<string, string>(StringComparer.Ordinal);
    await using (var indexVerify = new SqlConnection(installBuilder.ConnectionString))
    {
        await indexVerify.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT i.name, s.name, t.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id=i.object_id
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE i.index_id>0 AND i.name IS NOT NULL
              AND s.name=N'dbo';
            """, indexVerify) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (expectedIndexMap.ContainsKey(name))
            {
                if (!actualIndexes.TryAdd(name, reader.GetString(1) + "." + reader.GetString(2)))
                    throw new InvalidOperationException("SQL_ONLY_INDEX_DUPLICATE_CATALOG");
            }
        }
    }
    if (actualIndexes.Count != expectedIndexMap.Count ||
        expectedIndexMap.Any(entry => !actualIndexes.TryGetValue(entry.Key, out var table)
                                   || table != entry.Value))
        throw new InvalidOperationException("SQL_ONLY_INDEX_CATALOG_MISMATCH");

    // Apply all 9 source-backed route governance CHECKs in final 007/009/010
    // target state, with one-stop support and background-job reason.
    // No predecessor migration or artificial historical marker is inserted.
    var reasonDdlPath = Path.GetFullPath(Path.Combine(
        "database", "clean-install", "v180", "20_route_reason_check.sql"));
    if (!File.Exists(reasonDdlPath))
        throw new InvalidOperationException("CLEAN_ROUTE_CHECK_DDL_MISSING");
    var reasonDdlBytes = await File.ReadAllBytesAsync(reasonDdlPath);
    var reasonDdlSha = Convert.ToHexString(SHA256.HashData(reasonDdlBytes)).ToLowerInvariant();
    var routeCheckNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "CK_RouteCalculationAttempts_Basis",
        "CK_RouteCalculationAttempts_Reason",
        "CK_RouteCalculationAttempts_LeaderBasis",
        "CK_RouteCalculationAttempts_Vehicle",
        "CK_RouteCalculationAttempts_Mode",
        "CK_RouteCalculationAttempts_VehicleMode",
        "CK_RouteCalculationAttempts_StopCount",
        "CK_RouteCalculationAttempts_Status",
        "CK_RouteCalculationAttempts_Result"
    };
    await using (var routeConn = new SqlConnection(installBuilder.ConnectionString))
    {
        await routeConn.OpenAsync();
        await using var installChecks = new SqlCommand(Encoding.UTF8.GetString(reasonDdlBytes), routeConn)
        { CommandTimeout = 30 };
        await installChecks.ExecuteNonQueryAsync();
        var actualNames = new HashSet<string>(StringComparer.Ordinal);
        await using var proof = new SqlCommand("""
            SELECT name, definition, is_disabled, is_not_trusted
            FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(N'dbo.RouteCalculationAttempts',N'U')
              AND name LIKE N'CK_RouteCalculationAttempts[_]%';
            """, routeConn) { CommandTimeout = 10 };
        await using var reader = await proof.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!routeCheckNames.Contains(name) || !actualNames.Add(name) ||
                reader.GetBoolean(2) || reader.GetBoolean(3))
                throw new InvalidOperationException("CLEAN_ROUTE_CHECK_CATALOG_STATE");
            var definition = reader.GetString(1).ToLowerInvariant();
            if (name == "CK_RouteCalculationAttempts_Reason" &&
                !new[] { "visitorcalculate", "leaderretry",
                         "correctionrecalculate", "backgroundmileagejob" }
                     .All(x => definition.Contains(x, StringComparison.Ordinal)))
                throw new InvalidOperationException("CLEAN_ROUTE_REASON_VALUES");
            if (name == "CK_RouteCalculationAttempts_StopCount" &&
                !definition.Replace(" ", "").Replace("[", "").Replace("]", "")
                           .Contains("stopcount>=(1)", StringComparison.Ordinal) &&
                !definition.Replace(" ", "").Replace("[", "").Replace("]", "")
                           .Contains("stopcount>=1", StringComparison.Ordinal))
                throw new InvalidOperationException("CLEAN_ROUTE_ONESTOP_RULE");
        }
        if (!routeCheckNames.SetEquals(actualNames))
            throw new InvalidOperationException("CLEAN_ROUTE_NINE_CHECKS_MISSING");
    }

    // Complement the nine already-installed route checks with the nine
    // independent Geocoding/MileageGovernance/Mileage/Snapshot predicates
    // pinned to 1800_007. Never reinstall overlapping route constraints.
    var check007Path = Path.Combine(output, "007_source_checks.sql");
    var check007ManifestPath = Path.Combine(output, "007_source_checks-manifest.json");
    if (!File.Exists(check007Path) || !File.Exists(check007ManifestPath))
        throw new InvalidOperationException("CHECK_007_SOURCE_PACKAGE_MISSING");
    var check007Bytes = await File.ReadAllBytesAsync(check007Path);
    var check007Sha = Convert.ToHexString(SHA256.HashData(check007Bytes)).ToLowerInvariant();
    var expectedChecks007 = new Dictionary<string, string>(StringComparer.Ordinal);
    using (var check007Manifest = JsonDocument.Parse(await File.ReadAllTextAsync(check007ManifestPath)))
    {
        var root = check007Manifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-source-checks-007-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("count").GetInt32() != 9
            || root.GetProperty("sqlSha256").GetString() != check007Sha)
            throw new InvalidOperationException("CHECK_007_MANIFEST_INVALID");
        foreach (var entry in root.GetProperty("checks").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString() ?? "";
            var table = entry.GetProperty("table").GetString() ?? "";
            if (!expectedChecks007.TryAdd(name, table)
                || name.StartsWith("CK_RouteCalculationAttempts_", StringComparison.Ordinal))
                throw new InvalidOperationException("CHECK_007_SOURCE_OVERLAP");
        }
    }
    if (expectedChecks007.Count != 9)
        throw new InvalidOperationException("CHECK_007_EXPECTED_COUNT");
    await using (var checkInstall = new SqlConnection(installBuilder.ConnectionString))
    {
        await checkInstall.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(check007Bytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, checkInstall)
            { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var checked007 = new HashSet<string>(StringComparer.Ordinal);
    await using (var checkCatalog = new SqlConnection(installBuilder.ConnectionString))
    {
        await checkCatalog.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, s.name, t.name, c.is_disabled, c.is_not_trusted, c.definition
            FROM sys.check_constraints c
            JOIN sys.tables t ON t.object_id=c.parent_object_id
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE s.name=N'dbo';
            """, checkCatalog) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!expectedChecks007.TryGetValue(name, out var expectedTable))
                continue;
            if (reader.GetString(1) + "." + reader.GetString(2) != expectedTable
                || reader.GetBoolean(3) || reader.GetBoolean(4)
                || reader.IsDBNull(5) || !checked007.Add(name))
                throw new InvalidOperationException("CHECK_007_CATALOG_STATE_MISMATCH");
        }
    }
    if (checked007.Count != 9)
        throw new InvalidOperationException("CHECK_007_CATALOG_INCOMPLETE");

    // Replay the 17 source-pinned 001-004 period/deployment/location CHECKs.
    // These complement the 007 and 009/010 scopes without duplicates.
    var checks001Path = Path.Combine(output, "001_004_source_checks.sql");
    var checks001ManifestPath = Path.Combine(output, "001_004_source_checks-manifest.json");
    if (!File.Exists(checks001Path) || !File.Exists(checks001ManifestPath))
        throw new InvalidOperationException("CHECK_001_004_SOURCE_PACKAGE_MISSING");
    var checks001Bytes = await File.ReadAllBytesAsync(checks001Path);
    var checks001Sha = Convert.ToHexString(SHA256.HashData(checks001Bytes)).ToLowerInvariant();
    var expectedChecks001 = new Dictionary<string, string>(StringComparer.Ordinal);
    using (var checks001Manifest = JsonDocument.Parse(await File.ReadAllTextAsync(checks001ManifestPath)))
    {
        var root = checks001Manifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-source-checks-001-004-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("count").GetInt32() != 17
            || root.GetProperty("sqlSha256").GetString() != checks001Sha)
            throw new InvalidOperationException("CHECK_001_004_MANIFEST_INVALID");
        foreach (var entry in root.GetProperty("checks").EnumerateArray())
        {
            if (!expectedChecks001.TryAdd(entry.GetProperty("name").GetString() ?? "",
                                          entry.GetProperty("table").GetString() ?? ""))
                throw new InvalidOperationException("CHECK_001_004_DUPLICATE_NAME");
        }
    }
    if (expectedChecks001.Count != 17)
        throw new InvalidOperationException("CHECK_001_004_SCOPE_MISMATCH");
    await using (var install001 = new SqlConnection(installBuilder.ConnectionString))
    {
        await install001.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(checks001Bytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, install001) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var actualChecks001 = new HashSet<string>(StringComparer.Ordinal);
    await using (var catalog001 = new SqlConnection(installBuilder.ConnectionString))
    {
        await catalog001.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, s.name, t.name, c.is_disabled, c.is_not_trusted, c.definition
            FROM sys.check_constraints c
            JOIN sys.tables t ON t.object_id=c.parent_object_id
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE s.name=N'dbo';
            """, catalog001) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!expectedChecks001.TryGetValue(name, out var expectedTable))
                continue;
            if (reader.GetString(1) + "." + reader.GetString(2) != expectedTable
                || reader.GetBoolean(3) || reader.GetBoolean(4)
                || reader.IsDBNull(5) || !actualChecks001.Add(name))
                throw new InvalidOperationException("CHECK_001_004_CATALOG_STATE");
        }
    }
    if (actualChecks001.Count != 17)
        throw new InvalidOperationException("CHECK_001_004_CATALOG_INCOMPLETE");

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

    // After EVERY candidate layer is installed, prove that the whole dbo
    // catalog has EXACTLY the expected tables (rather than merely checking
    // each incremental subset). This is a *scoped candidate* completeness
    // assertion, NOT final A+C or IT handover certification.
    var expectedCandidateCatalog = new HashSet<string>(expectedTables, StringComparer.Ordinal);
    expectedCandidateCatalog.UnionWith(sqlOnlyExpected);
    expectedCandidateCatalog.UnionWith(expectedCandidateTables);
    if (!expectedCandidateCatalog.Add("SchemaVersions") ||
        expectedCandidateCatalog.Count != 69)
        throw new InvalidOperationException("CLEAN_CATALOG_EXPECTED_SET_INVALID");
    var installedCandidateCatalog = new HashSet<string>(StringComparer.Ordinal);
    await using (var finalCatalogConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await finalCatalogConnection.OpenAsync();
        await using var finalCommand = new SqlCommand("""
            SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo');
            """, finalCatalogConnection) { CommandTimeout = 10 };
        await using var reader = await finalCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (!installedCandidateCatalog.Add(reader.GetString(0)))
                throw new InvalidOperationException("CLEAN_CATALOG_DUPLICATE_TABLE");
    }
    if (!expectedCandidateCatalog.SetEquals(installedCandidateCatalog))
        throw new InvalidOperationException("CLEAN_CATALOG_TABLE_SET_MISMATCH");

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

    // Scope: actual EF persistence for a synthetic Visitor trip and audit,
    // with a distinct Leader identity. This does NOT prove API RBAC/approval.
    await using (var businessDb = new AppDbContext(options))
    await using (var businessTx = await businessDb.Database.BeginTransactionAsync())
    {
        var now = DateTime.UtcNow;
        var org = new Organization
        {
            OrganizationCode = "CI-" + Guid.NewGuid().ToString("N")[..12],
            OrganizationName = "Disposable Integration Organization",
            IsActive = true, CreatedAt = now
        };
        businessDb.Organizations.Add(org);
        await businessDb.SaveChangesAsync();
        var team = new Team
        {
            OrganizationId = org.OrganizationId,
            TeamCode = "CI-" + Guid.NewGuid().ToString("N")[..12],
            TeamName = "Disposable Team", IsActive = true, CreatedAt = now
        };
        businessDb.Teams.Add(team);
        await businessDb.SaveChangesAsync();
        var visitor = new User
        {
            OrganizationId = org.OrganizationId, TeamId = team.TeamId,
            DisplayName = "Synthetic Visitor", IsActive = true, CreatedAt = now
        };
        var leader = new User
        {
            OrganizationId = org.OrganizationId, TeamId = team.TeamId,
            DisplayName = "Synthetic Leader", IsActive = true, CreatedAt = now
        };
        businessDb.Users.AddRange(visitor, leader);
        businessDb.Roles.AddRange(
            new Role { RoleCode = "Visitor", RoleName = "Visitor", IsActive = true, CreatedAt = now },
            new Role { RoleCode = "Leader", RoleName = "Leader", IsActive = true, CreatedAt = now });
        await businessDb.SaveChangesAsync();
        var visitorRole = await businessDb.Roles.SingleAsync(x => x.RoleCode == "Visitor");
        var leaderRole = await businessDb.Roles.SingleAsync(x => x.RoleCode == "Leader");
        businessDb.UserRoles.AddRange(
            new UserRole { UserId = visitor.UserId, RoleId = visitorRole.RoleId, AssignedAt = now },
            new UserRole { UserId = leader.UserId, RoleId = leaderRole.RoleId, AssignedAt = now });
        await businessDb.SaveChangesAsync();

        var trip = new VisitTrip
        {
            TripNo = "CI-" + Guid.NewGuid().ToString("N")[..12],
            UserId = visitor.UserId, OrganizationId = org.OrganizationId,
            TeamId = team.TeamId, VisitDate = DateOnly.FromDateTime(now),
            Status = "Draft", Purpose = "Disposable SQL compatibility smoke",
            CreatedAt = now, CreatedByUserId = visitor.UserId
        };
        businessDb.VisitTrips.Add(trip);
        await businessDb.SaveChangesAsync();
        businessDb.AuditLogs.Add(new AuditLog
        {
            UserId = visitor.UserId, EntityType = "VisitTrip",
            EntityId = trip.VisitTripId.ToString(),
            Action = "Created", CreatedAt = now
        });
        await businessDb.SaveChangesAsync();
        // Real SQL2022 check: one-stop calculation and background-job reasons
        // survive source-derived CHECK/trigger contracts. Reject an invalid
        // reason at the database boundary; never treat this as API RBAC proof.
        var visitorAttempt = new RouteCalculationAttempt
        {
            VisitTripId = trip.VisitTripId, BasisType = "Draft",
            CalculationReason = "VisitorCalculate", RequestedVehicleType = "Car",
            TravelMode = "DRIVE", Provider = "SyntheticProvider",
            StopCount = 1, RequestBasisHash = new byte[32],
            Status = "Pending", CorrelationId = Guid.NewGuid(),
            RequestedAt = now, RequestedByUserId = visitor.UserId
        };
        var workerAttempt = new RouteCalculationAttempt
        {
            VisitTripId = trip.VisitTripId, BasisType = "Draft",
            CalculationReason = "BackgroundMileageJob", RequestedVehicleType = "Car",
            TravelMode = "DRIVE", Provider = "SyntheticProvider",
            StopCount = 1, RequestBasisHash = new byte[32],
            Status = "Pending", CorrelationId = Guid.NewGuid(),
            RequestedAt = now, RequestedByUserId = leader.UserId
        };
        businessDb.RouteCalculationAttempts.AddRange(visitorAttempt, workerAttempt);
        await businessDb.SaveChangesAsync();
        if (await businessDb.RouteCalculationAttempts.CountAsync(x =>
                x.VisitTripId == trip.VisitTripId && x.StopCount == 1) != 2)
            throw new InvalidOperationException("SYNTHETIC_ONESTOP_ROUTE_SQL_SMOKE_FAIL");
        if (!await businessDb.VisitTrips.AnyAsync(x => x.VisitTripId == trip.VisitTripId &&
                                                        x.UserId == visitor.UserId &&
                                                        x.TeamId == team.TeamId &&
                                                        x.Status == "Draft") ||
            !await businessDb.AuditLogs.AnyAsync(x => x.EntityType == "VisitTrip" &&
                                                    x.EntityId == trip.VisitTripId.ToString() &&
                                                    x.Action == "Created"))
            throw new InvalidOperationException("SYNTHETIC_TRIP_AUDIT_SQL_SMOKE_FAIL");
        var invalidReasonDenied = false;
        try
        {
            var invalidReason = "InvalidReasonMustFail";
            await businessDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.RouteCalculationAttempts SET CalculationReason={invalidReason} WHERE RouteCalculationAttemptId={visitorAttempt.RouteCalculationAttemptId}");
        }
        catch (SqlException error) when (error.Number == 547)
        {
            invalidReasonDenied = true;
        }
        if (!invalidReasonDenied)
            throw new InvalidOperationException("SYNTHETIC_ROUTE_INVALID_REASON_WAS_ALLOWED");
        await businessTx.RollbackAsync();
    }
    await using (var emptyBusiness = new AppDbContext(options))
    {
        if (await emptyBusiness.Organizations.AnyAsync() ||
            await emptyBusiness.Users.AnyAsync() ||
            await emptyBusiness.VisitTrips.AnyAsync() ||
            await emptyBusiness.AuditLogs.AnyAsync())
            throw new InvalidOperationException("SYNTHETIC_BUSINESS_ROLLBACK_FAIL");
    }

    var appDbContextSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(contextPath))).ToLowerInvariant();
    var scriptSha = Convert.ToHexString(SHA256.HashData(sqlBytes)).ToLowerInvariant();
    var manifest = new
    {
        format = "field-visit-clean-install-model-baseline-v1",
        sourceRcSha = expectedRc,
        appDbContextSha256 = appDbContextSha,
        generatedSqlSha256 = scriptSha,
        sqlOnlyTableSqlSha256 = sqlOnlyHash,
        newSchemaVersionLedgerSha256 = ledgerSqlSha,
        versionLedgerStatus = "EMPTY_NEW_BASELINE_NO_HISTORICAL_MARKERS",
        sourceDerivedRouteChecksSqlSha256 = reasonDdlSha,
        trustedRouteCheckCount = routeCheckNames.Count,
        sourceDerived007ChecksSha256 = check007Sha,
        installedSource007CheckCount = checked007.Count,
        sourceDerived001004ChecksSha256 = checks001Sha,
        installedSource001004CheckCount = actualChecks001.Count,
        sqlOnlyInstalledTableCount = sqlOnlyExpected.Length,
        effective010TriggerSqlSha256 = triggerSha,
        installedEffectiveTriggerCount = installedTriggers.Count,
        optioncCandidateTableSqlSha256 = optioncSha,
        optioncCandidateTableCount = candidateTables.Count,
        sqlOnlyIndexSqlSha256 = extraIndexSha,
        installedSqlOnlyIndexCount = actualIndexes.Count,
        sqlServerMajorVersion = detectedSqlMajorVersion,
        mappedTableCount = expectedTables.Length,
        mappedColumnCount = expectedColumns.Values.Sum(x => x.Count),
        verifiedCandidateCatalogTableCount = installedCandidateCatalog.Count,
        verifiedCandidateCatalogTableNames = installedCandidateCatalog.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        mappedTables = expectedTables,
        catalogObjectCounts = catalog,
        scope = "Disposable candidate only: EF 57 tables + SQL-only 11 tables + empty new SchemaVersions + effective 010 triggers + selected indexes and trusted 010 check; NOT a complete IT or UAT installer",
        sqlOnlyObjects = "Seven source-derived core tables and four disposable Option C candidate tables installed; final A+C 011-020 objects, security and complete migration-only facets remain unverified",
        seedData = "No accounts, roles, employees, business rows, or passwords are created"
    };
    await File.WriteAllTextAsync(Path.Combine(output, "model-baseline-manifest.json"),
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Status("MODEL_BASELINE_SQL2022_PASS");
    Status("EF_TABLE_COLUMN_PARITY_PASS");
    Status("MODEL_DDL_CONSTRAINT_TRUST_STATE_PASS");
    Status("EF_SYNTHETIC_WRITE_ROLLBACK_PASS");
    Status("EMPTY_NEW_SCHEMA_VERSION_LEDGER_SQL2022_PASS");
    Status("TRUSTED_007_009_010_ROUTE_CHECKS_SQL2022_PASS");
    Status("SOURCE_007_NINE_OTHER_CHECKS_SQL2022_PASS");
    Status("SOURCE_001_004_SEVENTEEN_CHECKS_SQL2022_PASS");
    Status("SQL_ONLY_SEVEN_TABLES_SQL2022_PASS");
    Status("EFFECTIVE_010_TRIGGERS_SQL2022_PASS");
    Status("OPTIONC_FOUR_CANDIDATE_TABLES_SQL2022_PASS");
    Status("SQL_ONLY_EXTRA_INDEXES_SQL2022_PASS");
    Status("SCOPED_CANDIDATE_69_TABLE_CATALOG_PASS");
    Status("SYNTHETIC_VISITOR_TRIP_AUDIT_EF_SQL_SMOKE_PASS");
    Status("SYNTHETIC_ONESTOP_ROUTE_REASON_NEGATIVE_SQL2022_PASS");
    Status("FULL_V180_INSTALL_HOLD_OTHER_OBJECTS_APP_COMPAT");
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
