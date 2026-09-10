namespace FieldVisit.Application;

/// <summary>
/// One narrow infrastructure boundary for operations that change the GLOBAL active
/// VisitType membership/order set. Business authority remains in MasterService.
/// </summary>
public interface IVisitTypeMembershipCoordinator
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
