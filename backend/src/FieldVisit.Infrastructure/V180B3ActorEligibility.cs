using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// B3-specific eligibility for BOTH visitor submissions and Admin review.
/// No permission is inferred from the JWT alone or from an external profile.
/// Missing profile is tolerated only as the established v1.7 legacy-Internal
/// compatibility path. This never enables B3 approval or publication.
/// </summary>
public static class V180B3ActorEligibility
{
    public static async Task RequireAsync(
        AppDbContext db,CurrentUserDto user,CancellationToken ct)
    {
        if(!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ORGANIZATION_REQUIRED");
        var account=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>
            x.UserId==user.UserId&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new UnauthorizedAccessException("B3_ACCOUNT_INVALID");
        if(!account.IsActive)
            throw new UnauthorizedAccessException("B3_ACCOUNT_DISABLED");
        var userType=await db.UserIdentityProfiles.AsNoTracking()
            .Where(x=>x.UserId==user.UserId)
            .Select(x=>x.UserType).SingleOrDefaultAsync(ct);
        if(userType is not null &&
            !string.Equals(userType,UserTypes.Internal,StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("B3_INTERNAL_IDENTITY_REQUIRED");
        if(!(await new V170AccessControl(db)
            .EvaluateLoginAsync(user.UserId,account.IsActive,ct)).IsAllowed)
            throw new UnauthorizedAccessException("B3_HR_STATUS_DENIED");
    }
}

/// <summary>
/// Present B3 candidate supports rejection of an owned, pending,
/// high-risk published Customer Location request ONLY. General review
/// operations require separate schema/IT/Owner authorization.
/// </summary>
public static class V180B3ReviewTargetRules
{
    public static void RequireSupportedTarget(
        V180B3ChangeRequest row,int reviewerOrganizationId,Guid requestedPublicId)
    {
        if(row.OrganizationId!=reviewerOrganizationId
           ||row.RequestPublicId!=requestedPublicId
           ||row.EntityKind!="Location"
           ||row.OperationCode!="UpdatePublishedLocation"
           ||row.RiskCode!="High"
           ||!row.TeamId.HasValue
           ||row.RequestedByUserId<=0
           ||!int.TryParse(row.EntityId,out var locationId)
           ||locationId<=0
           ||row.ExpectedEntityRowVersion?.Length!=8)
            throw new UnauthorizedAccessException("B3_REVIEW_TARGET_UNSUPPORTED");
    }
}
