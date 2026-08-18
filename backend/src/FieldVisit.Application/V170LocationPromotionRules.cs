using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public static class V170LocationPromotionRules
{
    public static bool CanPromote(Location location)
    {
        return location.IsTemporary
            && location.IsActive
            && string.Equals(location.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
            && string.Equals(location.GeocodingStatus, "Completed", StringComparison.OrdinalIgnoreCase)
            && location.Latitude.HasValue
            && location.Longitude.HasValue;
    }

    public static void EnsureCanPromote(Location location)
    {
        if (!location.IsTemporary)
            throw new InvalidOperationException("此地點已是正式地點，不需要再次轉換。");

        if (!location.IsActive)
            throw new InvalidOperationException("停用中的臨時地點不可轉為正式地點。");

        if (!string.Equals(location.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("臨時地點必須先完成核准，才能轉為正式地點。");

        if (!string.Equals(location.GeocodingStatus, "Completed", StringComparison.OrdinalIgnoreCase)
            || !location.Latitude.HasValue
            || !location.Longitude.HasValue)
            throw new InvalidOperationException("臨時地點必須先完成地理解析，才能轉為正式地點。");
    }
}
