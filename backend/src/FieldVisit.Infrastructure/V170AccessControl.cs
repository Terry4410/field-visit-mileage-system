using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Database-backed v1.7 access policy.
///
/// Security decisions are evaluated server-side. The frontend may hide
/// unavailable functions for UX, but hiding a button is never considered
/// authorization.
/// </summary>
public sealed class V170AccessControl(AppDbContext db)
    : IV170AccessControl
{
    public async Task<V170LoginEligibility> EvaluateLoginAsync(
        int userId,
        bool adminEnabled,
        CancellationToken ct)
    {
        var today = BusinessTime.Today;

        var identity = await db.UserIdentityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId,
                ct);

        // Migration compatibility:
        // missing profile means legacy internal user.
        var userType =
            identity?.UserType
            ?? UserTypes.Internal;

        var employment = await db.UserEmploymentPeriods
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue
                    || x.EffectiveTo >= today))
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.UserEmploymentPeriodId)
            .FirstOrDefaultAsync(ct);

        var allowed = V170AccessRules.IsSystemAccessAllowed(
            adminEnabled,
            employment?.EmploymentStatus,
            userType,
            identity?.AuthorizationFrom,
            identity?.AuthorizationTo,
            today);

        if (allowed)
        {
            return new V170LoginEligibility(
                true,
                userType,
                employment?.EmploymentStatus,
                null);
        }

        string reason;

        if (!adminEnabled)
        {
            reason = "此帳號未啟用。";
        }
        else if (string.Equals(
                     userType,
                     UserTypes.External,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (identity?.AuthorizationFrom is { } from
                && from > today)
            {
                reason = "外部帳號尚未到授權生效日。";
            }
            else if (identity?.AuthorizationTo is { } to
                     && to < today)
            {
                reason = "外部帳號授權已到期。";
            }
            else
            {
                reason = "目前外部帳號授權狀態不允許登入。";
            }
        }
        else
        {
            reason = employment?.EmploymentStatus switch
            {
                EmploymentStatuses.Leave =>
                    "目前為留停狀態，暫停系統登入。",

                EmploymentStatuses.Terminated =>
                    "目前為離職狀態，無法登入系統。",

                EmploymentStatuses.PreHire =>
                    "目前尚未到職，暫不可登入系統。",

                _ =>
                    "目前人事狀態不允許登入系統。"
            };
        }

        return new V170LoginEligibility(
            false,
            userType,
            employment?.EmploymentStatus,
            reason);
    }

    public async Task<V170ReadScope> ResolveReadScopeAsync(
        CurrentUserDto user,
        CancellationToken ct)
    {
        if (HasRole(user, "admin"))
        {
            return user.OrganizationId.HasValue
                ? new V170ReadScope(true, [])
                : new V170ReadScope(false, []);
        }

        if (HasRole(user, "leader"))
        {
            return new V170ReadScope(
                false,
                user.TeamIds.Distinct().ToList());
        }

        if (!HasRole(user, "supervisor")
            || !user.OrganizationId.HasValue)
        {
            return new V170ReadScope(false, []);
        }

        var today = BusinessTime.Today;
        var orgId = user.OrganizationId.Value;

        var effective = db.UserDataScopes
            .AsNoTracking()
            .Where(x =>
                x.UserId == user.UserId
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue
                    || x.EffectiveTo >= today));

        var organizationWide =
            await effective.AnyAsync(
                x =>
                    x.ScopeType == DataScopeTypes.Organization
                    && x.OrganizationId == orgId,
                ct);

        if (organizationWide)
        {
            return new V170ReadScope(true, []);
        }

        var teamIds = await (
            from scope in effective
            join team in db.Teams.AsNoTracking()
                on scope.TeamId equals team.TeamId
            where
                scope.ScopeType == DataScopeTypes.Team
                && scope.TeamId != null
                && team.OrganizationId == orgId
            select team.TeamId
        )
        .Distinct()
        .ToListAsync(ct);

        return new V170ReadScope(
            false,
            teamIds);
    }

    public async Task<bool> HasCapabilityAsync(
        int userId,
        string capabilityCode,
        CancellationToken ct)
    {
        var today = BusinessTime.Today;

        var row = await db.UserCapabilities
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId
                && x.CapabilityCode == capabilityCode
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue
                    || x.EffectiveTo >= today))
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.UserCapabilityId)
            .FirstOrDefaultAsync(ct);

        return row?.IsAllowed == true;
    }

    public async Task EnsureExportAllowedAsync(
        CurrentUserDto user,
        string format,
        CancellationToken ct)
    {
        if (!HasRole(user, "supervisor"))
            return;

        var normalized =
            (format ?? "")
            .Trim()
            .ToLowerInvariant();

        var capability = normalized switch
        {
            "xlsx" or "excel" =>
                CapabilityCodes.ExportExcel,

            "pdf" =>
                CapabilityCodes.ExportPdf,

            _ => null
        };

        // Unsupported formats are rejected by the export service itself.
        if (capability is null)
            return;

        if (!await HasCapabilityAsync(
                user.UserId,
                capability,
                ct))
        {
            var display =
                capability == CapabilityCodes.ExportExcel
                    ? "Excel"
                    : "PDF";

            throw new UnauthorizedAccessException(
                $"督導尚未取得下載 {display} 的授權。");
        }
    }

    public async Task AuditSupervisorQueryAsync(
        CurrentUserDto user,
        TripQueryRequest request,
        int resultCount,
        CancellationToken ct)
    {
        if (!HasRole(user, "supervisor"))
            return;

        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                UserId = user.UserId,
                EntityType = "TripQuery",
                EntityId = null,
                Action = "SupervisorQuery",
                NewValues = JsonSerializer.Serialize(
                    new
                    {
                        Filters = request,
                        ResultCount = resultCount
                    }),
                CreatedAt = DateTime.UtcNow
            },
            ct);

        await db.SaveChangesAsync(ct);
    }

    private static bool HasRole(
        CurrentUserDto user,
        string role)
        => user.Roles.Any(
            x => x.Equals(
                role,
                StringComparison.OrdinalIgnoreCase));
}
