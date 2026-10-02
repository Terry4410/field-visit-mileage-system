namespace FieldVisit.Application;

public sealed record V170LocationDuplicateComparable(
    int LocationId,
    string LocationName,
    string? Address,
    string? PlusCode,
    string? TaxId);

public static class V170LocationDuplicateRules
{
    public const string SuspectedReason = "疑似重複，待管理者人工覆核";

    public static IReadOnlyList<string> MatchReasons(
        V170LocationDuplicateComparable source,
        V170LocationDuplicateComparable candidate)
    {
        if (source.LocationId == candidate.LocationId)
            return [];

        var reasons = new List<string>();
        if (Exact(source.TaxId, candidate.TaxId)) reasons.Add("統一編號");
        if (Similar(source.LocationName, candidate.LocationName, 4)) reasons.Add(
            Exact(source.LocationName, candidate.LocationName) ? "名稱" : "名稱相似");
        if (Similar(source.Address, candidate.Address, 6)) reasons.Add(
            Exact(source.Address, candidate.Address) ? "地址" : "地址相似");
        if (Exact(source.PlusCode, candidate.PlusCode)) reasons.Add("Plus Code");
        return reasons;
    }

    public static string Signature(V170LocationDuplicateComparable value) =>
        string.Join("|",
            Normalize(value.TaxId),
            Normalize(value.LocationName),
            Normalize(value.Address),
            Normalize(value.PlusCode));

    private static bool Exact(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return a.Length > 0 && a == b;
    }

    private static bool Similar(string? left, string? right, int minimumLength)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length < minimumLength || b.Length < minimumLength) return false;
        if (a == b) return true;

        var max = Math.Max(a.Length, b.Length);
        if (Math.Abs(a.Length - b.Length) > 2) return false;
        var allowance = max <= 8 ? 1 : 2;
        return Distance(a, b, allowance) <= allowance;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var chars = value.Trim().ToUpperInvariant().Replace('臺', '台')
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private static int Distance(string a, string b, int stopAfter)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        var current = new int[b.Length + 1];

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > stopAfter) return rowMin;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
