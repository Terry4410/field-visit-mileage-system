using System.Reflection;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class ReportExportAuditTests
{
    [Fact]
    public async Task Export_audit_records_report_and_count_without_filters_or_payload()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-export-audit-{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var access = DispatchProxy.Create<IV170AccessControl, UnusedAccessProxy>();
        var repository = new V160FinalRepository(db, access);
        var user = new CurrentUserDto(5, "E005", "Test", null, 7, null, null, ["visitor"]);
        var sensitiveKeyword = "PRIVATE_EMPLOYEE_OR_LOCATION_991";

        await repository.AuditExportAsync(user, "xlsx",
            new TripQueryRequest(Keyword: sensitiveKeyword), 2, default);
        await repository.AuditReportExportAsync(user, "PERSONNEL", 4, default);

        var rows = await db.AuditLogs.AsNoTracking().OrderBy(x => x.AuditLogId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("ReportExport", row.Action));
        Assert.DoesNotContain(sensitiveKeyword, rows[0].NewValues);
        Assert.Contains("TRIP_MILEAGE", rows[0].NewValues);
        Assert.Contains("2", rows[0].NewValues);
        Assert.Contains("PERSONNEL", rows[1].NewValues);
        Assert.Contains("\"count\":4", rows[1].NewValues);
    }

    public class UnusedAccessProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
}
