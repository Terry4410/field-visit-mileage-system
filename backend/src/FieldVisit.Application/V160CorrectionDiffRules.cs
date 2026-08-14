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
