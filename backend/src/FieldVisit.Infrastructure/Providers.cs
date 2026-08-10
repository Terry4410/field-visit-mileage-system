using System.Security.Cryptography;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Infrastructure;

public sealed class MockRouteCalculationService : IRouteCalculationService
{
    public Task<RouteCalculationResult> CalculateAsync(VisitTrip trip, CancellationToken ct)
    {
        var claimed = trip.MileageCalculation?.ClaimedDistanceKm;
        if (claimed is null or <= 0) return Task.FromResult(new RouteCalculationResult(false, null, "CLAIMED_MILEAGE_MISSING", "缺少外訪員自算里程。"));
        if (trip.Stops.Count < 2) return Task.FromResult(new RouteCalculationResult(false, null, "STOP_INSUFFICIENT", "站點不足。"));
        if (trip.Stops.Any(x => string.IsNullOrWhiteSpace(x.AddressSnapshot))) return Task.FromResult(new RouteCalculationResult(false, null, "ADDRESS_INVALID", "至少一個地點缺少地址。"));
        var km = decimal.Round(claimed.Value * 0.965m, 1, MidpointRounding.AwayFromZero);
        return Task.FromResult(new RouteCalculationResult(true, km, null, null));
    }
}

public sealed class MockGeocodingService : IGeocodingService
{
    public Task<GeocodingResult> ResolveAsync(string? address, string? plusCode, CancellationToken ct)
    {
        var key = address ?? plusCode;
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult(new GeocodingResult(false, null, null, "ADDRESS_MISSING", "地址與 Plus Code 皆為空。"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var a = BitConverter.ToUInt16(hash, 0) / 65535m;
        var b = BitConverter.ToUInt16(hash, 2) / 65535m;
        var lat = decimal.Round(24.5m + a * 1.5m, 7);
        var lng = decimal.Round(120.8m + b * 1.8m, 7);
        return Task.FromResult(new GeocodingResult(true, lat, lng, null, null));
    }
}
