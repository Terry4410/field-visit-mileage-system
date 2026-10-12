using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void Approval_executor_must_match_entire_source_pinned_deny_all_body()
    {
        var source=File.ReadAllText(RepositoryFile(
            "backend/src/FieldVisit.Infrastructure/V180B3ChangeRequestService.cs"));
        var signature=Regex.Matches(source,
            @"(?m)^[ \t]*public\s+async\s+Task<V180B3RequestView>\s+ApproveAsync\s*\(\s*Guid\s+id\s*,\s*V180B3Review\s+input\s*,\s*CancellationToken\s+ct\s*\)\s*\{");
        var only=Assert.Single(signature.Cast<Match>());
        var following=source[(only.Index+only.Length)..];
        var closing=Regex.Match(following,@"(?m)^[ \t]{4}\}[ \t]*$");
        Assert.True(closing.Success,"B3 approval method boundary changed; manual review required.");
        var actualBody=following[..closing.Index];
        // A safety throw in a comment, after a return or in the NEXT method
        // is not a valid proof of deny-all authorization.
        var withoutComments=Regex.Replace(actualBody,
            @"/\*[\s\S]*?\*/|//[^\r\n]*","");
        var normalized=Regex.Replace(withoutComments,@"\s+","");
        Assert.Equal(
            "awaitReadyAsync(ct);"+
            "var(_,admin,_)=awaitLiveActorAsync(ct);"+
            "if(!admin)thrownewUnauthorizedAccessException(\"B3_ADMIN_REQUIRED\");"+
            "thrownewInvalidOperationException(\"B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED\");",
            normalized);
    }

    [Fact]
    public void Candidate_011_and_012_migration_files_and_dispatch_workflows_are_not_authorized()
    {
        // The Owner approved NONEXECUTABLE IA in docs/release only.
        // Any actual 011 migration or 011 deployment workflow is a hard CI failure.
        var appsettings=RepositoryFile("backend/src/FieldVisit.Api/appsettings.json");
        var root=Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(appsettings)!, "..","..",".."));
        var migrations=Path.Combine(root,"database","migrations");
        Assert.True(Directory.Exists(migrations),"Cannot verify migration directory safety");
        var forbidden=Directory.EnumerateFiles(migrations,"*",SearchOption.AllDirectories)
            .Where(path=>path.Contains("1800_011",StringComparison.OrdinalIgnoreCase)
                ||path.Contains("1800-011",StringComparison.OrdinalIgnoreCase)
                ||path.Contains("1800_012",StringComparison.OrdinalIgnoreCase)
                ||path.Contains("1800-012",StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(forbidden);

        var workflows=Path.Combine(root,".github","workflows");
        Assert.True(Directory.Exists(workflows),"Cannot verify workflow safety");
        var unauthorized=Directory.EnumerateFiles(workflows,"*",SearchOption.TopDirectoryOnly)
            .Where(path=>Path.GetFileName(path).Contains("1800_011",StringComparison.OrdinalIgnoreCase)
                ||Path.GetFileName(path).Contains("1800-011",StringComparison.OrdinalIgnoreCase)
                ||Path.GetFileName(path).Contains("1800_012",StringComparison.OrdinalIgnoreCase)
                ||Path.GetFileName(path).Contains("1800-012",StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(unauthorized);
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
