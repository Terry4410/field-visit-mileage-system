using System.Net;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Application.Tests;

public sealed class GoogleMapsProvidersTests
{
    [Fact]
    public async Task Routes_provider_uses_server_side_key_and_parses_distance()
    {
        HttpRequestMessage? captured=null;
        var handler=new StubHandler(req =>
        {
            captured=req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content=new StringContent("{\"routes\":[{\"distanceMeters\":12345,\"duration\":\"900s\",\"polyline\":{\"encodedPolyline\":\"abc\"}}]}",Encoding.UTF8,"application/json")
            };
        });
        var config=Config();
        var provider=new GoogleMapsRouteProvider(new HttpClient(handler),config);
        var result=await provider.CalculateAsync(new V180RouteProviderRequest(Guid.NewGuid(),"DRIVE","Start",new[]{"Stop A","Stop B"},"End"),CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(12.345m,result.SuggestedDistanceKm);
        Assert.Equal("GoogleMapsRoutes",provider.ProviderName);
        Assert.Equal("test-key",captured!.Headers.GetValues("X-Goog-Api-Key").Single());
        Assert.Contains("routes.distanceMeters",captured.Headers.GetValues("X-Goog-FieldMask").Single());
        Assert.DoesNotContain("test-key",captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task Geocoding_provider_parses_v4_coordinates()
    {
        var handler=new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content=new StringContent("{\"results\":[{\"location\":{\"latitude\":25.0478,\"longitude\":121.5319}}]}",Encoding.UTF8,"application/json")
        });
        var provider=new GoogleMapsGeocodingProvider(new HttpClient(handler),Config());
        var result=await provider.GeocodeAsync(new V180GeocodingProviderRequest(Guid.NewGuid(),"ADDRESS","台北市"),CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(25.0478m,result.Latitude);
        Assert.Equal(121.5319m,result.Longitude);
    }

    private static IConfiguration Config()
    {
        var config=new ConfigurationManager();
        config["GoogleMaps:ApiKey"]="test-key";
        config["GoogleMaps:TimeoutSeconds"]="20";
        return config;
    }

    private sealed class StubHandler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
