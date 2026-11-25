using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

// Focused real SQL workbook preview/confirm. This is an EF-shaped disposable DB,
// not a corporate clean-build or shared SQL permission claim.
public sealed class FinalAdminBulkSqlTests
{
    [Fact]
    public async Task Three_open_excel_imports_preview_confirm_and_read_back_sql()
    {
        var raw = Environment.GetEnvironmentVariable("FV_FINAL_SQL") ?? throw new Exception("FV_FINAL_SQL_REQUIRED");
        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        Assert.Contains(root.DataSource, new[] { "localhost,1433", "127.0.0.1,1433" });
        Assert.Equal("master", root.InitialCatalog);
        var name = "FinalBulk_" + Guid.NewGuid().ToString("N");
        await using var adminSql = new SqlConnection(root.ConnectionString);
        await adminSql.OpenAsync();
        await using(var create = adminSql.CreateCommand())
        { create.CommandText = $"CREATE DATABASE [{name}]"; await create.ExecuteNonQueryAsync(); }
        root.InitialCatalog = name;
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(root.ConnectionString).Options);
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var from = BusinessTime.Today.AddDays(-10);
            var org = new Organization { OrganizationCode="BLK", OrganizationName="Bulk Synthetic", IsActive=true, CreatedAt=now };
            db.Add(org); await db.SaveChangesAsync();
            var firstTeam = new Team { OrganizationId=org.OrganizationId, TeamCode="BLK-T1", TeamName="Bulk One", IsActive=true, CreatedAt=now };
            var secondTeam = new Team { OrganizationId=org.OrganizationId, TeamCode="BLK-T2", TeamName="Bulk Two", IsActive=true, CreatedAt=now };
            db.AddRange(firstTeam, secondTeam); await db.SaveChangesAsync();
            var user = new User { OrganizationId=org.OrganizationId, TeamId=firstTeam.TeamId,
                EmployeeNo="BLK-001", DisplayName="Bulk Before", IsActive=true, CreatedAt=now };
            db.Add(user); await db.SaveChangesAsync();
            var person = new Person { DisplayName=user.DisplayName, LegacyUserId=user.UserId, CreatedAt=now };
            db.Add(person); await db.SaveChangesAsync();
            var employment = new Employment { PersonId=person.PersonId, OrganizationId=org.OrganizationId,
                EmployeeNo="BLK-001", LegacyUserId=user.UserId, SourceType="Synthetic" };
            db.Add(employment); await db.SaveChangesAsync();
            db.AddRange(
                new UserIdentityProfile { UserId=user.UserId, EmploymentId=employment.EmploymentId,
                    UserCode="BLK-U1", UserType="Internal", CreatedAt=now },
                new TeamMembership { EmploymentId=employment.EmploymentId, TeamId=firstTeam.TeamId,
                    IsPrimary=true, EffectiveFrom=from },
                new UserTeamAssignment { UserId=user.UserId, TeamId=firstTeam.TeamId,
                    IsPrimary=true, EffectiveFrom=from, CreatedAt=now });
            await db.SaveChangesAsync();
            var actor = new CurrentUserDto(user.UserId, "BLK-U1", user.DisplayName, null, org.OrganizationId, firstTeam.TeamId, null, ["admin"]);
            var other = actor with { OrganizationId = org.OrganizationId + 99 };
            var personnelBook = Book(
                ("Personnel", [
                    ["UserCode","EmployeeNo","DisplayName","Email","HireDate","TerminationDate"],
                    ["BLK-U1","BLK-001","Bulk After","bulk@example.test","",""]
                ]),
                ("EmploymentStatus", [
                    ["UserCode","Status","EffectiveFrom","EffectiveTo"]
                ]));
            var personnelService = new V180PersonnelBulkService(db);
            var personnelPreview = await personnelService.PreviewAsync(actor, personnelBook, default);
            Assert.Equal(1, personnelPreview.ValidCount); Assert.Equal(0, personnelPreview.ErrorCount);
            Assert.Equal("Bulk Before", await db.Users.AsNoTracking().Where(x=>x.UserId==user.UserId).Select(x=>x.DisplayName).SingleAsync());
            var personnelResult = await personnelService.ConfirmAsync(actor, personnelBook, default);
            Assert.Equal(1, personnelResult.AppliedCount);
            Assert.Equal("Bulk After", await db.Users.AsNoTracking().Where(x=>x.UserId==user.UserId).Select(x=>x.DisplayName).SingleAsync());
            Console.WriteLine("PERSONNEL_BULK_SQL=PASS preview_no_write confirm=1 SQL_displayName=Bulk After");
            var membershipBook = Book(("TeamMemberships", [
                ["EmployeeNo","TeamCode","IsPrimary","EffectiveFrom","EffectiveTo"],
                ["BLK-001","BLK-T2","N",BusinessTime.Today.ToString("yyyy-MM-dd"),""]
            ]));
            var teamService = new V180TeamMembershipBulkService(db);
            var teamPreview = await teamService.PreviewTeamMembershipAsync(actor,membershipBook,default);
            Assert.Equal(1,teamPreview.ValidCount);Assert.Equal(0,teamPreview.ErrorCount);
            Assert.False(await db.TeamMemberships.AsNoTracking().AnyAsync(x=>x.EmploymentId==employment.EmploymentId&&x.TeamId==secondTeam.TeamId));
            var teamResult = await teamService.ConfirmTeamMembershipAsync(actor,membershipBook,default);
            Assert.Equal(1,teamResult.AppliedCount);
            Assert.True(await db.TeamMemberships.AsNoTracking().AnyAsync(x=>x.EmploymentId==employment.EmploymentId&&x.TeamId==secondTeam.TeamId));
            Console.WriteLine("TEAM_MEMBERSHIP_BULK_SQL=PASS preview_no_write confirm=1 SQL_secondary_team=BLK-T2");
            var masterBook = Book(
                ("EmploymentStatus",[["EmployeeNo","Status","EffectiveFrom","EffectiveTo"]]),
                ("Centers",[
                    ["CenterCode","CenterName","EffectiveFrom","EffectiveTo","IsActive"],
                    ["BLK-C","Bulk Center",from.ToString("yyyy-MM-dd"),"","true"]
                ]),
                ("TeamCenters",[["TeamCode","CenterCode","EffectiveFrom","EffectiveTo"]]),
                ("DeploymentSites",[["CenterCode","SiteCode","SiteName","LocationCode","EffectiveFrom","EffectiveTo","IsActive"]]),
                ("TeamSites",[["TeamCode","SiteCode","EffectiveFrom","EffectiveTo"]]),
                ("EmploymentSites",[["EmployeeNo","SiteCode","IsPrimary","EffectiveFrom","EffectiveTo"]])
            );
            var masterService = new V180MasterDataBulkWorkbookService(db);
            var masterPreview = await masterService.PreviewAsync(actor,masterBook,default);
            Assert.Equal(1,masterPreview.ValidCount);Assert.Equal(0,masterPreview.ErrorCount);
            Assert.False(await db.Centers.AsNoTracking().AnyAsync(x=>x.CenterCode=="BLK-C"));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>masterService.ConfirmAsync(other,masterPreview.ImportBatchId,default));
            var masterResult = await masterService.ConfirmAsync(actor,masterPreview.ImportBatchId,default);
            Assert.Equal(1,masterResult.Created);
            Assert.True(await db.Centers.AsNoTracking().AnyAsync(x=>x.OrganizationId==org.OrganizationId&&x.CenterCode=="BLK-C"));
            Console.WriteLine("MASTER_BULK_SQL=PASS preview_no_write cross_org_confirm=DENIED confirm=1 SQL_center=BLK-C");
        }
        finally
        {
            root.InitialCatalog="master";
            await using var drop=adminSql.CreateCommand();
            drop.CommandText=$"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await drop.ExecuteNonQueryAsync();
        }
    }
    private static byte[] Book(params (string Name,string[][] Rows)[] specs)
    {
        using var stream=new MemoryStream();
        using(var doc=SpreadsheetDocument.Create(stream,SpreadsheetDocumentType.Workbook,true))
        {
            var part=doc.AddWorkbookPart();part.Workbook=new Workbook();
            var sheets=part.Workbook.AppendChild(new Sheets());uint id=1;
            foreach(var spec in specs)
            {
                var worksheetPart=part.AddNewPart<WorksheetPart>();
                var data=new SheetData();
                foreach(var values in spec.Rows)
                {
                    var row=new Row();
                    foreach(var value in values)
                        row.Append(new Cell { DataType=CellValues.InlineString, InlineString=new InlineString(new Text(value)) });
                    data.Append(row);
                }
                worksheetPart.Worksheet=new Worksheet(data);
                sheets.Append(new Sheet { Id=part.GetIdOfPart(worksheetPart), SheetId=id++, Name=spec.Name });
            }
            part.Workbook.Save();
        }
        return stream.ToArray();
    }
}
