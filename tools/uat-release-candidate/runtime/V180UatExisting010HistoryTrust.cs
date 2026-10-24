using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

// Strict existing-010 only. SELECT metadata/version rows in-process; never emit raw rows,
// principal names, connection data or SQL provider errors. Original fresh-install trust untouched.
public static class V180UatExisting010HistoryTrust
{
    public sealed record Proof(string HistoricalMarkerSha256,string PromotionPackageId,string LedgerSha256);
    private const int HistoricalCount=26;
    private static readonly string[] Steps={"011","012","014","015","016","017","018","019","020"};
    private static readonly string[] NewMarkers={
        "1.8.0-011","1.8.0-012","1.8.0-014","1.8.0-015",
        "1.8.0-016","1.8.0-LEADER-REVIEW","1.8.0-017","1.8.0-CREATE-LOWRISK",
        "1.8.0-018","1.8.0-LOWRISK-DIRECT","1.8.0-019","1.8.0-020",
        "1.8.0-EXCEL-CANONICAL-STATUS"};
    private static readonly string[] StepAliases={"","", "", "", "1.8.0-LEADER-REVIEW",
        "1.8.0-CREATE-LOWRISK","1.8.0-LOWRISK-DIRECT","","1.8.0-EXCEL-CANONICAL-STATUS"};
    private static readonly string MarkersSql=string.Join(",",NewMarkers.Select(v=>"N'"+v+"'"));
    private static bool Hex(string? v)=>v is {Length:64} && v.All(Uri.IsHexDigit)
        && v==v.ToLowerInvariant();
    private static InvalidOperationException Denied()=>new("OPTIONC_UAT_TRUST_DENIED");
    private static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    private static string ReadSingle(SqlConnection c,SqlTransaction? tx,string sql)
    {
        using var cmd=new SqlCommand(sql,c,tx){CommandTimeout=10};
        var value=cmd.ExecuteScalar() as string;
        if(string.IsNullOrEmpty(value))throw Denied();
        return value;
    }
    private static int ReadCount(SqlConnection c,SqlTransaction? tx,string sql)
    {
        using var cmd=new SqlCommand(sql,c,tx){CommandTimeout=10};
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    public static Proof ReadAndVerify(SqlConnection c,SqlTransaction? tx,
        string historicalMarkerSha256,string promotionPackageId,string ledgerSha256)
    {
        if(!Hex(historicalMarkerSha256)||!Hex(promotionPackageId)||!Hex(ledgerSha256))
            throw Denied();
        // Fixed 26-row predecessor is a known UAT010 boundary, not a guess that every
        // 26-row database is compatible. A separately signed exact SHA binds all values.
        var baseFilter="VersionNumber NOT IN ("+MarkersSql+")";
        if(ReadCount(c,tx,"SELECT COUNT(*) FROM dbo.SchemaVersions WHERE "+baseFilter)!=HistoricalCount
          ||ReadCount(c,tx,"SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN ("+MarkersSql+")")!=13
          ||ReadCount(c,tx,"SELECT COUNT(*) FROM dbo.SchemaVersions")!=39
          ||ReadCount(c,tx,"SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010'")!=1
          ||ReadCount(c,tx,"SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-013'")!=0)
            throw Denied();
        var history=ReadSingle(c,tx,"SELECT (SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE "
            +baseFilter+" ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES)");
        if(Hash(history)!=historicalMarkerSha256)throw Denied();

        if(ReadCount(c,tx,"SELECT COUNT(*) FROM dbo.FieldVisitMigrationLedger")!=9)throw Denied();
        var ledger=ReadSingle(c,tx,"SELECT (SELECT VersionNumber,PackageId,PayloadSha256,CatalogSha256,MarkersSha256,AppliedAt FROM dbo.FieldVisitMigrationLedger ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES)");
        if(Hash(ledger)!=ledgerSha256)throw Denied();

        // The 9 signed ledger entries must contain exactly the nine source-approved
        // numeric phases and one common 32-byte immutable promotion package id.
        using(var cmd=new SqlCommand("SELECT VersionNumber,PackageId,MarkersSha256 FROM dbo.FieldVisitMigrationLedger ORDER BY VersionNumber",c,tx){CommandTimeout=10})
        using(var rows=cmd.ExecuteReader())
        {
            var stepIndex=0;
            while(rows.Read())
            {
                if(stepIndex>=Steps.Length
                    ||rows.GetString(0)!="1.8.0-"+Steps[stepIndex]
                    ||!CryptographicOperations.FixedTimeEquals(rows.GetFieldValue<byte[]>(1),Convert.FromHexString(promotionPackageId))
                    ||rows.IsDBNull(2)
                    ||rows.GetFieldValue<byte[]>(2).Length!=32)throw Denied();
                stepIndex++;
            }
            if(stepIndex!=Steps.Length)throw Denied();
        }

        // Match each 011..020 cumulative marker-content SHA to its ledger entry,
        // not just the marker names/count: post-deploy marker edits must deny.
        var accumulated=new List<string>();
        for(var i=0;i<Steps.Length;i++)
        {
            accumulated.Add("1.8.0-"+Steps[i]);
            if(!string.IsNullOrEmpty(StepAliases[i]))accumulated.Add(StepAliases[i]);
            var names=string.Join(",",accumulated.Select(v=>"N'"+v+"'"));
            var calculated=ReadSingle(c,tx,
                "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),"
                +"(SELECT VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions WHERE VersionNumber IN ("
                +names+") ORDER BY VersionNumber FOR JSON PATH,INCLUDE_NULL_VALUES))),2)");
            using var cmd=new SqlCommand("SELECT MarkersSha256 FROM dbo.FieldVisitMigrationLedger WHERE VersionNumber=@version",c,tx){CommandTimeout=10};
            cmd.Parameters.AddWithValue("@version","1.8.0-"+Steps[i]);
            var stored=cmd.ExecuteScalar() as byte[];
            if(stored is not {Length:32}
                ||!CryptographicOperations.FixedTimeEquals(stored,Convert.FromHexString(calculated)))throw Denied();
        }
        return new Proof(historicalMarkerSha256,promotionPackageId,ledgerSha256);
    }
}
