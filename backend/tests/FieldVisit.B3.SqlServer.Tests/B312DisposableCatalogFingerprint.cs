using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// TEST ONLY: captures an exact, read-only SQL Server catalog fingerprint
/// within the temporary 011-to-012 rollback transaction. Never accepts
/// snapshots sourced from an external/unreviewed database as a release
/// readiness baseline. A future 012 production gate requires an independently
/// reviewed immutable fingerprint plus a full Locations schema contract.
/// </summary>
internal static class B312DisposableCatalogFingerprint
{
    private const string Columns = """
        SELECT CONCAT(OBJECT_NAME(c.object_id),N'|',c.name,N'|',
            c.system_type_id,N'|',c.max_length,N'|',c.is_nullable,N'|',
            c.scale,N'|',c.is_identity,N'|',c.is_computed)
        FROM sys.columns c
        WHERE c.object_id IN (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                              OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
        ORDER BY OBJECT_NAME(c.object_id),c.column_id
        """;

    private const string Checks = """
        SELECT CONCAT(OBJECT_NAME(cc.parent_object_id),N'|',cc.name,N'|',
            cc.is_disabled,N'|',cc.is_not_trusted,N'|',cc.definition)
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id IN
            (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
             OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
        ORDER BY OBJECT_NAME(cc.parent_object_id),cc.name
        """;

    private const string ForeignKeys = """
        SELECT CONCAT(OBJECT_NAME(fk.parent_object_id),N'|',fk.name,N'|',
            fk.is_disabled,N'|',fk.is_not_trusted,N'|',
            fk.delete_referential_action,N'|',
            OBJECT_NAME(fk.referenced_object_id),N'|',
            ISNULL((SELECT STRING_AGG(
                    CONCAT(fkc.constraint_column_id,N':',pc.name,N'>',rc.name),N',')
                    WITHIN GROUP (ORDER BY fkc.constraint_column_id)
                FROM sys.foreign_key_columns fkc
                JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id
                                   AND pc.column_id=fkc.parent_column_id
                JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id
                                   AND rc.column_id=fkc.referenced_column_id
                WHERE fkc.constraint_object_id=fk.object_id),N''))
        FROM sys.foreign_keys fk
        WHERE fk.parent_object_id IN
            (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
             OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
        ORDER BY OBJECT_NAME(fk.parent_object_id),fk.name
        """;

    private const string UniqueIndexes = """
        SELECT CONCAT(OBJECT_NAME(i.object_id),N'|',i.name,N'|',
            i.is_unique,N'|',i.is_disabled,N'|',i.is_hypothetical,N'|',
            i.has_filter,N'|',ISNULL(i.filter_definition,N'<null>'),N'|',
            ISNULL((SELECT STRING_AGG(
                    CONCAT(ic.key_ordinal,N':',c.name),N',')
                    WITHIN GROUP (ORDER BY ic.key_ordinal)
                FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id=ic.object_id
                    AND c.column_id=ic.column_id
                WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                    AND ic.key_ordinal>0),N''))
        FROM sys.indexes i
        WHERE i.object_id IN (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                              OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
          AND i.name LIKE N'UX_B3_%'
        ORDER BY OBJECT_NAME(i.object_id),i.name
        """;

    private const string Triggers = """
        SELECT CONCAT(OBJECT_NAME(tr.parent_id),N'|',tr.name,N'|',
            tr.is_disabled,N'|',ISNULL(OBJECT_DEFINITION(tr.object_id),N'<null>'))
        FROM sys.triggers tr
        WHERE tr.parent_id IN (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                               OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
        ORDER BY OBJECT_NAME(tr.parent_id),tr.name
        """;

    private const string VersionHistory = """
        SELECT CONCAT(VersionNumber,N'|',CONVERT(nvarchar(30),AppliedAt,126))
        FROM dbo.SchemaVersions
        ORDER BY AppliedAt,VersionNumber
        """;

    private static async Task<string[]> GetRowsAsync(
        SqlConnection conn,SqlTransaction tx,string sql)
    {
        await using var cmd=new SqlCommand(sql,conn,tx){CommandTimeout=25};
        await using var reader=await cmd.ExecuteReaderAsync();
        var rows=new List<string>();
        while(await reader.ReadAsync())
        {
            if(reader.IsDBNull(0))
                throw new InvalidOperationException("012_CATALOG_NULL_DENIED");
            rows.Add(reader.GetString(0));
        }
        return rows.ToArray();
    }

    internal static async Task<string> CaptureAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        // All tables have been assembled by isolated fixture scripts. NEVER
        // use this snapshot as a self-authorizing production fingerprint.
        var columns=await GetRowsAsync(conn,tx,Columns);
        var checks=await GetRowsAsync(conn,tx,Checks);
        var foreignKeys=await GetRowsAsync(conn,tx,ForeignKeys);
        var indexes=await GetRowsAsync(conn,tx,UniqueIndexes);
        var triggers=await GetRowsAsync(conn,tx,Triggers);
        var versions=await GetRowsAsync(conn,tx,VersionHistory);
        if(columns.Length!=28 || checks.Length!=8 ||
            foreignKeys.Length!=6 || indexes.Length!=3 ||
            triggers.Length!=0 || versions.Length!=2 ||
            !versions[0].StartsWith("1.8.0-011|",StringComparison.Ordinal) ||
            !versions[1].StartsWith("1.8.0-012|",StringComparison.Ordinal))
            throw new InvalidOperationException("012_CATALOG_SHAPE_DENIED");

        var partial=Convert.ToInt32(
            await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,
                B312DisposableForwardOnlyRehearsal.PartialShapeSql));
        if(partial!=1)
            throw new InvalidOperationException("012_CATALOG_SHAPE_DENIED");

        // Preserve actual CHECK text and SQL Server metadata with no token
        // stripping, no runtime repair and no relaxed expression matching.
        var data=string.Join("\n",new[]
        {
            "COLUMNS:"+string.Join("\n",columns),
            "CHECKS:"+string.Join("\n",checks),
            "FOREIGN_KEYS:"+string.Join("\n",foreignKeys),
            "UNIQUE_INDEXES:"+string.Join("\n",indexes),
            "TRIGGERS:"+string.Join("\n",triggers),
            "VERSIONS:"+string.Join("\n",versions)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }

    internal static async Task RequireUnchangedAsync(
        SqlConnection conn,SqlTransaction tx,string expectedFixtureFingerprint)
    {
        if(string.IsNullOrWhiteSpace(expectedFixtureFingerprint) ||
           !string.Equals(await CaptureAsync(conn,tx),
                expectedFixtureFingerprint,StringComparison.Ordinal))
            throw new InvalidOperationException("012_CATALOG_FINGERPRINT_MISMATCH");
    }
}
