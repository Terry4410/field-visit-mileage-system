using FieldVisit.Application;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Only the approved B3 candidate contract is queryable. Same-organization
/// or same-team membership is NEVER authority to see another requester's
/// Mine payload, nor an unknown operation's proposed JSON.
/// The caller must separately pass LiveActorAsync; these predicates only
/// further restrict its read scope.
/// </summary>
public static class V180B3QueueScopeRules
{
    private static IQueryable<V180B3ChangeRequest> Supported(
        IQueryable<V180B3ChangeRequest> source) =>
        source.Where(x=>x.EntityKind=="Location"
            &&x.OperationCode=="UpdatePublishedLocation"
            &&x.RiskCode=="High"&&x.TeamId!=null);

    public static IQueryable<V180B3ChangeRequest> ForRequester(
        IQueryable<V180B3ChangeRequest> source,CurrentUserDto actor)
    {
        if(!actor.OrganizationId.HasValue||actor.OrganizationId.Value<=0
            ||actor.UserId<=0)
            throw new UnauthorizedAccessException("B3_REQUEST_READ_SCOPE_DENIED");
        return Supported(source).Where(x=>
            x.OrganizationId==actor.OrganizationId.Value
            &&x.RequestedByUserId==actor.UserId
            &&(x.Status=="Pending"||x.Status=="Rejected"));
    }

    public static IQueryable<V180B3ChangeRequest> ForAdminPending(
        IQueryable<V180B3ChangeRequest> source,CurrentUserDto actor)
    {
        if(!actor.OrganizationId.HasValue||actor.OrganizationId.Value<=0)
            throw new UnauthorizedAccessException("B3_REQUEST_READ_SCOPE_DENIED");
        // Admin role/HR has already been revalidated by the service.
        return Supported(source).Where(x=>
            x.OrganizationId==actor.OrganizationId.Value&&x.Status=="Pending");
    }
}
