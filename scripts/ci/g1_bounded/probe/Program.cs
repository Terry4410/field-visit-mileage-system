using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class Program
{
    const string Root="/home/site/wwwroot";
    const string Host="sql-fieldvisit-jpe-uat.database.windows.net";
    const string Database="db-fieldvisit-uat";
    const string Schema="742f2a95f7f58889d1609aceefa656030cf1f1365f10cc9c89968e89cfff0c63";
    static int opens;
    [UnmanagedCallersOnly]
    public static int G1Run()=>Main(Array.Empty<string>()).GetAwaiter().GetResult();
    [UnmanagedCallersOnly]
    public static int G1SelfTest()=>Main(new[]{"--self-test"}).GetAwaiter().GetResult();
    static Dictionary<string,string> approvedDependencies=new(StringComparer.Ordinal);
    static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    static void Emit(object value)=>Console.WriteLine(JsonSerializer.Serialize(value));
    static bool Target(string host,string db)
    {
        host=host.Trim().ToLowerInvariant();
        if(host.StartsWith("tcp:")) host=host[4..];
        return (host==Host || host==Host+",1433") && db==Database;
    }
    static DbCommand Command(DbConnection connection,string sql)
    {
        var command=connection.CreateCommand();
        command.CommandText=sql;command.CommandTimeout=30;
        return command;
    }
    static string FixedDependency(AssemblyName name)
    {
        var simple=name.Name ?? throw new InvalidOperationException();
        if(simple.Length>150 || simple.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='.'&&c!='_'&&c!='-'))
            throw new InvalidOperationException();
        var path=Path.Combine(Root,simple+".dll");
        if(!File.Exists(path) || new FileInfo(path).LinkTarget is not null ||
           !approvedDependencies.TryGetValue(simple+".dll",out var expected) ||
           new FileInfo(path).Length>67108864 || Hash(File.ReadAllBytes(path))!=expected)
            throw new InvalidOperationException();
        return path;
    }
    static async Task<string> ManagedToken(JsonElement input)
    {
        var endpoint=new Uri(input.GetProperty("identityEndpoint").GetString() ?? "");
        if(endpoint.Scheme!="http" || !endpoint.IsLoopback || endpoint.UserInfo!="" ||
           endpoint.Fragment!="" || endpoint.Query!="" || !endpoint.AbsolutePath.EndsWith("/token"))
            throw new InvalidOperationException();
        var client=input.GetProperty("managedClient").GetString() ?? "";
        if(client!="" && !Guid.TryParse(client,out _)) throw new InvalidOperationException();
        var url=endpoint+"?api-version=2019-08-01&resource="+Uri.EscapeDataString("https://database.windows.net/");
        if(client!="")url+="&client_id="+Uri.EscapeDataString(client);
        using var handler=new HttpClientHandler {AllowAutoRedirect=false,UseProxy=false};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(20)};
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.Add("X-IDENTITY-HEADER",input.GetProperty("identityHeader").GetString());
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(response.StatusCode!=System.Net.HttpStatusCode.OK)throw new InvalidOperationException();
        await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var memory=new MemoryStream();
        var buffer=new byte[4096];int read;
        while((read=await stream.ReadAsync(buffer,deadline.Token))>0)
        {if(memory.Length+read>131072)throw new InvalidOperationException();memory.Write(buffer,0,read);}
        using var json=JsonDocument.Parse(memory.ToArray());
        return json.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException();
    }
    static async Task<int> Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--self-test"}))
        {
            if(!Target("tcp:"+Host+",1433",Database)||Target(Host+".evil",Database)||
               Target(Host,"master")||Target(Host+",1444",Database))return 1;
            Emit(new{contract="PASS",sqlConnections=0,azure="NOT_ACCESSED"});return 0;
        }
        if(args.Length!=0)return 1;
        string stage="INPUT";
        DbConnection? connection=null;
        try
        {
            var line=await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if(line is null || line.Length>262144)throw new InvalidOperationException();
            using var packet=JsonDocument.Parse(line);var input=packet.RootElement;
            var nonce=input.GetProperty("nonce").GetString() ?? "";
            if(nonce.Length!=64 || nonce.Any(c=>!char.IsAsciiHexDigit(c)))throw new InvalidOperationException();
            stage="DRIVER";
            foreach(var item in input.GetProperty("driverHashes").EnumerateObject())
            {
                var hash=item.Value.GetString() ?? "";
                if(approvedDependencies.Count>=256 || hash.Length!=64 || hash.Any(c=>!char.IsAsciiHexDigit(c)) ||
                   !approvedDependencies.TryAdd(item.Name,hash))throw new InvalidOperationException();
            }
            AssemblyLoadContext.Default.Resolving+=(_,name)=>AssemblyLoadContext.Default.LoadFromAssemblyPath(FixedDependency(name));
            var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(FixedDependency(new AssemblyName("Microsoft.Data.SqlClient")));
            var builderType=assembly.GetType("Microsoft.Data.SqlClient.SqlConnectionStringBuilder",true)!;
            var builder=(DbConnectionStringBuilder)Activator.CreateInstance(builderType,new object[]{input.GetProperty("connectionString").GetString()!})!;
            if(!Target(Convert.ToString(builder["Data Source"]) ?? "",Convert.ToString(builder["Initial Catalog"]) ?? "") ||
               Convert.ToBoolean(builder["TrustServerCertificate"]) ||
               !new[]{"true","mandatory","strict"}.Contains(Convert.ToString(builder["Encrypt"])!.ToLowerInvariant()))
                throw new InvalidOperationException();
            foreach(var key in new[]{"AttachDbFilename","User Instance","Context Connection","Integrated Security"})
                if(builder.ContainsKey(key) && Convert.ToString(builder[key]) is string v &&
                   v!="" && !v.Equals("false",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException();
            if(Convert.ToString(builder["Failover Partner"])!="" ||
               !Convert.ToString(builder["ApplicationIntent"])!.Equals("ReadWrite",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException();
            var authentication=Convert.ToString(builder["Authentication"]) ?? "";
            var managed=authentication is "ActiveDirectoryManagedIdentity" or "ActiveDirectoryMSI";
            var sqlPassword=authentication is "" or "NotSpecified" or "SqlPassword";
            if(!managed&&!sqlPassword)throw new InvalidOperationException();
            if(sqlPassword && (Convert.ToString(builder["User ID"])=="" || Convert.ToString(builder["Password"])==""))
                throw new InvalidOperationException();
            var managedClient=managed ? Convert.ToString(builder["User ID"]) ?? "" : "";
            builder["Pooling"]=false;builder["ConnectRetryCount"]=0;builder["Connect Timeout"]=120;
            builder["MultiSubnetFailover"]=false;
            if(managed){builder.Remove("Authentication");builder.Remove("User ID");builder.Remove("Password");}
            builder["Application Name"]="FieldVisit reviewed G1 SELECT-only";
            connection=(DbConnection)Activator.CreateInstance(assembly.GetType("Microsoft.Data.SqlClient.SqlConnection",true)!,new object[]{builder.ConnectionString})!;
            Emit(new{kind="DB_PREPARED",nonce,authentication=managed?"API_MANAGED_IDENTITY":"API_SQL_LOGIN",sqlConnections=0});
            stage="CONSENT";
            var go=await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(180));
            if(go!="G1_SQL_GO "+nonce)throw new InvalidOperationException();
            if(managed)
            {
                stage="API_IDENTITY";
                // Same selected identity endpoint/header, used only within main-container memory.
                using var identityPacket=JsonDocument.Parse(JsonSerializer.Serialize(new{
                    identityEndpoint=input.GetProperty("identityEndpoint").GetString(),
                    identityHeader=input.GetProperty("identityHeader").GetString(),managedClient}));
                var token=await ManagedToken(identityPacket.RootElement);
                connection.GetType().GetProperty("AccessToken")!.SetValue(connection,token);
            }
            stage="OPEN";opens++;
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                await connection.OpenAsync(deadline.Token);
            stage="PRINCIPAL";
            const string principalSql="""
            SELECT DB_NAME(),CONVERT(int,SERVERPROPERTY(N'EngineEdition')),
             (SELECT sid FROM sys.database_principals WHERE principal_id=USER_ID()),
             CASE WHEN IS_ROLEMEMBER(N'db_owner')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY ROLE')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'IMPERSONATE ANY USER')=0
              AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'EXECUTE')=0
              AND HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER')=0
              AND HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'VIEW DEFINITION')=1
             THEN 1 ELSE 0 END;
            """;
            bool least;string principalHash;
            using(var command=Command(connection,principalSql))
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            await using(var rows=await command.ExecuteReaderAsync(deadline.Token))
            {
                if(!await rows.ReadAsync(deadline.Token)||rows.GetString(0)!=Database||rows.GetInt32(1)!=5||
                   rows.IsDBNull(2))throw new InvalidOperationException();
                principalHash=Hash((byte[])rows[2]);least=rows.GetInt32(3)==1;
                if(await rows.ReadAsync(deadline.Token))throw new InvalidOperationException();
            }
            stage="OBJECT_GRANTS";
            const string grantSql="""
            SELECT t.name,HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(t.name),N'OBJECT',N'SELECT'),
             HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(t.name),N'OBJECT',N'INSERT'),
             HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(t.name),N'OBJECT',N'UPDATE'),
             HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(t.name),N'OBJECT',N'DELETE')
            FROM sys.tables t WHERE t.schema_id=SCHEMA_ID(N'dbo') ORDER BY t.name;
            """;
            int tables=0,ledger=0;bool dml=true;
            using(var command=Command(connection,grantSql))
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            await using(var rows=await command.ExecuteReaderAsync(deadline.Token))
            while(await rows.ReadAsync(deadline.Token))
            {
                if(++tables>69)throw new InvalidOperationException();
                var isLedger=rows.GetString(0) is "SchemaVersions" or "SchemaMigrationDataBaselines";
                if(isLedger)ledger++;
                for(int n=1;n<=4;n++)
                {
                    int expected=isLedger&&n>1?0:1;
                    if(rows.IsDBNull(n)||rows.GetInt32(n)!=expected)dml=false;
                }
            }
            stage="CATALOG";
            string catalog;
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("G1.Catalog.sql")!)
            using(var text=new StreamReader(resource))catalog=await text.ReadToEndAsync();
            string catalogHash;
            using(var command=Command(connection,catalog))
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            await using(var rows=await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess,deadline.Token))
            {
                if(!await rows.ReadAsync(deadline.Token)||rows.IsDBNull(0))throw new InvalidOperationException();
                using var text=rows.GetTextReader(0);var content=new StringBuilder();var chars=new char[4096];int n;
                while((n=await text.ReadAsync(chars.AsMemory(),deadline.Token))>0)
                {if(content.Length+n>8388608)throw new InvalidOperationException();content.Append(chars,0,n);}
                catalogHash=Hash(Encoding.UTF8.GetBytes(content.ToString()));
                if(await rows.ReadAsync(deadline.Token))throw new InvalidOperationException();
            }
            Emit(new{kind="DB_RESULT",nonce,sqlConnections=opens,apiPrincipal=least&&dml&&ledger==2&&tables==69?"MATCH":"HOLD",
                schema=catalogHash==Schema?"MATCH":"HOLD",principalSidSha256=principalHash,
                actualSchemaSha256=catalogHash,rawSql="SUPPRESSED"});
            return 0;
        }
        catch
        {
            Emit(new{kind="DB_STOP",stage,sqlConnections=opens,details="SUPPRESSED"});return 1;
        }
        finally{if(connection is not null){try{await connection.DisposeAsync();}catch{}}}
    }
}
