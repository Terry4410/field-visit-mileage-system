using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public static class V170TemporaryLocationCleanupRules
{
    public static IReadOnlyList<int> GetPendingTemporaryLocationIds(
        VisitTrip trip)
    {
        return trip.Stops
            .Select(x => x.Location)
            .Where(x =>
                x is not null
                && x.LocationId > 0
                && x.IsTemporary
                && x.ApprovalStatus == "Pending")
            .Select(x => x!.LocationId)
            .Distinct()
            .ToList();
    }
}
