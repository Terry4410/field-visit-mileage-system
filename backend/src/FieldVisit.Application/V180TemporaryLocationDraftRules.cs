using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public static class V180TemporaryLocationDraftRules
{
    public static IReadOnlySet<int> ExistingPendingTemporaryLocationIds(
        VisitTrip trip)
        => trip.Stops
            .Where(x =>
                x.LocationId.HasValue
                && x.Location?.IsTemporary == true
                && string.Equals(
                    x.Location.ApprovalStatus,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase))
            .Select(x => x.LocationId!.Value)
            .ToHashSet();

    public static bool CanReusePendingTemporaryLocation(
        Location location,
        CurrentUserDto user,
        IReadOnlySet<int> existingPendingTemporaryLocationIds)
    {
        if (!existingPendingTemporaryLocationIds.Contains(location.LocationId))
            return false;
        if (!location.IsTemporary
            || !string.Equals(
                location.ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (location.CreatedByUserId != user.UserId)
            return false;
        if (user.OrganizationId.HasValue
            && location.OrganizationId != user.OrganizationId.Value)
            return false;

        return true;
    }

    public static IReadOnlyCollection<int> RemovedPendingTemporaryLocationIds(
        IReadOnlySet<int> existingPendingTemporaryLocationIds,
        IReadOnlyList<TripStopInput> requestedStops)
    {
        var requestedLocationIds = requestedStops
            .Where(x => x.LocationId.HasValue)
            .Select(x => x.LocationId!.Value)
            .ToHashSet();

        return existingPendingTemporaryLocationIds
            .Where(x => !requestedLocationIds.Contains(x))
            .ToArray();
    }

    public static string ResolveStopSourceType(
        VisitTripStop stop)
        => stop.Location?.IsTemporary == true
            ? "Temporary"
            : stop.LocationId.HasValue
                ? "Master"
                : "Temporary";
}
