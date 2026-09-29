using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed record V180DeploymentSiteBasis(string? SiteCode, string? Address);
public sealed record V180RouteStopBasis(int StopSequence, string? AddressSnapshot);
public sealed record V180RouteBasis(
    string? VehicleType,
    V180DeploymentSiteBasis? Start,
    IReadOnlyList<V180RouteStopBasis> Stops,
    V180DeploymentSiteBasis? End);
public sealed record V180AddressCanonicalBasis(string? InputKind, string? InputValue);

public sealed record V180GeocodingAttemptRequest(
    int LocationId, string Provider, byte[] AddressBasisHash, Guid CorrelationId,
    DateTime RequestedAt, int RequestedByUserId);

public sealed record V180RouteCalculationAttemptRequest(
    long VisitTripId, string BasisType, long? BasisVisitTripSnapshotId,
    string CalculationReason, string RequestedVehicleType, string TravelMode,
    string Provider, int StopCount, byte[] RequestBasisHash,
    Guid CorrelationId, DateTime RequestedAt, int RequestedByUserId);

public sealed record V180MileageGovernanceEventRequest(
    long VisitTripId, long? VisitTripSnapshotId, long? RouteCalculationAttemptId,
    string EventType, string? ReasonCode, string? Message, Guid CorrelationId,
    DateTime OccurredAt, int? ActorUserId);

public sealed record V180RouteProviderRequest(
    Guid CorrelationId,
    string TravelMode,
    string? StartAddress,
    IReadOnlyList<string?> StopAddresses,
    string? EndAddress);

public sealed record V180RouteProviderResult(
    bool Success,
    decimal? SuggestedDistanceKm,
    int? DurationSeconds,
    string? EncodedPolyline,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record V180GeocodingProviderRequest(
    Guid CorrelationId,
    string? InputKind,
    string? InputValue);

public sealed record V180GeocodingProviderResult(
    bool Success,
    decimal? Latitude,
    decimal? Longitude,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record V180RouteOrchestrationResult(
    long RouteCalculationAttemptId,
    Guid CorrelationId,
    string Status,
    decimal? SuggestedDistanceKm,
    int? DurationSeconds,
    string? EncodedPolyline,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record V180GeocodingOrchestrationResult(
    long GeocodingAttemptId,
    Guid CorrelationId,
    string Status,
    bool SelectedAsCurrent,
    decimal? Latitude,
    decimal? Longitude,
    string? ErrorCode,
    string? ErrorMessage);

public interface IV180RouteProvider
{
    string ProviderName { get; }
    Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct);
}

public interface IV180GeocodingProvider
{
    string ProviderName { get; }
    Task<V180GeocodingProviderResult> GeocodeAsync(V180GeocodingProviderRequest request, CancellationToken ct);
}

public interface IV180GoogleMileageGovernanceRepository
{
    Task<GeocodingAttempt> AddGeocodingAttemptAsync(V180GeocodingAttemptRequest request, CancellationToken ct);
    Task<RouteCalculationAttempt> AddRouteCalculationAttemptAsync(V180RouteCalculationAttemptRequest request, CancellationToken ct);
    Task<MileageGovernanceEvent> AddGovernanceEventAsync(V180MileageGovernanceEventRequest request, CancellationToken ct);
    Task<RouteCalculationAttempt?> GetRouteCalculationAttemptAsync(long attemptId, CancellationToken ct);
    Task<GeocodingAttempt?> GetGeocodingAttemptAsync(long attemptId, CancellationToken ct);
    Task<bool> TryFinalizeRouteCalculationAttemptAsync(
        long attemptId, string status, string? errorCode, string? errorMessage, DateTime completedAt, CancellationToken ct);
    Task<bool> TryFinalizeGeocodingAttemptAsync(
        long attemptId, string status, string? errorCode, string? errorMessage, DateTime completedAt, CancellationToken ct);
}

public static class V180MileageGovernanceRules
{
    public const string GovernanceVersion = "1.8.0";
    public const string SubmittedSnapshotBasisCode = "SubmittedSnapshot";

    public static byte[] RequireHash32(byte[] value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32) throw new ArgumentException($"{name} must be exactly 32 bytes.", name);
        return value.ToArray();
    }

    public static string RequireDecisionSource(string? value) => value?.Trim() switch
    {
        "ProviderSuggested" => "ProviderSuggested",
        "LeaderAdjusted" => "LeaderAdjusted",
        "ManualFallback" => "ManualFallback",
        _ => throw new InvalidOperationException("F_B_DECISION_SOURCE_INVALID：只允許 ProviderSuggested、LeaderAdjusted 或 ManualFallback。")
    };

    public static (string Code, string? Message) SanitizeProviderFailure(string? code, string? message)
    {
        var normalizedCode = Sanitize(code, 100, "PROVIDER_FAILURE", allowSpaces: false);
        var normalizedMessage = Sanitize(message, 1000, "Provider operation failed.", allowSpaces: true);
        return (normalizedCode, normalizedMessage);
    }

    public static void EnsureCurrentGeocodingSelection(Location location, GeocodingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.LocationId != location.LocationId || attempt.Status != "Succeeded")
            throw new InvalidOperationException(
                "F_B_GEOCODING_ATTEMPT_INVALID：只能選取同一地點的成功 geocoding attempt。");
        if (!attempt.AddressBasisHash.SequenceEqual(V180MileageCanonicalization.HashAddress(location)))
            throw new InvalidOperationException(
                "F_B_GEOCODING_ATTEMPT_STALE：geocoding attempt 不符合目前 F-ADDR-v1 basis。");
    }

    private static string Sanitize(string? value, int maxLength, string fallback, bool allowSpaces)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var ch in value.Trim())
        {
            if (builder.Length >= maxLength) break;
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' || (allowSpaces && char.IsWhiteSpace(ch)))
                builder.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
        }
        var result = builder.ToString().Trim();
        return result.Length == 0 ? fallback : result;
    }
}

public static class V180MileageCanonicalization
{
    public const string AddressVersion = "F-ADDR-v1";
    public const string RouteVersion = "F-ROUTE-v1";

    public static V180AddressCanonicalBasis BuildAddressBasis(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var address = NormalizeText(location.Address);
        if (!string.IsNullOrEmpty(address))
            return new("ADDRESS", address);
        var plusCode = NormalizeText(location.PlusCode)?.ToUpperInvariant();
        return string.IsNullOrEmpty(plusCode) ? new(null, null) : new("PLUS_CODE", plusCode);
    }

    public static byte[] SerializeAddress(Location location) =>
        SerializeAddress(BuildAddressBasis(location));

    public static byte[] SerializeAddress(V180AddressCanonicalBasis basis)
    {
        var builder = new StringBuilder();
        Append(builder, "version", AddressVersion);
        Append(builder, "inputKind", basis.InputKind);
        Append(builder, "inputValue", basis.InputValue);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] HashAddress(Location location) => SHA256.HashData(SerializeAddress(location));

    public static V180RouteBasis BuildDraftBasis(
        VisitTrip trip, V180DeploymentSiteBasis start, V180DeploymentSiteBasis end)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return new(trip.VehicleType, start,
            trip.Stops.OrderBy(x => x.StopSequence)
                .Select(x => new V180RouteStopBasis(x.StopSequence, x.AddressSnapshot)).ToArray(), end);
    }

    public static V180RouteBasis BuildSubmittedSnapshotBasis(VisitTripSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot.VehicleTypeSnapshot,
            new V180DeploymentSiteBasis(snapshot.StartDeploymentSiteCodeSnapshot, snapshot.StartDeploymentAddressSnapshot),
            snapshot.Stops.OrderBy(x => x.StopSequence)
                .Select(x => new V180RouteStopBasis(x.StopSequence, x.AddressSnapshot)).ToArray(),
            new V180DeploymentSiteBasis(snapshot.EndDeploymentSiteCodeSnapshot, snapshot.EndDeploymentAddressSnapshot));
    }

    public static byte[] SerializeRoute(V180RouteBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var builder = new StringBuilder();
        Append(builder, "version", RouteVersion);
        var vehicle = CanonicalVehicleType(basis.VehicleType);
        Append(builder, "vehicleType", vehicle);
        Append(builder, "travelMode", ToTravelMode(vehicle));
        Append(builder, "startDeploymentSiteCode", NormalizeCode(basis.Start?.SiteCode));
        Append(builder, "startAddress", NormalizeText(basis.Start?.Address));
        var stops = basis.Stops.OrderBy(x => x.StopSequence).ToArray();
        AppendInteger(builder, "stopCount", stops.Length);
        for (var i = 0; i < stops.Length; i++)
        {
            AppendInteger(builder, $"stop[{i}].stopSequence", stops[i].StopSequence);
            Append(builder, $"stop[{i}].address", NormalizeText(stops[i].AddressSnapshot));
        }
        Append(builder, "endDeploymentSiteCode", NormalizeCode(basis.End?.SiteCode));
        Append(builder, "endAddress", NormalizeText(basis.End?.Address));
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] HashRoute(V180RouteBasis basis) => SHA256.HashData(SerializeRoute(basis));

    public static string CanonicalVehicleType(string? vehicleType)
    {
        var value = NormalizeText(vehicleType)?.ToUpperInvariant();
        return value switch
        {
            "CAR" => "CAR",
            "MOTORCYCLE" => "MOTORCYCLE",
            _ => throw new InvalidOperationException("UNSUPPORTED_VEHICLE_TYPE")
        };
    }

    public static string ToDbRequestedVehicleType(string canonicalVehicleType) =>
        CanonicalVehicleType(canonicalVehicleType) == "CAR" ? "Car" : "Motorcycle";

    public static string ToTravelMode(string canonicalVehicleType) =>
        CanonicalVehicleType(canonicalVehicleType) == "CAR" ? "DRIVE" : "TWO_WHEELER";

    public static string? NormalizeText(string? value)
    {
        if (value is null) return null;
        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (builder.Length > 0) pendingSpace = true;
                continue;
            }
            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static string? NormalizeCode(string? value) => NormalizeText(value)?.ToUpperInvariant();

    private static void Append(StringBuilder builder, string field, string? value)
    {
        if (value is null)
        {
            builder.Append(field).Append("=-1:\n");
            return;
        }
        var bytes = Encoding.UTF8.GetByteCount(value);
        builder.Append(field).Append('=').Append(bytes.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(value).Append('\n');
    }

    private static void AppendInteger(StringBuilder builder, string field, int value) =>
        Append(builder, field, value.ToString(CultureInfo.InvariantCulture));
}
