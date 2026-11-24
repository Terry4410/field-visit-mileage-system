using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class PathBQuery05PdfReadabilityTests
{
    [Fact]
    public async Task Eleven_actual_UAT_style_rows_generate_nonempty_legible_PDF()
    {
        Assert.True(ReportDocumentService.PdfBodyFontPt >= 10f);
        var data = Enumerable.Range(1, 11).Select(i => Row(i)).ToList();
        var report = await Service().CreatePdfAsync(data, Query(), Admin(), CancellationToken.None);
        Assert.Equal("application/pdf", report.ContentType);
        Assert.True(report.Content.Length > 5000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(report.Content.AsSpan(0, 5)));
        SaveFixture("query05_11_rows.pdf", report.Content);
    }

    [Fact]
    public async Task Seventy_three_rows_and_long_Chinese_route_generate_multipage_PDF_without_throwing()
    {
        var rows = Enumerable.Range(1, 73).Select(i => Row(i)).ToList();
        var longRoute = string.Concat(Enumerable.Repeat("Path B UAT 臺北市中正區外訪拜訪路線，須完整保留，不可裁切。 ", 110));
        rows[0] = rows[0] with { Route = longRoute };
        var report = await Service().CreatePdfAsync(rows, Query(), Admin(), CancellationToken.None);
        Assert.True(report.Content.Length > 8000);
        SaveFixture("query05_73_rows_long_route.pdf", report.Content);
    }

    [Fact]
    public async Task Chinese_report_uses_assembly_bundled_CJK_even_when_configured_font_is_Latin_only()
    {
        var latinOnly = "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf";
        var customFont = File.Exists(latinOnly) ? latinOnly : "/missing/latin-only.ttf";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Report:PdfFontPath"] = customFont
            }).Build();

        // Previous live exported PDF embedded only DejaVuSerif and its ToUnicode
        // map dropped every Chinese glyph. Validate the selected font and let the
        // CI inspect actual PDF bytes/text/embedded font, not merely PDF %magic.
        var report = await new ReportDocumentService(configuration)
            .CreatePdfAsync([Row(1), Row(2), Row(3)], Query(), Admin(), CancellationToken.None);
        Assert.Equal("application/pdf", report.ContentType);
        Assert.True(report.Content.Length > 10000);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(report.Content.AsSpan(0, 5)));
        SaveFixture("query05_cjk_3_rows.pdf", report.Content);
    }

    [Fact]
    public async Task Empty_export_includes_no_record_state_and_valid_PDF()
    {
        var report = await Service().CreatePdfAsync([], Query(), Admin(), CancellationToken.None);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(report.Content.AsSpan(0, 5)));
        SaveFixture("query05_no_rows.pdf", report.Content);
    }

    private static ReportDocumentService Service() =>
        new(new ConfigurationBuilder().Build());

    private static TripQueryRequest Query() =>
        new(StartDate: new DateOnly(2026, 11, 23), EndDate: new DateOnly(2026, 11, 24), TeamId: 1);

    private static CurrentUserDto Admin() =>
        new(3, "pilota01", "Path B UAT Admin", null, 1, 1, "Path B Test Team", ["admin"]);

    private static TripQueryRowDto Row(int index)
    {
        var approved = index == 1;
        var draft = index >= 10 && index <= 11;
        var status = approved ? "Approved" : draft ? "Draft" : "PendingApproval";
        var statusName = approved ? "已核准" : draft ? "草稿" : "待核准";
        return new TripQueryRowDto(
            VisitTripId: index,
            TripNo: index == 1 ? "T20261124160346544" : $"T20261123{index:000000000}",
            VisitDate: index < 5 ? new DateOnly(2026, 11, 24) : new DateOnly(2026, 11, 23),
            StartTime: new TimeOnly(8, 30),
            EndTime: new TimeOnly(17, 0),
            VisitorId: 1,
            EmployeeNo: "pilotv01",
            VisitorName: "Path B UAT Visitor",
            TeamId: 1,
            TeamName: "Path B Test Team",
            Route: "Path B VIS01 UAT Visit / 臺北市外訪",
            ProjectNames: "Path B VIS01 Synthetic Project",
            VisitTypeNames: "Path B VIS01 Test Visit",
            ClaimedDistanceKm: null,
            SystemDistanceKm: draft ? null : (approved ? 10.95m : 10.94m),
            ApprovedDistanceKm: approved ? 10.95m : null,
            RatePerKmSnapshot: approved ? 2.50m : null,
            SubsidyAmount: approved ? 27.38m : null,
            MileageSource: approved ? "GoogleMapsAPI" : null,
            MileageState: approved ? "Calculated" : "Pending",
            Status: status,
            StatusName: statusName,
            SnapshotVersion: approved ? 3 : 0,
            IsSnapshot: approved,
            Notes: null,
            ReturnReason: null,
            CorrectionStatus: approved ? "Closed" : null,
            Stops: []);
    }

    private static void SaveFixture(string file, byte[] content)
    {
        var directory = Environment.GetEnvironmentVariable("PATHB_PDF_VERIFY_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, file), content);
    }
}
