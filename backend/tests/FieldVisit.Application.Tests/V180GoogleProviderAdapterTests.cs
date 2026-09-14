using System.Net;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180GoogleProviderAdapterTests
{
    [Theory]
    [InlineData("DRIVE")]
    [InlineData("TWO_WHEELER")]
    public async Task Routes_uses_exact_field_mask_order_and_frozen_mode_without_retry(string mode)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"routes":[{"distanceMeters":12345,"duration":"901.2s","polyline":{"encodedPolyline":"TRANSIENT"}},{"distanceMeters":99999,"duration":"1s","polyline":{"encodedPolyline":"IGNORED"}}]}"""));
        var provider = new GoogleRoutesV180RouteProvider(new HttpClient(handler), Options());

        var result = await provider.CalculateAsync(new V180RouteProviderRequest(
            Guid.NewGuid(), mode, "START", new[] { "STOP-1", "STOP-2" }, "END"), default);

        Assert.True(result.Success);
        Assert.Equal(12.345m, result.SuggestedDistanceKm);
        Assert.Equal(902, result.DurationSeconds);
        Assert.Equal("TRANSIENT", result.EncodedPolyline);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline", handler.Headers["X-Goog-FieldMask"]);
        Assert.Equal("ROUTES_TEST_KEY", handler.Headers["X-Goog-Api-Key"]);
        Assert.Contains($"\"travelMode\":\"{mode}\"", handler.Body);
        Assert.Contains("\"computeAlternativeRoutes\":false", handler.Body);
        Assert.Contains("\"optimizeWaypointOrder\":false", handler.Body);
        Assert.True(handler.Body.IndexOf("STOP-1", StringComparison.Ordinal) < handler.Body.IndexOf("STOP-2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_wheeler_failure_does_not_downgrade_or_retry()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var result = await new GoogleRoutesV180RouteProvider(new HttpClient(handler), Options()).CalculateAsync(
            new(Guid.NewGuid(), "TWO_WHEELER", "A", Array.Empty<string?>(), "B"), default);
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("\"travelMode\":\"TWO_WHEELER\"", handler.Body);
        Assert.DoesNotContain("\"DRIVE\"", handler.Body);
    }

    [Fact]
    public async Task Routes_invalid_payload_is_sanitized_and_not_retried()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"raw\":\"SECRET_SENTINEL\"}"));
        var result = await new GoogleRoutesV180RouteProvider(new HttpClient(handler), Options()).CalculateAsync(
            new(Guid.NewGuid(), "DRIVE", "A", Array.Empty<string?>(), "B"), default);
        Assert.False(result.Success);
        Assert.Equal("GOOGLE_ROUTES_INVALID_RESPONSE", result.ErrorCode);
        Assert.DoesNotContain("SECRET_SENTINEL", result.ErrorMessage);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Routes_internal_timeout_returns_failure_and_is_not_retried()
    {
        var handler = new BlockingHandler();
        var provider = new GoogleRoutesV180RouteProvider(new HttpClient(handler), ShortTimeoutOptions());
        var result = await provider.CalculateAsync(
            new(Guid.NewGuid(), "DRIVE", "A", Array.Empty<string?>(), "B"), default);
        Assert.False(result.Success);
        Assert.Equal("GOOGLE_ROUTES_TIMEOUT", result.ErrorCode);
        Assert.Equal("Google Routes request timed out.", result.ErrorMessage);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Routes_caller_cancellation_still_propagates_and_is_not_retried()
    {
        var handler = new BlockingHandler();
        var provider = new GoogleRoutesV180RouteProvider(new HttpClient(handler), Options());
        using var caller = new CancellationTokenSource();
        var operation = provider.CalculateAsync(
            new(Guid.NewGuid(), "DRIVE", "A", Array.Empty<string?>(), "B"), caller.Token);
        await handler.Started;
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Two_wheeler_internal_timeout_does_not_downgrade_or_retry()
    {
        var handler = new BlockingHandler();
        var provider = new GoogleRoutesV180RouteProvider(new HttpClient(handler), ShortTimeoutOptions());
        var result = await provider.CalculateAsync(
            new(Guid.NewGuid(), "TWO_WHEELER", "A", Array.Empty<string?>(), "B"), default);
        Assert.Equal("GOOGLE_ROUTES_TIMEOUT", result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("\"travelMode\":\"TWO_WHEELER\"", handler.Body);
        Assert.DoesNotContain("\"DRIVE\"", handler.Body);
    }

    [Theory]
    [InlineData("ADDRESS", "台北市信義路五段7號")]
    [InlineData("PLUS_CODE", "2G2H+XP 台北市")]
    public async Task Geocoding_accepts_address_or_plus_code_and_returns_transient_coordinates(string kind, string input)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"status":"OK","results":[{"geometry":{"location":{"lat":25.0339,"lng":121.5645}}}]}"""));
        var provider = new GoogleGeocodingV180GeocodingProvider(new HttpClient(handler), Options());
        var result = await provider.GeocodeAsync(new(Guid.NewGuid(), kind, input), default);
        Assert.True(result.Success);
        Assert.Equal(25.0339m, result.Latitude);
        Assert.Equal(121.5645m, result.Longitude);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("address=", handler.Uri);
    }

    [Fact]
    public async Task Geocoding_non_result_is_sanitized_and_not_retried()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"status":"ZERO_RESULTS","error_message":"SECRET_SENTINEL","results":[]}"""));
        var result = await new GoogleGeocodingV180GeocodingProvider(new HttpClient(handler), Options()).GeocodeAsync(
            new(Guid.NewGuid(), "ADDRESS", "A"), default);
        Assert.False(result.Success);
        Assert.Equal("GOOGLE_GEOCODING_NO_RESULT", result.ErrorCode);
        Assert.DoesNotContain("SECRET_SENTINEL", result.ErrorMessage);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Geocoding_http_failure_is_sanitized_and_not_retried()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Forbidden, "{\"error\":\"SECRET_SENTINEL\"}"));
        var result = await new GoogleGeocodingV180GeocodingProvider(new HttpClient(handler), Options()).GeocodeAsync(
            new(Guid.NewGuid(), "PLUS_CODE", "2G2H+XP"), default);
        Assert.False(result.Success);
        Assert.Equal("GOOGLE_GEOCODING_HTTP_403", result.ErrorCode);
        Assert.DoesNotContain("SECRET_SENTINEL", result.ErrorMessage);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Geocoding_internal_timeout_returns_failure_and_is_not_retried()
    {
        var handler = new BlockingHandler();
        var provider = new GoogleGeocodingV180GeocodingProvider(new HttpClient(handler), ShortTimeoutOptions());
        var result = await provider.GeocodeAsync(new(Guid.NewGuid(), "ADDRESS", "A"), default);
        Assert.False(result.Success);
        Assert.Equal("GOOGLE_GEOCODING_TIMEOUT", result.ErrorCode);
        Assert.Equal("Google Geocoding request timed out.", result.ErrorMessage);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Geocoding_caller_cancellation_still_propagates_and_is_not_retried()
    {
        var handler = new BlockingHandler();
        var provider = new GoogleGeocodingV180GeocodingProvider(new HttpClient(handler), Options());
        using var caller = new CancellationTokenSource();
        var operation = provider.GeocodeAsync(new(Guid.NewGuid(), "PLUS_CODE", "2G2H+XP"), caller.Token);
        await handler.Started;
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Default_configuration_is_disabled_and_has_separate_keys()
    {
        var options = new V180GoogleProviderOptions();
        Assert.False(options.Enabled);
        Assert.Empty(options.RoutesApiKey);
        Assert.Empty(options.GeocodingApiKey);
    }

    private static V180GoogleProviderOptions Options() => new()
    {
        Enabled = true,
        RoutesApiKey = "ROUTES_TEST_KEY",
        GeocodingApiKey = "GEOCODING_TEST_KEY",
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static V180GoogleProviderOptions ShortTimeoutOptions() => new()
    {
        Enabled = true,
        RoutesApiKey = "ROUTES_TEST_KEY",
        GeocodingApiKey = "GEOCODING_TEST_KEY",
        Timeout = TimeSpan.FromMilliseconds(10)
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string Body { get; private set; } = "";
        public string Uri { get; private set; } = "";
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Uri = request.RequestUri?.ToString() ?? "";
            foreach (var header in request.Headers) Headers[header.Key] = string.Join(",", header.Value);
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public string Body { get; private set; } = "";
        public Task Started => _started.Task;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            _started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
