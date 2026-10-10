using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Current, server-side Admin authorization for security-changing commands.
/// The User account, HR eligibility, effective-dated roles and compatibility
/// role projection must all agree. Never infer Admin from team membership
/// or from a stale JWT. This does NOT authorize B3 approval/apply.
/// </summary>
public static class V180CurrentAdminWriteGuard
{
    public static async Task<CurrentUserDto> RequireAsync(
        AppDbContext db,CurrentUserDto user,CancellationToken ct)
    {
        if(!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("ADMIN_WRITE_ORG_REQUIRED");
        var account=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>
            x.UserId==user.UserId&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new UnauthorizedAccessException("ADMIN_WRITE_ACCOUNT_INVALID");

        // Explicitly reject disabled accounts in sensitive security commands.
        // HR eligibility remains an independent mandatory check.
        if(!account.IsActive)
            throw new UnauthorizedAccessException("ADMIN_WRITE_ACCOUNT_DISABLED");
        if(!(await new V170AccessControl(db).EvaluateLoginAsync(
            user.UserId,account.IsActive,ct)).IsAllowed)
            throw new UnauthorizedAccessException("ADMIN_WRITE_HR_DENIED");

        var today=BusinessTime.Today;
        var datedRoles=await(
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId==user.UserId&&r.IsActive
                &&a.EffectiveFrom<=today
                &&(!a.EffectiveTo.HasValue||a.EffectiveTo>=today)
            select r.RoleCode).ToListAsync(ct);
        var projectedRoles=await(
            from a in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId==user.UserId&&r.IsActive
            select r.RoleCode).ToListAsync(ct);
        V180LocationAdminMutationRules.RequireCurrentAdmin(user,datedRoles,projectedRoles);
        return user;
    }
}
