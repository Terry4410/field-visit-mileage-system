using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Real V180TripContextReader + EF InMemory, NO live SQL, NO API HTTP.
/// All rows are private disposable fixtures. B3 stays OFF and B4 is HOLD.
/// </summary>
public sealed class V180DeploymentSiteIsolatedRuntimeTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly DateOnly Date=new(2026,10,12);
        internal readonly AppDbContext Db;
        internal readonly CurrentUserDto User=new(
            41,"P41","Person 41",null,1,7,"Team 7",["visitor"]);

        internal Fixture()
        {
            Db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("SiteIsolated-"+Guid.NewGuid().ToString("N"))
                .Options);
            var start=new DateOnly(2026,1,1);
            Db.UserIdentityProfiles.Add(new UserIdentityProfile
            {
                UserId=41,EmploymentId=9001,UserType=UserTypes.Internal,
                UserCode="P41"
            });
            Db.Employments.Add(new Employment
            {
                EmploymentId=9001,PersonId=11,OrganizationId=1,
                EmployeeNo="P41",HireDate=start
            });
            Db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
            {
                EmploymentStatusPeriodId=101,EmploymentId=9001,
                EmploymentStatus=EmploymentStatuses.Active,
                EffectiveFrom=start
            });
            Db.Teams.Add(new Team
            {
                TeamId=7,OrganizationId=1,TeamCode="T7",TeamName="Team 7",
                IsActive=true,EffectiveFrom=start
            });
            Db.TeamMemberships.Add(new TeamMembership
            {
                TeamMembershipId=201,EmploymentId=9001,TeamId=7,
                IsPrimary=true,EffectiveFrom=start
            });
            Db.Centers.Add(new Center
            {
                CenterId=81,OrganizationId=1,CenterCode="C81",
                CenterName="Center 81",IsActive=true,EffectiveFrom=start
            });
            Db.TeamCenterAssignments.Add(new TeamCenterAssignment
            {
                TeamCenterAssignmentId=301,TeamId=7,CenterId=81,
                EffectiveFrom=start
            });
            Db.DeploymentSites.Add(new DeploymentSite
            {
                DeploymentSiteId=101,CenterId=81,SiteCode="S101",
                SiteName="Site 101",IsActive=true,EffectiveFrom=start
            });
            Db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
            {
                TeamDeploymentSiteAssignmentId=401,TeamId=7,
                DeploymentSiteId=101,EffectiveFrom=start
            });
            Db.EmploymentDeploymentSiteAssignments.Add(
                new EmploymentDeploymentSiteAssignment
                {
                    EmploymentDeploymentSiteAssignmentId=501,
                    EmploymentId=9001,DeploymentSiteId=101,
                    IsPrimary=true,EffectiveFrom=start
                });
            Db.Locations.Add(new Location
            {
                LocationId=1001,OrganizationId=1,LocationCode="L1001",
                LocationName="Official Site 101",LocationType="Official",
                IsActive=true,ApprovalStatus="Approved",
                CreatedAt=DateTime.UtcNow
            });
            Db.DeploymentSiteLocationAssignments.Add(
                new DeploymentSiteLocationAssignment
                {
                    DeploymentSiteLocationAssignmentId=601,
                    DeploymentSiteId=101,LocationId=1001,
                    EffectiveFrom=start
                });
            Db.SaveChanges();
        }

        internal async Task<V180TripContextDto> Resolve(int? teamId=7)=>
            await new V180TripContextReader(Db)
                .ResolveAsync(User,Date,teamId,CancellationToken.None);

        internal void Alternate(int organizationId=1)
        {
            var start=new DateOnly(2026,1,1);
            Db.Centers.Add(new Center
            {
                CenterId=82,OrganizationId=organizationId,CenterCode="C82",
                CenterName="Center 82",IsActive=true,EffectiveFrom=start
            });
            Db.DeploymentSites.Add(new DeploymentSite
            {
                DeploymentSiteId=102,CenterId=82,SiteCode="S102",
                SiteName="Site 102",IsActive=true,EffectiveFrom=start
            });
            Db.Locations.Add(new Location
            {
                LocationId=1002,OrganizationId=organizationId,
                LocationCode="L1002",LocationName="Official Site 102",
                LocationType="Official",IsActive=true,
                ApprovalStatus="Approved",CreatedAt=DateTime.UtcNow
            });
            Db.DeploymentSiteLocationAssignments.Add(
                new DeploymentSiteLocationAssignment
                {
                    DeploymentSiteLocationAssignmentId=602,
                    DeploymentSiteId=102,LocationId=1002,
                    EffectiveFrom=start
                });
            Db.SaveChanges();
        }

        public void Dispose()=>Db.Dispose();
    }

    [Fact]
    public async Task SITE_live_context_resolves_primary_and_eligible_official_site()
    {
        using var f=new Fixture();
        var c=await f.Resolve();
        Assert.True(c.EligibleForTrip);
        Assert.Equal(7,c.SelectedTeamId);
        Assert.Equal(101,c.PrimaryDeploymentSiteId);
        Assert.Single(c.EligibleDeploymentSites);
        Assert.Single(c.OfficialDeploymentSites!);
    }

    [Fact]
    public async Task SITE_official_alternate_same_org_can_be_selected_without_employment_assignment()
    {
        using var f=new Fixture();
        f.Alternate();
        var c=await f.Resolve();
        Assert.Single(c.EligibleDeploymentSites);
        Assert.Equal(2,c.OfficialDeploymentSites!.Count);
        var sites=V180TripPersistenceRules.ResolveDraftSites(c,102,101);
        Assert.Equal(102,sites.Start);
        V180TripPersistenceRules.EnsureReadyForSubmit(
            c,9001,7,102,101);
    }

    [Fact]
    public async Task SITE_foreign_org_official_site_never_appears_in_picker()
    {
        using var f=new Fixture();
        f.Alternate(organizationId:2);
        var c=await f.Resolve();
        Assert.Single(c.OfficialDeploymentSites!);
        Assert.DoesNotContain(c.OfficialDeploymentSites!,x=>x.DeploymentSiteId==102);
    }

    [Fact]
    public async Task SITE_inactive_deployment_site_not_eligible()
    {
        using var f=new Fixture();
        f.Db.DeploymentSites.Single().IsActive=false;
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Resolve()).EligibleForTrip);
    }

    [Fact]
    public async Task SITE_future_deployment_site_not_eligible()
    {
        using var f=new Fixture();
        f.Db.DeploymentSites.Single().EffectiveFrom=f.Date.AddDays(1);
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Resolve()).EligibleForTrip);
    }

    [Fact]
    public async Task SITE_expired_center_not_eligible()
    {
        using var f=new Fixture();
        f.Db.Centers.Single().EffectiveTo=f.Date.AddDays(-1);
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Resolve()).EligibleForTrip);
    }

    [Fact]
    public async Task SITE_expired_site_location_assignment_not_eligible()
    {
        using var f=new Fixture();
        f.Db.DeploymentSiteLocationAssignments.Single().EffectiveTo=f.Date.AddDays(-1);
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Resolve()).EligibleForTrip);
    }

    [Fact]
    public async Task SITE_inactive_location_not_eligible()
    {
        using var f=new Fixture();
        f.Db.Locations.Single().IsActive=false;
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Resolve()).EligibleForTrip);
    }

    [Fact]
    public async Task SITE_pending_location_not_eligible()
    {
        using var f=new Fixture();
        f.Db.Locations.Single().ApprovalStatus="Pending";
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Resolve()).EligibleForTrip);
    }

    [Fact]
    public async Task SITE_cross_org_employment_site_denied_before_official_picker()
    {
        using var f=new Fixture();
        f.Db.Centers.Single().OrganizationId=2;
        await f.Db.SaveChangesAsync();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Resolve());
        Assert.Contains("TRIP_CONTEXT_SITE_ORGANIZATION_MISMATCH",ex.Message);
    }

    [Fact]
    public async Task SITE_no_team_center_must_fail_closed_before_creating_draft()
    {
        using var f=new Fixture();
        f.Db.TeamCenterAssignments.Remove(f.Db.TeamCenterAssignments.Single());
        await f.Db.SaveChangesAsync();
        var c=await f.Resolve();
        Assert.False(c.EligibleForTrip);
        Assert.Equal(7,c.SelectedTeamId);
        Assert.Contains("NO_ELIGIBLE_DEPLOYMENT_SITE",c.ValidationCode);
        Assert.Throws<InvalidOperationException>(()=>
            V180TripPersistenceRules.ResolveDraftSites(c,null,null));
    }

    [Fact]
    public async Task SITE_expired_membership_not_eligible_even_if_site_is_active()
    {
        using var f=new Fixture();
        f.Db.TeamMemberships.Single().EffectiveTo=f.Date.AddDays(-1);
        await f.Db.SaveChangesAsync();
        // With no current membership, the API's explicit TeamId must
        // fail; an omitted TeamId returns a non-eligible context.
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(
            ()=>f.Resolve(teamId:7));
        Assert.Contains("TRIP_CONTEXT_TEAM_NOT_ASSIGNED",ex.Message);
        var c=await f.Resolve(teamId:null);
        Assert.False(c.EligibleForTrip);
        Assert.Equal("NO_TEAM_MEMBERSHIP",c.ValidationCode);
    }

    [Fact]
    public async Task SITE_unassigned_requested_team_denied()
    {
        using var f=new Fixture();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Resolve(teamId:8));
        Assert.Contains("TRIP_CONTEXT_TEAM_NOT_ASSIGNED",ex.Message);
    }

    [Fact]
    public async Task SITE_ambiguous_primary_employment_deployments_denied()
    {
        using var f=new Fixture();
        f.Db.EmploymentDeploymentSiteAssignments.Add(
            new EmploymentDeploymentSiteAssignment
            {
                EmploymentDeploymentSiteAssignmentId=502,
                EmploymentId=9001,DeploymentSiteId=102,
                IsPrimary=true,EffectiveFrom=new DateOnly(2026,1,1)
            });
        await f.Db.SaveChangesAsync();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Resolve());
        Assert.Contains("AMBIGUOUS_PRIMARY_DEPLOYMENT_SITE",ex.Message);
    }

    [Fact]
    public async Task SITE_ambiguous_site_to_location_assignment_denied()
    {
        using var f=new Fixture();
        f.Db.DeploymentSiteLocationAssignments.Add(
            new DeploymentSiteLocationAssignment
            {
                DeploymentSiteLocationAssignmentId=603,
                DeploymentSiteId=101,LocationId=12345,
                EffectiveFrom=new DateOnly(2026,1,1)
            });
        await f.Db.SaveChangesAsync();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Resolve());
        Assert.Contains("AMBIGUOUS_DEPLOYMENT_LOCATION",ex.Message);
    }

    [Fact]
    public async Task SITE_ambiguous_team_center_assignment_denied()
    {
        using var f=new Fixture();
        f.Db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId=302,TeamId=7,CenterId=81,
            EffectiveFrom=new DateOnly(2026,1,1)
        });
        await f.Db.SaveChangesAsync();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Resolve());
        Assert.Contains("AMBIGUOUS_TEAM_CENTER",ex.Message);
    }

    [Fact]
    public async Task SITE_identity_without_employment_binding_denied()
    {
        using var f=new Fixture();
        f.Db.UserIdentityProfiles.Single().EmploymentId=null;
        await f.Db.SaveChangesAsync();
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Resolve());
        Assert.Contains("TRIP_CONTEXT_EMPLOYMENT_MISSING",ex.Message);
    }

    [Fact]
    public async Task SITE_context_HR_leave_day_disallows_trip()
    {
        using var f=new Fixture();
        f.Db.EmploymentStatusPeriods.Single().EmploymentStatus=EmploymentStatuses.Leave;
        await f.Db.SaveChangesAsync();
        var c=await f.Resolve();
        Assert.False(c.EligibleForTrip);
        Assert.Equal("EMPLOYMENT_NOT_ACTIVE",c.ValidationCode);
    }

    [Fact]
    public async Task SITE_invalid_existing_override_falls_back_to_valid_primary()
    {
        using var f=new Fixture();
        var c=await f.Resolve();
        var sites=V180TripPersistenceRules.ResolveDraftSites(
            c,null,null,existingStart:999,existingEnd:101);
        Assert.Equal(101,sites.Start);
        Assert.Equal(101,sites.End);
    }

    [Fact]
    public async Task SITE_ineligible_context_never_accepts_forged_official_endpoints()
    {
        using var f=new Fixture();
        f.Db.DeploymentSites.Single().IsActive=false;
        await f.Db.SaveChangesAsync();
        var c=await f.Resolve();
        Assert.False(c.EligibleForTrip);
        Assert.Throws<InvalidOperationException>(()=>
            V180TripPersistenceRules.ResolveDraftSites(c,101,101));
    }
}
