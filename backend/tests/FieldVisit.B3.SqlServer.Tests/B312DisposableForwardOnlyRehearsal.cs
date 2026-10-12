using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// WORK / disposable-only 012 rehearsal on the actual *names* of the 011
/// ChangeRequests and ChangeRequestEvents tables. All DDL/DML and the 012
/// version marker run inside a SQL transaction that is ALWAYS rolled back.
/// Not a migration, an application readiness check, or an apply executor.
/// </summary>
internal static class B312DisposableForwardOnlyRehearsal
{
    internal const string PartialShapeSql = """
        SELECT CAST(CASE WHEN
          (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions
             ORDER BY AppliedAt DESC, VersionNumber DESC)=N'1.8.0-012'
          AND (SELECT COUNT(*) FROM sys.check_constraints cc
               WHERE cc.is_disabled=0 AND cc.is_not_trusted=0
                 AND ((cc.parent_object_id=OBJECT_ID(N'dbo.ChangeRequests',N'U')
                     AND cc.name IN (N'CK_B3_ChangeRequests_KnownCodes',
                       N'CK_B3_ChangeRequests_KnownStatus',
                       N'CK_B3_ChangeRequests_ExpectedLocationVersion',
                       N'CK_B3_ChangeRequests_ProposedJson',
                       N'CK_B3_ChangeRequests_ReviewState'))
                  OR (cc.parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')
                     AND cc.name IN (N'CK_B3_ChangeRequestEvents_EventType',
                       N'CK_B3_ChangeRequestEvents_DetailsJson',
                       N'CK_B3_ChangeRequestEvents_DecisionState'))))=8
          AND EXISTS (SELECT 1 FROM sys.check_constraints cc
               WHERE cc.parent_object_id=OBJECT_ID(N'dbo.ChangeRequests',N'U')
                 AND cc.name=N'CK_B3_ChangeRequests_KnownStatus'
                 AND LOWER(cc.definition) LIKE N'%applied%')
          AND EXISTS (SELECT 1 FROM sys.check_constraints cc
               WHERE cc.parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')
                 AND cc.name=N'CK_B3_ChangeRequestEvents_EventType'
                 AND LOWER(cc.definition) LIKE N'%approved%'
                 AND LOWER(cc.definition) LIKE N'%applied%')
          AND (SELECT COUNT(*) FROM sys.foreign_keys fk
               WHERE fk.is_disabled=0 AND fk.is_not_trusted=0
                 AND fk.delete_referential_action=0
                 AND fk.parent_object_id IN
                    (OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                     OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')))=6
          AND (SELECT COUNT(*) FROM sys.indexes i WHERE
                 i.is_unique=1 AND i.is_disabled=0
                 AND i.is_hypothetical=0
                 AND ((i.object_id=OBJECT_ID(N'dbo.ChangeRequests',N'U')
                   AND i.name IN (N'UX_B3_ChangeRequests_RequestPublicId',
                                  N'UX_B3_ChangeRequests_Org_Entity_Pending'))
                   OR (i.object_id=OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')
                   AND i.name=N'UX_B3_ChangeRequestEvents_DecisionKey')))=3
          THEN 1 ELSE 0 END AS int)
        """;

    internal static async Task<object?> ScalarAsync(SqlConnection conn,
        SqlTransaction tx, string sql, params (string Name,object? Value)[] args)
    {
        await using var command = new SqlCommand(sql,conn,tx) { CommandTimeout=25 };
        foreach(var (name,value) in args)
            command.Parameters.AddWithValue(name,value??DBNull.Value);
        return await command.ExecuteScalarAsync();
    }

    internal static async Task ExecuteAsync(SqlConnection conn,
        SqlTransaction tx, string sql, params (string Name,object? Value)[] args)
    {
        await using var command = new SqlCommand(sql,conn,tx) { CommandTimeout=25 };
        foreach(var (name,value) in args)
            command.Parameters.AddWithValue(name,value??DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task RunRollbackOnlyAsync(
        DisposableSqlServer fixture,Func<SqlConnection,SqlTransaction,Task> check)
    {
        await using var conn=await fixture.ConnectAsync();
        await using var tx=(SqlTransaction)await conn.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            // Only our randomly named localhost fixture may enter this path.
            if(!fixture.DatabaseName.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
               || !string.Equals(conn.Database,fixture.DatabaseName,StringComparison.Ordinal))
                throw new InvalidOperationException("012_DISPOSABLE_ONLY");
            var version=await ScalarAsync(conn,tx,
                "SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC");
            if(!string.Equals((string?)version,"1.8.0-011",StringComparison.Ordinal))
                throw new InvalidOperationException("012_REQUIRES_EXACT_011_FIXTURE");
            await ExecuteAsync(conn,tx, """
                ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_KnownStatus;
                ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_KnownStatus
                  CHECK ([Status] IN (N'Pending',N'Rejected',N'Applied'));
                """);
            await ExecuteAsync(conn,tx, """
                ALTER TABLE dbo.ChangeRequests DROP CONSTRAINT CK_B3_ChangeRequests_ReviewState;
                ALTER TABLE dbo.ChangeRequests WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequests_ReviewState CHECK(
                    ([Status]=N'Pending'
                      AND [ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL
                      AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL)
                    OR ([Status]=N'Rejected'
                      AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]>0
                      AND [ReviewedByUserId]<>[RequestedByUserId]
                      AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt]
                      AND [ReviewReason] IS NOT NULL
                      AND LEN(LTRIM(RTRIM([ReviewReason])))>0
                      AND [AppliedAt] IS NULL)
                    OR ([Status]=N'Applied'
                      AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]>0
                      AND [ReviewedByUserId]<>[RequestedByUserId]
                      AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt]
                      AND [ReviewReason] IS NOT NULL
                      AND LEN(LTRIM(RTRIM([ReviewReason])))>0
                      AND [AppliedAt] IS NOT NULL AND [AppliedAt]>=[ReviewedAt])
                );
                """);
            await ExecuteAsync(conn,tx, """
                ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_EventType;
                ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_EventType
                   CHECK ([EventType] IN (N'Submitted',N'Rejected',N'Approved',N'Applied'));
                """);
            await ExecuteAsync(conn,tx, """
                ALTER TABLE dbo.ChangeRequestEvents DROP CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState;
                ALTER TABLE dbo.ChangeRequestEvents WITH CHECK ADD CONSTRAINT CK_B3_ChangeRequestEvents_DecisionState CHECK(
                    [ActorUserId] IS NOT NULL AND [ActorUserId]>0
                    AND [CorrelationId]<>'00000000-0000-0000-0000-000000000000'
                    AND (([EventType] IN (N'Submitted',N'Applied') AND [DecisionKey] IS NULL)
                      OR ([EventType] IN (N'Rejected',N'Approved')
                         AND [DecisionKey] IS NOT NULL
                         AND [DecisionKey]<>'00000000-0000-0000-0000-000000000000'))
                );
                """);
            await ExecuteAsync(conn,tx,
                "INSERT INTO dbo.SchemaVersions(VersionNumber,AppliedAt) VALUES(N'1.8.0-012',DATEADD(second,10,SYSUTCDATETIME()))");
            await check(conn,tx);
        }
        finally
        {
            // Deliberate rollback on success AND every exception. No 012 DDL,
            // marker, row or event is allowed to survive this test.
            await tx.RollbackAsync();
        }
    }
}
