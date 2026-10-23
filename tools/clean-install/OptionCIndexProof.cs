using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

/// <summary>
/// SQL2022 catalog proof for all 15 *effective* 011/014/016/017 Option C
/// indexes. 011/016 are EF-origin and MUST NOT be installed twice; 014/017
/// come from source-pinned SQL-only layers. This is an audit, not new DDL.
/// Reuses the same disposable fresh database and the exact accepted RC root.
/// </summary>
internal static class CleanInstallOptionCIndexProof
{
    internal sealed record Evidence(
        int VerifiedIndexCount,
        int MappedIndexCount,
        int SqlOnlyIndexCount,
        IReadOnlyDictionary<string, string> SourceSha256);

    private sealed record Index(
        string Source, string Name, string Table, string Keys,
        bool Unique, string? FilterToken);

    private static readonly Index[] Expected =
    [
        new("011", "UX_B3_ChangeRequests_RequestPublicId", "ChangeRequests",
            "RequestPublicId", true, null),
        new("011", "UX_B3_ChangeRequests_Org_Entity_Pending", "ChangeRequests",
            "OrganizationId,EntityKind,EntityId", true, "status='pending'"),
        new("011", "IX_B3_ChangeRequests_Org_Status_SubmittedAt", "ChangeRequests",
            "OrganizationId,Status,SubmittedAt", false, null),
        new("011", "IX_B3_ChangeRequests_Requester_SubmittedAt", "ChangeRequests",
            "RequestedByUserId,SubmittedAt", false, null),
        new("011", "UX_B3_ChangeRequestEvents_DecisionKey", "ChangeRequestEvents",
            "DecisionKey", true, "decisionkeyisnotnull"),
        new("011", "IX_B3_ChangeRequestEvents_Request_OccurredAt", "ChangeRequestEvents",
            "ChangeRequestId,OccurredAt,ChangeRequestEventId", false, null),
        new("011", "IX_B3_ChangeRequestEvents_CorrelationId", "ChangeRequestEvents",
            "CorrelationId", false, null),
        new("014", "UX_OptionC_Stages_Request", "LocationPublicationStages",
            "ChangeRequestId", true, null),
        new("014", "UX_OptionC_Stages_OpenLocation", "LocationPublicationStages",
            "OrganizationId,LocationId", true,
            "stagestatusin'pendinggeocode','pendingduplicatereview','readyforpublish'"),
        new("014", "UX_OptionC_Published_Current", "LocationPublishedVersions",
            "LocationId", true, "iscurrent=1"),
        new("014", "UX_OptionC_Published_Request", "LocationPublishedVersions",
            "ChangeRequestId", true, "changerequestidisnotnull"),
        new("014", "UX_OptionC_Published_Number", "LocationPublishedVersions",
            "LocationId,VersionNumber", true, null),
        new("016", "UX_OptionC_LeaderReview_Request_Stage", "ChangeRequestLeaderReviews",
            "ChangeRequestId,ReviewStage", true, null),
        new("016", "UX_OptionC_LeaderReview_DecisionKey", "ChangeRequestLeaderReviews",
            "DecisionKey", true, "decisionkeyisnotnull"),
        new("017", "UX_OptionC_CreateReservation_Location", "LocationCreateReservations",
            "LocationId", true, null)
    ];

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? "").ToLowerInvariant(), @"[\[\]\s()]", "")
            .Replace("n'", "'");

    internal static async Task<Evidence> VerifyAsync(string rcRoot, string connection)
    {
        var target = new SqlConnectionStringBuilder(connection);
        if (target.DataSource != "localhost,1433"
            || target.InitialCatalog != "OptionCIsolated_FieldVisitV180Clean