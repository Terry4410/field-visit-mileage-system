using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// v1.7 People/Access write model.
///
/// External supervisors:
/// - have no EmployeeNo
/// - are never Team Members
/// - receive read visibility only through UserDataScopes
/// - receive export permission only through UserCapabilities
/// </summary>
public sealed class V170PeopleAdminWriter(
    AppDbContext db)
    : IV170PeopleAdminWriter
{
    public async Task<int> CreateExternalSupervisorAsync(
        CurrentUserDto admin,
        SaveExternalSupervisorRequest request,
        CancellationToken ct)
    {
        request =
            V170ExternalSupervisorRules.Normalize(
                request);

        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var duplicateEmail =
            await db.Users
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Email != null
                        && x.Email.ToLower()
                           == request.Email.ToLower(),
                    ct);

        if (duplicateEmail)
        {
            throw new InvalidOperationException(
                "此 Email 已存在於系統中。");
        }

        if (request.ScopeType
            == DataScopeTypes.Team)
        {
            var validTeamIds =
                await db.Teams
                    .AsNoTracking()
                    .Where(
                        x =>
                            request.TeamIds.Contains(
                                x.TeamId)
                            && x.OrganizationId == orgId
                            && x.IsActive)
                    .Select(x => x.TeamId)
                    .ToListAsync(ct);

            if (validTeamIds.Count
                != request.TeamIds.Count)
            {
                throw new InvalidOperationException(
                    "包含不存在、已停用或不屬於目前 Organization 的 Team。");
            }
        }

        var role =
            await db.Roles
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);

        var supervisorRole =
            role.FirstOrDefault(
                x =>
                    x.RoleCode.Equals(
                        "supervisor",
                        StringComparison.OrdinalIgnoreCase)
                    || x.RoleCode.Equals(
                        "government",
                        StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "找不到 Supervisor Role。");

        var strategy =
            db.Database.CreateExecutionStrategy();

        var createdUserId = 0;

        await strategy.ExecuteAsync(
            async () =>
            {
                db.ChangeTracker.Clear();

                await using var tx =
                    await db.Database
                        .BeginTransactionAsync(ct);

                var now =
                    DateTime.UtcNow;

                var user =
                    new User
                    {
                        OrganizationId = orgId,
                        TeamId = null,

                        // External identity deliberately has
                        // no employee number.
                        EmployeeNo = null,

                        DisplayName =
                            request.DisplayName,

                        Email =
                            request.Email,

                        EntraObjectId = null,

                        IsActive =
                            request.AdminEnabled,

                        CreatedAt = now
                    };

                await db.Users.AddAsync(
                    user,
                    ct);

                await db.SaveChangesAsync(ct);

                createdUserId =
                    user.UserId;

                var userCode =
                    await NewExternalUserCodeAsync(
                        ct);

                await db.UserIdentityProfiles
                    .AddAsync(
                        new UserIdentityProfile
                        {
                            UserId =
                                user.UserId,

                            UserType =
                                UserTypes.External,

                            UserCode =
                                userCode,

                            // UAT uses Demo authentication.
                            // Production is switched to
                            // Entra ID B2B after IT Gate.
                            IdentityProvider =
                                "Demo",

                            ExternalOrganization =
                                request.ExternalOrganization,

                            ExternalTitle =
                                request.ExternalTitle,

                            AuthorizationFrom =
                                request.AuthorizationFrom,

                            AuthorizationTo =
                                request.AuthorizationTo,

                            CreatedAt =
                                now
                        },
                        ct);

                // Effective-dated source of truth.
                await db.UserRoleAssignments
                    .AddAsync(
                        new UserRoleAssignment
                        {
                            UserId =
                                user.UserId,

                            RoleId =
                                supervisorRole.RoleId,

                            EffectiveFrom =
                                request.AuthorizationFrom,

                            EffectiveTo =
                                request.AuthorizationTo,

                            AssignedByUserId =
                                admin.UserId,

                            CreatedAt =
                                now
                        },
                        ct);

                // v1.6 compatibility projection.
                await db.UserRoles.AddAsync(
                    new UserRole
                    {
                        UserId =
                            user.UserId,

                        RoleId =
                            supervisorRole.RoleId,

                        AssignedAt =
                            now
                    },
                    ct);

                if (request.ScopeType
                    == DataScopeTypes.Organization)
                {
                    await db.UserDataScopes
                        .AddAsync(
                            new UserDataScope
                            {
                                UserId =
                                    user.UserId,

                                ScopeType =
                                    DataScopeTypes.Organization,

                                OrganizationId =
                                    orgId,

                                TeamId =
                                    null,

                                EffectiveFrom =
                                    request.AuthorizationFrom,

                                EffectiveTo =
                                    request.AuthorizationTo,

                                GrantedByUserId =
                                    admin.UserId,

                                CreatedAt =
                                    now
                            },
                            ct);
                }
                else
                {
                    foreach (var teamId
                             in request.TeamIds)
                    {
                        await db.UserDataScopes
                            .AddAsync(
                                new UserDataScope
                                {
                                    UserId =
                                        user.UserId,

                                    ScopeType =
                                        DataScopeTypes.Team,

                                    OrganizationId =
                                        null,

                                    TeamId =
                                        teamId,

                                    EffectiveFrom =
                                        request.AuthorizationFrom,

                                    EffectiveTo =
                                        request.AuthorizationTo,

                                    GrantedByUserId =
                                        admin.UserId,

                                    CreatedAt =
                                        now
                                },
                                ct);
                    }
                }

                await AddCapabilityAsync(
                    user.UserId,
                    CapabilityCodes.ExportExcel,
                    request.CanExportExcel,
                    request.AuthorizationFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await AddCapabilityAsync(
                    user.UserId,
                    CapabilityCodes.ExportPdf,
                    request.CanExportPdf,
                    request.AuthorizationFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await db.AuditLogs.AddAsync(
                    new AuditLog
                    {
                        UserId =
                            admin.UserId,

                        EntityType =
                            "User",

                        EntityId =
                            user.UserId.ToString(),

                        Action =
                            "ExternalSupervisorCreate",

                        NewValues =
                            JsonSerializer.Serialize(
                                new
                                {
                                    UserCode =
                                        userCode,

                                    request.DisplayName,
                                    request.Email,
                                    request.ExternalOrganization,
                                    request.ExternalTitle,
                                    request.AuthorizationFrom,
                                    request.AuthorizationTo,
                                    request.ScopeType,
                                    request.TeamIds,
                                    request.CanExportExcel,
                                    request.CanExportPdf,
                                    request.AdminEnabled
                                }),

                        CreatedAt =
                            now
                    },
                    ct);

                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });

        return createdUserId;
    }

    public async Task UpdateExternalSupervisorAsync(
        CurrentUserDto admin,
        int userId,
        UpdateExternalSupervisorRequest request,
        CancellationToken ct)
    {
        var today =
            BusinessTime.Today;

        request =
            V170ExternalSupervisorUpdateRules.Normalize(
                request,
                today);

        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var strategy =
            db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async () =>
            {
                db.ChangeTracker.Clear();

                await using var tx =
                    await db.Database
                        .BeginTransactionAsync(ct);

                var now =
                    DateTime.UtcNow;

                var user =
                    await db.Users
                        .FirstOrDefaultAsync(
                            x =>
                                x.UserId == userId
                                && x.OrganizationId == orgId,
                            ct)
                    ?? throw new KeyNotFoundException(
                        "找不到外部督導。");

                var identity =
                    await db.UserIdentityProfiles
                        .FirstOrDefaultAsync(
                            x => x.UserId == userId,
                            ct)
                    ?? throw new InvalidOperationException(
                        "此帳號缺少 v1.7 Identity Profile。");

                if (!identity.UserType.Equals(
                        UserTypes.External,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "只有 External User 可以使用此外部督導異動功能。");
                }

                var duplicateEmail =
                    await db.Users
                        .AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.UserId != userId
                                && x.Email != null
                                && x.Email.ToLower()
                                   == request.Email.ToLower(),
                            ct);

                if (duplicateEmail)
                {
                    throw new InvalidOperationException(
                        "此 Email 已存在於系統中。");
                }

                if (request.ScopeType
                    == DataScopeTypes.Team)
                {
                    var validTeamIds =
                        await db.Teams
                            .AsNoTracking()
                            .Where(
                                x =>
                                    request.TeamIds.Contains(
                                        x.TeamId)
                                    && x.OrganizationId == orgId
                                    && x.IsActive)
                            .Select(x => x.TeamId)
                            .ToListAsync(ct);

                    if (validTeamIds.Count
                        != request.TeamIds.Count)
                    {
                        throw new InvalidOperationException(
                            "包含不存在、已停用或不屬於目前 Organization 的 Team。");
                    }
                }

                if (!request.ConfirmRetroactive)
                {
                    var authorizationStartChangedToPast =
                        identity.AuthorizationFrom
                            != request.AuthorizationFrom
                        && request.AuthorizationFrom < today;

                    var authorizationEndChangedToPast =
                        identity.AuthorizationTo
                            != request.AuthorizationTo
                        && request.AuthorizationTo < today;

                    if (authorizationStartChangedToPast
                        || authorizationEndChangedToPast)
                    {
                        throw new InvalidOperationException(
                            "授權日期異動涉及歷史期間，請二次確認回溯異動。");
                    }
                }

                var supervisorRole =
                    await db.Roles
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .FirstOrDefaultAsync(
                            x =>
                                x.RoleCode.ToLower()
                                    == "supervisor"
                                || x.RoleCode.ToLower()
                                    == "government",
                            ct)
                    ?? throw new InvalidOperationException(
                        "找不到 Supervisor Role。");

                var roleAssignments =
                    await db.UserRoleAssignments
                        .Where(
                            x =>
                                x.UserId == userId
                                && x.RoleId
                                   == supervisorRole.RoleId)
                        .ToListAsync(ct);

                if (roleAssignments.Count != 1)
                {
                    throw new InvalidOperationException(
                        "External Supervisor Role Assignment 資料不完整，請由 IT 檢查。");
                }

                var scopesBefore =
                    await db.UserDataScopes
                        .AsNoTracking()
                        .Where(x => x.UserId == userId)
                        .OrderBy(x => x.EffectiveFrom)
                        .Select(
                            x => new
                            {
                                x.ScopeType,
                                x.OrganizationId,
                                x.TeamId,
                                x.EffectiveFrom,
                                x.EffectiveTo
                            })
                        .ToListAsync(ct);

                var capabilitiesBefore =
                    await db.UserCapabilities
                        .AsNoTracking()
                        .Where(x => x.UserId == userId)
                        .OrderBy(x => x.CapabilityCode)
                        .ThenBy(x => x.EffectiveFrom)
                        .Select(
                            x => new
                            {
                                x.CapabilityCode,
                                x.IsAllowed,
                                x.EffectiveFrom,
                                x.EffectiveTo
                            })
                        .ToListAsync(ct);

                var oldValues =
                    new
                    {
                        user.DisplayName,
                        user.Email,
                        user.IsActive,

                        identity.ExternalOrganization,
                        identity.ExternalTitle,
                        identity.AuthorizationFrom,
                        identity.AuthorizationTo,

                        Scopes =
                            scopesBefore,

                        Capabilities =
                            capabilitiesBefore
                    };

                user.DisplayName =
                    request.DisplayName;

                user.Email =
                    request.Email;

                user.IsActive =
                    request.AdminEnabled;

                user.UpdatedAt =
                    now;

                identity.ExternalOrganization =
                    request.ExternalOrganization;

                identity.ExternalTitle =
                    request.ExternalTitle;

                identity.AuthorizationFrom =
                    request.AuthorizationFrom;

                identity.AuthorizationTo =
                    request.AuthorizationTo;

                identity.UpdatedAt =
                    now;

                var roleAssignment =
                    roleAssignments.Single();

                roleAssignment.EffectiveFrom =
                    request.AuthorizationFrom;

                roleAssignment.EffectiveTo =
                    request.AuthorizationTo;

                await PrepareScopeVersionAsync(
                    userId,
                    request.ChangeEffectiveFrom,
                    ct);

                await PrepareCapabilityVersionAsync(
                    userId,
                    CapabilityCodes.ExportExcel,
                    request.ChangeEffectiveFrom,
                    ct);

                await PrepareCapabilityVersionAsync(
                    userId,
                    CapabilityCodes.ExportPdf,
                    request.ChangeEffectiveFrom,
                    ct);

                if (request.ScopeType
                    == DataScopeTypes.Organization)
                {
                    await db.UserDataScopes
                        .AddAsync(
                            new UserDataScope
                            {
                                UserId =
                                    userId,

                                ScopeType =
                                    DataScopeTypes.Organization,

                                OrganizationId =
                                    orgId,

                                TeamId =
                                    null,

                                EffectiveFrom =
                                    request.ChangeEffectiveFrom,

                                EffectiveTo =
                                    request.AuthorizationTo,

                                GrantedByUserId =
                                    admin.UserId,

                                CreatedAt =
                                    now
                            },
                            ct);
                }
                else
                {
                    foreach (var teamId
                             in request.TeamIds)
                    {
                        await db.UserDataScopes
                            .AddAsync(
                                new UserDataScope
                                {
                                    UserId =
                                        userId,

                                    ScopeType =
                                        DataScopeTypes.Team,

                                    OrganizationId =
                                        null,

                                    TeamId =
                                        teamId,

                                    EffectiveFrom =
                                        request.ChangeEffectiveFrom,

                                    EffectiveTo =
                                        request.AuthorizationTo,

                                    GrantedByUserId =
                                        admin.UserId,

                                    CreatedAt =
                                        now
                                },
                                ct);
                    }
                }

                await AddCapabilityAsync(
                    userId,
                    CapabilityCodes.ExportExcel,
                    request.CanExportExcel,
                    request.ChangeEffectiveFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await AddCapabilityAsync(
                    userId,
                    CapabilityCodes.ExportPdf,
                    request.CanExportPdf,
                    request.ChangeEffectiveFrom,
                    request.AuthorizationTo,
                    admin.UserId,
                    now,
                    ct);

                await db.AuditLogs.AddAsync(
                    new AuditLog
                    {
                        UserId =
                            admin.UserId,

                        EntityType =
                            "User",

                        EntityId =
                            userId.ToString(),

                        Action =
                            request.AdminEnabled
                                ? "ExternalSupervisorUpdate"
                                : "ExternalSupervisorDisable",

                        NewValues =
                            JsonSerializer.Serialize(
                                new
                                {
                                    Old =
                                        oldValues,

                                    New =
                                        new
                                        {
                                            request.DisplayName,
                                            request.Email,
                                            request.ExternalOrganization,
                                            request.ExternalTitle,
                                            request.AuthorizationFrom,
                                            request.AuthorizationTo,
                                            request.ScopeType,
                                            request.TeamIds,
                                            request.CanExportExcel,
                                            request.CanExportPdf,
                                            request.AdminEnabled,
                                            request.ChangeEffectiveFrom
                                        },

                                    request.ConfirmRetroactive
                                }),

                        CreatedAt =
                            now
                    },
                    ct);

                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);
            });
    }

    private async Task PrepareScopeVersionAsync(
        int userId,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var rows =
            await db.UserDataScopes
                .Where(x => x.UserId == userId)
                .ToListAsync(ct);

        if (rows.Any(
                x => x.EffectiveFrom > effectiveFrom))
        {
            throw new InvalidOperationException(
                "此督導已有較晚生效的 Data Scope 排程，請先處理該排程後再異動。");
        }

        var previousDay =
            PreviousDay(effectiveFrom);

        foreach (var row in rows.Where(
                     x =>
                         x.EffectiveFrom < effectiveFrom
                         && (!x.EffectiveTo.HasValue
                             || x.EffectiveTo
                                >= effectiveFrom)))
        {
            row.EffectiveTo =
                previousDay;
        }

        var sameStart =
            rows.Where(
                    x =>
                        x.EffectiveFrom
                        == effectiveFrom)
                .ToList();

        if (sameStart.Count > 0)
        {
            db.UserDataScopes.RemoveRange(
                sameStart);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task PrepareCapabilityVersionAsync(
        int userId,
        string capabilityCode,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var rows =
            await db.UserCapabilities
                .Where(
                    x =>
                        x.UserId == userId
                        && x.CapabilityCode
                           == capabilityCode)
                .ToListAsync(ct);

        if (rows.Any(
                x => x.EffectiveFrom > effectiveFrom))
        {
            throw new InvalidOperationException(
                $"此督導已有較晚生效的 {capabilityCode} 排程，請先處理該排程後再異動。");
        }

        var previousDay =
            PreviousDay(effectiveFrom);

        foreach (var row in rows.Where(
                     x =>
                         x.EffectiveFrom < effectiveFrom
                         && (!x.EffectiveTo.HasValue
                             || x.EffectiveTo
                                >= effectiveFrom)))
        {
            row.EffectiveTo =
                previousDay;
        }

        var sameStart =
            rows.Where(
                    x =>
                        x.EffectiveFrom
                        == effectiveFrom)
                .ToList();

        if (sameStart.Count > 0)
        {
            db.UserCapabilities.RemoveRange(
                sameStart);
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly PreviousDay(
        DateOnly value)
    {
        if (value == DateOnly.MinValue)
        {
            throw new InvalidOperationException(
                "異動生效日不正確。");
        }

        return value.AddDays(-1);
    }

    private async Task<string> NewExternalUserCodeAsync(
        CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var code =
                $"EXT-{Guid.NewGuid():N}"
                .ToUpperInvariant();

            if (!await db.UserIdentityProfiles
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.UserCode == code,
                        ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "無法產生唯一的 External UserCode。");
    }

    private async Task AddCapabilityAsync(
        int userId,
        string capabilityCode,
        bool isAllowed,
        DateOnly effectiveFrom,
        DateOnly effectiveTo,
        int grantedByUserId,
        DateTime now,
        CancellationToken ct)
    {
        await db.UserCapabilities.AddAsync(
            new UserCapability
            {
                UserId =
                    userId,

                CapabilityCode =
                    capabilityCode,

                IsAllowed =
                    isAllowed,

                EffectiveFrom =
                    effectiveFrom,

                EffectiveTo =
                    effectiveTo,

                GrantedByUserId =
                    grantedByUserId,

                CreatedAt =
                    now
            },
            ct);
    }
}
