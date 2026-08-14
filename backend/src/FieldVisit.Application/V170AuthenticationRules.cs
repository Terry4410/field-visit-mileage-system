namespace FieldVisit.Application;

public static class AuthenticationModes
{
    public const string Demo = "Demo";
    public const string Entra = "Entra";
}

public sealed record EntraLoginIdentity(
    Guid TenantId,
    Guid ObjectId,
    string? Email);

public static class V170AuthenticationRules
{
    public static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return AuthenticationModes.Demo;

        if (string.Equals(
            mode.Trim(),
            AuthenticationModes.Demo,
            StringComparison.OrdinalIgnoreCase))
            return AuthenticationModes.Demo;

        if (string.Equals(
            mode.Trim(),
            AuthenticationModes.Entra,
            StringComparison.OrdinalIgnoreCase))
            return AuthenticationModes.Entra;

        throw new InvalidOperationException(
            $"不支援的 Auth Mode：{mode}。");
    }

    public static void EnsureMode(
        string? actualMode,
        string requiredMode)
    {
        var actual = NormalizeMode(actualMode);

        if (!string.Equals(
            actual,
            requiredMode,
            StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"目前登入模式為 {actual}，不可使用 {requiredMode} 登入。");
        }
    }

    public static bool HasRequiredScope(
        string? scopeClaim,
        string? requiredScope)
    {
        if (string.IsNullOrWhiteSpace(requiredScope))
            return true;

        if (string.IsNullOrWhiteSpace(scopeClaim))
            return false;

        return scopeClaim
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Contains(
                requiredScope.Trim(),
                StringComparer.Ordinal);
    }

    public static Guid ParseRequiredGuidClaim(
        string? value,
        string claimName)
    {
        if (!Guid.TryParse(value, out var result))
            throw new UnauthorizedAccessException(
                $"Microsoft Entra token 缺少有效的 {claimName}。");

        return result;
    }

    public static string? NormalizeEmail(
        string? email)
    {
        var normalized =
            email?.Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }
}
