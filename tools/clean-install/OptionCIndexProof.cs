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
            || target.InitialCatalog != "FieldVisitV180CleanInstall"
            || target.UserID != "sa" || target.IntegratedSecurity)
            throw new InvalidOperationException("OPTIONC_INDEX_NONDISPOSABLE_SQL_TARGET");

        if (Expected.Length != 15 ||
            Expected.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 15 ||
            Expected.Count(x => x.Source is "011" or "016") != 9 ||
            Expected.Count(x => x.Source is "014" or "017") != 6)
            throw new InvalidOperationException("OPTIONC_INDEX_PROOF_SOURCE_SET");

        var shaBySource = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourceId in new[] { "011", "014", "016", "017" })
        {
            var path = Path.Combine(rcRoot, "docs", "release",
                "optionc-canonical-sql-draft", "candidate",
                "1800_" + sourceId, "Up.sql");
            var bytes = await File.ReadAllBytesAsync(path);
            var sql = System.Text.Encoding.UTF8.GetString(bytes);
            shaBySource.Add(sourceId, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            foreach (var expected in Expected.Where(x => x.Source == sourceId))
            {
                // Pin the actual complete source declaration rather than
                // assume an index name alone implies its intended keys.
                var pattern = @"\bCREATE\s+(?<unique>UNIQUE\s+)?INDEX\s+"
                    + Regex.Escape(expected.Name)
                    + @"\s+ON\s+dbo\." + Regex.Escape(expected.Table)
                    + @"\s*\((?<keys>[^)]*)\)";
                var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);
                if (matches.Count != 1 ||
                    matches[0].Groups["unique"].Success != expected.Unique ||
                    Normalize(matches[0].Groups["keys"].Value) != Normalize(expected.Keys))
                    throw new InvalidOperationException("OPTIONC_INDEX_PINNED_SOURCE_MISMATCH");
                // .NET 8/C# 12: avoid Span locals in an async method.
                var after = sql[(matches[0].Index + matches[0].Length)..];
                var end = after.IndexOf(';');
                if (end < 0) throw new InvalidOperationException("OPTIONC_INDEX_SOURCE_UNTERMINATED");
                var clause = after[..end];
                if (expected.FilterToken is null)
                {
                    if (Regex.IsMatch(clause, @"\bWHERE\b", RegexOptions.IgnoreCase))
                        throw new InvalidOperationException("OPTIONC_INDEX_UNEXPECTED_SOURCE_FILTER");
                }
                else if (!Regex.IsMatch(clause, @"\bWHERE\b", RegexOptions.IgnoreCase)
                    || !Normalize(clause).Contains(expected.FilterToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("OPTIONC_INDEX_SOURCE_FILTER_MISMATCH");
            }
        }

        await using var db = new SqlConnection(target.ConnectionString);
        await db.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT i.name, t.name, i.is_unique, i.has_filter, i.filter_definition,
                i.is_disabled, i.is_hypothetical,
                (SELECT STRING_AGG(CONVERT(nvarchar(max), col.name),N',')
                        WITHIN GROUP (ORDER BY ic.key_ordinal)
                 FROM sys.index_columns ic
                 JOIN sys.columns col
                   ON col.object_id=ic.object_id AND col.column_id=ic.column_id
                 WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                   AND ic.key_ordinal>0) AS key_columns
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id=i.object_id
            WHERE t.schema_id=SCHEMA_ID(N'dbo')
                AND i.is_primary_key=0 AND i.is_unique_constraint=0
                AND i.index_id>0;
            """, db) { CommandTimeout = 15 };
        var expectedByName = Expected.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!expectedByName.TryGetValue(name, out var expected)) continue;
            var filter = reader.IsDBNull(4) ? null : reader.GetString(4);
            if (!found.Add(name) ||
                reader.GetString(1) != expected.Table ||
                reader.GetBoolean(2) != expected.Unique ||
                reader.GetBoolean(3) != (expected.FilterToken is not null) ||
                reader.GetBoolean(5) || reader.GetBoolean(6) ||
                reader.IsDBNull(7) || Normalize(reader.GetString(7)) != Normalize(expected.Keys))
                throw new InvalidOperationException("OPTIONC_INDEX_SQL_CATALOG_MISMATCH");
            if (expected.FilterToken is not null &&
                !Normalize(filter).Contains(expected.FilterToken, StringComparison.Ordinal))
                throw new InvalidOperationException("OPTIONC_INDEX_SQL_FILTER_MISMATCH");
        }
        if (found.Count != 15)
            throw new InvalidOperationException("OPTIONC_INDEX_SQL_CATALOG_INCOMPLETE");
        return new Evidence(15, 9, 6, shaBySource);
    }
}
