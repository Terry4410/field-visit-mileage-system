namespace FieldVisit.Application;

public sealed class V170LocationService(
    ICurrentUserService current,
    IV170LocationRepository locations)
{
    public async Task<V170LocationSearchResult> SearchAsync(
        V170LocationSearchRequest request,
        CancellationToken ct)
    {
        var user = current.GetRequired();

        V170LocationSearchRules.EnsurePickerRole(user.Roles);

        if (!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException(
                "目前帳號缺少 Organization scope，無法搜尋地點。");

        var spec =
            V170LocationSearchRules.Normalize(request);

        return await locations.SearchAsync(
            user,
            spec,
            ct);
    }
}
