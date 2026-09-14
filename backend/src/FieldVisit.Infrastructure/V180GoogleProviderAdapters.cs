using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FieldVisit.Application;

namespace FieldVisit.Infrastructure;

public sealed class V180GoogleProviderOptions
{
    public bool Enabled { get; init; }
    public string RoutesApiKey { get; init; } = "";
    public string GeocodingApiKey { get; init; } = "";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("EpicF:Google:ProviderTimeoutSeconds must be between 1 and 120.");
    }
}

/// <summary>Safe F-C default. It performs no network I/O and no retries.</summary>
public sealed class UnconfiguredV180RouteProvider : IV180RouteProvider
{
    public string ProviderName => "Unconfigured";

    public Task<V180RouteProviderResult> CalculateAsync(
        V180RouteProviderRequest request, CancellationToken ct) =>
        Task.FromResult(new V180RouteProviderResult(
            false, null, null, null, "PROVIDER_UNCONFIGURED",
            "Epic-F route provider is not configured."));
}

/// <summary>No-network, no-retry geocoding default.</summary>
public sealed class UnconfiguredV180GeocodingProvider : IV180GeocodingProvider
{
    public string ProviderName => "Unconfigured";

    public Task<V180GeocodingProviderResult> GeocodeAsync(
        V180GeocodingProviderRequest request, CancellationToken ct) =>
        Task.FromResult(new V180GeocodingProviderResult(
            false, null, null, "PROVIDER_UNCONFIGURED",
            "Epic-F geocoding provider is not configured."));
}

public sealed class GoogleRoutesV180RouteProvider(HttpClient http, V180GoogleProviderOptions options) : IV180RouteProvider
{
    internal const string Endpoint = "https://routes.googleapis.com/directions/v2:computeRoutes";
    internal const string FieldMask = "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline";
    public string ProviderName => "GoogleRoutes";

    public async Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request, CancellationToken ct)
    {
        if (request.TravelMode is not ("DRIVE" or "TWO_WHEELER"))
            return Fail("GOOGLE_ROUTES_TRAVEL_MODE_INVALID", "Unsupported route travel mode.");
        if (string.IsNullOrWhiteSpace(request.StartAddress) || string.IsNullOrWhiteSpace(request.EndAddress))
            return Fail("GOOGLE_ROUTES_BASIS_INVALID", "Route origin and destination are required.");

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Add("X-Goog-Api-Key", options.RoutesApiKey);
        message.Headers.Add("X-Goog-FieldMask", FieldMask);
        message.Content = JsonContent.Create(new
        {
            origin = new { address = request.StartAddress },
            destination = new { address = request.EndAddress },
            intermediates = request.StopAddresses.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new { address = x }).ToArray(),
            travelMode = request.TravelMode,
            computeAlternativeRoutes = false,
            optimizeWaypointOrder = false,
            polylineQuality = "OVERVIEW"
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            return Fail($"GOOGLE_ROUTES_HTTP_{(int)response.StatusCode}", "Google Routes request failed.");

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var routes = json.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0) return Fail("GOOGLE_ROUTES_NO_ROUTE", "No route was returned.");
            var route = routes[0];
            var meters = route.GetProperty("distanceMeters").GetDecimal();
            var duration = ParseDurationSeconds(route.GetProperty("duration").GetString());
            var polyline = route.GetProperty("polyline").GetProperty("encodedPolyline").GetString();
            return new(true, decimal.Round(meters / 1000m, 3), duration, polyline, null, null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or OverflowException)
        {
            return Fail("GOOGLE_ROUTES_INVALID_RESPONSE", "Google Routes returned an invalid response.");
        }
    }

    private static int ParseDurationSeconds(string? value)
    {
        if (value is null || !value.EndsWith('s') || !decimal.TryParse(value[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var seconds))
            throw new FormatException("Invalid duration.");
        return checked((int)Math.Ceiling(seconds));
    }

    private static V180RouteProviderResult Fail(string code, string message) => new(false, null, null, null, code, message);
}

public sealed class GoogleGeocodingV180GeocodingProvider(HttpClient http, V180GoogleProviderOptions options) : IV180GeocodingProvider
{
    internal const string Endpoint = "https://maps.googleapis.com/maps/api/geocode/json";
    public string ProviderName => "GoogleGeocoding";

    public async Task<V180GeocodingProviderResult> GeocodeAsync(V180GeocodingProviderRequest request, CancellationToken ct)
    {
        if (request.InputKind is not ("ADDRESS" or "PLUS_CODE") || string.IsNullOrWhiteSpace(request.InputValue))
            return Fail("GOOGLE_GEOCODING_BASIS_INVALID", "Address or Plus Code is required.");

        var url = $"{Endpoint}?address={Uri.EscapeDataString(request.InputValue)}&key={Uri.EscapeDataString(options.GeocodingApiKey)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            return Fail($"GOOGLE_GEOCODING_HTTP_{(int)response.StatusCode}", "Google Geocoding request failed.");

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            if (json.RootElement.GetProperty("status").GetString() != "OK")
                return Fail("GOOGLE_GEOCODING_NO_RESULT", "No geocoding result was returned.");
            var results = json.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return Fail("GOOGLE_GEOCODING_NO_RESULT", "No geocoding result was returned.");
            var location = results[0].GetProperty("geometry").GetProperty("location");
            return new(true, location.GetProperty("lat").GetDecimal(), location.GetProperty("lng").GetDecimal(), null, null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or OverflowException)
        {
            return Fail("GOOGLE_GEOCODING_INVALID_RESPONSE", "Google Geocoding returned an invalid response.");
        }
    }

    private static V180GeocodingProviderResult Fail(string code, string message) => new(false, null, null, code, message);
}
