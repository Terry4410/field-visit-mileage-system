using FieldVisit.Application;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Safe F-B default. Live Google calls require a later, explicitly authorized
/// adapter package; this implementation performs no network I/O and no retries.
/// </summary>
public sealed class UnconfiguredV180RouteProvider : IV180RouteProvider
{
    public string ProviderName => "Unconfigured";

    public Task<V180RouteProviderResult> CalculateAsync(
        V180RouteProviderRequest request, CancellationToken ct) =>
        Task.FromResult(new V180RouteProviderResult(
            false, null, null, null, "PROVIDER_UNCONFIGURED",
            "Epic-F route provider is not configured."));
}

/// <summary>No-network, no-retry geocoding default for F-B.</summary>
public sealed class UnconfiguredV180GeocodingProvider : IV180GeocodingProvider
{
    public string ProviderName => "Unconfigured";

    public Task<V180GeocodingProviderResult> GeocodeAsync(
        V180GeocodingProviderRequest request, CancellationToken ct) =>
        Task.FromResult(new V180GeocodingProviderResult(
            false, null, null, "PROVIDER_UNCONFIGURED",
            "Epic-F geocoding provider is not configured."));
}
