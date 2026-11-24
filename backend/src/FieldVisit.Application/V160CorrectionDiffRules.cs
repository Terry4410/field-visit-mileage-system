using System.Globalization;

namespace FieldVisit.Application;

public static class V160CorrectionDiffRules
{
    public static bool AreEquivalent<T>(
        T oldValue,
        T newValue)
        => EqualityComparer<T>.Default.Equals(
            oldValue,
            newValue);

    // A partially persisted correction request must never be approved into a new Snapshot.
    public static void RequirePersistedChangesForApproval(int changeCount)
    {
        if (changeCount <= 0)
            throw new InvalidOperationException(
                "更正差異明細不存在，禁止核准；請聯絡管理者確認資料完整性。");
    }

    public static string? ToDisplayValue<T>(
        T value)
    {
        if (value is null)
            return null;

        // Decimal 的 scale 不屬於業務差異：
        // 10.00、10.0、10 都應呈現為 10。
        if (value is decimal number)
        {
            return number.ToString(
                "0.############################",
                CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }
}
