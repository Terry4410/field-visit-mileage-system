using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Immutable, source-authored WORK-only candidate metadata manifest for the
/// two B3 disposable 012 request/event tables. Unlike a self-captured hash,
/// the expected object names are fixed independently of the live SQL result.
/// NOT a production full-schema fingerprint and NOT signed by HR/IT/Owner.
/// Do not use this as permission to deploy, migrate or activate B3.
/// </summary>
internal static class B312PinnedCandidateNameManifest
{
    private static readonly HashSet<string> Expected=new(StringComparer.Ordinal)
    {
        "C|ChangeRequests|ChangeRequestId",
        "C|ChangeRequests|RequestPublicId",
        "C|ChangeRequests|OrganizationId",
        "C|ChangeRequests|TeamId",
        "C|ChangeRequests|EntityKind",
        "C|ChangeRequests|EntityId",
        "C|ChangeRequests|OperationCode",
        "C|ChangeRequests|RiskCode",
        "C|ChangeRequests|ExpectedEntityRowVersion",
        "C|ChangeRequests|BeforeJson",
        "C|ChangeRequests|ProposedJson",
        "C|ChangeRequests|EvidenceJson",
        "C|ChangeRequests|RequestedByUserId",
        "C|ChangeRequests|SubmittedAt",
        "C|ChangeRequests|Status",
        "C|ChangeRequests|ReviewedByUserId",
        "C|ChangeRequests|ReviewedAt",
        "C|ChangeRequests|ReviewReason",
        "C|ChangeRequests|AppliedAt",
        "C|ChangeRequests|RowVersion",
        "C|ChangeRequestEvents|ChangeRequestEventId",
        "C|ChangeRequestEvents|ChangeRequestId",
        "C|ChangeRequestEvents|EventType",
        "C|ChangeRequestEvents|ActorUserId",
        "C|ChangeRequestEvents|OccurredAt",
        "C|ChangeRequestEvents|CorrelationId",
        "C|ChangeRequestEvents|DecisionKey",
        "C|ChangeRequestEvents|DetailsJson",
        "CK|ChangeRequests|CK_B3_ChangeRequests_KnownCodes",
        "CK|ChangeRequests|CK_B3_ChangeRequests_KnownStatus",
        "CK|ChangeRequests|CK_B3_ChangeRequests_ExpectedLocationVersion",
        "CK|ChangeRequests|CK_B3_ChangeRequests_ProposedJson",
        "CK|ChangeRequests|CK_B3_ChangeRequests_ReviewState",
        "CK|ChangeRequestEvents|CK_B3_ChangeRequestEvents_EventType",
        "CK|ChangeRequestEvents|CK_B3_ChangeRequestEvents_DetailsJson",
        "CK|ChangeRequestEvents|CK_B3_ChangeRequestEvents_DecisionState",
        "FK|ChangeRequests|FK_B3_Requests_Org",
        "FK|ChangeRequests|FK_B3_Requests_Team",
        "FK|ChangeRequests|FK_B3_Requests_Requester",
        "FK|ChangeRequests|FK_B3_Requests_Reviewer",
        "FK|ChangeRequestEvents|FK_B3_Events_Request",
        "FK|ChangeRequestEvents|FK_B3_Events_Actor",
        "UX|ChangeRequests|UX_B3_ChangeRequests_RequestPublicId",
        "UX|ChangeRequests|UX_B3_ChangeRequests_Org_Entity_Pending",
        "UX|ChangeRequestEvents|UX_B3_ChangeRequestEvents_DecisionKey"
    };

    internal static async Task RequireExactNamedObjectsAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("012_PINNED_MANIFEST_DISPOSABLE_ONLY");
        var sql="""
            SELECT CONCAT(N'C|',OBJECT_NAME(object_id),N'|',name)
            FROM sys.columns WHERE object_id IN (
                 OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                 OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            UNION ALL
            SELECT CONCAT(N'CK|',OBJECT_NAME(parent_object_id),N'|',name)
            FROM sys.check_constraints WHERE parent_object_id IN (
                 OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                 OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            UNION ALL
            SELECT CONCAT(N'FK|',OBJECT_NAME(parent_object_id),N'|',name)
            FROM sys.foreign_keys WHERE parent_object_id IN (
                 OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                 OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            UNION ALL
            SELECT CONCAT(N'UX|',OBJECT_NAME(object_id),N'|',name)
            FROM sys.indexes WHERE object_id IN (
                 OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                 OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
                 AND name LIKE N'UX_B3_%'
            UNION ALL
            SELECT CONCAT(N'TR|',OBJECT_NAME(parent_id),N'|',name)
            FROM sys.triggers WHERE parent_id IN (
                 OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                 OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            """;
        await using var cmd=new SqlCommand(sql,conn,tx){CommandTimeout=25};
        var actual=new HashSet<string>(StringComparer.Ordinal);
        await using(var rd=await cmd.ExecuteReaderAsync())
        {
            while(await rd.ReadAsync())
            {
                var key=rd.GetString(0);
                if(!actual.Add(key))
                    throw new InvalidOperationException("012_PINNED_MANIFEST_DUPLICATE");
            }
        }
        if(Expected.Count!=45 || !actual.SetEquals(Expected))
            throw new InvalidOperationException("012_PINNED_MANIFEST_DRIFT");
    }
}
