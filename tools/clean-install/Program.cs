using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Option C production code deliberately permits publication flows only when
// the opened SQL connection is loopback and the database has this disposable
// prefix. The fixed local target is still unique to this workflow run.
const string databaseName = "OptionCIsolated_FieldVisitV180CleanInstall";
const string expectedHost = "localhost,1433";
const int sqlMajorVersion = 16;
var masterConnectionString = "";
var databaseCreated = false;
var exitCode = 1;
// Stage identifiers are fixed, synthetic-only and reveal no SQL, identity or data.
var stage = "INIT";

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

    stage = "EF_MODEL_DDL";
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
        await new SqlCommand("CREATE DATABASE [OptionCIsolated_FieldVisitV180CleanInstall]", master)
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
    stage = "CLEAN_LEDGER";
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
    stage = "CORE_TABLES";
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
    stage = "EFFECTIVE_TRIGGERS";
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
    stage = "OPTIONC_TABLES";
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
    stage = "SQL_ONLY_INDEXES";
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
    stage = "ROUTE_CHECKS";
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
    stage = "CHECKS_007";
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
    stage = "CHECKS_001_004";
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

    // 1800_005 contains four non-EF CHECKs and five non-EF indexes.
    // This is source-exact target DDL extracted from immutable RC, not an
    // execution/claim of historical migration 005 or any marker insertion.
    stage = "SOURCE_005_PROJECT_RATE_CONTRACT";
    var source005File = Path.Combine(rcRoot,
        "database/migrations/1800_005_project_visit_rate_lifecycle/Up.sql");
    var source005Bytes = await File.ReadAllBytesAsync(source005File);
    var source005 = Encoding.UTF8.GetString(source005Bytes);
    var source005Sha = Convert.ToHexString(SHA256.HashData(source005Bytes)).ToLowerInvariant();
    var controls005 = new List<string>();
    var checks005 = new (string Table, string Name)[]
    {
        ("Projects", "CK_Projects_Dates"),
        ("MileageRateRules", "CK_MileageRateRules_VehicleType"),
        ("MileageRateRules", "CK_MileageRateRules_Dates"),
        ("MileageRateRules", "CK_MileageRateRules_NonNegativeRate")
    };
    var indexes005 = new (string Table, string Name)[]
    {
        ("Projects", "IX_Projects_Search"),
        ("VisitTypes", "UX_VisitTypes_VisitTypeCode"),
        ("VisitTypes", "IX_VisitTypes_Active_Sort"),
        ("MileageRateRules", "UX_MileageRateRules_Scope_Vehicle_Start"),
        ("MileageRateRules", "IX_MileageRateRules_AsOf")
    };
    foreach (var (table, name) in checks005)
    {
        var matches = Regex.Matches(source005,
            @"\bCONSTRAINT\s+" + Regex.Escape(name) + @"\s+CHECK\s*(?=\()",
            RegexOptions.IgnoreCase);
        if (matches.Count != 1)
            throw new InvalidOperationException("SOURCE_005_CHECK_NOT_UNIQUE");
        var paren = source005.IndexOf('(', matches[0].Index + matches[0].Length);
        var depth = 0;
        var inQuote = false;
        var end = -1;
        for (var n = paren; n < source005.Length; n++)
        {
            var ch = source005[n];
            if (ch == '\'')
            {
                if (inQuote && n + 1 < source005.Length && source005[n + 1] == '\'')
                    n++;
                else
                    inQuote = !inQuote;
            }
            else if (!inQuote && ch == '(') depth++;
            else if (!inQuote && ch == ')' && --depth == 0) { end = n; break; }
        }
        if (paren < 0 || end < paren || inQuote)
            throw new InvalidOperationException("SOURCE_005_CHECK_PARENS");
        var checkDdl = source005.Substring(matches[0].Index,
            end - matches[0].Index + 1);
        controls005.Add("IF OBJECT_ID(N'dbo." + name + "',N'C') IS NOT NULL " +
            "THROW 56150,N'SOURCE_005_CHECK_ALREADY_EXISTS',1;\n" +
            "ALTER TABLE dbo." + table + " WITH CHECK ADD " + checkDdl + ";\n" +
            "ALTER TABLE dbo." + table + " CHECK CONSTRAINT " + name + ";\nGO\n");
    }
    foreach (var (table, name) in indexes005)
    {
        var matches = Regex.Matches(source005,
            @"\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+" + Regex.Escape(name)
             + @"\s+ON\s+dbo\." + Regex.Escape(table) + @"\s*\(",
            RegexOptions.IgnoreCase);
        if (matches.Count != 1)
            throw new InvalidOperationException("SOURCE_005_INDEX_NOT_UNIQUE");
        var end = source005.IndexOf(';', matches[0].Index + matches[0].Length);
        if (end < 0 || end - matches[0].Index > 3000)
            throw new InvalidOperationException("SOURCE_005_INDEX_SYNTAX");
        var indexDdl = source005.Substring(matches[0].Index,
            end - matches[0].Index + 1);
        controls005.Add("IF EXISTS(SELECT 1 FROM sys.indexes WHERE " +
            "object_id=OBJECT_ID(N'dbo." + table + "',N'U') AND name=N'" + name +
            "') THROW 56151,N'SOURCE_005_INDEX_ALREADY_EXISTS',1;\n" +
            indexDdl + "\nGO\n");
    }
    if (controls005.Count != 9)
        throw new InvalidOperationException("SOURCE_005_EXPECTED_SET");
    var controls005Bytes = Encoding.UTF8.GetBytes(
        "-- Source-bound disposable final 005 controls. No migrations replayed.\n"
        + string.Join("\n", controls005));
    var controls005Sha = Convert.ToHexString(
        SHA256.HashData(controls005Bytes)).ToLowerInvariant();
    await File.WriteAllBytesAsync(Path.Combine(output, "005_source_controls.sql"),
        controls005Bytes);
    await File.WriteAllTextAsync(Path.Combine(output, "005_source_controls-manifest.json"),
        JsonSerializer.Serialize(new
        {
            format = "field-visit-v180-source-005-controls-v1",
            rcSourceSha = expectedRc,
            sourcePath = "database/migrations/1800_005_project_visit_rate_lifecycle/Up.sql",
            sourceSha256 = source005Sha,
            sqlSha256 = controls005Sha,
            checks = checks005.Select(x => x.Name).ToArray(),
            indexes = indexes005.Select(x => x.Name).ToArray(),
            scope = "NONDEPLOY_ONLY_SOURCE_CONTRACT_NOT_COMPLETE_SCHEMA"
        }) + "\n");
    await using (var sql005 = new SqlConnection(installBuilder.ConnectionString))
    {
        await sql005.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(controls005Bytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await using var cmd = new SqlCommand(batch, sql005) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync();
        }
        await using var inspect = new SqlCommand("""
            SELECT
             (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN
               (N'CK_Projects_Dates',N'CK_MileageRateRules_VehicleType',
                N'CK_MileageRateRules_Dates',N'CK_MileageRateRules_NonNegativeRate')
               AND is_disabled=0 AND is_not_trusted=0 AND definition IS NOT NULL),
             (SELECT COUNT(*) FROM sys.indexes WHERE name IN
               (N'IX_Projects_Search',N'UX_VisitTypes_VisitTypeCode',
                N'IX_VisitTypes_Active_Sort',
                N'UX_MileageRateRules_Scope_Vehicle_Start',
                N'IX_MileageRateRules_AsOf') AND is_disabled=0),
             (SELECT COUNT(*) FROM sys.indexes WHERE name IN
                (N'UX_VisitTypes_VisitTypeCode',
                 N'UX_MileageRateRules_Scope_Vehicle_Start') AND is_unique=1),
             (SELECT COUNT(*) FROM sys.indexes WHERE
                name=N'UX_MileageRateRules_Scope_Vehicle_Start'
                AND is_unique=1 AND has_filter=1 AND filter_definition IS NOT NULL);
            """, sql005) { CommandTimeout = 10 };
        await using var rows = await inspect.ExecuteReaderAsync();
        if (!await rows.ReadAsync() || rows.GetInt32(0) != 4 ||
            rows.GetInt32(1) != 5 || rows.GetInt32(2) != 2 ||
            rows.GetInt32(3) != 1)
            throw new InvalidOperationException("SOURCE_005_CATALOG_MISMATCH");
    }
    Status("SOURCE_005_PROJECT_RATE_CHECKS_INDEXES_SQL2022_PASS");

    // Apply final A+C 011–020 CHECK states on disposable clean SQL2022.
    // Eight EF-origin and one 014 candidate checks are replaced under a
    // source-pinned explicit allowlist, not bypassed or disabled.
    stage = "OPTIONC_FINAL_CHECKS";
    var optioncChecksPath = Path.Combine(output, "optionc-final-target-checks.sql");
    var optioncChecksManifestPath = Path.Combine(output, "optionc-final-target-checks-manifest.json");
    if (!File.Exists(optioncChecksPath) || !File.Exists(optioncChecksManifestPath))
        throw new InvalidOperationException("OPTIONC_FINAL_CHECK_PACKAGE_MISSING");
    var optioncChecksBytes = await File.ReadAllBytesAsync(optioncChecksPath);
    var optioncChecksSha = Convert.ToHexString(SHA256.HashData(optioncChecksBytes)).ToLowerInvariant();
    var expectedOptionCChecks = new Dictionary<string, string>(StringComparer.Ordinal);
    var expectedReplacementChecks = new HashSet<string>(StringComparer.Ordinal)
    {
        "CK_B3_ChangeRequests_KnownCodes",
        "CK_B3_ChangeRequests_KnownStatus",
        "CK_B3_ChangeRequests_ExpectedLocationVersion",
        "CK_B3_ChangeRequests_ProposedJson",
        "CK_B3_ChangeRequests_ReviewState",
        "CK_B3_ChangeRequestEvents_EventType",
        "CK_B3_ChangeRequestEvents_DetailsJson",
        "CK_B3_ChangeRequestEvents_DecisionState",
        "CK_OptionC_Published_Origin"
    };
    var actualReplacementChecks = new HashSet<string>(StringComparer.Ordinal);
    using (var optioncFinalManifest = JsonDocument.Parse(await File.ReadAllTextAsync(optioncChecksManifestPath)))
    {
        var root = optioncFinalManifest.RootElement;
        if (root.GetProperty("format").GetString() != "field-visit-v180-optionc-effective-checks-v1"
            || root.GetProperty("rcSourceSha").GetString() != expectedRc
            || root.GetProperty("checkCount").GetInt32() != 15
            || root.GetProperty("sqlSha256").GetString() != optioncChecksSha)
            throw new InvalidOperationException("OPTIONC_FINAL_CHECK_MANIFEST_INVALID");
        foreach (var entry in root.GetProperty("checks").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString() ?? "";
            var table = entry.GetProperty("table").GetString() ?? "";
            if (!expectedOptionCChecks.TryAdd(name, table))
                throw new InvalidOperationException("OPTIONC_FINAL_CHECK_DUPLICATE_NAME");
            if (entry.GetProperty("replacesReviewedPredecessor").GetBoolean())
                actualReplacementChecks.Add(name);
        }
    }
    if (expectedOptionCChecks.Count != 15 ||
        !expectedReplacementChecks.SetEquals(actualReplacementChecks))
        throw new InvalidOperationException("OPTIONC_FINAL_CHECK_SOURCE_SCOPE");
    await using (var optioncCheckConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await optioncCheckConnection.OpenAsync();
        foreach (var batch in Regex.Split(Encoding.UTF8.GetString(optioncChecksBytes),
                     "(?im)^\\s*GO\\s*(?:--.*)?$")
                     .Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await using var command = new SqlCommand(batch, optioncCheckConnection) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync();
        }
    }
    var actualOptionCChecks = new HashSet<string>(StringComparer.Ordinal);
    await using (var optioncCheckCatalog = new SqlConnection(installBuilder.ConnectionString))
    {
        await optioncCheckCatalog.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT c.name, s.name, t.name, c.is_disabled, c.is_not_trusted, c.definition
            FROM sys.check_constraints c
            JOIN sys.tables t ON t.object_id=c.parent_object_id
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE s.name=N'dbo';
            """, optioncCheckCatalog) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!expectedOptionCChecks.TryGetValue(name, out var table))
                continue;
            if (reader.GetString(1) + "." + reader.GetString(2) != table ||
                reader.GetBoolean(3) || reader.GetBoolean(4) ||
                reader.IsDBNull(5) || !actualOptionCChecks.Add(name))
                throw new InvalidOperationException("OPTIONC_FINAL_CHECK_TRUST_OR_SCOPE");
        }
    }
    if (actualOptionCChecks.Count != 15)
        throw new InvalidOperationException("OPTIONC_FINAL_CHECK_CATALOG_INCOMPLETE");

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

    // Verify the *actual* 011/014/016/017 source-derived Option C index
    // definitions against real sys.indexes and indexed column order.
    // Nine are EF-mapped (never reinstall); six are in SQL-only DDL layers.
    stage = "OPTIONC_INDEX_CONTRACT";
    var optioncIndexProof = await CleanInstallOptionCIndexProof.VerifyAsync(
        rcRoot, installBuilder.ConnectionString);

    // Bind the capability row to the exact 017 source before validating the
    // complete SQL catalog contract below. This is not a historical marker.
    stage = "CAPABILITY_017";
    var reservationSourcePath = "docs/release/optionc-canonical-sql-draft/candidate/1800_017/Up.sql";
    var reservationSourceBytes = await File.ReadAllBytesAsync(Path.Combine(rcRoot, reservationSourcePath));
    var reservationSourceSha = Convert.ToHexString(SHA256.HashData(reservationSourceBytes)).ToLowerInvariant();
    using (var candidateManifest = JsonDocument.Parse(await File.ReadAllTextAsync(optioncManifestPath)))
    {
        var reservationEntry = candidateManifest.RootElement.GetProperty("objects").EnumerateArray()
            .SingleOrDefault(x => x.GetProperty("name").GetString() == "dbo.LocationCreateReservations");
        if (reservationEntry.ValueKind != JsonValueKind.Object ||
            reservationEntry.GetProperty("sourcePath").GetString() != reservationSourcePath ||
            reservationEntry.GetProperty("sourceSha256").GetString() != reservationSourceSha)
            throw new InvalidOperationException("CREATE_CAPABILITY_SOURCE_BINDING");
    }
    // Create the clean-install capability only after the exact 017 reservation
    // facets, PK, four FKs and their NO ACTION/trusted states, CHECK, unique
    // key, and dependency objects have been verified in disposable SQL2022.
    var capabilityContract = string.Join("\n", new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["rc"] = expectedRc,
        ["runtime_candidate_context"] = "40d7ce4079e634b2d048eb9badc209518a622dfe7bbb7a5f1c80d325e580d77f",
        ["optionc_tables"] = optioncSha,
        ["optionc_checks"] = optioncChecksSha,
        ["optionc_indexes"] = optioncIndexProof.SourceSha256["017"],
        ["sql_only_indexes"] = extraIndexSha,
        ["catalog_tables"] = string.Join(",", installedCandidateCatalog.OrderBy(x => x, StringComparer.Ordinal))
    }.Select(x => x.Key + "=" + x.Value));
    var capabilitySha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capabilityContract)))
        .ToLowerInvariant();
    var capabilityReplaySha = "";
    await using (var capabilityConnection = new SqlConnection(installBuilder.ConnectionString))
    {
        await capabilityConnection.OpenAsync();

        // 56101 previously hid which 017 facet diverged. Read *only*
        // catalog booleans on the disposal-only database, never raw metadata,
        // constraint text, SQL provider messages, or actual rows into CI logs.
        await using (var facetCommand = new SqlCommand("""
            WITH expectedColumns(name, type_name, max_length, scale, nullable, identity_column) AS
            (
              SELECT * FROM (VALUES
                (N'ChangeRequestId',N'bigint',8,0,0,0),
                (N'LocationId',N'int',4,0,0,0),
                (N'OrganizationId',N'int',4,0,0,0),
                (N'TeamId',N'int',4,0,0,0),
                (N'ReservedLocationRowVersion',N'binary',8,0,0,0),
                (N'ReservationStatus',N'nvarchar',40,0,0,0),
                (N'CreatedAt',N'datetime2',7,3,0,0),
                (N'UpdatedAt',N'datetime2',7,3,0,0),
                (N'RowVersion',N'timestamp',8,0,0,0)
              ) v(name,type_name,max_length,scale,nullable,identity_column)
            ),
            expectedFk(name, parent_column, referenced_table, referenced_column) AS
            (
              SELECT * FROM (VALUES
                (N'FK_OptionC_CreateReservation_Request',N'ChangeRequestId',N'ChangeRequests',N'ChangeRequestId'),
                (N'FK_OptionC_CreateReservation_Location',N'LocationId',N'Locations',N'LocationId'),
                (N'FK_OptionC_CreateReservation_Org',N'OrganizationId',N'Organizations',N'OrganizationId'),
                (N'FK_OptionC_CreateReservation_Team',N'TeamId',N'Teams',N'TeamId')
              ) v(name,parent_column,referenced_table,referenced_column)
            )
            SELECT
              CASE WHEN (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions)=0 THEN 1 ELSE 0 END,
              CASE WHEN (SELECT COUNT(*) FROM sys.columns
                 WHERE object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))=9
                AND (SELECT COUNT(*) FROM expectedColumns e
                  JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                    AND c.name=e.name AND TYPE_NAME(c.user_type_id)=e.type_name
                    AND c.max_length=e.max_length AND c.scale=e.scale
                    AND c.is_nullable=e.nullable AND c.is_identity=e.identity_column)=9
                AND NOT EXISTS(SELECT 1 FROM sys.columns
                  WHERE object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                    AND (default_object_id<>0 OR is_computed=1))
                THEN 1 ELSE 0 END,
              CASE WHEN EXISTS(SELECT 1 FROM sys.key_constraints kc
                 JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id
                   AND ic.index_id=kc.unique_index_id AND ic.key_ordinal=1
                 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                 WHERE kc.parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                   AND kc.type='PK' AND kc.name=N'PK_OptionC_CreateReservation'
                   AND c.name=N'ChangeRequestId'
                   AND (SELECT COUNT(*) FROM sys.index_columns x
                      WHERE x.object_id=kc.parent_object_id AND x.index_id=kc.unique_index_id
                        AND x.key_ordinal>0)=1) THEN 1 ELSE 0 END,
              CASE WHEN (SELECT COUNT(*) FROM sys.foreign_keys
                 WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))=4
                 AND (SELECT COUNT(*) FROM expectedFk e
                   JOIN sys.foreign_keys f ON f.name=e.name
                     AND f.parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND f.is_disabled=0 AND f.is_not_trusted=0
                     AND f.is_not_for_replication=0 AND f.delete_referential_action=0
                   JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
                   JOIN sys.columns pc ON pc.object_id=fc.parent_object_id
                      AND pc.column_id=fc.parent_column_id AND pc.name=e.parent_column
                   JOIN sys.tables rt ON rt.object_id=fc.referenced_object_id
                      AND rt.name=e.referenced_table AND SCHEMA_NAME(rt.schema_id)=N'dbo'
                   JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id
                      AND rc.column_id=fc.referenced_column_id AND rc.name=e.referenced_column)=4
                  THEN 1 ELSE 0 END,
              CASE WHEN EXISTS(SELECT 1 FROM sys.check_constraints
                 WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                   AND name=N'CK_OptionC_CreateReservation_Status'
                   AND is_disabled=0 AND is_not_trusted=0 AND definition IS NOT NULL)
                   THEN 1 ELSE 0 END,
              CASE WHEN EXISTS(SELECT 1 FROM sys.check_constraints
                 WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                   AND name=N'CK_OptionC_CreateReservation_Status'
                   AND is_disabled=0 AND is_not_trusted=0
                   AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                       REPLACE(REPLACE(definition,CHAR(10),N''),CHAR(13),N''),
                       N'[',N''),N']',N''),N' ',N''),N'(',N''),N')',N''))
                   IN (N'reservationstatusinn''pending'',n''published'',n''abandoned''',
                        N'reservationstatus=n''pending''orreservationstatus=n''published''orreservationstatus=n''abandoned''',
                        N'reservationstatus=n''pending''orreservationstatus=n''abandoned''orreservationstatus=n''published''',
                        N'reservationstatus=n''published''orreservationstatus=n''pending''orreservationstatus=n''abandoned''',
                        N'reservationstatus=n''published''orreservationstatus=n''abandoned''orreservationstatus=n''pending''',
                        N'reservationstatus=n''abandoned''orreservationstatus=n''pending''orreservationstatus=n''published''',
                        N'reservationstatus=n''abandoned''orreservationstatus=n''published''orreservationstatus=n''pending'''))
                  THEN 1 ELSE 0 END,
              CASE WHEN EXISTS(SELECT 1 FROM sys.indexes i
                 WHERE i.object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                   AND i.name=N'UX_OptionC_CreateReservation_Location'
                   AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0
                   AND (SELECT COUNT(*) FROM sys.index_columns ic
                       WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                         AND ic.key_ordinal>0)=1
                   AND EXISTS(SELECT 1 FROM sys.index_columns ic
                     JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                     WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                       AND ic.key_ordinal=1 AND c.name=N'LocationId'))
                  THEN 1 ELSE 0 END;
            """, capabilityConnection) { CommandTimeout = 10 })
        await using (var facets = await facetCommand.ExecuteReaderAsync())
        {
            if (!await facets.ReadAsync()) throw new InvalidOperationException("CREATE_FACET_PROBE_EMPTY");
            var facetLabels = new[] { "LEDGER", "COLUMNS", "PK", "FOREIGN_KEYS",
                                      "CHECK_TRUST", "CHECK_DEFINITION", "LOCATION_INDEX" };
            for (var facet = 0; facet < facetLabels.Length; facet++)
                if (facets.GetInt32(facet) != 1)
                    Status("CAPABILITY_017_FACET_" + facetLabels[facet] + "_MISMATCH");
        }
        await using var prove = new SqlCommand("""
            IF (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions)<>0
                THROW 56100,N'CLEAN_BASELINE_UNEXPECTED_HISTORY',1;
            DECLARE @expectedColumns TABLE
            (
                name sysname NOT NULL, type_name sysname NOT NULL,
                max_length smallint NOT NULL, scale tinyint NOT NULL,
                nullable bit NOT NULL, identity_column bit NOT NULL,
                PRIMARY KEY(name)
            );
            INSERT @expectedColumns VALUES
                (N'ChangeRequestId',N'bigint',8,0,0,0),
                (N'LocationId',N'int',4,0,0,0),
                (N'OrganizationId',N'int',4,0,0,0),
                (N'TeamId',N'int',4,0,0,0),
                (N'ReservedLocationRowVersion',N'binary',8,0,0,0),
                (N'ReservationStatus',N'nvarchar',40,0,0,0),
                (N'CreatedAt',N'datetime2',7,3,0,0),
                (N'UpdatedAt',N'datetime2',7,3,0,0),
                (N'RowVersion',N'timestamp',8,0,0,0);
            DECLARE @expectedForeignKeys TABLE
            (
                fk_name sysname NOT NULL, parent_column sysname NOT NULL,
                referenced_table sysname NOT NULL, referenced_column sysname NOT NULL,
                PRIMARY KEY(fk_name)
            );
            INSERT @expectedForeignKeys VALUES
                (N'FK_OptionC_CreateReservation_Request',N'ChangeRequestId',N'ChangeRequests',N'ChangeRequestId'),
                (N'FK_OptionC_CreateReservation_Location',N'LocationId',N'Locations',N'LocationId'),
                (N'FK_OptionC_CreateReservation_Org',N'OrganizationId',N'Organizations',N'OrganizationId'),
                (N'FK_OptionC_CreateReservation_Team',N'TeamId',N'Teams',N'TeamId');
            IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
               OR (SELECT COUNT(*) FROM sys.columns
                   WHERE object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))<>9
               OR EXISTS
                  (SELECT name,type_name,max_length,scale,nullable,identity_column
                   FROM @expectedColumns
                   EXCEPT
                   SELECT c.name,TYPE_NAME(c.user_type_id),c.max_length,c.scale,
                          c.is_nullable,c.is_identity
                   FROM sys.columns c
                   WHERE c.object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))
               OR EXISTS
                  (SELECT c.name,TYPE_NAME(c.user_type_id),c.max_length,c.scale,
                          c.is_nullable,c.is_identity
                   FROM sys.columns c
                   WHERE c.object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                   EXCEPT
                   SELECT name,type_name,max_length,scale,nullable,identity_column
                   FROM @expectedColumns)
               OR EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND (default_object_id<>0 OR is_computed=1))
               OR (SELECT COUNT(*) FROM sys.key_constraints
                   WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND type='PK' AND name=N'PK_OptionC_CreateReservation')<>1
               OR NOT EXISTS (SELECT 1 FROM sys.key_constraints kc
                   JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id
                     AND ic.index_id=kc.unique_index_id AND ic.key_ordinal=1
                   JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                   WHERE kc.parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND kc.type='PK' AND kc.name=N'PK_OptionC_CreateReservation'
                     AND c.name=N'ChangeRequestId'
                     AND (SELECT COUNT(*) FROM sys.index_columns x
                          WHERE x.object_id=kc.parent_object_id
                            AND x.index_id=kc.unique_index_id AND x.key_ordinal>0)=1)
               OR (SELECT COUNT(*) FROM sys.foreign_keys
                   WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))<>4
               OR EXISTS (SELECT 1 FROM
                   (SELECT f.name,pc.name AS parent_column,rt.name AS referenced_table,
                           rc.name AS referenced_column,f.is_disabled,f.is_not_trusted,
                           f.is_not_for_replication,f.delete_referential_action
                    FROM sys.foreign_keys f
                    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=f.object_id
                    JOIN sys.tables rt ON rt.object_id=f.referenced_object_id
                    JOIN sys.schemas rs ON rs.schema_id=rt.schema_id AND rs.name=N'dbo'
                    JOIN sys.columns pc ON pc.object_id=f.parent_object_id AND pc.column_id=fkc.parent_column_id
                    JOIN sys.columns rc ON rc.object_id=f.referenced_object_id AND rc.column_id=fkc.referenced_column_id
                    WHERE f.parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')) actual
                   WHERE NOT EXISTS (SELECT 1 FROM @expectedForeignKeys expected
                       WHERE expected.fk_name=actual.name
                         AND expected.parent_column=actual.parent_column
                         AND expected.referenced_table=actual.referenced_table
                         AND expected.referenced_column=actual.referenced_column)
                      OR actual.is_disabled=1 OR actual.is_not_trusted=1
                      OR actual.is_not_for_replication=1
                      OR actual.delete_referential_action<>0)
               OR EXISTS (SELECT fk_name,parent_column,referenced_table,referenced_column
                          FROM @expectedForeignKeys
                          EXCEPT
                          SELECT f.name,pc.name,rt.name,rc.name
                          FROM sys.foreign_keys f
                          JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=f.object_id
                          JOIN sys.tables rt ON rt.object_id=f.referenced_object_id
                          JOIN sys.schemas rs ON rs.schema_id=rt.schema_id AND rs.name=N'dbo'
                          JOIN sys.columns pc ON pc.object_id=f.parent_object_id AND pc.column_id=fkc.parent_column_id
                          JOIN sys.columns rc ON rc.object_id=f.referenced_object_id AND rc.column_id=fkc.referenced_column_id
                          WHERE f.parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U'))
               OR NOT EXISTS (SELECT 1 FROM sys.indexes i
                   WHERE i.object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND i.name=N'UX_OptionC_CreateReservation_Location'
                     AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0
                     AND (SELECT COUNT(*) FROM sys.index_columns ic
                          WHERE ic.object_id=i.object_id
                            AND ic.index_id=i.index_id AND ic.key_ordinal>0)=1
                     AND EXISTS (SELECT 1 FROM sys.index_columns ic
                         JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                         WHERE ic.object_id=i.object_id
                           AND ic.index_id=i.index_id AND ic.key_ordinal=1
                           AND c.name=N'LocationId'))
               OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys
                   WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND name=N'FK_OptionC_CreateReservation_Request'
                     AND is_disabled=0 AND is_not_trusted=0)
               OR NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND name=N'CK_OptionC_CreateReservation_Status'
                     AND is_disabled=0 AND is_not_trusted=0
                     AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                         REPLACE(REPLACE(definition,CHAR(10),N''),CHAR(13),N''),
                         N'[',N''),N']',N''),N' ',N''),N'(',N''),N')',N''))
                         IN (N'reservationstatusinn''pending'',n''published'',n''abandoned''',
                        N'reservationstatus=n''pending''orreservationstatus=n''published''orreservationstatus=n''abandoned''',
                        N'reservationstatus=n''pending''orreservationstatus=n''abandoned''orreservationstatus=n''published''',
                        N'reservationstatus=n''published''orreservationstatus=n''pending''orreservationstatus=n''abandoned''',
                        N'reservationstatus=n''published''orreservationstatus=n''abandoned''orreservationstatus=n''pending''',
                        N'reservationstatus=n''abandoned''orreservationstatus=n''pending''orreservationstatus=n''published''',
                        N'reservationstatus=n''abandoned''orreservationstatus=n''published''orreservationstatus=n''pending'''))
               OR EXISTS (SELECT 1 FROM sys.foreign_keys
                   WHERE parent_object_id=OBJECT_ID(N'dbo.LocationCreateReservations',N'U')
                     AND (is_disabled=1 OR is_not_trusted=1))
                THROW 56101,N'CLEAN_CREATE_CAPABILITY_CONTRACT_INVALID',1;
            """, capabilityConnection) { CommandTimeout = 10 };
        var capabilityDescription = "Clean-install capability: verified 017 CreateLocation contract; not historical migration execution. Contract SHA-256=" + capabilitySha;
        // Emit the *same executable SQL* that the disposable runner applies,
        // including source-scoped 017 catalog proof and a single atomic marker.
        // No 001-020 historical migration entries are inserted or claimed.
        var replayDescription = capabilityDescription.Replace("'", "''", StringComparison.Ordinal);
        var replaySql = "SET XACT_ABORT ON;\nBEGIN TRANSACTION;\n" + prove.CommandText
            + "\nINSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)\n"
            + "VALUES(N'1.8.0-CREATE-LOWRISK',N'" + replayDescription
            + "',SYSUTCDATETIME(),N'clean-install-v180');\n"
            + "IF @@ROWCOUNT<>1 THROW 56102,N'CLEAN_CREATE_CAPABILITY_WRITE_FAILED',1;\n"
            + "COMMIT TRANSACTION;\n";
        var replayBytes = new UTF8Encoding(false).GetBytes(replaySql);
        capabilityReplaySha = Convert.ToHexString(SHA256.HashData(replayBytes)).ToLowerInvariant();
        await File.WriteAllBytesAsync(
            Path.Combine(output, "40_clean_install_017_capability.sql"), replayBytes);
        await using var record = new SqlCommand(replaySql, capabilityConnection)
            { CommandTimeout = 20 };
        await record.ExecuteNonQueryAsync();
        await using var verify = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM dbo.SchemaVersions
            WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK'
              AND AppliedBy=N'clean-install-v180'
              AND Description=@description;
            """, capabilityConnection) { CommandTimeout = 10 };
        verify.Parameters.AddWithValue("@description", capabilityDescription);
        if (Convert.ToInt64(await verify.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("CREATE_CAPABILITY_RECORD_INVALID");
    }
    Status("OPTIONC_017_COMPLETE_SOURCE_BOUND_CAPABILITY_PASS");

    // Actual EF SQL write/read compatibility on a synthetic transaction; rollback is explicit.
    stage = "EF_ROLLBACK";
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
    stage = "EF_SYNTHETIC_TRIP";
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

    // Verify the exact published RC-derived candidate package reaches this
    // newly installed disposable DB via loopback HTTP.
    stage = "HTTP_API_REAL_SQL";
    try
    {
        var candidateApiRoot = Require("CLEAN_INSTALL_API_CANDIDATE_ROOT");
        await CleanInstallHttpProbe.RunAsync(candidateApiRoot, installBuilder.ConnectionString);
    }
    catch (Exception error)
    {
        // Print only bounded synthetic stage codes (never raw exceptions,
        // connection strings, tokens, HTTP bodies or business data).
        if (error is InvalidOperationException
            && Regex.IsMatch(error.Message, @"^CLEAN_BIZ_[A-Z0-9_]{1,60}$"))
            Status(error.Message);
        Status("RC_API_SAME_CLEAN_DB_HTTP_SQL_FAIL");
        throw;
    }
    Status("CANDIDATE_API_SAME_CLEAN_DB_READY_UNAUTH_DENY_PASS");
    Status("CANDIDATE_API_FRESH_SQL_TEST_LOGIN_ROLE_MATRIX_PASS");
    Status("CANDIDATE_API_VISITOR_LOCATION_CREATE_LEADER_REVIEW_ADMIN_APPROVAL_PASS");
    Status("CANDIDATE_API_ADMIN_SELF_APPROVAL_DENIAL_NO_PARTIAL_WRITE_PASS");
    Status("CANDIDATE_API_FRESH_SQL_VISITOR_CONTEXT_TEAM_SCOPE_PASS");
    Status("CANDIDATE_API_FRESH_SQL_TEMPORARY_DRAFT_SUBMISSION_PASS");
    Status("OPTIONC_017_CAPABILITY_SOURCE_AND_CATALOG_CONTRACT_PASS");

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
        versionLedgerStatus = "NEW_BASELINE_CAPABILITY_ONLY_NO_HISTORICAL_MIGRATION_MARKERS",
        optioncCreateCapabilitySha256 = capabilitySha,
        cleanInstall017CapabilityReplaySqlSha256 = capabilityReplaySha,
        optioncCreateCapabilitySourceSha256 = reservationSourceSha,
        sourceDerivedRouteChecksSqlSha256 = reasonDdlSha,
        trustedRouteCheckCount = routeCheckNames.Count,
        sourceDerived007ChecksSha256 = check007Sha,
        installedSource007CheckCount = checked007.Count,
        sourceDerived001004ChecksSha256 = checks001Sha,
        source005Sha256 = source005Sha,
        source005SqlSha256 = controls005Sha,
        source005InstalledCheckCount = 4,
        source005InstalledIndexCount = 5,
        installedSource001004CheckCount = actualChecks001.Count,
        optioncEffectiveChecksSqlSha256 = optioncChecksSha,
        optioncEffectiveCheckCount = actualOptionCChecks.Count,
        optioncEffectiveIndexCount = optioncIndexProof.VerifiedIndexCount,
        optioncEfOriginIndexCount = optioncIndexProof.MappedIndexCount,
        optioncSqlOnlyIndexCount = optioncIndexProof.SqlOnlyIndexCount,
        optioncIndexSourceSha256 = optioncIndexProof.SourceSha256,
        optioncReplacedReviewedPredecessorCount = actualReplacementChecks.Count,
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
        scope = "Disposable candidate: EF 57 tables + source-derived SQL-only 11 tables + new baseline ledger with one source-bound clean-install capability row + effective 010 triggers + scoped A+C checks and indexes; not the complete 25-case UAT contract or standalone IT installer",
        sqlOnlyObjects = "Seven source-derived core tables and four disposable Option C candidate tables installed; scoped 017 contract verified; remaining 011-020 facets and unrelated application functions remain unverified",
        rcApiSameCleanDbHttpSmoke = "PUBLISHED_RC_DERIVED_CANDIDATE_REAL_HTTP_VISITOR_CREATE_LEADER_REVIEW_ADMIN_APPROVAL_SELF_APPROVAL_DENIAL_AND_TRIP_SUBMISSION",
        partial017ReservationRuntimeBoundary = "Source SHA, exact column facets, PK/FK targets and actions, trusted CHECK, unique index, dependency objects, final scoped check/index sets verified before capability row insertion; no historical migration markers asserted.",
        seedData = "Only ephemeral synthetic three-role accounts and employment/role assignments in disposable DB; random Demo password not packaged; entire disposable DB destroyed"
    };
    await File.WriteAllTextAsync(Path.Combine(output, "model-baseline-manifest.json"),
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Status("MODEL_BASELINE_SQL2022_PASS");
    Status("EF_TABLE_COLUMN_PARITY_PASS");
    Status("MODEL_DDL_CONSTRAINT_TRUST_STATE_PASS");
    Status("EF_SYNTHETIC_WRITE_ROLLBACK_PASS");
    Status("NEW_BASELINE_CAPABILITY_ONLY_NO_HISTORICAL_MARKERS_PASS");
    Status("TRUSTED_007_009_010_ROUTE_CHECKS_SQL2022_PASS");
    Status("SOURCE_007_NINE_OTHER_CHECKS_SQL2022_PASS");
    Status("SOURCE_001_004_SEVENTEEN_CHECKS_SQL2022_PASS");
    Status("OPTIONC_FINAL_FIFTEEN_CHECKS_SQL2022_PASS");
    Status("OPTIONC_FINAL_FIFTEEN_INDEX_DEFINITIONS_SQL2022_PASS");
    Status("SQL_ONLY_SEVEN_TABLES_SQL2022_PASS");
    Status("EFFECTIVE_010_TRIGGERS_SQL2022_PASS");
    Status("OPTIONC_FOUR_CANDIDATE_TABLES_SQL2022_PASS");
    Status("SQL_ONLY_EXTRA_INDEXES_SQL2022_PASS");
    Status("SCOPED_CANDIDATE_69_TABLE_CATALOG_PASS");
    Status("SYNTHETIC_VISITOR_TRIP_AUDIT_EF_SQL_SMOKE_PASS");
    Status("SYNTHETIC_ONESTOP_ROUTE_REASON_NEGATIVE_SQL2022_PASS");
    Status("CLEAN_INSTALL_HOLD_REMAINING_RC_CONTRACTS_AND_IT_PACKAGE");
    Console.WriteLine($"MAPPED_TABLES={expectedTables.Length}");
    Console.WriteLine($"MAPPED_COLUMNS={expectedColumns.Values.Sum(x => x.Count)}");
    Console.WriteLine($"MODEL_BASELINE_SHA256={scriptSha}");
    exitCode = 0;
}
catch (Exception error)
{
    // Fixed-stage and SQL error-number only. Never print raw provider exception,
    // connection string, SQL statement, user data, HTTP body or credentials.
    Status("FAIL_AT_" + stage);
    if (error is SqlException sql)
        Status("SQL_ERROR_NUMBER_" + Math.Abs((long)sql.Number));
    else if (error is DbUpdateException { InnerException: SqlException nestedSql })
        Status("SQL_ERROR_NUMBER_" + Math.Abs((long)nestedSql.Number));
    else if (error is InvalidOperationException &&
             Regex.IsMatch(error.Message, @"\A[A-Z][A-Z0-9_]{1,80}\z"))
        Status("SAFE_CODE_" + error.Message);
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
                IF DB_ID(N'OptionCIsolated_FieldVisitV180CleanInstall') IS NOT NULL
                BEGIN
                    ALTER DATABASE [OptionCIsolated_FieldVisitV180CleanInstall] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [OptionCIsolated_FieldVisitV180CleanInstall];
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
