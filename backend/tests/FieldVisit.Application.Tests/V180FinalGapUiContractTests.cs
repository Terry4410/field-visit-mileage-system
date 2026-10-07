using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180FinalGapUiContractTests
{
    [Fact]
    public void Unified_query_uses_explicit_mileage_source_language()
    {
        var source = Read("frontend/src/pages/UnifiedQueryPage.tsx");
        Assert.Contains("里程來源", source);
        Assert.Contains("來源里程", source);
        Assert.Contains("核定里程", source);
        Assert.DoesNotContain("<th>自算</th>", source);
        Assert.DoesNotContain("<th>系統</th>", source);
        Assert.DoesNotContain("里程：</strong>自算", source);
    }

    [Fact]
    public void Visitor_manual_fallback_is_available_only_after_google_failure()
    {
        var source = Read("frontend/src/pages/VisitorPage.tsx");
        Assert.Contains("Google Maps API 成功時不使用人工備援", source);
        Assert.Contains("只有 Google Maps API 無法取得可用里程時，才使用人工備援", source);
        Assert.DoesNotContain("不合理時再填人工里程", source);
        Assert.DoesNotContain("Google 路線與實際行程不符，可填寫人工里程", source);
    }

    [Fact]
    public void Official_site_advanced_maintenance_is_a_location_subpage_not_page_bottom_content()
    {
        var app = Read("frontend/src/App.tsx");
        var admin = Read("frontend/src/pages/AdminPage.tsx");
        var modal = Read("frontend/src/components/LocationMaintenanceModal.tsx");
        Assert.Contains("/admin/locations/official", app);
        Assert.Contains("LocationAdminTabs", admin);
        Assert.Contains("official-sites", admin);
        Assert.Contains("切換「官方據點進階維護」子分頁", modal);
        Assert.DoesNotContain("頁面下方「官方據點進階維護」", modal);
    }

    [Fact]
    public void Global_header_does_not_claim_a_hard_coded_route_provider()
    {
        var app = Read("frontend/src/App.tsx");
        Assert.DoesNotContain("Route Provider：Mock", app);
        Assert.DoesNotContain("Route Provider：Google", app);
    }

    [Fact]
    public void Route_or_distance_changing_corrections_cannot_use_ungoverned_batch_close()
    {
        var source = Read("frontend/src/pages/CorrectionAdminPage.tsx");
        Assert.Contains("requiresRouteDecision", source);
        Assert.Contains("routeBasisChanged", source);
        Assert.Contains("!requiresRouteDecision(r)", source);
        Assert.Contains("Google 重算里程", source);
        Assert.Contains("CORRECTION_DISTANCE_MISMATCH", source);
        Assert.Contains("distanceDecisionSource:\"ProviderSuggested\"", source);
        Assert.Contains("distanceDecisionSource:\"ManualFallback\"", source);
        Assert.Contains("沒有填寫人工備援里程", source);
    }

    private static string Read(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
