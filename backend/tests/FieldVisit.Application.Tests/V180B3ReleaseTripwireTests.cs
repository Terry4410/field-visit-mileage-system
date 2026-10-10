using System.Text.Json;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Non-deploy CI tripwire for the current B3 HARD HOLD. Inspects checked-in
/// source/config; does NOT open a network connection or execute SQL.
/// Not a replacement for runtime flag and database authorization gates.
/// </summary>
public sealed class V180B3ReleaseTripwireTests
{
    private static string RepositoryFile(string relative)
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var path=Path.Combine(dir.FullName,
                relative.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(path))return path;
            dir=dir.Parent;
        }
        throw new FileNotFoundException(
            "CI cannot verify B3 HARD HOLD because repository source is unavailable: "+relative);
    }

    [Fact]
    public void Checked_in_API_config_must_keep_B3_disabled()
    {
        var json=File.ReadAllText(RepositoryFile(
            "backend/src/FieldVisit.Api/appsettings.json"));
        using var root=JsonDocument.Parse(json);
        Assert.False(root.RootElement.GetProperty("PackageB")
            .GetProperty("B3").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void Approval_executor_must_remain_explicitly_denied()
    {
        var source=File.ReadAllText(RepositoryFile(
            "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs"));
        var start=source.IndexOf("public async Task<V180B3RequestView> ApproveAsync(",
            StringComparison.Ordinal);
        Assert.True(start>=0,"B3 approval entrypoint was changed: manual review required.");
        var body=source[start..];
        Assert.Contains("throw new InvalidOperationException(\"B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED\")",
            body,StringComparison.Ordinal);
        Assert.DoesNotContain("db.ChangeRequestEvents.Add(",body,StringComparison.Ordinal);
        Assert.DoesNotContain("await db.SaveChangesAsync(",body,StringComparison.Ordinal);
    }

    [Fact]
    public void Unauthorized_CI_test_must_never_trigger_schema_or_release_workflow()
    {
        var workflow=File.ReadAllText(RepositoryFile(
            ".github/workflows/package-b-b2-b3-controlled-verify.yml"));
        Assert.Contains("NO DEPLOY",workflow,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("azure/login",workflow,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlcmd",workflow,StringComparison.OrdinalIgnoreCase);
    }
}
