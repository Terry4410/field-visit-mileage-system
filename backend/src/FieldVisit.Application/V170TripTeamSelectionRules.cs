namespace FieldVisit.Application;

public static class V170TripTeamSelectionRules
{
    public static int Resolve(
        CurrentUserDto user,
        int? requestedTeamId)
    {
        var allowed =
            user.TeamIds
                .Distinct()
                .ToList();

        if (allowed.Count == 0)
            throw new InvalidOperationException(
                "外訪員目前沒有有效的小組歸屬，無法建立行程。");

        var selected =
            requestedTeamId
            ?? user.TeamId
            ?? (allowed.Count == 1
                ? allowed[0]
                : (int?)null);

        if (!selected.HasValue)
            throw new InvalidOperationException(
                "請選擇本次行程的歸屬小組。");

        if (!allowed.Contains(selected.Value))
            throw new UnauthorizedAccessException(
                "無權將行程歸屬到未授權的小組。");

        return selected.Value;
    }

    public static void EnsureStillAllowed(
        CurrentUserDto user,
        int? tripTeamId)
    {
        if (!tripTeamId.HasValue
            || !user.TeamIds.Contains(tripTeamId.Value))
        {
            throw new UnauthorizedAccessException(
                "此行程的歸屬小組已不在目前授權範圍內，請先調整行程歸屬小組後再送出。");
        }
    }
}
