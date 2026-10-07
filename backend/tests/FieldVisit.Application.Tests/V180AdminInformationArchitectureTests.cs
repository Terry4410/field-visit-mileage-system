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
