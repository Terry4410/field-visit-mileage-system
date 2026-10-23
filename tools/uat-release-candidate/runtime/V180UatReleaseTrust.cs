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
        DateTimeOffset NotBefore, DateTimeOffset Expires);
    public sealed record Facts(string CandidateSourceSha, string ApiSha256,
        string FrontendSha256, string SchemaSha256, string PrincipalSidSha256,
        string PrincipalName, string Server, string Database, int EngineEdition,
        bool EncryptedCertificateVerified, bool NonOwnerNoDdl, string Features);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const string Features = "LocationReview,LowRiskDirect,ExcelReview";
    public const string Server = "sql-fieldvisit-jpe-uat.database.windows.net";
    public const string Database = "db-fieldvisit-uat";
    private static InvalidOperationException Denied() => new("OPTIONC_UAT_TRUST_DENIED");
    private static bool Hex(string s,int n) => s.Length==n && s.All(c=>char.IsAsciiHexDigit(c));
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static void Validate(byte[] payload, string signature, byte[] key, Facts facts, DateTimeOffset now)
    {
        if (key.Length<32 || payload.Length>16384 || !Hex(signature,64)) throw Denied();
        var actual=HMACSHA256.HashData(key,payload);
        if (!CryptographicOperations.FixedTimeEquals(actual,Convert.FromHexString(signature))) throw Denied();
        var a=JsonSerializer.Deserialize<Attestation>(payload,Json) ?? throw Denied();
        if (a.NotBefore>now || a.Expires<=now || a.Expires-a.NotBefore>TimeSpan.FromHours(24)
            || !Hex(a.CandidateSourceSha,40) || !Hex(a.ApiSha256,64) || !Hex(a.FrontendSha256,64)
            || !Hex(a.SchemaSha256,64) || !Hex(a.PrincipalSidSha256,64)
            || a.CandidateSourceSha!=facts.CandidateSourceSha || a.ApiSha256!=facts.ApiSha256
            || a.FrontendSha256!=facts.FrontendSha256 || a.SchemaSha256!=facts.SchemaSha256
            || a.PrincipalSidSha256!=facts.PrincipalSidSha256 || a.PrincipalName!=facts.PrincipalName
            || a.Server!=Server || a.Database!=Database || facts.Server!=Server || facts.Database!=Database
            || a.Features!=Features || facts.Features!=Features || facts.EngineEdition!=5
            || !facts.EncryptedCertificateVerified || !facts.NonOwnerNoDdl) throw Denied();
    }
    public static string CatalogJson(SqlConnection connection)
    {
        using var resource=typeof(V180UatReleaseTrust).Assembly.GetManifestResourceStream("FieldVisit.Release.SchemaFingerprint.sql") ?? throw new InvalidOperationException("SCHEMA_RESOURCE_MISSING");
        using var reader=new StreamReader(resource);
        using var cmd=new SqlCommand(reader.ReadToEnd(),connection) {CommandTimeout=15};
        string json;
        using(var rows=cmd.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            if(!rows.Read() || rows.IsDBNull(0)) throw new InvalidOperationException("SCHEMA_EMPTY_RESULT");
            using var text=rows.GetTextReader(0);json=text.ReadToEnd();
            if(rows.Read()) throw new InvalidOperationException("SCHEMA_MULTIPLE_RESULTS");
        }
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Array) throw new InvalidOperationException("SCHEMA_ROOT_NOT_ARRAY");
        if(doc.RootElement.GetArrayLength()!=69) throw new InvalidOperationException("SCHEMA_TABLE_COUNT");
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
    public static void Require(SqlConnection connection)
    {
        try { RequireCore(connection); }
        catch { throw Denied(); } // Never propagate identities, paths, SQL or provider text.
    }
    private static void RequireCore(SqlConnection connection)
    {
        if(connection.State!=ConnectionState.Open || connection.DataSource!=Server || connection.Database!=Database
            || Environment.GetEnvironmentVariable("FIELDVISIT_UAT_ACTIVATION")!="true") throw Denied();
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
        using var read=new SqlCommand("""
            SELECT CONVERT(int,SERVERPROPERTY('EngineEdition')),USER_NAME(),
              (SELECT sid FROM sys.database_principals WHERE principal_id=USER_ID()),
              CASE WHEN IS_ROLEMEMBER(N'db_owner')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY ROLE')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'IMPERSONATE ANY USER')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'UPDATE')=0
                AND (SELECT COUNT(*) FROM dbo.SchemaVersions)=3
                AND (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN (N'1.8.0-CREATE-LOWRISK',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-EXCEL-CANONICAL-STATUS'))=3
                THEN 1 ELSE 0 END;
            """,connection) {CommandTimeout=10};
        int engine; string principal,sid; bool least;
        using(var rows=read.ExecuteReader())
        {
            if(!rows.Read()) throw Denied();
            engine=rows.GetInt32(0); principal=rows.GetString(1); sid=Hash((byte[])rows[2]); least=rows.GetInt32(3)==1;
        }
        var schema=Hash(Encoding.UTF8.GetBytes(CatalogJson(connection)));
        var api=Hash(File.ReadAllBytes(Path.Combine(root,"FieldVisit.Api.dll")));
        if(schema!=candidate.GetProperty("schemaSha256").GetString() || api!=candidate.GetProperty("apiSha256").GetString()) throw Denied();
        var facts=new Facts(UatReleaseIdentity.SourceSha,api,candidate.GetProperty("frontendSha256").GetString()!,
            schema,sid,principal,connection.DataSource,connection.Database,engine,tls,least,Features);
        Validate(payload,signature,key,facts,DateTimeOffset.UtcNow);
    }
}
