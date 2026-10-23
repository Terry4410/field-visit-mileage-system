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
        detectedSqlMajorVersion = Convert.ToInt32(awai