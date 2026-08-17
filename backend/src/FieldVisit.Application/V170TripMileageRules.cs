namespace FieldVisit.Application;

public static class V170TripMileageRules
{
    public const string MinimumStopsMessage =
        "正式送出至少需要 2 個公務地點，才能計算外訪里程與申請補助。";

    public const string ClaimedMileageMessage =
        "正式送出前必須填寫大於 0 的外訪員自行計算里程。";

    public const string ApprovalMinimumStopsMessage =
        "此行程不足 2 個公務地點，無法計算里程與申請補助，因此不得核准。";

    public static void EnsureReadyForSubmission(int stopCount, decimal? claimedDistanceKm)
    {
        if (stopCount < 2)
            throw new InvalidOperationException(MinimumStopsMessage);

        if (claimedDistanceKm is null or <= 0)
            throw new InvalidOperationException(ClaimedMileageMessage);
    }

    public static void EnsureReadyForApproval(int stopCount)
    {
        if (stopCount < 2)
            throw new InvalidOperationException(ApprovalMinimumStopsMessage);
    }
}
