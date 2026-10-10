namespace FieldVisit.Application;

/// <summary>
/// B1 location mutations are more restrictive than read/picker visibility.
/// A visitor owns only records they created; a leader may own records of a
/// currently authorized managed team. Pending drafts cannot be published here.
/// </summary>
public static class V180LocationOwnershipRules
{
    private static bool HasRole(CurrentUserDto actor,string role) =>
        actor.Roles.Contains(role,StringComparer.OrdinalIgnoreCase);

    public static void EnsureDraftCreate(
        CurrentUserDto actor,int? teamId,IReadOnlyCollection<int> effectiveTeams,
        bool requestedActive,string locationType)
    {
        if(HasRole(actor,"admin"))return;
        if(!HasRole(actor,"visitor")&&!HasRole(actor,"leader"))
            throw new UnauthorizedAccessException("目前角色不得建立地點。");
        if(!actor.OrganizationId.HasValue || !teamId.HasValue
           || !effectiveTeams.Contains(teamId.Value))
            throw new UnauthorizedAccessException("只能在目前有效授權的小組新增地點。");
        if(requestedActive || !string.Equals(locationType,"Customer",StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("一般使用者僅可新增待審核客戶地點，不可直接啟用或建立官方據點。");
    }

    public static void EnsureDraftUpdate(
        CurrentUserDto actor,int? organizationId,int? teamId,int? createdBy,
        string status,bool isActive,IReadOnlyCollection<int> effectiveTeams,
        int? requestedTeamId,bool requestedActive,string requestedType,string currentType)
    {
        if(HasRole(actor,"admin"))return;
        if(!HasRole(actor,"visitor")&&!HasRole(actor,"leader"))
            throw new UnauthorizedAccessException("目前角色不得維護地點。");
        if(!actor.OrganizationId.HasValue || organizationId!=actor.OrganizationId
           || !teamId.HasValue || !effectiveTeams.Contains(teamId.Value))
            throw new UnauthorizedAccessException("不得維護其他小組、共用或跨組織地點。");
        if(HasRole(actor,"visitor")&&createdBy!=actor.UserId)
            throw new UnauthorizedAccessException("外訪員只能維護本人建立的地點。");
        if(!string.Equals(status,"Pending",StringComparison.OrdinalIgnoreCase)||isActive)
            throw new UnauthorizedAccessException("已發布地點的主檔變更需要管理者核准，目前只能編修待審核草稿。");
        if(requestedTeamId!=teamId || requestedActive
           || !string.Equals(requestedType,currentType,StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("一般使用者不得轉移小組、變更地點類型或啟用地點。");
    }

    public static void EnsurePublishedMasterWrite(CurrentUserDto actor)
    {
        if(!HasRole(actor,"admin"))
            throw new UnauthorizedAccessException("正式地點主檔修改需管理者核准；可先使用小組備註或維護本人待審核草稿。");
    }
}
