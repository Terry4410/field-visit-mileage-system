using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NPOI.XSSF.UserModel;
using Xunit;

namespace FieldVisit.Application.Tests;

// A test-only overlay on exact 03508fd. Uses a disposable local SQL Server 2022 DB.
public sealed class FinalAdminReportBytesSqlTests
{
    private const string Key = "final-admin-report-isolated-jwt-key-over-32-characters";
    [Fact]
    public async Task Ten_enabled_admin_exports_have_real_xlsx_content_and_policy_denials()
    {
        var raw = Environment.GetEnvironmentVariable("FV_FINAL_SQL");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("FV_FINAL_SQL_REQUIRED");
        var root = new SqlConnectionStringBuilder(raw) { Pooling = false, ConnectTimeout = 5 };
        Assert.Contains(root.DataSource, new[] { "localhost,1433", "127.0.0.1,1433" });
        Assert.Equal("master", root.InitialCatalog);
        var dbName = "FinalReports_" + Guid.NewGuid().ToString("N");
        await using var admin = new SqlConnection(root.ConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{dbName}]";
            await create.ExecuteNonQueryAsync();
        }
        root.InitialCatalog = dbName;
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(root.ConnectionString).Options);
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var from = BusinessTime.Today.AddDays(-20);
            var org = new Organization { OrganizationCode = "FIN", OrganizationName = "Final Synthetic", IsActive = true, CreatedAt = now };
            var other = new Organization { OrganizationCode = "OTH", OrganizationName = "Other Synthetic", IsActive = true, CreatedAt = now };
            db.AddRange(org, other);
            await db.SaveChangesAsync();
            var team = new Team { OrganizationId = org.OrganizationId, TeamCode = "FIN-T", TeamName = "Final Team", IsActive = true, CreatedAt = now };
            var role = new Role { RoleCode = "admin", RoleName = "Admin", IsActive = true, CreatedAt = now };
            db.AddRange(team, role);
            await db.SaveChangesAsync();
            var user = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "FIN-001", DisplayName = "Final Admin", IsActive = true, CreatedAt = now };
            db.Add(user);
            await db.SaveChangesAsync();
            db.AddRange(
                new UserIdentityProfile { UserId = user.UserId, UserCode = "FIN-001", UserType = "Internal", CreatedAt = now },
                new UserEmploymentPeriod { UserId = user.UserId, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = from, SourceType = "Synthetic", CreatedAt = now },
                new UserRole { UserId = user.UserId, RoleId = role.RoleId, AssignedAt = now },
                new UserRoleAssignment { UserId = user.UserId, RoleId = role.RoleId, EffectiveFrom = from, CreatedAt = now },
                new UserTeamAssignment { UserId = user.UserId, TeamId = team.TeamId, IsPrimary = true, EffectiveFrom = from, CreatedAt = now });
            var location = new Location { OrganizationId = org.OrganizationId, TeamId = team.TeamId,
                LocationCode = "FIN-L", LocationName = "Final Location", LocationType = "Official",
                ApprovalStatus = "Approved", GeocodingStatus = "Resolved", IsActive = true, CreatedAt = now };
            var project = new Project { OrganizationId = org.OrganizationId, TeamId = team.TeamId,
                ProjectCode = "FIN-P", ProjectName = "Final Project", IsActive = true, CreatedAt = now };
            var visit = new VisitType { VisitTypeCode = "FIN-V", VisitTypeName = "Final Visit", SortOrder = 1, IsActive = true, CreatedAt = now };
            var center = new Center { OrganizationId = org.OrganizationId, CenterCode = "FIN-C", CenterName = "Final Center", EffectiveFrom = from, IsActive = true };
            db.AddRange(location, project, visit, center);
            await db.SaveChangesAsync();
            var site = new DeploymentSite { CenterId = center.CenterId, SiteCode = "FIN-S", SiteName = "Final Site",
                EffectiveFrom = from, IsActive = true, CreatedAt = now };
            db.Add(site);
            await db.SaveChangesAsync();
            db.AddRange(
                new DeploymentSiteLocationAssignment { DeploymentSiteId = site.DeploymentSiteId, LocationId = location.LocationId,
                    EffectiveFrom = from, ChangeReason = "Final Synthetic", CreatedAt = now },
                new TeamCenterAssignment { TeamId = team.TeamId, CenterId = center.CenterId, EffectiveFrom = from, CreatedAt = now },
                new TeamDeploymentSiteAssignment { TeamId = team.TeamId, DeploymentSiteId = site.DeploymentSiteId,
                    EffectiveFrom = from, CreatedAt = now });
            await db.SaveChangesAsync();
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", root.ConnectionString);
                builder.UseSetting("Auth:Mode", "Demo");
                builder.UseSetting("Auth:JwtKey", Key);
                builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
                builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            void Token(string selected = "admin", int? claimedOrg = null)
            {
                var claims = new[] {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Role, selected),
                    new Claim("employee_no", "FIN-001"),
                    new Claim("organization_id", (claimedOrg ?? org.OrganizationId).ToString()),
                    new Claim("team_id", team.TeamId.ToString())
                };
                var jwt = new JwtSecurityToken(issuer: "FieldVisit.UAT", audience: "FieldVisit.UAT.Users", claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(10),
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
            }
            var cases = new (string Kind, string Sheet, string Expected)[] {
                ("personnel","查詢結果","FIN-001"), ("personnel-full","人員完整總覽","FIN-001"),
                ("roles","查詢結果","FIN-001"), ("teams","查詢結果","FIN-T"),
                ("members","查詢結果","FIN-001"), ("locations","查詢結果","FIN-L"),
                ("centers","查詢結果","FIN-C"), ("deployment-sites","查詢結果","FIN-S"),
                ("locations-official","地點主檔","FIN-L"), ("projects","查詢結果","FIN-P")
            };
            var output = Environment.GetEnvironmentVariable("FV_FINAL_REPORT_DIR") ?? throw new Exception("FV_FINAL_REPORT_DIR_REQUIRED");
            Directory.CreateDirectory(output);
            Token();
            foreach (var item in cases)
            {
                var suffix = item.Kind == "members" ? $"?teamId={team.TeamId}" : "";
                using var response = await client.GetAsync($"api/v1/admin/query-exports/{item.Kind}.xlsx{suffix}");
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{item.Kind} HTTP {(int)response.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
                Assert.True(bytes.Length > 400);
                Assert.Equal("PK", Encoding.ASCII.GetString(bytes.AsSpan(0, 2)));
                using var stream = new MemoryStream(bytes);
                using var workbook = new XSSFWorkbook(stream);
                var sheet = workbook.GetSheet(item.Sheet);
                Assert.NotNull(sheet);
                Assert.True(sheet.LastRowNum >= 1, $"{item.Kind} expected nonempty {item.Sheet}");
                var cells = string.Join("|", Enumerable.Range(0, sheet.LastRowNum + 1)
                    .SelectMany(i => sheet.GetRow(i)?.Cells.Select(c => c.ToString()) ?? []));
                Assert.Contains(item.Expected, cells);
                if (item.Kind == "locations-official")
                    Assert.Equal(7, workbook.NumberOfSheets);
                if (item.Kind == "personnel-full")
                    Assert.Equal(5, workbook.NumberOfSheets);
                File.WriteAllBytes(Path.Combine(output, item.Kind + ".xlsx"), bytes);
                Console.WriteLine($"REPORT_BYTES=PASS kind={item.Kind} bytes={bytes.Length} sheet={item.Sheet} rows={sheet.LastRowNum} expected={item.Expected}");
            }
            using (var held = await client.GetAsync("api/v1/admin/query-exports/visit-types.xlsx"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, held.StatusCode);
                Assert.Contains("REPORT_EXPORT_VISIT_TYPE_POLICY_HOLD", await held.Content.ReadAsStringAsync());
            }
            Console.WriteLine("VISIT_TYPE_REPORT_POLICY=DENIED expected_on_03508fd");
            var start = BusinessTime.Today.AddDays(-10);
            using var createdRateResponse = await client.PostAsJsonAsync("api/v1/mileage-rate-rules",
                new CreateMileageRateRequest("Final Car", "CAR", 2.5m, start, null));
            Assert.Equal(HttpStatusCode.OK, createdRateResponse.StatusCode);
            var rate = await createdRateResponse.Content.ReadFromJsonAsync<MileageRateDto>();
            Assert.NotNull(rate);
            Assert.Equal("Car", rate.VehicleType);
            using (var overlap = await client.PostAsJsonAsync("api/v1/mileage-rate-rules",
                new CreateMileageRateRequest("Overlap", "car", 9m, start.AddDays(1), null)))
                Assert.False(overlap.IsSuccessStatusCode);
            Assert.Equal(1, await db.MileageRateRules.CountAsync(x => x.OrganizationId == org.OrganizationId));
            var approved = new VisitTrip { TripNo = "FIN-APPROVED-RATE", UserId = user.UserId, CreatedByUserId = user.UserId,
                OrganizationId = org.OrganizationId, TeamId = team.TeamId, VisitDate = BusinessTime.Today,
                VehicleType = "Car", Status = "Approved", CreatedAt = now };
            db.Add(approved);
            await db.SaveChangesAsync();
            db.Add(new MileageCalculation { VisitTripId = approved.VisitTripId, MileageRateRuleId = rate.MileageRateRuleId,
                ApprovedDistanceKm = 10m, RatePerKmSnapshot = 2.5m, ApprovedAmount = 25m, CreatedAt = now });
            await db.SaveChangesAsync();
            using (var impact = await client.GetAsync($"api/v1/mileage-rate-rules/impact?effectiveFrom={start:yyyy-MM-dd}&vehicleType=car"))
            {
                Assert.Equal(HttpStatusCode.OK, impact.StatusCode);
                var body = await impact.Content.ReadFromJsonAsync<MileageRateImpactDto>();
                Assert.Equal(1, body?.ApprovedTripCount);
            }
            using (var noAck = await client.PutAsJsonAsync($"api/v1/mileage-rate-rules/{rate.MileageRateRuleId}",
                new UpdateMileageRateRequest("Final Car Updated", "cAr", 3m, start, null, true)))
                Assert.False(noAck.IsSuccessStatusCode);
            using (var changed = await client.PutAsJsonAsync($"api/v1/mileage-rate-rules/{rate.MileageRateRuleId}",
                new UpdateMileageRateRequest("Final Car Updated", "cAr", 3m, start, null, true, true)))
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.Equal(25m, await db.MileageCalculations.AsNoTracking()
                .Where(x => x.VisitTripId == approved.VisitTripId).Select(x => x.ApprovedAmount).SingleAsync());
            using (var noAck = await client.DeleteAsync($"api/v1/mileage-rate-rules/{rate.MileageRateRuleId}"))
                Assert.False(noAck.IsSuccessStatusCode);
            using (var disabled = await client.DeleteAsync($"api/v1/mileage-rate-rules/{rate.MileageRateRuleId}?acknowledgeHistoricalImpact=true"))
                Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
            Assert.False(await db.MileageRateRules.AsNoTracking()
                .Where(x => x.MileageRateRuleId == rate.MileageRateRuleId).Select(x => x.IsActive).SingleAsync());
            Assert.Equal(25m, await db.MileageCalculations.AsNoTracking()
                .Where(x => x.VisitTripId == approved.VisitTripId).Select(x => x.ApprovedAmount).SingleAsync());
            Console.WriteLine("RATE_HTTP_SQL=PASS create canonical=Car overlap=REJECT history_ack=REQUIRED update=PASS deactivate=PASS approved_snapshot=25.00_UNCHANGED");
            using var projectResponse = await client.PostAsJsonAsync("api/v1/projects",
                new SaveProjectRequest(team.TeamId, "FIN-P2", "New Project", "Synthetic", "List", null, null, true));
            Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
            var newProject = await projectResponse.Content.ReadFromJsonAsync<ProjectDto>();
            Assert.NotNull(newProject);
            using (var changed = await client.PutAsJsonAsync($"api/v1/projects/{newProject.ProjectId}",
                new SaveProjectRequest(team.TeamId, "FIN-P2", "Updated Project", "Synthetic", "List", null, null, true)))
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            using (var disabled = await client.DeleteAsync($"api/v1/projects/{newProject.ProjectId}"))
                Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
            Assert.False(await db.Projects.AsNoTracking()
                .Where(x => x.ProjectId == newProject.ProjectId).Select(x => x.IsActive).SingleAsync());
            using var visitResponse = await client.PostAsJsonAsync("api/v1/visit-types",
                new SaveVisitTypeRequest("FIN-V2", "New Visit", "Synthetic", 2, true));
            Assert.Equal(HttpStatusCode.OK, visitResponse.StatusCode);
            var newVisit = await visitResponse.Content.ReadFromJsonAsync<VisitTypeDto>();
            Assert.NotNull(newVisit);
            using (var changed = await client.PutAsJsonAsync($"api/v1/visit-types/{newVisit.VisitTypeId}",
                new SaveVisitTypeRequest("FIN-V2", "Updated Visit", "Synthetic", 2, true)))
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            using (var disabled = await client.DeleteAsync($"api/v1/visit-types/{newVisit.VisitTypeId}"))
                Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
            Assert.False(await db.VisitTypes.AsNoTracking()
                .Where(x => x.VisitTypeId == newVisit.VisitTypeId).Select(x => x.IsActive).SingleAsync());
            Console.WriteLine("MASTER_HTTP_SQL=PASS project create/update/deactivate; visit-type create/update/deactivate");
            Token("visitor");
            foreach (var item in cases)
            {
                using var response = await client.GetAsync($"api/v1/admin/query-exports/{item.Kind}.xlsx");
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            Token("admin", other.OrganizationId);
            using (var response = await client.GetAsync("api/v1/admin/query-exports/teams.xlsx"))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Console.WriteLine("REPORT_DENY=PASS visitor=10/10 enabled plus visit-type-policy forgedOrganization=FORBIDDEN");
        }
        finally
        {
            root.InitialCatalog = "master";
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
