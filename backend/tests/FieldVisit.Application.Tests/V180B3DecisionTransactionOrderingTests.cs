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
        var load=body.IndexOf("V180B3ApprovalLockSql.RequestByPublicId(",StringComparison.Ordinal);
        var mutate=body.IndexOf("row.Status=\"Rejected\"",StringComparison.Ordinal);
        var save=body.IndexOf("await db.SaveChangesAsync(ct)",StringComparison.Ordinal);
        var commit=body.IndexOf("await tx.CommitAsync(ct)",StringComparison.Ordinal);
        Assert.True(tx>=0 && tx<auth && auth<load && load<mutate
            && mutate<save && save<commit);
        Assert.Contains("System.Data.IsolationLevel.Serializable",body);
        Assert.Contains("RequireIndependentReview(",body);
        Assert.Contains("B3_DECISION_KEY_REPLAY",body);
        Assert.Contains("B3_ADMIN_REQUIRED",body);
        Assert.Contains("FromSqlInterpolated(",body);
    }
    [Fact]
    public void Approval_executor_remains_explicitly_denied_without_location_or_event_writes()
    {
        var source = Source();
        var begin = source.IndexOf("public async Task<V180B3RequestView> ApproveAsync(",
            StringComparison.Ordinal);
        Assert.True(begin >= 0);
        var body = source[begin..];
        Assert.Contains("await ReadyAsync(ct)", body);
        Assert.Contains("LiveActorAsync(ct)", body);
        Assert.Contains("B3_ADMIN_REQUIRED", body);
        Assert.Contains("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED", body);
        Assert.DoesNotContain("db.SaveChangesAsync(", body);
        Assert.DoesNotContain("db.ChangeRequestEvents.Add(", body);
        Assert.DoesNotContain("db.Locations.Update(", body);
        Assert.DoesNotContain("db.Database.ExecuteSql", body);
        Assert.DoesNotContain("BeginTransactionAsync(", body);
    }

    [Fact]
    public void Both_decision_paths_use_request_first_parameterized_locking()
    {
        var source=Source();
        var reject=source.IndexOf("public async Task<V180B3RequestView> RejectAsync(",
            StringComparison.Ordinal);
        var preview=source.IndexOf("internal async Task<V180B3ApprovalApplyPlan> PreviewApprovalFromLiveDataAsync(",
            StringComparison.Ordinal);
        var approve=source.IndexOf("public async Task<V180B3RequestView> ApproveAsync(",
            StringComparison.Ordinal);
        Assert.True(reject>=0 && preview>reject && approve>preview);
        var rejectBody=source[reject..preview];
        var previewBody=source[preview..approve];
        Assert.Contains("V180B3ApprovalLockSql.RequestByPublicId(",rejectBody);
        var requestLock=previewBody.IndexOf("V180B3ApprovalLockSql.RequestByPublicId(",
            StringComparison.Ordinal);
        var locationLock=previewBody.IndexOf("V180B3ApprovalLockSql.LocationById(",
            StringComparison.Ordinal);
        Assert.True(requestLock>=0 && locationLock>requestLock);
        Assert.Contains("await tx.RollbackAsync(ct)",previewBody);
        Assert.DoesNotContain("db.SaveChangesAsync(",previewBody);
        Assert.DoesNotContain("db.ChangeRequestEvents.Add(",previewBody);
    }

    [Fact]
    public void Lock_queries_remain_parameterized_and_explicitly_scoped()
    {
        var source=Source();
        var path=Path.Combine(
            Path.GetDirectoryName(Path.Combine(
                FindRepositoryRoot(), "backend", "src",
                "FieldVisit.Infrastructure", "V180B3ChangeRequestService.cs"))!,
            "V180B3ApprovalLockSql.cs");
        var sql=File.ReadAllText(path);
        Assert.Contains("FormattableString RequestByPublicId(",sql);
        Assert.Contains("FormattableString LocationById(",sql);
        Assert.Equal(2,sql.Split("WITH (UPDLOCK, HOLDLOCK)").Length-1);
        Assert.Contains("RequestPublicId = {publicId} AND OrganizationId = {organizationId}",sql);
        Assert.Contains("LocationId = {locationId} AND OrganizationId = {organizationId}",sql);
        Assert.Contains("FromSqlInterpolated(",source);
    }

    private static string FindRepositoryRoot()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir.FullName,"backend","src",
                "FieldVisit.Infrastructure","V180B3ChangeRequestService.cs")))
                return dir.FullName;
            dir=dir.Parent;
        }
        throw new FileNotFoundException("Cannot locate repository root");
    }

}
