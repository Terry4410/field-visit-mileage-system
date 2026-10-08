using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180SafeDeleteGovernanceTests
{
    [Fact]
    public void Safe_delete_is_impact_gated_and_has_no_database_migration()
    {
        var service=Read("backend/src/FieldVisit.Infrastructure/V180SafeDeleteService.cs");
        var controller=Read("backend/src/FieldVisit.Api/Controllers/V180SafeDeleteController.cs");
        Assert.Contains("PersonImpactAsync",service);
        Assert.Contains("TeamImpactAsync",service);
        Assert.Contains("ProjectImpactAsync",service);
        Assert.Contains("VisitTypeImpactAsync",service);
        Assert.Contains("MileageRateImpactAsync",service);
        Assert.Contains("CenterImpactAsync",service);
        Assert.Contains("SiteImpactAsync",service);
        Assert.Contains("CenterPermanentDelete",service);
        Assert.Contains("DeploymentSitePermanentDelete",service);
        Assert.Contains("IsolationLevel.Serializable",service);
        Assert.Contains("StartDeploymentSiteIdSnapshot",service);
        Assert.Contains("CenterIdSnapshot",service);
        Assert.Contains("if(!impact.CanDelete)",service);
        Assert.Contains("PersonPermanentDelete",service);
        Assert.Contains("TeamPermanentDelete",service);
        Assert.Contains("ProjectPermanentDelete",service);
        Assert.Contains("VisitTypePermanentDelete",service);
        Assert.Contains("MileageRatePermanentDelete",service);
        Assert.Contains("delete-impact",controller);
        Assert.Contains("/permanent",controller);
        var root=Root();
        Assert.False(Directory.Exists(Path.Combine(root,"database","migrations","1800_011")));
    }

    [Fact]
    public void Person_delete_blocks_business_history_but_removes_configuration_only_rows()
    {
        var service=Read("backend/src/FieldVisit.Infrastructure/V180SafeDeleteService.cs");
        Assert.Contains("db.VisitTrips.CountAsync",service);
        Assert.Contains("db.VisitTripSnapshots.CountAsync",service);
        Assert.Contains("db.CorrectionRequests.CountAsync",service);
        Assert.Contains("db.AuditLogs.CountAsync",service);
        Assert.Contains("db.UserRoleAssignments.Where",service);
        Assert.Contains("db.TeamMemberships.Where",service);
        Assert.Contains("db.EmploymentStatusPeriods.Where",service);
        Assert.Contains("db.UserIdentityProfiles.Where",service);
        Assert.Contains("db.Employments.Where",service);
        Assert.Contains("db.Persons.Where",service);
    }

    [Fact]
    public void Global_mileage_rates_are_read_only_in_admin_ui()
    {
        var admin=Read("frontend/src/pages/AdminPage.tsx");
        Assert.Contains("const isSharedRate=(r:MileageRate)=>r.organizationId==null;",admin);
        Assert.Contains("系統共用（唯讀）",admin);
        Assert.Contains("系統共用費率為唯讀",admin);
    }

    [Fact]
    public void Safe_delete_actions_are_on_their_single_owner_workspaces()
    {
        var people=Read("frontend/src/pages/PeopleAndAccessPage.tsx");
        var teams=Read("frontend/src/pages/TeamManagementPage.tsx");
        var projects=Read("frontend/src/pages/ProjectManagementPage.tsx");
        var admin=Read("frontend/src/pages/AdminPage.tsx");
        var sites=Read("frontend/src/components/OfficialSiteMaintenance.tsx");
        Assert.Contains("safeDelete",sites);
        Assert.Contains("/delete-impact",sites);
        Assert.Contains("/permanent",sites);
        Assert.Contains("/delete-impact",people);
        Assert.Contains("/permanent",people);
        Assert.Contains("/delete-impact",teams);
        Assert.Contains("/permanent",teams);
        Assert.Contains("/delete-impact",projects);
        Assert.Contains("/permanent",projects);
        Assert.Contains("/admin/visit-types/",admin);
        Assert.Contains("/admin/mileage-rate-rules/",admin);
        Assert.Contains("deleteType",admin);
        Assert.Contains("deleteRate",admin);
    }

    private static string Read(string path)=>
        File.ReadAllText(Path.Combine(Root(),path.Replace('/',Path.DirectorySeparatorChar)));

    private static string Root()
    {
        DirectoryInfo? current=new(AppContext.BaseDirectory);
        while(current is not null)
        {
            if(Directory.Exists(Path.Combine(current.FullName,".git")))return current.FullName;
            current=current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
