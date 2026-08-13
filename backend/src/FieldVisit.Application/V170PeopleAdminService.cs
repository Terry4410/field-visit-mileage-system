namespace FieldVisit.Application;

public sealed class V170PeopleAdminService(
    ICurrentUserService current,
    IV170PeopleAdminRepository repository,
    IV170PeopleAdminWriter writer)
{
    public Task<PagedResult<V170PeopleRowDto>> QueryAsync(
        V170PeopleQueryRequest request,
        CancellationToken ct)
    {
        var admin = RequireAdmin();

        return repository.QueryAsync(
            admin,
            V170PeopleQueryRules.Normalize(request),
            ct);
    }

    public Task<V170PersonDetailDto> GetAsync(
        int userId,
        CancellationToken ct)
    {
        if (userId <= 0)
            throw new InvalidOperationException(
                "UserId 不正確。");

        return repository.GetAsync(
            RequireAdmin(),
            userId,
            ct);
    }

    public async Task<V170PersonDetailDto>
        CreateExternalSupervisorAsync(
            SaveExternalSupervisorRequest request,
            CancellationToken ct)
    {
        var admin =
            RequireAdmin();

        request =
            V170ExternalSupervisorRules.Normalize(
                request);

        var userId =
            await writer.CreateExternalSupervisorAsync(
                admin,
                request,
                ct);

        return await repository.GetAsync(
            admin,
            userId,
            ct);
    }

    public async Task<V170PersonDetailDto>
        UpdateExternalSupervisorAsync(
            int userId,
            UpdateExternalSupervisorRequest request,
            CancellationToken ct)
    {
        if (userId <= 0)
            throw new InvalidOperationException(
                "UserId 不正確。");

        var admin =
            RequireAdmin();

        request =
            V170ExternalSupervisorUpdateRules.Normalize(
                request,
                BusinessTime.Today);

        await writer.UpdateExternalSupervisorAsync(
            admin,
            userId,
            request,
            ct);

        return await repository.GetAsync(
            admin,
            userId,
            ct);
    }

    public async Task<V170PersonDetailDto>
        UpdateInternalUserAccessAsync(
            int userId,
            UpdateInternalUserAccessRequest request,
            CancellationToken ct)
    {
        if (userId <= 0)
            throw new InvalidOperationException(
                "UserId 不正確。");

        var admin =
            RequireAdmin();

        request =
            V170InternalUserAccessRules.Normalize(
                request,
                BusinessTime.Today);

        await writer.UpdateInternalUserAccessAsync(
            admin,
            userId,
            request,
            ct);

        return await repository.GetAsync(
            admin,
            userId,
            ct);
    }

    private CurrentUserDto RequireAdmin()
    {
        var user = current.GetRequired();

        if (!user.Roles.Any(
                x => x.Equals(
                    "admin",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException(
                "只有管理者可以維護人員與權限。");
        }

        if (!user.OrganizationId.HasValue)
        {
            throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");
        }

        return user;
    }
}
