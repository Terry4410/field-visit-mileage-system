using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180GoogleMileageCanonicalizationTests
{
    [Fact]
    public void Address_golden_vector_uses_utf8_byte_lengths_and_exact_sha256()
    {
        var location = new Location { Address = "  台北  101  ", PlusCode = "+8Q7X+XX" };
        var bytes = V180MileageCanonicalization.SerializeAddress(location);
        var expected = "version=9:F-ADDR-v1\ninputKind=7:ADDRESS\ninputValue=10:台北 101\n";
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
        Assert.Equal("d3ebb4c5c5409b2e7ce97d00bb729e95e68a439174e013c463d361629a493f5f",
            Convert.ToHexString(V180MileageCanonicalization.HashAddress(location)).ToLowerInvariant());
    }

    [Fact]
    public void Address_fallback_uppercases_plus_code_without_losing_plus_sign()
    {
        var basis = V180MileageCanonicalization.BuildAddressBasis(new Location { PlusCode = " 8q7x+xx " });
        Assert.Equal("PLUS_CODE", basis.InputKind);
        Assert.Equal("8Q7X+XX", basis.InputValue);
        Assert.NotEqual(
            V180MileageCanonicalization.SerializeAddress(new V180AddressCanonicalBasis(null, null)),
            V180MileageCanonicalization.SerializeAddress(new V180AddressCanonicalBasis("PLUS_CODE", "8Q7X+XX")));
        Assert.NotEqual(
            Encoding.UTF8.GetString(V180MileageCanonicalization.SerializeAddress(new V180AddressCanonicalBasis(null, null))),
            Encoding.UTF8.GetString(V180MileageCanonicalization.SerializeAddress(new V180AddressCanonicalBasis("", ""))));
    }

    [Fact]
    public void Normalization_is_nfc_unicode_whitespace_and_case_sensitive_for_addresses()
    {
        Assert.Equal("école centrale", V180MileageCanonicalization.NormalizeText(" e\u0301cole\u00a0  centrale "));
        Assert.NotEqual(V180MileageCanonicalization.NormalizeText("Main St"), V180MileageCanonicalization.NormalizeText("main st"));
        Assert.Equal("A  B", V180MileageCanonicalization.NormalizeText("A\n\tB"));
    }

    [Fact]
    public void Route_golden_vector_sorts_only_by_stop_sequence_and_keeps_duplicates()
    {
        var basis = new V180RouteBasis(" car ",
            new V180DeploymentSiteBasis(" s-1 ", "  Main  St. "),
            [new(2, "台北"), new(1, "台北")],
            new V180DeploymentSiteBasis(" e-2 ", "End"));
        var expected = "version=10:F-ROUTE-v1\nvehicleType=3:CAR\ntravelMode=5:DRIVE\n" +
            "startDeploymentSiteCode=3:S-1\nstartAddress=8:Main St.\nstopCount=1:2\n" +
            "stop[0].stopSequence=1:1\nstop[0].address=6:台北\n" +
            "stop[1].stopSequence=1:2\nstop[1].address=6:台北\n" +
            "endDeploymentSiteCode=3:E-2\nendAddress=3:End\n";
        Assert.Equal(expected, Encoding.UTF8.GetString(V180MileageCanonicalization.SerializeRoute(basis)));
        Assert.Equal("fe590b4bebe0e1837f304275203fb06e51bac67965e9837bba1369fb74e25d8b",
            Convert.ToHexString(V180MileageCanonicalization.HashRoute(basis)).ToLowerInvariant());
        var changed = basis with { Start = new V180DeploymentSiteBasis("S-1", "Changed") };
        Assert.NotEqual(V180MileageCanonicalization.HashRoute(basis), V180MileageCanonicalization.HashRoute(changed));
    }

    [Fact]
    public void Vehicle_mapping_is_explicit_and_never_silently_falls_back()
    {
        Assert.Equal("Car", V180MileageCanonicalization.ToDbRequestedVehicleType("CAR"));
        Assert.Equal("DRIVE", V180MileageCanonicalization.ToTravelMode("CAR"));
        Assert.Equal("Motorcycle", V180MileageCanonicalization.ToDbRequestedVehicleType("MOTORCYCLE"));
        Assert.Equal("TWO_WHEELER", V180MileageCanonicalization.ToTravelMode("MOTORCYCLE"));
        Assert.Throws<InvalidOperationException>(() => V180MileageCanonicalization.CanonicalVehicleType("BICYCLE"));
    }

    [Fact]
    public void Governance_hashes_are_raw_32_byte_values()
    {
        var hash = V180MileageCanonicalization.HashAddress(new Location { Address = "A" });
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, V180MileageGovernanceRules.RequireHash32(hash, "hash"));
        Assert.Throws<ArgumentException>(() => V180MileageGovernanceRules.RequireHash32(new byte[31], "hash"));
    }

    [Fact]
    public void Draft_uses_current_site_facts_and_trip_stop_snapshots()
    {
        var trip = new VisitTrip { VehicleType = "CAR" };
        trip.Stops.Add(new VisitTripStop { StopSequence = 2, AddressSnapshot = "second" });
        trip.Stops.Add(new VisitTripStop { StopSequence = 1, AddressSnapshot = "first" });
        var basis = V180MileageCanonicalization.BuildDraftBasis(trip,
            new V180DeploymentSiteBasis("CURRENT-A", "Current A"),
            new V180DeploymentSiteBasis("CURRENT-B", "Current B"));
        Assert.Equal("CURRENT-A", basis.Start!.SiteCode);
        Assert.Equal([1, 2], basis.Stops.Select(x => x.StopSequence));
        Assert.Equal("first", basis.Stops[0].AddressSnapshot);
    }

    [Fact]
    public void Submitted_snapshot_uses_immutable_snapshot_facts_after_master_data_changes()
    {
        var snapshot = new VisitTripSnapshot
        {
            VehicleTypeSnapshot = "MOTORCYCLE",
            StartDeploymentSiteCodeSnapshot = "OLD-A",
            StartDeploymentAddressSnapshot = "Old address",
            EndDeploymentSiteCodeSnapshot = "OLD-B",
            EndDeploymentAddressSnapshot = "Old end",
            Stops = [new VisitTripSnapshotStop { StopSequence = 1, AddressSnapshot = "Frozen stop" }]
        };
        var basis = V180MileageCanonicalization.BuildSubmittedSnapshotBasis(snapshot);
        Assert.Equal("OLD-A", basis.Start!.SiteCode);
        Assert.Equal("Frozen stop", basis.Stops.Single().AddressSnapshot);
        Assert.Equal("TWO_WHEELER", V180MileageCanonicalization.ToTravelMode(basis.VehicleType));
    }
}
