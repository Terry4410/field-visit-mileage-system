using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldVisit.Application;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Server-side Google Maps Platform providers. API keys are read only from
/// configuration (Azure App Service settings / secret injection) and are never
/// returned to callers or persisted in application data.
/// </summary>
public sealed class GoogleMapsRouteProvider : IV180RouteProvider
{
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly string baseUrl;
    private readonly int maxIntermediateWaypoints;

    public GoogleMapsRouteProvider(HttpClient http,IConfiguration configuration)
    {
        this.http=http;
        apiKey=RequireKey(configuration);
        baseUrl=(configuration["GoogleMaps:RoutesBaseUrl"]??"https://routes.googleapis.com").TrimEnd('/');
        maxIntermediateWaypoints=configuration.GetValue<int?>("GoogleMaps:MaxIntermediateWaypoints")??25;
        this.http.Timeout=TimeSpan.FromSeconds(configuration.GetValue<int?>("GoogleMaps:TimeoutSeconds")??20);
    }

    public string ProviderName=>"GoogleMapsRoutes";

    public async Task<V180RouteProviderResult> CalculateAsync(V180RouteProviderRequest request,CancellationToken ct)
    {
        var start=request.StartAddress?.Trim();
        var end=request.EndAddress?.Trim();
        var stops=request.StopAddresses.Select(x=>x?.Trim()).ToArray();
        if(string.IsNullOrWhiteSpace(start)||string.IsNullOrWhiteSpace(end)||stops.Any(string.IsNullOrWhiteSpace))
            return new(false,null,null,null,"GOOGLE_ROUTE_INPUT_REQUIRED","Google route requires start, all visit stops, and end addresses.");
        if(stops.Length>maxIntermediateWaypoints)
            return new(false,null,null,null,"GOOGLE_ROUTE_WAYPOINT_LIMIT",$"Google route supports at most {maxIntermediateWaypoints} intermediate waypoints in this UAT configuration.");

        var body=new
        {
            origin=new{address=start},
            destination=new{address=end},
            intermediates=stops.Select(x=>new{address=x}).ToArray(),
            travelMode=request.TravelMode,
            computeAlternativeRoutes=false,
            languageCode="zh-TW",
            units="METRIC"
        };

        using var message=new HttpRequestMessage(HttpMethod.Post,$"{baseUrl}/directions/v2:computeRoutes");
        message.Headers.TryAddWithoutValidation("X-Goog-Api-Key",apiKey);
        message.Headers.TryAddWithoutValidation("X-Goog-FieldMask","routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline");
        message.Headers.TryAddWithoutValidation("X-FieldVisit-Correlation-Id",request.CorrelationId.ToString("D"));
        message.Content=JsonContent.Create(body);

        using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);
        var payload=await response.Content.ReadAsStringAsync(ct);
        if(!response.IsSuccessStatusCode)
            return new(false,null,null,null,$"GOOGLE_ROUTES_HTTP_{(int)response.StatusCode}",SafeHttpMessage(response.StatusCode));

        try
        {
            using var doc=JsonDocument.Parse(payload);
            if(!doc.RootElement.TryGetProperty("routes",out var routes)||routes.GetArrayLength()==0)
                return new(false,null,null,null,"GOOGLE_ROUTES_EMPTY","Google Routes returned no usable route.");

            var route=routes[0];
            if(!route.TryGetProperty("distanceMeters",out var distanceNode)||!distanceNode.TryGetInt64(out var meters)||meters<=0)
                return new(false,null,null,null,"GOOGLE_ROUTES_DISTANCE_MISSING","Google Routes returned no positive distance.");

            int? durationSeconds=null;
            if(route.TryGetProperty("duration",out var durationNode))
            {
                var raw=durationNode.GetString();
                if(raw?.EndsWith('s')==true
                    &&decimal.TryParse(raw[..^1],NumberStyles.Number,CultureInfo.InvariantCulture,out var seconds))
                    durationSeconds=(int)Math.Round(seconds,MidpointRounding.AwayFromZero);
            }

            string? polyline=null;
            if(route.TryGetProperty("polyline",out var polylineNode)
                &&polylineNode.TryGetProperty("encodedPolyline",out var encoded))
                polyline=encoded.GetString();

            return new(true,decimal.Round(meters/1000m,3),durationSeconds,polyline,null,null);
        }
        catch(JsonException)
        {
            return new(false,null,null,null,"GOOGLE_ROUTES_INVALID_JSON","Google Routes returned an invalid response.");
        }
    }

    private static string RequireKey(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration["GoogleMaps:ApiKey"])
            ?configuration["GoogleMaps:ApiKey"]!
            :throw new InvalidOperationException("GoogleMaps__ApiKey 尚未設定。");

    private static string SafeHttpMessage(HttpStatusCode code)
        =>$"Google Routes request failed with HTTP {(int)code}.";
}

public sealed class GoogleMapsGeocodingProvider : IV180GeocodingProvider
{
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly string baseUrl;

    public GoogleMapsGeocodingProvider(HttpClient http,IConfiguration configuration)
    {
        this.http=http;
        apiKey=!string.IsNullOrWhiteSpace(configuration["GoogleMaps:ApiKey"])
            ?configuration["GoogleMaps:ApiKey"]!
            :throw new InvalidOperationException("GoogleMaps__ApiKey 尚未設定。");
        baseUrl=(configuration["GoogleMaps:GeocodingBaseUrl"]??"https://geocode.googleapis.com").TrimEnd('/');
        this.http.Timeout=TimeSpan.FromSeconds(configuration.GetValue<int?>("GoogleMaps:TimeoutSeconds")??20);
    }

    public string ProviderName=>"GoogleMapsGeocoding";

    public async Task<V180GeocodingProviderResult> GeocodeAsync(V180GeocodingProviderRequest request,CancellationToken ct)
    {
        var value=request.InputValue?.Trim();
        if(string.IsNullOrWhiteSpace(value))
            return new(false,null,null,"GOOGLE_GEOCODING_INPUT_REQUIRED","Google geocoding requires an address or Plus Code.");

        var encoded=Uri.EscapeDataString(value).Replace("%20","+");
        using var message=new HttpRequestMessage(HttpMethod.Get,$"{baseUrl}/v4/geocode/address/{encoded}");
        message.Headers.TryAddWithoutValidation("X-Goog-Api-Key",apiKey);
        message.Headers.TryAddWithoutValidation("X-FieldVisit-Correlation-Id",request.CorrelationId.ToString("D"));

        using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);
        var payload=await response.Content.ReadAsStringAsync(ct);
        if(!response.IsSuccessStatusCode)
            return new(false,null,null,$"GOOGLE_GEOCODING_HTTP_{(int)response.StatusCode}",$"Google Geocoding request failed with HTTP {(int)response.StatusCode}.");

        try
        {
            using var doc=JsonDocument.Parse(payload);
            if(!doc.RootElement.TryGetProperty("results",out var results)||results.GetArrayLength()==0)
                return new(false,null,null,"GOOGLE_GEOCODING_EMPTY","Google Geocoding returned no result.");
            var result=results[0];
            if(!result.TryGetProperty("location",out var location)
                ||!location.TryGetProperty("latitude",out var latNode)
                ||!location.TryGetProperty("longitude",out var lngNode)
                ||!latNode.TryGetDecimal(out var lat)
                ||!lngNode.TryGetDecimal(out var lng))
                return new(false,null,null,"GOOGLE_GEOCODING_COORDINATES_MISSING","Google Geocoding returned no usable coordinates.");
            return new(true,lat,lng,null,null);
        }
        catch(JsonException)
        {
            return new(false,null,null,"GOOGLE_GEOCODING_INVALID_JSON","Google Geocoding returned an invalid response.");
        }
    }
}
