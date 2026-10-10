using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Read-only, fail-closed B3 catalog contract and known unique-index error
/// translation. Not a migration; no feature flag change and no apply grant.
/// </summary>
public static class V180B3SqlSafetyRules
{
    public const string RequestPublicIdIndex="UX_B3_ChangeRequests_RequestPublicId";
    public const string PendingRequestIndex="UX_B3_ChangeRequests_Org_Entity_Pending";
    public const string DecisionKeyIndex="UX_B3_ChangeRequestEvents_DecisionKey";
    public const string LatestSchemaVersionSql=
        "SELECT TOP (1) VersionNumber AS Value FROM dbo.SchemaVersions " +
        "ORDER BY AppliedAt DESC, VersionNumber DESC";

    // This check runs ONLY when B3 feature flag is explicitly enabled.
    // Absence of any required object / index produces 0, never creates DDL.
    // SQL Server filtered-index definitions may contain brackets, N prefix,
    // spaces and redundant parentheses, but no additional predicates.
    public const string CatalogCheckSql = """
        SELECT CAST(CASE WHEN
            OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL
            AND OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NOT NULL
            AND OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NOT NULL
            -- Neither B3 table is permitted to have an enabled trigger:
            -- an AFTER/INSTEAD OF trigger could mutate Locations or audit
            -- outside the authorized B3 approval executor (DENY ALL).
            AND NOT EXISTS (
                SELECT 1 FROM sys.triggers tr
                WHERE tr.is_disabled=0
                  AND tr.parent_id IN (
                    OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                    OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')
                  )
            )
            AND EXISTS (
                SELECT 1 FROM sys.columns c
                WHERE c.object_id=OBJECT_ID(N'dbo.ChangeRequests',N'U')
                    AND c.name=N'RowVersion' AND c.system_type_id=189
                    AND c.is_nullable=0
            )
            AND EXISTS (
                SELECT 1 FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id=i.object_id
                    AND ic.index_id=i.index_id AND ic.key_ordinal=1
                JOIN sys.columns c ON c.object_id=ic.object_id
                    AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'dbo.ChangeRequests',N'U')
                    AND i.name=N'UX_B3_ChangeRequests_RequestPublicId'
                    AND i.is_unique=1 AND i.has_filter=0
                    AND i.is_disabled=0 AND i.is_hypothetical=0
                    AND c.name=N'RequestPublicId'
                    AND (SELECT COUNT(*) FROM sys.index_columns ix
                         WHERE ix.object_id=i.object_id
                           AND ix.index_id=i.index_id AND ix.key_ordinal>0)=1
            )
            AND EXISTS (
                SELECT 1 FROM sys.indexes i
                WHERE i.object_id=OBJECT_ID(N'dbo.ChangeRequests',N'U')
                    AND i.name=N'UX_B3_ChangeRequests_Org_Entity_Pending'
                    AND i.is_unique=1 AND i.has_filter=1
                    AND i.is_disabled=0 AND i.is_hypothetical=0
                    AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LOWER(i.filter_definition),N'[',N''),N']',N''),
                        N' ',N''),N'(',N''),N')',N'')
                        IN (N'status=''pending''',N'status=n''pending''')
                    AND (SELECT COUNT(*) FROM sys.index_columns ix
                         WHERE ix.object_id=i.object_id
                           AND ix.index_id=i.index_id AND ix.key_ordinal>0)=3
                    AND EXISTS (
                        SELECT 1 FROM sys.index_columns ic
                        JOIN sys.columns c ON c.object_id=ic.object_id
                            AND c.column_id=ic.column_id
                        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                            AND ic.key_ordinal=1 AND c.name=N'OrganizationId')
                    AND EXISTS (
                        SELECT 1 FROM sys.index_columns ic
                        JOIN sys.columns c ON c.object_id=ic.object_id
                            AND c.column_id=ic.column_id
                        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                            AND ic.key_ordinal=2 AND c.name=N'EntityKind')
                    AND EXISTS (
                        SELECT 1 FROM sys.index_columns ic
                        JOIN sys.columns c ON c.object_id=ic.object_id
                            AND c.column_id=ic.column_id
                        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                            AND ic.key_ordinal=3 AND c.name=N'EntityId')
            )
            AND EXISTS (
                SELECT 1 FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id=i.object_id
                    AND ic.index_id=i.index_id AND ic.key_ordinal=1
                JOIN sys.columns c ON c.object_id=ic.object_id
                    AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')
                    AND i.name=N'UX_B3_ChangeRequestEvents_DecisionKey'
                    AND i.is_unique=1 AND i.has_filter=1
                    AND i.is_disabled=0 AND i.is_hypothetical=0
                    AND c.name=N'DecisionKey'
                    AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LOWER(i.filter_definition),N'[',N''),N']',N''),
                        N' ',N''),N'(',N''),N')',N'')=N'decisionkeyisnotnull'
                    AND (SELECT COUNT(*) FROM sys.index_columns ix
                         WHERE ix.object_id=i.object_id
                           AND ix.index_id=i.index_id AND ix.key_ordinal>0)=1
            )


            -- Exact structural shape for the candidate 011 fields.
            -- Type ids: bigint 127, int 56, uniqueidentifier 36,
            -- nvarchar 231, varbinary 165, datetime2 42, rowversion 189.
            -- NVARCHAR widths are stored as UTF-16 BYTES; -1 means MAX.
            -- MaxBytes=-2 means compare datetime2 scale only.
            AND (
                SELECT COUNT(*) FROM (VALUES
                    (N'ChangeRequests',N'ChangeRequestId',127,8,0,-1),
                    (N'ChangeRequests',N'RequestPublicId',36,16,0,-1),
                    (N'ChangeRequests',N'OrganizationId',56,4,0,-1),
                    (N'ChangeRequests',N'TeamId',56,4,1,-1),
                    (N'ChangeRequests',N'EntityKind',231,80,0,-1),
                    (N'ChangeRequests',N'EntityId',231,160,0,-1),
                    (N'ChangeRequests',N'OperationCode',231,160,0,-1),
                    (N'ChangeRequests',N'RiskCode',231,40,0,-1),
                    (N'ChangeRequests',N'ExpectedEntityRowVersion',165,8,1,-1),
                    (N'ChangeRequests',N'BeforeJson',231,-1,1,-1),
                    (N'ChangeRequests',N'ProposedJson',231,-1,0,-1),
                    (N'ChangeRequests',N'EvidenceJson',231,-1,1,-1),
                    (N'ChangeRequests',N'RequestedByUserId',56,4,0,-1),
                    (N'ChangeRequests',N'SubmittedAt',42,-2,0,3),
                    (N'ChangeRequests',N'Status',231,60,0,-1),
                    (N'ChangeRequests',N'ReviewedByUserId',56,4,1,-1),
                    (N'ChangeRequests',N'ReviewedAt',42,-2,1,3),
                    (N'ChangeRequests',N'ReviewReason',231,2000,1,-1),
                    (N'ChangeRequests',N'AppliedAt',42,-2,1,3),
                    (N'ChangeRequests',N'RowVersion',189,8,0,-1),
                    (N'ChangeRequestEvents',N'ChangeRequestEventId',127,8,0,-1),
                    (N'ChangeRequestEvents',N'ChangeRequestId',127,8,0,-1),
                    (N'ChangeRequestEvents',N'EventType',231,80,0,-1),
                    (N'ChangeRequestEvents',N'ActorUserId',56,4,1,-1),
                    (N'ChangeRequestEvents',N'OccurredAt',42,-2,0,3),
                    (N'ChangeRequestEvents',N'CorrelationId',36,16,0,-1),
                    (N'ChangeRequestEvents',N'DecisionKey',36,16,1,-1),
                    (N'ChangeRequestEvents',N'DetailsJson',231,-1,1,-1)
                ) AS required(TableName,ColumnName,SqlType,MaxBytes,IsNullable,ScaleValue)
                WHERE EXISTS (
                    SELECT 1 FROM sys.columns c
                    WHERE c.object_id=OBJECT_ID(N'dbo.'+required.TableName,N'U')
                        AND c.name=required.ColumnName
                        AND c.system_type_id=required.SqlType
                        AND (required.MaxBytes=-2 OR c.max_length=required.MaxBytes)
                        AND c.is_nullable=required.IsNullable
                        AND (required.ScaleValue=-1 OR c.scale=required.ScaleValue)
                )
            )=28

            -- Trusted, enabled, single-column, NO ACTION relationships:
            -- absent/untrusted/cascade FKs are unsafe for historical audit.
            AND (
                SELECT COUNT(*) FROM (VALUES
                    (N'ChangeRequests',N'OrganizationId',N'Organizations',N'OrganizationId'),
                    (N'ChangeRequests',N'TeamId',N'Teams',N'TeamId'),
                    (N'ChangeRequests',N'RequestedByUserId',N'Users',N'UserId'),
                    (N'ChangeRequests',N'ReviewedByUserId',N'Users',N'UserId'),
                    (N'ChangeRequestEvents',N'ChangeRequestId',N'ChangeRequests',N'ChangeRequestId'),
                    (N'ChangeRequestEvents',N'ActorUserId',N'Users',N'UserId')
                ) AS required(ParentTable,ParentColumn,ReferencedTable,ReferencedColumn)
                WHERE EXISTS (
                    SELECT 1 FROM sys.foreign_keys fk
                    JOIN sys.foreign_key_columns fkc
                        ON fkc.constraint_object_id=fk.object_id
                    JOIN sys.columns parent_column
                        ON parent_column.object_id=fkc.parent_object_id
                        AND parent_column.column_id=fkc.parent_column_id
                    JOIN sys.columns reference_column
                        ON reference_column.object_id=fkc.referenced_object_id
                        AND reference_column.column_id=fkc.referenced_column_id
                    WHERE fk.parent_object_id=OBJECT_ID(N'dbo.'+required.ParentTable,N'U')
                        AND fk.referenced_object_id=OBJECT_ID(N'dbo.'+required.ReferencedTable,N'U')
                        AND fk.is_disabled=0 AND fk.is_not_trusted=0
                        AND fk.delete_referential_action=0
                        AND fkc.constraint_column_id=1
                        AND parent_column.name=required.ParentColumn
                        AND reference_column.name=required.ReferencedColumn
                        AND NOT EXISTS (
                            SELECT 1 FROM sys.foreign_key_columns other
                            WHERE other.constraint_object_id=fk.object_id
                                AND other.constraint_column_id>1
                        )
                )
            )=6

            -- The 8 candidate 011 CHECK constraints must all be enabled and
            -- trusted. Names + essential expression tokens are checked here;
            -- full normalized definition matching and live SQL Server proof
            -- remain an independent IT/Owner gate before feature activation.
            AND (
                SELECT COUNT(*) FROM (VALUES
                    (N'ChangeRequests',N'CK_B3_ChangeRequests_KnownCodes',N'entitykind',N'operationcode'),
                    (N'ChangeRequests',N'CK_B3_ChangeRequests_KnownStatus',N'pending',N'rejected'),
                    (N'ChangeRequests',N'CK_B3_ChangeRequests_ExpectedLocationVersion',N'datalength',N'expectedentityrowversion'),
                    (N'ChangeRequests',N'CK_B3_ChangeRequests_ProposedJson',N'isjson',N'proposedjson'),
                    (N'ChangeRequests',N'CK_B3_ChangeRequests_ReviewState',N'reviewedbyuserid',N'appliedat'),
                    (N'ChangeRequestEvents',N'CK_B3_ChangeRequestEvents_EventType',N'submitted',N'rejected'),
                    (N'ChangeRequestEvents',N'CK_B3_ChangeRequestEvents_DetailsJson',N'isjson',N'detailsjson'),
                    (N'ChangeRequestEvents',N'CK_B3_ChangeRequestEvents_DecisionState',N'decisionkey',N'actoruserid')
                ) AS required(TableName,ConstraintName,Token1,Token2)
                WHERE EXISTS (
                    SELECT 1 FROM sys.check_constraints cc
                    WHERE cc.parent_object_id=OBJECT_ID(N'dbo.'+required.TableName,N'U')
                      AND cc.name=required.ConstraintName
                      AND cc.is_disabled=0 AND cc.is_not_trusted=0
                      AND LOWER(cc.definition) LIKE N'%'+required.Token1+N'%'
                      AND LOWER(cc.definition) LIKE N'%'+required.Token2+N'%'
                      -- Pending/Rejected are the ONLY staged candidate
                      -- states. Never treat an old 'Applied' schema as ready
                      -- before independent approval/apply authorization.
                      AND (required.ConstraintName<>N'CK_B3_ChangeRequests_KnownStatus'
                           OR (LOWER(cc.definition) NOT LIKE N'%applied%'
                               AND LOWER(cc.definition) NOT LIKE N'%returned%'
                               AND LOWER(cc.definition) NOT LIKE N'%cancelled%'))
                )
            )=8
            THEN 1 ELSE 0 END AS int) AS Value
        """;

    public static void RequireLatestSchemaVersion(string? latestVersion)
    {
        if(!string.Equals(latestVersion,"1.8.0-011",StringComparison.Ordinal))
            throw new InvalidOperationException("B3_SCHEMA_NOT_VERIFIED");
    }

    public static string? RecognizedUniqueConflict(int sqlNumber,string? sqlMessage)
    {
        if(sqlNumber is not (2601 or 2627) || string.IsNullOrEmpty(sqlMessage))
            return null;
        if(sqlMessage.Contains(PendingRequestIndex,StringComparison.OrdinalIgnoreCase))
            return "B3_PENDING_REQUEST_EXISTS";
        if(sqlMessage.Contains(DecisionKeyIndex,StringComparison.OrdinalIgnoreCase))
            return "B3_DECISION_KEY_REPLAY";
        return null; // Other SQL violations are NOT approval for retries.
    }

    public static bool IsUnavailableCode(string? code) =>
        code is "B3_DISABLED" or "B3_SCHEMA_NOT_VERIFIED";

    public static bool IsConflictCode(string? code) =>
        code is "B3_PENDING_REQUEST_EXISTS" or "B3_DECISION_KEY_REPLAY";

    public static void RethrowRecognizedUniqueConflict(DbUpdateException error)
    {
        if(error.InnerException is SqlException sql &&
            RecognizedUniqueConflict(sql.Number,sql.Message) is { } code)
            throw new InvalidOperationException(code,error);
    }
}
