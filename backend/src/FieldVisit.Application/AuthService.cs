namespace FieldVisit.Application;

public sealed class AuthService(IUserRepository users, ITokenService tokens)
{
    public async Task<DemoLoginResponse> LoginAsync(DemoLoginRequest request, string demoPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(demoPassword))
            throw new InvalidOperationException("UAT Demo Password 尚未設定。");

        if (!string.Equals(request.Password, demoPassword, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("帳號或密碼錯誤。");

        var user = await users.FindByAccountAsync(request.Account.Trim(), ct)
            ?? throw new UnauthorizedAccessException("帳號或密碼錯誤。");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("此帳號未啟用。");

        var profile = await users.GetProfileAsync(user.UserId, ct)
            ?? throw new UnauthorizedAccessException("帳號角色設定不完整。");

        var (token, expires) = tokens.Create(profile);
        return new DemoLoginResponse(token, expires, profile);
    }
}
