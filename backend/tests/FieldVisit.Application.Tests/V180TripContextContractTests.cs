using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TripContextContractTests
{
    private static V180TripContextDto Context(
        long employmentId = 7001,
        bool eligibleForTrip = true,
        int? selectedTeamId = 11,
        int? defaultStart = 101,
        int? defaultEnd = 102) =>
        new(
            EmploymentId: employmentId,
            VisitDate: new DateOnly(2026, 9, 29),
            EligibleForTrip: eligibleForTrip,
            ValidationCode: "TRIP_CONTEXT_INVALID",
            ValidationMessage: "Trip context is not eligible.",
            Teams:
            [
                new V180TripContextTeamDto(
                    TeamId: 11,
                    Code: "T11",
                    Name: "Team 11",
                    IsPrimary: true)
            ],
            SelectedTeamId: selectedTeamId,
            EligibleDeploymentSites:
            [
                Site(101, true),
                Site(102, false),
                Site(103, false)
            ],
            PrimaryDeploymentSiteId: 101,
            DefaultStartDeploymentSiteId: defaultStart,
            DefaultEndDeploymentSiteId: defaultEnd);

    private static V180TripContextDeploymentSiteDto Site(
        int id,
        bool isPrimary) =>
        new(
            DeploymentSiteId: id,
            CenterId: 7,
            CenterCode: "C07",
            CenterName: "Center 7",
            SiteCode: $"S{id}",
            SiteName: $"Site {id}",
            LocationId: 1000 + id,
            LocationCode: $"L{id}",
            LocationName: $"Location {id}",
            Address: $"Address {id}",
            IsPrimary: isPrimary);

    [Fact]
    public void ResolveDraftSites_accepts_valid_trip_specific_override()
    {
        var result =
            V180TripPersistenceRules.ResolveDraftSites(
                Context(),
                requestedStart: 103,
                requestedEnd: 101);

        Assert.Equal(103, result.Start);
        Assert.Equal(101, result.End);
    }

    [Fact]
    public void ResolveDraftSites_accepts_official_site_outside_employment_scope()
    {
        var context = Context() with
        {
            OfficialDeploymentSites =
            [
                Site(101, true),
                Site(102, false),
                Site(201, false)
            ]
        };

        var result =
            V180TripPersistenceRules.ResolveDraftSites(
                context,
                requestedStart: 201,
                requestedEnd: 101);

        Assert.Equal(201, result.Start);
        Assert.Equal(101, result.End);
    }

    [Fact]
    public void ResolveDraftSites_rejects_invalid_requested_site()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.ResolveDraftSites(
                    Context(),
                    requestedStart: 999,
                    requestedEnd: 102));

        Assert.Contains(
            "DEPLOYMENT_SITE_INELIGIBLE",
            ex.Message);
    }

    [Fact]
    public void ResolveDraftSites_preserves_existing_valid_override_on_edit()
    {
        var result =
            V180TripPersistenceRules.ResolveDraftSites(
                Context(),
                requestedStart: null,
                requestedEnd: null,
                existingStart: 103,
                existingEnd: 102);

        Assert.Equal(103, result.Start);
        Assert.Equal(102, result.End);
    }

    [Fact]
    public void ResolveDraftSites_uses_deterministic_default_when_appropriate()
    {
        var result =
            V180TripPersistenceRules.ResolveDraftSites(
                Context(),
                requestedStart: null,
                requestedEnd: null,
                existingStart: 999,
                existingEnd: 998);

        Assert.Equal(101, result.Start);
        Assert.Equal(102, result.End);
    }

    [Fact]
    public void EnsureReadyForSubmit_rejects_employment_mismatch()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.EnsureReadyForSubmit(
                    Context(),
                    employmentId: 7002,
                    teamId: 11,
                    startSiteId: 101,
                    endSiteId: 102));

        Assert.Contains(
            "TRIP_CONTEXT_EMPLOYMENT_CHANGED",
            ex.Message);
    }

    [Fact]
    public void EnsureReadyForSubmit_rejects_team_or_context_mismatch()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.EnsureReadyForSubmit(
                    Context(),
                    employmentId: 7001,
                    teamId: 12,
                    startSiteId: 101,
                    endSiteId: 102));

        Assert.Contains(
            "TRIP_CONTEXT_INVALID",
            ex.Message);
    }

    [Fact]
    public void EnsureReadyForSubmit_rejects_missing_start_site()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.EnsureReadyForSubmit(
                    Context(),
                    employmentId: 7001,
                    teamId: 11,
                    startSiteId: null,
                    endSiteId: 102));

        Assert.Contains(
            "START_DEPLOYMENT_SITE_REQUIRED",
            ex.Message);
    }

    [Fact]
    public void EnsureReadyForSubmit_rejects_missing_end_site()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.EnsureReadyForSubmit(
                    Context(),
                    employmentId: 7001,
                    teamId: 11,
                    startSiteId: 101,
                    endSiteId: null));

        Assert.Contains(
            "END_DEPLOYMENT_SITE_REQUIRED",
            ex.Message);
    }

    [Fact]
    public void EnsureReadyForSubmit_rejects_ineligible_start_site()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.EnsureReadyForSubmit(
                    Context(),
                    employmentId: 7001,
                    teamId: 11,
                    startSiteId: 999,
                    endSiteId: 102));

        Assert.Contains(
            "START_DEPLOYMENT_SITE_INELIGIBLE",
            ex.Message);
    }

    [Fact]
    public void EnsureReadyForSubmit_rejects_ineligible_end_site()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => V180TripPersistenceRules.EnsureReadyForSubmit(
                    Context(),
                    employmentId: 7001,
                    teamId: 11,
                    startSiteId: 101,
                    endSiteId: 999));

        Assert.Contains(
            "END_DEPLOYMENT_SITE_INELIGIBLE",
            ex.Message);
    }

    [Fact]
    public void EnsureReadyForSubmit_accepts_official_site_outside_employment_scope()
    {
        var context = Context() with
        {
            OfficialDeploymentSites =
            [
                Site(101, true),
                Site(102, false),
                Site(201, false)
            ]
        };

        V180TripPersistenceRules.EnsureReadyForSubmit(
            context,
            employmentId: 7001,
            teamId: 11,
            startSiteId: 201,
            endSiteId: 102);
    }

    [Fact]
    public void EnsureReadyForSubmit_accepts_valid_context()
    {
        V180TripPersistenceRules.EnsureReadyForSubmit(
            Context(),
            employmentId: 7001,
            teamId: 11,
            startSiteId: 101,
            endSiteId: 102);
    }
}
