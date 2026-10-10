using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Offline source-order guard, not proof of SQL Server serializable behavior.
/// </summary>
public sealed class V180B3DecisionTransactionOrderingTests
{
    private static string Source()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var path=Path.Combine(dir.FullName,"backend","src",
                "FieldVisit.Infrastructure","V180B3ChangeRequestService.cs");
            if(File.Exists(path))return File.ReadAllText(path);
            dir=dir.Parent;
        }
        throw new FileNotFoundException("Cannot verify B3 decision transaction");
    }

    [Fact]
    public void Reviewer_live_authorization_is_inside_serializable_decision_transaction()
    {
        var source=Source();
        var begin=source.IndexOf("public async Task<V180B3RequestView> RejectAsync(",
            StringComparison.Ordinal);
        var end=source.IndexOf("public async Task<V180B3RequestView> ApproveAsync(",
            StringComparison.Ordinal);
        Assert.True(begin>=0 && end>begin);
        var body=source[begin..end];
        var tx=body.IndexOf("BeginTransactionAsync(",StringComparison.Ordinal);
        var auth=body.IndexOf("LiveActorAsync(ct)",StringComparison.Ordinal);
        var load=body.IndexOf("db.ChangeRequests.SingleOrDefaultAsync(",StringComparison.Ordinal);
        var mutate=body.IndexOf("row.Status=\"Rejected\"",StringComparison.Ordinal);
        var save=body.IndexOf("await db.SaveChangesAsync(ct)",StringComparison.Ordinal);
        var commit=body.IndexOf("await tx.CommitAsync(ct)",StringComparison.Ordinal);
        Assert.True(tx>=0 && tx<auth && auth<load && load<mutate
            && mutate<save && save<commit);
        Assert.Contains("System.Data.IsolationLevel.Serializable",body);
        Assert.Contains("RequireIndependentReview(",body);
        Assert.Contains("B3_DECISION_KEY_REPLAY",body);
        Assert.Contains("B3_ADMIN_REQUIRED",body);
    }
}
