using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Real SQL Server 2022 / V180TripContextReader with independently created
/// random disposable database. No Azure SQL, migration or UAT deployment.
/// THIS IS NOT B4 business acceptance; no Approve/Apply is exercised.
/// </summary>
public sealed class B4DatedSiteSqlServerTests
{
    private static readonly DateOnly VisitDate = new(2026,10,12);
    private static readonly DateOnly Start = new(2026,1,1);

    private sealed class SqlSiteDatabase : IAsyncDisposable
    {
        private readonly string _master;
        private readonly string _name;
        public readonly string Connection;
        public int Org,UserId,TeamId,SiteId,LocationId,EmploymentId;

        private SqlSiteDatabase(string master,string name,string connection)
        { _master=master;_name=name;Connection=connection; }

        private static SqlConnectionStringBuilder RequireLocal()
        {
            var raw=Environment.GetEnvironmentVariable("B3_SQL_TEST_CONNECTION")
                ??throw new InvalidOperationException("B4_SITE_SQL_LOCAL_CONNECTION_REQUIRED");
            var b=new SqlConnectionStringBuilder(raw);
            if(b.DataSource is not ("127.0.0.1,14335" or "localhost,14335")
               || !b.InitialCatalog.Equals("master",StringComparison.OrdinalIgnoreCase)
               || !b.UserID.Equals("sa",StringComparison.OrdinalIgnoreCase)
               || b.IntegratedSecurity)
                throw new InvalidOperationException("B4_SITE_SQL_LOCAL_ONLY_14335_MASTER");
            return b;
        }

        public static async Task<SqlSiteDatabase> CreateAsync()
        {
            var b=RequireLocal();
            var master=b.ConnectionString;
            var name="B4SiteSql_"+Guid.NewGuid().ToString("N");
            await using(var conn=new SqlConnection(master))
            {
                await conn.OpenAsync();
                await using var cmd=conn.CreateCommand();
                cmd.CommandText=$"CREATE DATABASE [{name}]";
                await cmd.ExecuteNonQueryAsync();
            }
            b.InitialCatalog=name;
            var fixture=new SqlSiteDatabase(master,name,b.ConnectionString);
            try
            {
                await using var db=fixture.Db();
                await db.Database.EnsureCreatedAsync();
                await fixture.SeedAsync(db);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(Connection).Options);

        public CurrentUserDto Actor()=>new(
            UserId,"SQL-TEST-VISITOR","Isolated SQL Visitor",null,
            Org,TeamId,"SQL Team",["visitor"]);

        public async Task<V180TripContextDto> ResolveAsync(int? team=null)
        {
            await using var db=Db();
            return await new V180TripContextReader(db).ResolveAsync(
                Actor(),VisitDate,team??TeamId,CancellationToken.None);
        }

        private async Task SeedAsync(AppDbContext db)
        {
            var org=new Organization
            {
                OrganizationCode="B4-ORG-"+Guid.NewGuid().ToString("N"),
                OrganizationName="B4 SQL isolated",IsActive=true,
                EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
            };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            Org=org.OrganizationId;

            var user=new User
            {
                OrganizationId=Org,EmployeeNo="SQL-VIS-"+Guid.NewGuid().ToString("N"),
                DisplayName="SQL Visitor",IsActive=true,CreatedAt=DateTime.UtcNow
            };
            var team=new Team
            {
                OrganizationId=Org,TeamCode="B4-T7",TeamName="Team Seven",
                IsActive=true,EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
            };
            var center=new Center
            {
                OrganizationId=Org,CenterCode="B4-C1",
                CenterName="Center One",EffectiveFrom=Start,IsActive=true
            };
            var location=new Location
            {
                OrganizationId=Org,LocationCode="B4-L1",
                LocationName="Office Location",LocationType="Official",
                IsActive=true,ApprovalStatus="Approved",
                CreatedAt=DateTime.UtcNow
            };
            var employment=new Employment
            {
                PersonId=1,OrganizationId=Org,
                EmployeeNo="SQL-VISITOR",SourceType="DisposableFixture"
            };
            db.AddRange(user,team,center,location,employment);
            await db.SaveChangesAsync();
            UserId=user.UserId;TeamId=team.TeamId;
            LocationId=location.LocationId;EmploymentId=checked((int)employment.EmploymentId);

            user.TeamId=TeamId;
            var profile=new UserIdentityProfile
            {
                UserId=UserId,EmploymentId=employment.EmploymentId,
                UserType=UserTypes.Internal,UserCode="SQL-VISITOR"
            };
            var status=new EmploymentStatusPeriod
            {
                EmploymentId=employment.EmploymentId,
                EmploymentStatus=EmploymentStatuses.Active,
                EffectiveFrom=Start,SourceType="DisposableFixture"
            };
            var membership=new TeamMembership
            {
                EmploymentId=employment.EmploymentId,TeamId=TeamId,
                IsPrimary=true,EffectiveFrom=Start
            };
            var teamCenter=new TeamCenterAssignment
            {
                TeamId=TeamId,CenterId=center.CenterId,
                EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
            };
            var site=new DeploymentSite
            {
                CenterId=center.CenterId,SiteCode="B4-S101",
                SiteName="Official Site",EffectiveFrom=Start,
                IsActive=true,CreatedAt=DateTime.UtcNow
            };
            db.AddRange(profile,status,membership,teamCenter,site);
            await db.SaveChangesAsync();
            SiteId=site.DeploymentSiteId;

            db.AddRange(
                new EmploymentDeploymentSiteAssignment
                {
                    EmploymentId=employment.EmploymentId,
                    DeploymentSiteId=SiteId,IsPrimary=true,
                    EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
                },
                new TeamDeploymentSiteAssignment
                {
                    TeamId=TeamId,DeploymentSiteId=SiteId,
                    EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
                },
                new DeploymentSiteLocationAssignment
                {
                    DeploymentSiteId=SiteId,LocationId=LocationId,
                    EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if(!_name.StartsWith("B4SiteSql_",StringComparison.Ordinal))return;
            SqlConnection.ClearAllPools();
            await using var conn=new SqlConnection(_master);
            await conn.OpenAsync();
            await using var cmd=conn.CreateCommand();
            cmd.CommandText=$"IF DB_ID(N'{_name}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" +
                $"DROP DATABASE [{_name}]; END";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SQL_SITE_valid_primary_and_cross_org_picker_isolation()
    {
        await using var f=await SqlSiteDatabase.CreateAsync();
        await using(var db=f.Db())
        {
            var foreign=new Organization
            {
                OrganizationCode="B4-FOREIGN-"+Guid.NewGuid().ToString("N"),
                OrganizationName="Foreign",IsActive=true,EffectiveFrom=Start,
                CreatedAt=DateTime.UtcNow
            };
            db.Organizations.Add(foreign);
            await db.SaveChangesAsync();
            var c=new Center{OrganizationId=foreign.OrganizationId,
                CenterCode="FOREIGN",CenterName="Other center",
                EffectiveFrom=Start,IsActive=true};
            db.Centers.Add(c);
            await db.SaveChangesAsync();
            var site=new DeploymentSite{CenterId=c.CenterId,
                SiteCode="FOREIGN-S",SiteName="Other site",
                EffectiveFrom=Start,IsActive=true};
            var location=new Location{OrganizationId=foreign.OrganizationId,
                LocationCode="FOREIGN-L",LocationName="Other Location",
                IsActive=true,ApprovalStatus="Approved",
                CreatedAt=DateTime.UtcNow};
            db.AddRange(site,location);
            await db.SaveChangesAsync();
            db.DeploymentSiteLocationAssignments.Add(
                new DeploymentSiteLocationAssignment{
                    DeploymentSiteId=site.DeploymentSiteId,
                    LocationId=location.LocationId,EffectiveFrom=Start});
            await db.SaveChangesAsync();
        }
        var result=await f.ResolveAsync();
        Assert.True(result.EligibleForTrip);
        Assert.Equal(f.SiteId,result.PrimaryDeploymentSiteId);
        Assert.Single(result.EligibleDeploymentSites);
        Assert.Single(result.OfficialDeploymentSites!);
    }

    [Fact]
    public async Task SQL_SITE_disabled_site_cannot_create_draft_after_stale_picker()
    {
        await using var f=await SqlSiteDatabase.CreateAsync();
        var before=await f.ResolveAsync();
        Assert.True(before.EligibleForTrip);
        await using(var db=f.Db())
        {
            var site=await db.DeploymentSites.SingleAsync(x=>x.DeploymentSiteId==f.SiteId);
            site.IsActive=false;
            await db.SaveChangesAsync();
        }
        var after=await f.ResolveAsync();
        Assert.False(after.EligibleForTrip);
        Assert.Throws<InvalidOperationException>(()=>
            V180TripPersistenceRules.ResolveDraftSites(after,f.SiteId,f.SiteId));
        await using var verify=f.Db();
        Assert.False(await verify.VisitTrips.AnyAsync());
        Assert.False(await verify.AuditLogs.AnyAsync());
    }

    [Fact]
    public async Task SQL_SITE_expired_membership_rejects_explicit_team()
    {
        await using var f=await SqlSiteDatabase.CreateAsync();
        await using(var db=f.Db())
        {
            var membership=await db.TeamMemberships.SingleAsync(
                x=>x.EmploymentId==f.EmploymentId);
            membership.EffectiveTo=VisitDate.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(
            ()=>f.ResolveAsync());
        Assert.Contains("TRIP_CONTEXT_TEAM_NOT_ASSIGNED",ex.Message);
    }

    [Fact]
    public async Task SQL_SITE_duplicate_effective_location_mapping_fails_closed()
    {
        await using var f=await SqlSiteDatabase.CreateAsync();
        await using(var db=f.Db())
        {
            db.DeploymentSiteLocationAssignments.Add(
                new DeploymentSiteLocationAssignment
                {
                    DeploymentSiteId=f.SiteId,LocationId=f.LocationId,
                    EffectiveFrom=Start,CreatedAt=DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.ResolveAsync());
        Assert.Contains("AMBIGUOUS_DEPLOYMENT_LOCATION",ex.Message);
    }

    [Fact]
    public async Task SQL_SITE_expired_location_link_removes_official_picker_entry()
    {
        await using var f=await SqlSiteDatabase.CreateAsync();
        await using(var db=f.Db())
        {
            var link=await db.DeploymentSiteLocationAssignments.SingleAsync(
                x=>x.DeploymentSiteId==f.SiteId);
            link.EffectiveTo=VisitDate.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var result=await f.ResolveAsync();
        Assert.False(result.EligibleForTrip);
        Assert.Empty(result.OfficialDeploymentSites!);
    }

    [Fact]
    public async Task SQL_SITE_rowversion_rejects_stale_concurrent_site_edit()
    {
        await using var f=await SqlSiteDatabase.CreateAsync();
        await using var first=f.Db();
        await using var stale=f.Db();
        var current=await first.DeploymentSites.SingleAsync(
            x=>x.DeploymentSiteId==f.SiteId);
        var old=await stale.DeploymentSites.SingleAsync(
            x=>x.DeploymentSiteId==f.SiteId);
        current.IsActive=false;
        await first.SaveChangesAsync();
        old.SiteName="Stale overwritten name";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            ()=>stale.SaveChangesAsync());
        await using var check=f.Db();
        var persisted=await check.DeploymentSites.AsNoTracking().SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.NotEqual("Stale overwritten name",persisted.SiteName);
    }
}
