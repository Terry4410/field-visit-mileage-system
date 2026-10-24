using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;

// Disposable integration only. The harness rejects Azure/server names and never
// prints SQL, connection strings, historic rows, tokens or provider exceptions.
internal static class SqlHistoryBridge
{
    private static int tests;
    private static void Pass(string name)
    {
        tests++;
        Console.WriteLine("UpgradeSqlCase="+name+"; Status=PASS;");
    }
    private static string ScalarString(SqlConnection connection,string sql,SqlTransaction? tx=null)
    {
        using var c=new SqlCommand(sql,connection,tx){CommandTimeout=15};
        return c.ExecuteScalar() as string ?? throw new InvalidOperationException("SQL_SCALAR_STOP");
    }
    private static int ScalarInt(SqlConnection connection,string sql,SqlTransaction? tx=null)
    {
        using var c=new SqlCommand(sql,connection,tx){CommandTimeout=15};
        return Convert.ToInt32(c.ExecuteScalar());
    }
    private static void Exec(SqlConnection connection,string sql,SqlTransaction? tx=null,int timeout=150)
    {
        using var cmd=new SqlCommand(sql,connection,tx){CommandTimeout=timeout};
        cmd.ExecuteNonQuery();
    }
    private static string Hash(string s)=>Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    private static void Deny(string name,Action action)
    {
        try { action(); }
        catch(InvalidOperationException e) when(e.Message=="OPTIONC_UAT_TRUST_DENIED")
        { Pass(name); return; }
        throw new InvalidOperationException("EXPECTED_DENIAL_NOT_OBSERVED");
    }
    public static int Execute(string source)
    {
        string? dbName=null;
        try
        {
            if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"
                || Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")!="Terry4410/field-visit-mileage-system"
                || !Directory.Exists(source))throw new InvalidOperationException("HARNESS_SCOPE_STOP");
            var raw=Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION")
                ?? throw new InvalidOperationException("LOCAL_CONNECTION_MISSING");
            var builder=new SqlConnectionStringBuilder(raw);
            if(builder.DataSource is not ("localhost,1433" or "127.0.0.1,1433")
                || builder.InitialCatalog!="master" || builder.UserID!="sa"
                || builder.IntegratedSecurity || builder.Encrypt.ToString()=="Strict")
                throw new InvalidOperationException("LOCAL_ONLY_STOP");
            builder.Pooling=false;
            builder.ConnectRetryCount=0;
            builder.ConnectTimeout=5;
            using var admin=new SqlConnection(builder.ConnectionString);admin.Open();
            if(ScalarInt(admin,"SELECT CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))")!=16
                ||ScalarInt(admin,"SELECT CONVERT(int,SERVERPROPERTY(N'EngineEdition'))")!=3)
                throw new InvalidOperationException("LOCAL_SQL2022_DEVELOPER_REQUIRED");

            // PR50 fixture, SQL and manifest are checked out at an exact commit
            // and never copied into this PR, changed, recreated or redirected.
            var fixture=File.ReadAllText(Path.Combine(source,"scripts/ci/run_full_candidate_migration.ps1"));
            var match=Regex.Match(fixture,@"(?s)\$boot=@'\r?\n(.*?)\r?\n'@");
            if(!match.Success)throw new InvalidOperationException("PINNED_FIXTURE_MISSING");
            var promotion=File.ReadAllText(Path.Combine(source,"docs/release/g1-execution/promotion/Promotion.sql"));
            var verify=File.ReadAllText(Path.Combine(source,"docs/release/g1-execution/promotion/Verify.sql"));
            dbName="OptionCIsolated_"+Guid.NewGuid().ToString("N");
            Exec(admin,"CREATE DATABASE ["+dbName+"]",timeout:40);
            builder.InitialCatalog=dbName;
            using(var db=new SqlConnection(builder.ConnectionString))
            {
                db.Open();
                Exec(db,"EXEC sys.sp_set_session_context @key=N'FieldVisit.G1.NonDeploy',@value=1;");
                Exec(db,match.Groups[1].Value);
                for(int i=1;i<=25;i++)
                {
                    string marker=i<=9?$"1.8.0-{i:000}":$"1.7.0-SYNTHETIC-{i-9:000}";
                    Exec(db,$"INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) SELECT N'{marker}',N'SYNTHETIC_PRE010',DATEADD(day,-{i+1},AppliedAt),N'isolated' FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010';");
                }
                if(ScalarInt(db,"SELECT COUNT(*) FROM dbo.SchemaVersions")!=26)
                    throw new InvalidOperationException("HISTORY_FIXTURE_NOT_26");
                Exec(db,promotion,timeout:150);
                Exec(db,verify,timeout:90);
                if(ScalarInt(db,"SELECT COUNT(*) FROM dbo.SchemaVersions")!=39
                   || ScalarInt(db,"SELECT COUNT(*) FROM dbo.FieldVisitMigrationLedger")!=9)
                    throw new InvalidOperationException("PROMOTION_POST_SHAPE_STOP");
                const string newMarkers="N'1.8.0-011',N'1.8.0-012',N'1.8.0-014',N'1.8.0-015',N'1.8.0-016',N'1.8.0-LEADER-REVIEW',N'1.8.0-017',N'1.8.0-CREATE-LOWRISK',N'1.8.0-018',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-019',N'1.8.0-020',N'1.8.0-EXCEL-CANONICAL-STATUS'";
                var historic=Hash(ScalarString(db,"SELECT (SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber NOT IN ("+newMarkers+") ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES)"));
                var ledger=Hash(ScalarString(db,"SELECT (SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt FROM dbo.FieldVisitMigrationLedger ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES)"));
                string package;
                using(var cmd=new SqlCommand("SELECT TOP(1) PackageId FROM dbo.FieldVisitMigrationLedger",db){CommandTimeout=10})
                    package=Convert.ToHexString((byte[])cmd.ExecuteScalar()!).ToLowerInvariant();
                V180UatExisting010HistoryTrust.ReadAndVerify(db,null,historic,package,ledger);
                Pass("RealSql26History13Markers9LedgerPositive");

                Deny("WrongHistoryDigestRefused",()=>V180UatExisting010HistoryTrust.ReadAndVerify(db,null,new string('0',64),package,ledger));
                Deny("WrongPackageIdRefused",()=>V180UatExisting010HistoryTrust.ReadAndVerify(db,null,historic,new string('1',64),ledger));
                Deny("WrongLedgerDigestRefused",()=>V180UatExisting010HistoryTrust.ReadAndVerify(db,null,historic,package,new string('2',64)));

                using(var tx=db.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    Exec(db,"UPDATE dbo.SchemaVersions SET Description=N'SYNTHETIC_TAMPERED' WHERE VersionNumber=N'1.8.0-010'",tx);
                    Deny("OriginalMarkerContentTamperedRefused",()=>V180UatExisting010HistoryTrust.ReadAndVerify(db,tx,historic,package,ledger));
                    tx.Rollback();
                }
                using(var tx=db.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    Exec(db,"UPDATE dbo.FieldVisitMigrationLedger SET MarkersSha256=0x00 WHERE VersionNumber=N'1.8.0-020'",tx);
                    Deny("CumulativeLedgerHashTamperedRefused",()=>V180UatExisting010HistoryTrust.ReadAndVerify(db,tx,historic,package,ledger));
                    tx.Rollback();
                }
                using(var tx=db.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    Exec(db,"INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES (N'1.8.0-013',N'SYNTHETIC_RESERVED',SYSUTCDATETIME(),N'isolated')",tx);
                    Deny("Reserved013Refused",()=>V180UatExisting010HistoryTrust.ReadAndVerify(db,tx,historic,package,ledger));
                    tx.Rollback();
                }
                V180UatExisting010HistoryTrust.ReadAndVerify(db,null,historic,package,ledger);
                Pass("PostRollbackStillValid");
            }
            Console.WriteLine("UpgradeSqlRuntime=PASS; Cases="+tests+"; Engine=LOCAL_SQL2022_DEVELOPER; UatSql=NOT_ACCESSED;");
            return 0;
        }
        catch
        {
            Console.WriteLine("UpgradeSqlRuntime=STOP; Details=SUPPRESSED;");
            return 1;
        }
        finally
        {
            if(dbName is not null)
            {
                try
                {
                    var builder=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("E1_R1_SQL_CONNECTION") ?? "");
                    builder.InitialCatalog="master";builder.Pooling=false;
                    using var admin=new SqlConnection(builder.ConnectionString);admin.Open();
                    Exec(admin,"ALTER DATABASE ["+dbName+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+dbName+"]",timeout:50);
                }
                catch { Console.WriteLine("UpgradeSqlCleanup=STOP; Details=SUPPRESSED;"); }
            }
        }
    }
}
