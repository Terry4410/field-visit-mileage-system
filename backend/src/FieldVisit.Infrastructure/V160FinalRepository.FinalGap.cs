using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Infrastructure;

public sealed partial class V160FinalRepository
{
    // More-specific overload selected by ReviewCorrectionAsync / MapCorrectionAsync,
    // both of which pass List<CorrectionRequestChange>. Route-affecting Stop changes
    // must reach Admin close even when ApprovedDistanceKm happens to stay unchanged.
    private static bool RequiresAdminClose(List<CorrectionRequestChange> changes) =>
        changes.Any(x => x.FieldName is "ApprovedDistanceKm" or "RatePerKm" or "SubsidyAmount")
        || StopsAffectRoute(changes);

    private static bool StopsAffectRoute(IEnumerable<CorrectionRequestChange> changes)
    {
        var change = changes.FirstOrDefault(x => x.FieldName == "Stops");
        if (change is null) return false;

        try
        {
            var before = JsonSerializer.Deserialize<List<CorrectionStopProposal>>(
                change.OldValue ?? "[]", JsonOptions) ?? [];
            var after = JsonSerializer.Deserialize<List<CorrectionStopProposal>>(
                change.NewValue ?? "[]", JsonOptions) ?? [];

            static string Normalize(string? value) =>
                string.Join(' ', (value ?? "").Trim()
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            var beforeRoute = before
                .OrderBy(x => x.StopSequence)
                .Select(x => (x.StopSequence, Address: Normalize(x.Address)))
                .ToArray();
            var afterRoute = after
                .OrderBy(x => x.StopSequence)
                .Select(x => (x.StopSequence, Address: Normalize(x.Address)))
                .ToArray();

            return !beforeRoute.SequenceEqual(afterRoute);
        }
        catch (JsonException)
        {
            // Fail closed: malformed historical change evidence must not bypass Admin review.
            return true;
        }
    }
}
