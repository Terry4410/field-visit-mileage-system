namespace FieldVisit.Application;

public static class BusinessTime
{
    private static readonly Lazy<TimeZoneInfo> Taipei = new(() =>
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    });

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Taipei.Value);
    public static DateOnly Today => DateOnly.FromDateTime(Now);
    public static DateTime ToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), Taipei.Value);
}
