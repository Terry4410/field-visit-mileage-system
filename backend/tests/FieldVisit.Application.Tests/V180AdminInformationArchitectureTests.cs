using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180AdminInformationArchitectureTests
{
    [Fact]
    public void Location_duplicate_review_has_one_formal_admin_workspace()
    {
        var app = ReadRepositoryFile("frontend/src/App.tsx");
        var tabs = ReadRepositoryFile("frontend/src/components/LocationAdminTabs.tsx");
        var review = ReadRepositoryFile("frontend/src/pages/LocationDuplicateReviewPage.tsx");
        var maintenance = ReadRepositoryFile("frontend/src/components/LocationMaintenanceModal.tsx");

        Assert.Contains("/admin/locations/duplicates", app);
        Assert.Contains("疑似重複覆核", tabs);
        Assert.Contains("未處理", review);
        Assert.Contains("已確認不同", review);
        Assert.Contains("已沿用", review);
        Assert.Contains("已合併", review);
        Assert.Contains("確認不同", review);
        Assert.Contains("沿用此主檔", review);
        Assert.Contains("合併／選欄位", review);
        Assert.DoesNotContain("沿用此主檔", maintenance);
        Assert.DoesNotContain("合併／選欄位", maintenance);
        Assert.Contains("前往疑似重複覆核", maintenance);
    }

    [Fact]
    public void Project_admin_ia_starts_from_list_and_separates_bulk_and_fixed_locations()
    {
        var app = ReadRepositoryFile("frontend/src/App.tsx");
        var page = ReadRepositoryFile("frontend/src/pages/ProjectManagementPage.tsx");
        var tabs = ReadRepositoryFile("frontend/src/components/ProjectAdminTabs.tsx");

        Assert.Contains("/admin/projects/bulk", app);
        Assert.Contains("專案清單", tabs);
        Assert.Contains("Excel 批次維護", tabs);
        Assert.Contains("＋新增專案", page);
        Assert.Contains("維護專案", page);
        Assert.Contains("基本資料", page);
        Assert.Contains("固定地點", page);
        Assert.Contains("persistedAllowsLocations", page);
        Assert.Contains("ProjectLocationManager", page);
    }

    [Fact]
    public void Legacy_admin_workspaces_are_removed_after_single_owner_split()
    {
        var admin = ReadRepositoryFile("frontend/src/pages/AdminPage.tsx");

        Assert.DoesNotContain("帳號啟用", admin);
        Assert.DoesNotContain("<h2>專案主檔</h2>", admin);
        Assert.DoesNotContain("<h2>更正流程</h2>", admin);
        Assert.DoesNotContain("function Users(", admin);
        Assert.DoesNotContain("function Projects(", admin);
        Assert.DoesNotContain("function Corrections(", admin);
        Assert.Contains("function VisitTypes(", admin);
        Assert.Contains("<h2>拜訪形式</h2>", admin);
    }

    [Fact]
    public void Owner_final_ui_corrections_keep_admin_responsibilities_clear()
    {
        var teams = ReadRepositoryFile("frontend/src/pages/TeamManagementPage.tsx");
        var styles = ReadRepositoryFile("frontend/src/styles.css");
        var locationTabs = ReadRepositoryFile("frontend/src/components/LocationAdminTabs.tsx");
        var admin = ReadRepositoryFile("frontend/src/pages/AdminPage.tsx");
        var projects = ReadRepositoryFile("frontend/src/pages/ProjectManagementPage.tsx");

        Assert.Contains("team-membership-item", teams);
        Assert.Contains(".team-membership-item", styles);
        Assert.Contains("官方據點進階維護", locationTabs);
        Assert.Contains("資料／官方標記", admin);
        Assert.Contains("includeInactive=true", projects);
        Assert.DoesNotContain("Team ${p.teamId}", projects);
    }

    [Fact]
    public void Admin_ia_still_has_no_1800_011_migration()
    {
        var root = RepositoryRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "database", "migrations", "1800_011")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "database", "migrations"), "1800_011*"));
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
