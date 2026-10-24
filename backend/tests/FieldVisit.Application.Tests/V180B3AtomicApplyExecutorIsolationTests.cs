using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Guards that the tested 012-capable writer is intentionally separate from
/// the public approval boundary.  SQL Server behavior is proven only by the
/// disposable fixture project.
/// </summary>
public sealed class V180B3AtomicApplyExecutorIsolationTests
{
    private static string Read(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null)
        {
            var path = Path.Combine(root.FullName, relative);
            if(File.Exists(path)) return File.ReadAllText(path);
            root = root.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void Atomic_writer_is_internal_unregistered_and_public_approve_remains_denied()
    {
        var writer = Read(Path.Combine("backend","src","FieldVisit.Infrastructure",
            "V180B3AtomicApplyExecutor.cs"));
        var service = Read(Path.Combine("backend","src","FieldVisit.Infrastructure",
            "V180B3ChangeRequestService.cs"));
        var program = Read(Path.Combine("backend","src","FieldVisit.Api","Program.cs"));
        var approve = service[service.IndexOf(
            "public async Task<V180B3RequestView> ApproveAsync(",
            StringComparison.Ordinal)..];
        Assert.Contains("internal static class V180B3AtomicApplyExecutor", writer);
        Assert.Contains("IsolationLevel.Serializable", writer);
        Assert.Contains("RequireLockedApprovalBoundary(", writer);
        Assert.Contains("RequireExactlyOneGuardedSourceUpdate(", writer);
        Assert.Contains("RequireExactlyOneGuardedRequestTransition(", writer);
        Assert.Contains("RequireAtomicAuditPair(", writer);
        Assert.DoesNotContain("CommitAsync", writer);
        Assert.DoesNotContain("BeginTransaction", writer);
        Assert.DoesNotContain("V180B3AtomicApplyExecutor", program);
        Assert.DoesNotContain("V180B3AtomicApplyExecutor", approve);
        Assert.Contains("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED", approve);
    }

    [Fact]
    public void Orchestrator_is_internal_request_first_and_never_retries_unknown_commit()
    {
        var orchestrator = Read(Path.Combine("backend","src","FieldVisit.Infrastructure",
            "V180B3AtomicApplyOrchestrator.cs"));
        var service = Read(Path.Combine("backend","src","FieldVisit.Infrastructure",
            "V180B3ChangeRequestService.cs"));
        var program = Read(Path.Combine("backend","src","FieldVisit.Api","Program.cs"));
        Assert.Contains("internal sealed class V180B3AtomicApplyOrchestrator", orchestrator);
        Assert.Contains("BeginTransactionAsync(\n            IsolationLevel.Serializable", orchestrator);
        var requestLock = orchestrator.IndexOf("RequestByPublicId(", StringComparison.Ordinal);
        var locationLock = orchestrator.IndexOf("LocationById(", StringComparison.Ordinal);
        Assert.True(requestLock >= 0 && locationLock > requestLock);
        Assert.Contains("V180B3AtomicApplyExecutor.ExecuteAsync(", orchestrator);
        Assert.Contains("await transaction.CommitAsync(ct)", orchestrator);
        Assert.Contains("new SqlConnection(connectionString)", orchestrator);
        Assert.Contains("B3_UNKNOWN_COMMIT_", orchestrator);
        Assert.DoesNotContain("V180B3AtomicApplyOrchestrator", program);
        Assert.DoesNotContain("V180B3AtomicApplyOrchestrator", service);
        Assert.DoesNotContain("ExecuteAsync(id,input", service);
    }
}
