using System.Text;
using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// WORK-only, source-pinned logical CHECK definitions for the *disposable*
/// ChangeRequests/Events 011-to-012 candidate, independent of sys catalog
/// self-captured fingerprints. The eight expressions are fixed, reviewed
/// candidate expectations; this is not IT-signed production migration DDL.
/// </summary>
internal static class B312PinnedCandidateCheckDefaultsManifest
{
    // SQL Server 2022 rewrites IN lists as equality/OR predicates and wraps
    // numeric constants with parentheses in sys.check_constraints.definition.
    // These are source-authored, FIXED compiler-normalized expectations.
    // Operators and grouping stay pinned; no self-captured runtime baseline.
    private static readonly Dictionary<string,string> ExpectedChecks=
        new(StringComparer.Ordinal)
        {
            ["ChangeRequests|CK_B3_ChangeRequests_KnownCodes"] =
                "[EntityKind]=N'Location' AND [OperationCode]=N'UpdatePublishedLocation' AND [RiskCode]=N'High'",
            ["ChangeRequests|CK_B3_ChangeRequests_KnownStatus"] =
                "[Status]=N'Applied' OR [Status]=N'Rejected' OR [Status]=N'Pending'",
            ["ChangeRequests|CK_B3_ChangeRequests_ExpectedLocationVersion"] =
                "[ExpectedEntityRowVersion] IS NOT NULL AND DATALENGTH([ExpectedEntityRowVersion])=(8)",
            ["ChangeRequests|CK_B3_ChangeRequests_ProposedJson"] =
                "ISJSON([ProposedJson])=(1)",
            ["ChangeRequests|CK_B3_ChangeRequests_ReviewState"] = """
                [Status]=N'Pending' AND [ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL
                OR [Status]=N'Rejected' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]>(0)
                    AND [ReviewedByUserId]<>[RequestedByUserId] AND [ReviewedAt] IS NOT NULL
                    AND [ReviewedAt]>=[SubmittedAt] AND [ReviewReason] IS NOT NULL
                    AND LEN(LTRIM(RTRIM([ReviewReason])))>(0) AND [AppliedAt] IS NULL
                OR [Status]=N'Applied' AND [ReviewedByUserId] IS NOT NULL
                    AND [ReviewedByUserId]>(0) AND [ReviewedByUserId]<>[RequestedByUserId]
                    AND [ReviewedAt] IS NOT NULL AND [ReviewedAt]>=[SubmittedAt]
                    AND [ReviewReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ReviewReason])))>(0)
                    AND [AppliedAt] IS NOT NULL AND [AppliedAt]>=[ReviewedAt]
                """,
            ["ChangeRequestEvents|CK_B3_ChangeRequestEvents_EventType"] =
                "[EventType]=N'Applied' OR [EventType]=N'Approved' OR [EventType]=N'Rejected' OR [EventType]=N'Submitted'",
            ["ChangeRequestEvents|CK_B3_ChangeRequestEvents_DetailsJson"] =
                "[DetailsJson] IS NULL OR ISJSON([DetailsJson])=(1)",
            ["ChangeRequestEvents|CK_B3_ChangeRequestEvents_DecisionState"] = """
                [ActorUserId] IS NOT NULL AND [ActorUserId]>(0)
                AND [CorrelationId]<>'00000000-0000-0000-0000-000000000000'
                AND (([EventType]=N'Applied' OR [EventType]=N'Submitted') AND [DecisionKey] IS NULL
                    OR ([EventType]=N'Approved' OR [EventType]=N'Rejected')
                    AND [DecisionKey] IS NOT NULL
                    AND [DecisionKey]<>'00000000-0000-0000-0000-000000000000')
                """
        };

    private static void RequireDisposable(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
           || tx.Connection!=conn)
            throw new InvalidOperationException("012_CHECK_DEFAULT_DISPOSABLE_ONLY");
    }

    // Keep parenthesis structure and all SQL operators. Only remove cosmetic
    // identifier brackets and whitespace OUTSIDE of string literals.
    // No regex that erases parentheses/OR, no runtime-generated expected values.
    private static string Normalize(string sql)
    {
        var normalized=new StringBuilder(sql.Length);
        bool inLiteral=false;
        for(int i=0;i<sql.Length;i++)
        {
            var c=sql[i];
            if(c=='\'')
            {
                normalized.Append(c);
                if(inLiteral && i+1<sql.Length && sql[i+1]=='\'')
                {
                    normalized.Append(sql[++i]);
                    continue;
                }
                inLiteral=!inLiteral;
            }
            else if(inLiteral)
                normalized.Append(c);
            else if(!char.IsWhiteSpace(c)&&c!='['&&c!=']')
                normalized.Append(char.ToUpperInvariant(c));
        }
        if(inLiteral)throw new InvalidOperationException("012_CHECK_DEFINITION_MALFORMED");
        var text=normalized.ToString();
        // Ignore redundant *outer* parentheses only when they balance around
        // the WHOLE expression, not boolean grouping inside the CHECK.
        static bool IsWholeParenthesized(string s)
        {
            if(s.Length<2||s[0]!='('||s[^1]!=')')return false;
            int depth=0;
            bool quoted=false;
            for(int i=0;i<s.Length;i++)
            {
                if(s[i]=='\'')
                {
                    if(quoted&&i+1<s.Length&&s[i+1]=='\''){i++;continue;}
                    quoted=!quoted;
                }
                if(quoted)continue;
                if(s[i]=='(')depth++;
                if(s[i]==')')depth--;
                if(depth==0&&i<s.Length-1)return false;
                if(depth<0)return false;
            }
            return depth==0&&!quoted;
        }
        while(IsWholeParenthesized(text))text=text[1..^1];
        return text;
    }

    internal static async Task RequireCheckDefinitionsAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        RequireDisposable(conn,tx);
        await using var cmd=new SqlCommand("""
            SELECT OBJECT_NAME(parent_object_id),name,definition,
                   is_disabled,is_not_trusted
            FROM sys.check_constraints
            WHERE parent_object_id IN (
                OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
            """,conn,tx){CommandTimeout=25};
        var actual=new Dictionary<string,string>(StringComparer.Ordinal);
        await using(var reader=await cmd.ExecuteReaderAsync())
        {
            while(await reader.ReadAsync())
            {
                if(reader.GetBoolean(3)||reader.GetBoolean(4)
                    ||reader.IsDBNull(2))
                    throw new InvalidOperationException("012_CHECK_UNTRUSTED_OR_DISABLED");
                string key=reader.GetString(0)+"|"+reader.GetString(1);
                if(!actual.TryAdd(key,Normalize(reader.GetString(2))))
                    throw new InvalidOperationException("012_CHECK_DUPLICATE");
            }
        }
        if(ExpectedChecks.Count!=8||actual.Count!=8)
            throw new InvalidOperationException("012_CHECK_COUNT_DRIFT");
        var discrepancies=new List<string>();
        foreach(var (key,expected) in ExpectedChecks)
        {
            var pinned=Normalize(expected);
            actual.TryGetValue(key,out var got);
            if(!string.Equals(got,pinned,StringComparison.Ordinal))
                discrepancies.Add(key+" EXPECTED=["+pinned+"] ACTUAL=["+got+"]");
        }
        if(discrepancies.Count>0)
            throw new InvalidOperationException("012_CHECK_DEFINITION_DRIFT "
                +string.Join(" \\n ",discrepancies));
    }

    /// <summary>
    /// Current reduced candidate creates NO DEFAULT on the 28 B3 columns.
    /// This is an explicit pinned 0, NOT an endorsement of production DDL.
    /// </summary>
    internal static async Task RequireNoUnreviewedDefaultsAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        RequireDisposable(conn,tx);
        var scalar=await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx, """
            SELECT
                (SELECT COUNT(*) FROM sys.default_constraints d
                  WHERE d.parent_object_id IN (
                     OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                     OBJECT_ID(N'dbo.ChangeRequestEvents',N'U')))
              + (SELECT COUNT(*) FROM sys.columns c
                  WHERE c.object_id IN (
                     OBJECT_ID(N'dbo.ChangeRequests',N'U'),
                     OBJECT_ID(N'dbo.ChangeRequestEvents',N'U'))
                     AND c.default_object_id<>0)
            """);
        if(Convert.ToInt32(scalar)!=0)
            throw new InvalidOperationException("012_UNREVIEWED_DEFAULT_DENIED");
    }

    internal static async Task RequireAllAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        await B312PinnedCandidatePhysicalManifest.RequireAllAsync(conn,tx);
        await RequireCheckDefinitionsAsync(conn,tx);
        await RequireNoUnreviewedDefaultsAsync(conn,tx);
    }
}
