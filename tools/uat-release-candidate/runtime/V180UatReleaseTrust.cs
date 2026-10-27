using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

// Deployment-independent trust verification. No database mutation, Azure call,
// configuration write, feature enablement or arbitrary endpoint override.
public static class V180UatReleaseTrust
{
    public sealed record Attestation(string CandidateSourceSha, string ApiSha256,
        string FrontendSha256, string SchemaSha256, string PrincipalSidSha256,
        string PrincipalName, string Server, string Database, string Features,
        DateTimeOffset NotBefore, DateTimeOffset Expires)
    {
        public string? SchemaProfile { get; init; }
        public string? HistoricalMarkerSha256 { get; init; }
        public string? PromotionPackageId { get; init; }
        public string? LedgerSha256 { get; init; }
        public int? TableCount { get; init; }
    }
    public sealed record Facts(string CandidateSourceSha, string ApiSha256,
        string FrontendSha256, string SchemaSha256, string PrincipalSidSha256,
        string PrincipalName, string Server, string Database, int EngineEdition,
        bool EncryptedCertificateVerified, bool NonOwnerNoDdl, string Features)
    {
        public string? SchemaProfile { get; init; }
        public string? HistoricalMarkerSha256 { get; init; }
        public string? PromotionPackageId { get; init; }
        public string? LedgerSha256 { get; init; }
        public int? TableCount { get; init; }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const string Features = "LocationReview,LowRiskDirect,ExcelReview";
    public const string PathBFeatures = "DemoAccounts,LocationReview,LowRiskDirect,ExcelReview,OptionCCandidate,OptionCApproval,GoogleMapsRoutes,Worker";
    public const string Server = "sql-fieldvisit-jpe-uat.database.windows.net";
    public const string Database = "db-fieldvisit-uat";
    public const string PathBServer = "sql-fieldvisit-jpe-pathb-uat.database.windows.net";
    public const string PathBDatabase = "db-fieldvisit-pathb-uat";
    public const string PathBProfile = "PathBNewEmpty";
    private static InvalidOperationException Denied() => new("OPTIONC_UAT_TRUST_DENIED");
    private static bool Hex(string s,int n) => s.Length==n && s.All(c=>char.IsAsciiHexDigit(c));
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string ApiHash(string root)
    {
        var names=new[]{"FieldVisit.Api.dll","FieldVisit.Application.dll","FieldVisit.Domain.dll","FieldVisit.Infrastructure.dll"};
        return Hash(Encoding.UTF8.GetBytes(string.Concat(names.Select(name=>name+":"+Hash(File.ReadAllBytes(Path.Combine(root,name)))+"\n"))));
    }
    public static void Validate(byte[] payload, string signature, byte[] key, Facts facts, DateTimeOffset now)
    {
        if (key.Length<32 || payload.Length>16384 || !Hex(signature,64)) throw Denied();
        var actual=HMACSHA256.HashData(key,payload);
        if (!CryptographicOperations.FixedTimeEquals(actual,Convert.FromHexString(signature))) throw Denied();
        var a=JsonSerializer.Deserialize<Attestation>(payload,Json) ?? throw Denied();
        var isPathB=facts.SchemaProfile==PathBProfile;
        var expectedServer=isPathB?PathBServer:Server;
        var expectedDatabase=isPathB?PathBDatabase:Database;
        var expectedFeatures=isPathB?PathBFeatures:Features;
        if (a.NotBefore>now || a.Expires<=now || a.Expires-a.NotBefore>TimeSpan.FromHours(24)
            || !Hex(a.CandidateSourceSha,40) || !Hex(a.ApiSha256,64) || !Hex(a.FrontendSha256,64)
            || !Hex(a.SchemaSha256,64) || !Hex(a.PrincipalSidSha256,64)
            || a.CandidateSourceSha!=facts.CandidateSourceSha || a.ApiSha256!=facts.ApiSha256
            || a.FrontendSha256!=facts.FrontendSha256 || a.SchemaSha256!=facts.SchemaSha256
            || a.PrincipalSidSha256!=facts.PrincipalSidSha256 || a.PrincipalName!=facts.PrincipalName
            || a.Features!=expectedFeatures || facts.Features!=expectedFeatures || facts.EngineEdition!=5
            || !facts.EncryptedCertificateVerified || !facts.NonOwnerNoDdl) throw Denied();
        if(a.Server!=expectedServer || a.Database!=expectedDatabase
            || facts.Server!=expectedServer || facts.Database!=expectedDatabase) throw Denied();
        // Existing010 and Path B use distinct signed target profiles.
        if (facts.SchemaProfile=="Existing010Upgrade")
        {
            if(a.SchemaProfile!="Existing010Upgrade" || !Hex(a.HistoricalMarkerSha256 ?? "",64)
                || !Hex(a.PromotionPackageId ?? "",64) || !Hex(a.LedgerSha256 ?? "",64)
                || a.HistoricalMarkerSha256!=facts.HistoricalMarkerSha256
                || a.PromotionPackageId!=facts.PromotionPackageId
                || a.LedgerSha256!=facts.LedgerSha256
                || a.TableCount!=facts.TableCount || a.TableCount is null or <70 or >100)
                throw Denied();
        }
        else if(facts.SchemaProfile==PathBProfile)
        {
            if(a.SchemaProfile!=PathBProfile || facts.TableCount!=69 || a.TableCount!=69
                || a.HistoricalMarkerSha256 is not null || a.PromotionPackageId is not null
                || a.LedgerSha256 is not null || facts.HistoricalMarkerSha256 is not null
                || facts.PromotionPackageId is not null || facts.LedgerSha256 is not null)
                throw Denied();
        }
        else if(facts.SchemaProfile is not null || a.SchemaProfile is not null
            || a.HistoricalMarkerSha256 is not null || a.PromotionPackageId is not null
            || a.LedgerSha256 is not null || a.TableCount is not null)
            throw Denied();
    }
    public static string CatalogJson(SqlConnection connection, SqlTransaction? transaction=null, int expectedTableCount=69)
    {
        using var resource=typeof(V180UatReleaseTrust).Assembly.GetManifestResourceStream("FieldVisit.Release.SchemaFingerprint.sql") ?? throw new InvalidOperationException("SCHEMA_RESOURCE_MISSING");
        using var reader=new StreamReader(resource);
        using var cmd=new SqlCommand(reader.ReadToEnd(),connection,transaction) {CommandTimeout=15};
        string json;
        using(var rows=cmd.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            if(!rows.Read() || rows.IsDBNull(0)) throw new InvalidOperationException("SCHEMA_EMPTY_RESULT");
            using var text=rows.GetTextReader(0);json=text.ReadToEnd();
            if(rows.Read()) throw new InvalidOperationException("SCHEMA_MULTIPLE_RESULTS");
        }
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Array) throw new InvalidOperationException("SCHEMA_ROOT_NOT_ARRAY");
        if(expectedTableCount!=69 && (expectedTableCount<70 || expectedTableCount>100))
            throw new InvalidOperationException("SCHEMA_PROFILE_TABLE_COUNT");
        if(doc.RootElement.GetArrayLength()!=expectedTableCount) throw new InvalidOperationException("SCHEMA_TABLE_COUNT");
        foreach(var table in doc.RootElement.EnumerateArray())
        {
            var indexes=table.GetProperty("indexes");
            if(indexes.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)) throw new InvalidOperationException("SCHEMA_INDEX_LIST_TYPE");
            if(indexes.ValueKind==JsonValueKind.Array)
            foreach(var index in indexes.EnumerateArray())
            {
                var bit=index.GetProperty("has_filter");
                var filtered=bit.ValueKind switch {JsonValueKind.True=>true,JsonValueKind.False=>false,
                    JsonValueKind.Number when bit.GetInt32()==1=>true,
                    JsonValueKind.Number when bit.GetInt32()==0=>false,
                    _=>throw new InvalidOperationException("SCHEMA_FILTER_FLAG_TYPE")};
                if(filtered && index.GetProperty("filter_definition").ValueKind==JsonValueKind.Null) throw new InvalidOperationException("SCHEMA_INDEX_METADATA_HIDDEN");
            }
            var triggers=table.GetProperty("triggers");
            if(triggers.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)) throw new InvalidOperationException("SCHEMA_TRIGGER_LIST_TYPE");
            if(triggers.ValueKind==JsonValueKind.Array)
            foreach(var trigger in triggers.EnumerateArray())
                if(trigger.GetProperty("definition").ValueKind==JsonValueKind.Null) throw new InvalidOperationException("SCHEMA_TRIGGER_METADATA_HIDDEN");
        }
        return json;
    }
    public static void Require(SqlConnection connection, SqlTransaction? transaction=null)
    {
        try { RequireCore(connection,transaction); }
        catch(SqlException error) when(error.Number is 1205 or 1222) {throw;}
        catch { throw Denied(); } // Never propagate identities, paths, SQL or provider text.
    }
    private static void RequireCore(SqlConnection connection, SqlTransaction? transaction)
    {
        if(connection.State!=ConnectionState.Open || Environment.GetEnvironmentVariable("FIELDVISIT_UAT_ACTIVATION")!="true") throw Denied();
        var cs=new SqlConnectionStringBuilder(connection.ConnectionString);
        var tls=cs.Encrypt.ToString() is "True" or "Strict";
        if(!tls || cs.TrustServerCertificate) throw Denied();
        var path=Environment.GetEnvironmentVariable("FIELDVISIT_UAT_ATTESTATION") ?? throw Denied();
        var key=Convert.FromHexString(Environment.GetEnvironmentVariable("FIELDVISIT_UAT_TRUST_KEY") ?? "");
        var signature=Environment.GetEnvironmentVariable("FIELDVISIT_UAT_SIGNATURE") ?? "";
        if(new FileInfo(path).Length>16384) throw Denied();
        var payload=File.ReadAllBytes(path);
        var root=AppContext.BaseDirectory;
        using var contract=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"release-contract.json")));
        var candidate=contract.RootElement;
        if(candidate.GetProperty("candidateSourceSha").GetString()!=UatReleaseIdentity.SourceSha) throw Denied();
        var profile=candidate.TryGetProperty("schemaProfile",out var v) ? v.GetString() : null;
        if(profile is not (null or "Existing010Upgrade" or PathBProfile)) throw Denied();
        bool upgrade=profile=="Existing010Upgrade";
        bool pathB=profile==PathBProfile;
        int expectedTables=upgrade ? candidate.GetProperty("tableCount").GetInt32() : 69;
        var jwtKey=Environment.GetEnvironmentVariable("Auth__JwtKey") ?? "";
        if(pathB && (Environment.GetEnvironmentVariable("FIELDVISIT_UAT_PROFILE")!=PathBProfile
            || Environment.GetEnvironmentVariable("Auth__Mode")!="Demo"
            || (Environment.GetEnvironmentVariable("Auth__DemoPassword") ?? "").Length<12
            || jwtKey.Length<32 || Environment.GetEnvironmentVariable("OPTIONC_EXECUTION_MODE")!="Uat"
            || Environment.GetEnvironmentVariable("OptionC__Candidate__Enabled")!="true"
            || Environment.GetEnvironmentVariable("OptionC__Approval__Enabled")!="true"
            || Environment.GetEnvironmentVariable("BackgroundJobs__WorkerEnabled")!="true"
            || Environment.GetEnvironmentVariable("Providers__Route")!="GoogleMaps"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GoogleMaps__ApiKey")))) throw Denied();
        var expectedServer=pathB?PathBServer:Server;
        var expectedDatabase=pathB?PathBDatabase:Database;
        if(candidate.GetProperty("server").GetString()!=expectedServer
            || candidate.GetProperty("database").GetString()!=expectedDatabase
            || connection.DataSource!=expectedServer || connection.Database!=expectedDatabase) throw Denied();
        if(upgrade && (expectedTables<70 || expectedTables>100)) throw Denied();
        using var read=new SqlCommand("""
            SELECT CONVERT(int,SERVERPROPERTY('EngineEdition')),USER_NAME(),
              (SELECT sid FROM sys.database_principals WHERE principal_id=USER_ID()),
              CASE WHEN IS_ROLEMEMBER(N'db_owner')=0
                AND IS_ROLEMEMBER(N'db_datawriter')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY ROLE')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'IMPERSONATE ANY USER')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'UPDATE')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'DELETE')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaMigrationDataBaselines',N'OBJECT',N'INSERT')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaMigrationDataBaselines',N'OBJECT',N'UPDATE')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaMigrationDataBaselines',N'OBJECT',N'DELETE')=0
                AND (OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U') IS NULL OR
                    (HAS_PERMS_BY_NAME(N'dbo.FieldVisitMigrationLedger',N'OBJECT',N'INSERT')=0
                     AND HAS_PERMS_BY_NAME(N'dbo.FieldVisitMigrationLedger',N'OBJECT',N'UPDATE')=0
                     AND HAS_PERMS_BY_NAME(N'dbo.FieldVisitMigrationLedger',N'OBJECT',N'DELETE')=0))
                THEN 1 ELSE 0 END;
            """,connection,transaction) {CommandTimeout=10};
        int engine; string principal,sid; bool least;
        using(var rows=read.ExecuteReader())
        {
            if(!rows.Read()) throw Denied();
            engine=rows.GetInt32(0); principal=rows.GetString(1); sid=Hash((byte[])rows[2]); least=rows.GetInt32(3)==1;
        }
        var schema=Hash(Encoding.UTF8.GetBytes(CatalogJson(connection,transaction,expectedTables)));
        var api=ApiHash(root);
        if(schema!=candidate.GetProperty("schemaSha256").GetString() || api!=candidate.GetProperty("apiSha256").GetString()) throw Denied();
        V180UatExisting010HistoryTrust.Proof? proof=null;
        if(upgrade)
        {
            proof=V180UatExisting010HistoryTrust.ReadAndVerify(connection,transaction,
                candidate.GetProperty("historicalMarkerSha256").GetString()!,
                candidate.GetProperty("promotionPackageId").GetString()!,
                candidate.GetProperty("ledgerSha256").GetString()!);
        }
        else
        {
            // Keep the original fresh-install 69-table/3-capability contract exact.
            using var markers=new SqlCommand("""
                SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SchemaVersions)=3
                    AND (SELECT COUNT(*) FROM dbo.SchemaVersions
                         WHERE VersionNumber IN(N'1.8.0-CREATE-LOWRISK',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-EXCEL-CANONICAL-STATUS'))=3
                    THEN 1 ELSE 0 END
                """,connection,transaction) {CommandTimeout=10};
            if(Convert.ToInt32(markers.ExecuteScalar())!=1) throw Denied();
        }
        var runtimeFeatures=pathB?PathBFeatures:Features;
        var facts=new Facts(UatReleaseIdentity.SourceSha,api,candidate.GetProperty("frontendSha256").GetString()!,
            schema,sid,principal,connection.DataSource,connection.Database,engine,tls,least,runtimeFeatures)
        {
            SchemaProfile=profile,HistoricalMarkerSha256=proof?.HistoricalMarkerSha256,
            PromotionPackageId=proof?.PromotionPackageId,LedgerSha256=proof?.LedgerSha256,
            TableCount=upgrade || pathB ? expectedTables : null
        };
        Validate(payload,signature,key,facts,DateTimeOffset.UtcNow);
    }
}
