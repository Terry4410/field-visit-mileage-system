using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180LeaderLocationGrantRulesTests
{
    private static V180LeaderLocationGrantEvidence Evidence(
        bool direct = true, bool delegation = false, bool attested = true,
        bool hr = true, bool role = true, bool organization = true,
        bool membership = true) =>
        new(hr, role, organization, membership, direct, delegation, attested);

    [Fact]
    public void Independently_attested_current_direct_appointment_is_allowed() =>
        Assert.True(V180LeaderLocationGrantRules.AllowsManagedTeamLocationWrite(Evidence()));

    [Fact]
    public void Independently_attested_current_delegation_is_allowed_only_with_current_source_appointment() =>
        Assert.True(V180LeaderLocationGrantRules.AllowsManagedTeamLocationWrite(
            Evidence(direct: false, delegation: true)));

    [Theory]
    [InlineData(false, false, true, true, true, true, true)] // historical/expired appointment
    [InlineData(true, false, false, true, true, true, true)] // no independent attestation
    [InlineData(true, false, true, false, true, true, true)] // leave/revoked HR status
    [InlineData(true, false, true, true, false, true, true)] // revoked leader role
    [InlineData(true, false, true, true, true, false, true)] // foreign organization
    [InlineData(true, false, true, true, true, true, false)] // no current team membership
    public void Incomplete_or_forged_provenance_is_denied(
        bool direct, bool delegation, bool attested, bool hr, bool role,
        bool organization, bool membership) =>
        Assert.False(V180LeaderLocationGrantRules.AllowsManagedTeamLocationWrite(
            Evidence(direct, delegation, attested, hr, role, organization, membership)));
}
