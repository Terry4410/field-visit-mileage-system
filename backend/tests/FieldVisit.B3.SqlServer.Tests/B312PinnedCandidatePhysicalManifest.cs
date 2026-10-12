using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Independent, source-pinned PHYSICAL metadata contract for the 011->012
/// disposable ChangeRequests/ChangeRequestEvents candidate only.
/// This is NOT a real 012 migration, UAT catalogue capture, an IT-approved
/// release checksum, nor a real deployment readiness probe.
/// All methods require the existing localhost-only rollback transaction.
/// </summary>
internal static class B312PinnedCandidatePhysicalManifest
{
    // Format: table|column|SQL system_type_id|max_length bytes|scale|
    //         nullable|identity|computed. An nvarchar(n) is stored as 2*n bytes.
    // Explicit hand-reviewed values, never computed from runtime sys.columns.
    private static readonly HashSet<string> ExpectedColumns=
        new(StringComparer.Ordinal)
        {
            "ChangeRequests|ChangeRequestId|127|8|0|0|1|0",
            "ChangeRequests|RequestPublicId|36|16|0|0|0|0",
            "ChangeRequests|OrganizationId|56|4|0|0|0|0",
            "ChangeRequests|TeamId|56|4|0|1|0|0",
            "ChangeRequests|EntityKind|231|80|0|0|0|0",
            "ChangeRequests|EntityId|231|160|0|0|0|0",
            "ChangeRequests|OperationCode|231|160|0|0|0|0",
            "ChangeRequests|RiskCode|231|40|0|0|0|0",
            "ChangeRequests|ExpectedEntityRowVersion|165|8|0|1|0|0",
            "ChangeRequests|BeforeJson|231|-1|0|1|0|0",
            "ChangeRequests|ProposedJson|231|-1|0|0|0|0",
            "ChangeRequests|EvidenceJson|231|-1|0|1|0|0",
            "ChangeRequests|RequestedByUserId|56|4|0|0|0|0",
            "ChangeRequests|SubmittedAt|42|7|3|0|0|0",
            "ChangeRequests|Status|231|60|0|0|0|0",
            "ChangeRequests|ReviewedByUserId|56|4|0|1|0|0",
            "ChangeRequests|ReviewedAt|42|7|3|1|0|0",
            "ChangeRequests|ReviewReason|231|2000|0|1|0|0",
            "ChangeRequests|AppliedAt|42|7|3|1|0|0",
            "ChangeRequests|RowVersion|189|8|0|0|0|0",
            "ChangeRequestEvents|ChangeRequestEventId|127|8|0|0|1|0",
            "ChangeRequestEvents|ChangeRequestId|127|8|0|0|0|0",
            "ChangeRequestEvents|EventType|231|80|0|0|0|0",
            "ChangeRequestEvents|ActorUserId|56|4|0|1|0|0",
            "ChangeRequestEvents|OccurredAt|42|7|3|0|0|0",
            "ChangeRequestEvents|CorrelationId|36|16|0|0|0|0",
            "ChangeRequestEvents|DecisionKey|36|16|0|1|0|0",
            "ChangeRequestEvents|DetailsJson|231|-1|0|1|0|0"
        };

    private static readonly HashSet<string> ExpectedUniqueIndexes=
        new(StringComparer.Ordinal)
        {
            "ChangeRequests|UX_B3_ChangeRequests_RequestPublicId|1:RequestPublicId|-",
            "ChangeRequests|UX_B3_ChangeRequests_Org_Entity_Pending|1:OrganizationId|2:EntityKind|3:EntityId|status='pending'",
            "ChangeRequestEvents|UX_B3_ChangeRequestEvents_DecisionKey|1:DecisionKey|decisionkeyisnotnull"
        };

    private static readonly HashSet<string> ExpectedForeignKeys=
        new(StringComparer.Ordinal)
        {
            "ChangeRequests|FK_B3_Requests_Org|OrganizationId|Organizations|OrganizationId",
            "ChangeRequests|FK_B3_Requests_Team|TeamId|Teams|TeamId",
            "ChangeRequests|FK_B3_Requests_Requester|RequestedByUserId|Users|UserId",
            "ChangeRequests|FK_B3_Requests_Reviewer|ReviewedByUserId|Users|UserId",
            "ChangeRequestEvents|FK_B3_Events_Request|ChangeRequestId|ChangeRequests|ChangeRequestId",
            "ChangeRequestEvents|FK_B3_Events_Actor|ActorUserId|Users|UserId"
        };

    private static void RequireDisposable(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
            ||tx.Connection!=conn)
            throw new InvalidOperationException("012_PHYSICAL_MANIFEST_DISPOSABLE_ONLY");
    }

    internal static async Task RequireColumnsAsync(SqlConnection conn,SqlTransaction tx)
    {
        RequireDisposable(conn,tx);
        await using var cmd=new SqlCommand("""
            SELECT CONCAT(OBJECT_NAME(c.object_id),N'|',c.name,N'|',
                c.system_type_id,N'|',c.max_length,N'|',c.scale,N'|',
                CONVERT(int,c.is_nullable),N'|',CONVERT(int,c.is_identity),
                N'|',CONVERT(int,c.is_computed))
            FROM sys.columns c
            WHERE c.object_id IN (
                OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            """,conn,tx){CommandTimeout=25};
        var actual=new HashSet<string>(StringComparer.Ordinal);
        await using(var reader=await cmd.ExecuteReaderAsync())
        {
            while(await reader.ReadAsync())
            {
                if(!actual.Add(reader.GetString(0)))
                    throw new InvalidOperationException("012_CANDIDATE_COLUMN_DUPLICATE");
            }
        }
        if(ExpectedColumns.Count!=28 || !actual.SetEquals(ExpectedColumns))
            throw new InvalidOperationException("012_CANDIDATE_PHYSICAL_COLUMN_DRIFT");
    }

    private static string NormalizeFilter(string? source)
    {
        if(source is null)return "-";
        // SQL Server may surround a filtered predicate with parentheses
        // and [identifier] brackets; preserve operators and literal values.
        return string.Concat(source.Where(c=>
            !char.IsWhiteSpace(c)&&c is not '[' and not ']' and not '(' and not ')'))
            .ToLowerInvariant();
    }

    internal static async Task RequireUniqueIndexesAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        RequireDisposable(conn,tx);
        await using var cmd=new SqlCommand("""
            SELECT OBJECT_NAME(i.object_id),i.name,i.is_unique,i.is_disabled,
                   i.is_hypothetical,i.filter_definition,
                   (SELECT STRING_AGG(CONCAT(ic.key_ordinal,N':',c.name),N'|')
                            WITHIN GROUP(ORDER BY ic.key_ordinal)
                    FROM sys.index_columns ic
                    JOIN sys.columns c ON c.object_id=ic.object_id
                        AND c.column_id=ic.column_id
                    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                        AND ic.key_ordinal>0),
                   (SELECT COUNT(*) FROM sys.index_columns ic
                    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                      AND ic.is_included_column=1)
            FROM sys.indexes i
            WHERE i.object_id IN (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
                AND i.is_primary_key=0 AND (
                    i.is_unique=1 OR i.name LIKE N'UX_B3_%')
            """,conn,tx){CommandTimeout=25};
        var actual=new HashSet<string>(StringComparer.Ordinal);
        await using(var rd=await cmd.ExecuteReaderAsync())
        {
            while(await rd.ReadAsync())
            {
                if(!rd.GetBoolean(2)||rd.GetBoolean(3)||rd.GetBoolean(4)
                    ||rd.GetInt32(7)!=0)
                    throw new InvalidOperationException("012_CANDIDATE_UNIQUE_INDEX_INVALID");
                var row=string.Join("|",rd.GetString(0),rd.GetString(1),
                    rd.IsDBNull(6)?"":rd.GetString(6),
                    NormalizeFilter(rd.IsDBNull(5)?null:rd.GetString(5)));
                if(!actual.Add(row))
                    throw new InvalidOperationException("012_CANDIDATE_UNIQUE_INDEX_DUPLICATE");
            }
        }
        if(ExpectedUniqueIndexes.Count!=3||!actual.SetEquals(ExpectedUniqueIndexes))
            throw new InvalidOperationException("012_CANDIDATE_UNIQUE_INDEX_DRIFT");
    }

    internal static async Task RequireForeignKeysAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        RequireDisposable(conn,tx);
        await using var cmd=new SqlCommand("""
            SELECT OBJECT_NAME(fk.parent_object_id),fk.name,p.name,
                   OBJECT_NAME(fk.referenced_object_id),r.name,
                   fk.is_disabled,fk.is_not_trusted,
                   fk.delete_referential_action,fk.update_referential_action,
                   fkc.constraint_column_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc
                ON fkc.constraint_object_id=fk.object_id
            JOIN sys.columns p ON p.object_id=fkc.parent_object_id
                AND p.column_id=fkc.parent_column_id
            JOIN sys.columns r ON r.object_id=fkc.referenced_object_id
                AND r.column_id=fkc.referenced_column_id
            WHERE fk.parent_object_id IN (
                OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            """,conn,tx){CommandTimeout=25};
        var actual=new HashSet<string>(StringComparer.Ordinal);
        await using(var rd=await cmd.ExecuteReaderAsync())
        {
            while(await rd.ReadAsync())
            {
                if(rd.GetBoolean(5)||rd.GetBoolean(6)
                    ||rd.GetByte(7)!=0||rd.GetByte(8)!=0
                    ||rd.GetInt32(9)!=1)
                    throw new InvalidOperationException("012_CANDIDATE_FK_UNTRUSTED_OR_CASCADE");
                var row=string.Join("|",Enumerable.Range(0,5).Select(rd.GetString));
                if(!actual.Add(row))
                    throw new InvalidOperationException("012_CANDIDATE_FK_DUPLICATE");
            }
        }
        if(ExpectedForeignKeys.Count!=6||!actual.SetEquals(ExpectedForeignKeys))
            throw new InvalidOperationException("012_CANDIDATE_FK_COLUMN_DRIFT");
    }

    internal static async Task RequireAllAsync(SqlConnection conn,SqlTransaction tx)
    {
        await B312PinnedCandidateNameManifest.RequireExactNamedObjectsAsync(conn,tx);
        await RequireColumnsAsync(conn,tx);
        await RequireUniqueIndexesAsync(conn,tx);
        await RequireForeignKeysAsync(conn,tx);
    }
}
