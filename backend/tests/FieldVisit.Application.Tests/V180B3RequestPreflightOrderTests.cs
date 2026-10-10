using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// B4 source-contract test. Guards the order of disabled-schema,
/// untrusted-input validation, live authorization and serializable
/// transaction entry without executing any SQL/DDL.
/// </summary>
public sealed class V180B3RequestPreflightOrderTests
{
    private static string Source()
    {
        var folder=new DirectoryInfo(AppContext.BaseDirectory);
        while(folder is not null)
        {
            var candidate=Path.Combine(folder.FullName,"backend","src",
                "FieldVisit.Infrastructure","V180B3ChangeRequestService.cs");
            if(File.Exists(candidate))return File.ReadAllText(candidate);
            folder=folder.Parent;
        }
        throw new FileNotFoundException("Cannot verify B3 input/transaction ordering");
    }

    private static string Method(string name,string following)
    {
        var code=Source();
        var start=code.IndexOf("public async Task<"+name,StringComparison.Ordinal);
        var end=code.IndexOf("public async Task<"+following,StringComparison.Ordinal);
        Assert.True(start>=0 && end>start, "B3 method signature changed; source review required");
        return code[start..end];
    }

    [Fact]
    public void Submit_checks_flag_schema_then_bounded_payload_before_transaction_and_live_permissions()
    {
        var s=Method("V180B3RequestView> SubmitAsync(","IReadOnlyList<V180B3RequestView>> MineAsync(");
        var ready=s.IndexOf("await ReadyAsync(ct)",StringComparison.Ordinal);
        var payload=s.IndexOf("V180B3RequestInputRules.RequireSubmission(input)",StringComparison.Ordinal);
        var tx=s.IndexOf("BeginTransactionAsync(",StringComparison.Ordinal);
        var live=s.IndexOf("LiveActorAsync(ct)",StringComparison.Ordinal);
        var record=s.IndexOf("db.Locations.AsNoTracking()",StringComparison.Ordinal);
        var commit=s.IndexOf("await tx.CommitAsync(ct)",StringComparison.Ordinal);
        Assert.True(ready>=0 && ready<payload && payload<tx && tx<live
            && live<record && record<commit);
        Assert.Contains("IsolationLevel.Serializable",s);
        Assert.Contains("loc.RowVersion.Length!=8",s);
        Assert.Contains("expectedVersion",s);
    }

    [Fact]
    public void Reject_checks_flag_schema_then_review_shape_before_transaction_and_live_admin()
    {
        var s=Method("V180B3RequestView> RejectAsync(","V180B3RequestView> ApproveAsync(");
        var ready=s.IndexOf("await ReadyAsync(ct)",StringComparison.Ordinal);
        var payload=s.IndexOf("V180B3RequestInputRules.RequireReviewTarget(id,input)",StringComparison.Ordinal);
        var tx=s.IndexOf("BeginTransactionAsync(",StringComparison.Ordinal);
        var live=s.IndexOf("LiveActorAsync(ct)",StringComparison.Ordinal);
        var save=s.IndexOf("await db.SaveChangesAsync(ct)",StringComparison.Ordinal);
        var commit=s.IndexOf("await tx.CommitAsync(ct)",StringComparison.Ordinal);
        Assert.True(ready>=0 && ready<payload && payload<tx && tx<live
            && live<save && save<commit);
        Assert.Contains("RequireIndependentReview(",s);
        Assert.Contains("IsolationLevel.Serializable",s);
    }

    [Fact]
    public void Approval_remains_denied_without_adding_transaction_or_publishing()
    {
        var source=Source();
        var start=source.IndexOf("public async Task<V180B3RequestView> ApproveAsync(",
            StringComparison.Ordinal);
        Assert.True(start>=0);
        var method=source[start..];
        Assert.Contains("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED",method);
        Assert.DoesNotContain("BeginTransactionAsync(",method);
        Assert.DoesNotContain("db.SaveChangesAsync(",method);
        Assert.DoesNotContain("db.Locations.Update(",method);
    }
}
