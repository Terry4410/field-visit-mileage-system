namespace FieldVisit.Application;

public static class V160CorrectionFinancialRules
{
    public static bool ShouldPreserveSnapshotRate(
        DateOnly baseVisitDate,
        DateOnly proposedVisitDate)
        => baseVisitDate == proposedVisitDate;

    public static decimal RequireSnapshotRate(decimal? snapshotRate)
        => snapshotRate
           ?? throw new InvalidOperationException(
               "原核准 Snapshot 缺少補助費率，無法進行財務更正。");

    public static decimal CalculateSubsidy(
        decimal approvedDistanceKm,
        decimal ratePerKm)
        => decimal.Round(approvedDistanceKm * ratePerKm, 2);
}
