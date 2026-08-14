namespace FieldVisit.Application;

public sealed class AuthService(
    IUserRepository users,
    ITokenService tokens,
    IV170AccessControl access)
{
    public async Task<DemoLoginResponse> LoginAsync(
        DemoLoginRequest request,
        string demoPassword,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(demoPassword))
            throw new InvalidOperationException(
                "UAT Demo Password 尚未設定。");

        if (!string.Equals(
            request.Password,
            demoPassword,
            StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "帳號或密碼錯誤。");
        }

        var user =
            await users.FindByAccountAsync(
                request.Account.Trim(),
                ct)
            ?? throw new UnauthorizedAccessException(
                "帳號或密碼錯誤。");

        return await IssueApplicationTokenAsync(
            user,
            ct);
    }

    public async Task<DemoLoginResponse> LoginEntraAsync(
        EntraLoginIdentity identity,
        bool allowFirstLoginEmailBinding,
        CancellationToken ct)
    {
        var user =
            await users.FindByEntraIdentityAsync(
                identity.TenantId,
                identity.ObjectId,
                ct);

        if (user is null
            && allowFirstLoginEmailBinding
            && !string.IsNullOrWhiteSpace(identity.Email))
        {
            user =
                await users.BindEntraIdentityByEmailAsync(
                    identity.TenantId,
                    identity.ObjectId,
                    identity.Email,
                    ct);
        }

        if (user is null)
        {
            throw new UnauthorizedAccessException(
                "Microsoft Entra 帳號尚未綁定此系統使用者。");
        }

        return await IssueApplicationTokenAsync(
            user,
            ct);
    }

    private async Task<DemoLoginResponse>
        IssueApplicationTokenAsync(
            FieldVisit.Domain.Entities.User user,
            CancellationToken ct)
    {
        var eligibility =
            await access.EvaluateLoginAsync(
                user.UserId,
                user.IsActive,
                ct);

        if (!eligibility.IsAllowed)
        {
            throw new UnauthorizedAccessException(
                eligibility.Reason
                ?? "目前帳號無法登入系統。");
        }

        var profile =
            await users.GetProfileAsync(
                user.UserId,
                ct)
            ?? throw new UnauthorizedAccessException(
                "帳號角色設定不完整。");

        var (token, expires) =
            tokens.Create(profile);

        return new DemoLoginResponse(
            token,
            expires,
            profile);
    }
}
