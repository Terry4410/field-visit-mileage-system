using FieldVisit.Application;

namespace FieldVisit.Infrastructure;

public sealed class V180DisabledRouteProvider : IV180RouteProvider
{
    public string ProviderName => "Disabled";

    public Task<V180RouteProviderResult> CalculateAsync(
        V180RouteProviderRequest request,
        CancellationToken ct) =>
        Task.FromResult(new V180RouteProviderResult(
            false, null, null, null,
            "PROVIDER_DISABLED",
            "Live route provider is not enabled in P2B."));
}

public sealed class V180DisabledGeocodingProvider : IV180GeocodingProvider
{
    public string ProviderName => "Disabled";

    public Task<V180GeocodingProviderResult> GeocodeAsync(
        V180GeocodingProviderRequest request,
        CancellationToken ct) =>
        Task.FromResult(new V180GeocodingProviderResult(
            false, null, null,
            "PROVIDER_DISABLED",
            "Live geocoding provider is not enabled in P2B."));
}
